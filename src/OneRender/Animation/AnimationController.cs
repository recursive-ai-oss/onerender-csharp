using RecursiveAI.OneHtml;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Animation;

/// <summary>How much of the pipeline a tick of the controller changed, in increasing cost.</summary>
internal enum MotionLevel : byte
{
    /// <summary>Nothing visible changed.</summary>
    None,

    /// <summary>Only transform or opacity changed: the display list can be patched in place.</summary>
    Compositor,

    /// <summary>A paint-only property changed (a color, a shadow, a filter): repaint, no relayout.</summary>
    Paint,

    /// <summary>A property that affects layout changed: relayout and repaint.</summary>
    Layout,
}

/// <summary>
/// CSS Transitions 1 and CSS Animations 1 for one document, driven by a time the host passes in (milliseconds on any monotonic
/// clock the host chooses; the engine never reads a clock). After a style recalculation, <see cref="Recalc"/> starts, retargets and
/// cancels transitions and animations against the fresh cascade; <see cref="Tick"/> then moves them to a new time, writing the animated
/// values into rows of the style table that belong to the animated elements.
/// </summary>
/// <remarks>
/// <para>Composite order (CSS Cascade 5 §4.? and CSS Animations 1 §3): an element's style is its cascaded style, overridden by its running
/// transitions, overridden by its running animations; among animations the later one in <c>animation-name</c> wins.</para>
/// <para>Ticking allocates nothing once the animations are set up, for properties whose interpolation does not build new objects (numbers,
/// colors, lengths in one unit, transforms of matching function lists). Setup, events and the less common value types allocate.</para>
/// </remarks>
internal sealed partial class AnimationController : IStyleHolder
{
    /// <summary>Most elements animated at once; more are left unanimated.</summary>
    public const int MaxAnimatedElements = 100_000;

    /// <summary>Most transitions one element runs at once.</summary>
    public const int MaxTransitionsPerElement = 256;

    /// <summary>Most animations one element runs at once.</summary>
    public const int MaxAnimationsPerElement = 64;

    /// <summary>Most events queued between two dispatches; further ones are dropped.</summary>
    public const int MaxPendingEvents = 65_536;

    private readonly Dictionary<int, ElementEffects> _effects = new();
    private readonly List<ElementEffects> _active = [];
    private readonly List<ElementEffects> _pendingRemoval = [];
    private PendingMotionEvent[] _events = new PendingMotionEvent[32];
    private int _eventCount;
    private int _stamp;
    private int _order;
    private int[] _changedNodes = new int[64];
    private int _changedCount;
    private int[] _compositorNodes = new int[64];
    private int _compositorCount;
    private StyledTree? _tree;
    private StyleTable? _table;
    private StyleTable? _registeredWith;

    /// <summary>Allocates a private row, first making sure the table's sweeps see the rows this controller holds.</summary>
    private bool AllocateRow(StyleTable table, in ComputedValues values, out StyleId row)
    {
        if (!ReferenceEquals(_registeredWith, table))
        {
            table.AddHolder(this);
            _registeredWith = table;
        }

        return table.TryAllocatePrivate(in values, out row);
    }

    /// <inheritdoc />
    void IStyleHolder.MarkStyles(StyleTable table)
    {
        foreach (KeyValuePair<int, ElementEffects> kv in _effects)
        {
            ElementEffects e = kv.Value;
            if (e.HasRow)
            {
                table.Mark(e.Row.Index);
            }

            foreach (StyleId row in e.FollowerRows)
            {
                table.Mark(row.Index);
            }
        }
    }
    private bool _sameTable;
    private readonly int[] _propStamp = new int[Properties.Count];
    private int _propStampValue;

    private DocumentStyles? _styles;

    /// <summary>Creates a controller reading <c>@keyframes</c> from <paramref name="keyframes"/>.</summary>
    public AnimationController(KeyframesRegistry keyframes)
    {
        Keyframes = keyframes;
    }

    /// <summary>The <c>@keyframes</c> rules animations look up by name.</summary>
    public KeyframesRegistry Keyframes { get; set; }

    /// <summary>Elements that currently have a transition or animation (including finished ones holding a fill).</summary>
    public int ElementCount => _effects.Count;

    /// <summary>The number of animations and transitions that still change over time.</summary>
    public int LiveElementCount => _active.Count;

    /// <summary>The events queued since the last <see cref="PendingEvents"/>, in dispatch order.</summary>
    public ReadOnlySpan<PendingMotionEvent> PendingEvents => new(_events, 0, _eventCount);

    /// <summary>Forgets the queued events (call after dispatching them).</summary>
    public void ClearEvents() => _eventCount = 0;

    /// <summary>The node indexes of the styled tree whose styles the last <see cref="Tick"/> or <see cref="Recalc"/> changed.</summary>
    public ReadOnlySpan<int> ChangedNodes => new(_changedNodes, 0, _changedCount);

    /// <summary>
    /// After a <see cref="Tick"/> that also changed paint or layout properties: the nodes whose only change was transform or opacity.
    /// They are not in <see cref="ChangedNodes"/> then, because the display list patches them without painting or laying out again. Empty
    /// after a tick that changed nothing else (those nodes are <see cref="ChangedNodes"/> itself).
    /// </summary>
    public ReadOnlySpan<int> CompositorNodes => new(_compositorNodes, 0, _compositorCount);

    /// <summary>The earliest time, in the host's units, at which a live element next needs a tick; the host may sleep until then. NaN when nothing is live.</summary>
    public bool HasLiveEffects => _active.Count > 0;

    // ------------------------------------------------------------------------------------------------------------ identity

    // An element is identified across rebuilds by the index of its source node, or, for a builder tree, by its position.
    private static int KeyOf(StyledTree tree, int i)
    {
        int s = tree.SourceIndexOf(i);
        return s >= 0 ? s : -2 - i;
    }

    private static bool HasTransitionsConfigured(in ComputedValues v)
    {
        TransitionTarget[] props = v.TransitionProperty;
        if (props.Length == 0 || (props.Length == 1 && props[0].Kind == TransitionTargetKind.None))
        {
            return false;
        }

        double[] dur = v.TransitionDuration, del = v.TransitionDelay;
        for (int i = 0; i < dur.Length; i++)
        {
            if (dur[i] > 0)
            {
                return true;
            }
        }

        for (int i = 0; i < del.Length; i++)
        {
            if (del[i] > 0)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------------------------ recalc

    /// <summary>
    /// Brings the running transitions and animations in line with a freshly styled tree: starts transitions where a transitionable property
    /// changed between the previous tree and this one (CSS Transitions 1 §3), starts, updates and cancels animations to match
    /// <c>animation-name</c> and friends, and gives every animated element a style row holding its animated values at <paramref name="now"/>.
    /// Call it after the front end built <paramref name="tree"/> and before the box tree is built from it.
    /// </summary>
    /// <param name="tree">The styled tree just built (its styles are the cascade without animation).</param>
    /// <param name="previous">The tree the page showed before, or null for the first style. Its styles are the before-change style.</param>
    /// <param name="now">The host's time in ms.</param>
    /// <param name="styles">The cascade the tree was built from; important declarations in it keep their properties out of animations. May be null.</param>
    public void Recalc(StyledTree tree, StyledTree? previous, double now, DocumentStyles? styles = null)
    {
        _styles = styles;
        ArgumentNullException.ThrowIfNull(tree);
        now = EffectTiming.ClampTime(now);
        _tree = tree;
        StyleTable table = tree.Styles;
        bool sameTable = ReferenceEquals(_table, table);
        _sameTable = sameTable;
        _table = table;
        _stamp++;
        _changedCount = 0;
        for (int i = 0; i < tree.Count; i++)
        {
            RecalcNode(tree, i, previous, false, default, now);
        }

        // Elements of the previous generation that are gone from the tree: their effects are cancelled.
        _pendingRemoval.Clear();
        foreach (KeyValuePair<int, ElementEffects> kv in _effects)
        {
            if (kv.Value.Stamp != _stamp)
            {
                _pendingRemoval.Add(kv.Value);
            }
        }

        foreach (ElementEffects gone in _pendingRemoval)
        {
            CancelAll(gone, now);
            _effects.Remove(gone.Key);
            _active.Remove(gone);
        }

        _pendingRemoval.Clear();
        SortEvents();
    }

    /// <summary>
    /// Recalculation for just the elements a restyle in place changed: the same as <see cref="Recalc"/> for those nodes, with each node's
    /// before-change style taken from the styled tree's record of what it was before the change set. The changed nodes are listed in
    /// <see cref="ChangedNodes"/>.
    /// </summary>
    /// <param name="tree">The styled tree, restyled but not yet cleared of its pending changes.</param>
    /// <param name="now">The host's time in ms.</param>
    /// <param name="styles">The cascade the tree was restyled from; may be null.</param>
    /// <returns>True when any restyled element has, or had, transitions or animations (its style row may differ in content from the recorded old style).</returns>
    public bool RecalcPending(StyledTree tree, double now, DocumentStyles? styles = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        _styles = styles;
        now = EffectTiming.ClampTime(now);
        _tree = tree;
        _sameTable = true;
        _table = tree.Styles;
        _stamp++;
        _changedCount = 0;
        bool touched = false;
        int count = tree.PendingCount;
        for (int k = 0; k < count; k++)
        {
            if ((tree.PendingKinds(k) & NodeChanges.Style) == 0)
            {
                continue;
            }

            int node = tree.PendingNode(k);
            if (tree.KindOf(node) != (byte)StyledNodeKind.Element)
            {
                continue;
            }

            int key = KeyOf(tree, node);
            bool had = _effects.TryGetValue(key, out ElementEffects? already);
            if (already is not null && already.Stamp == _stamp)
            {
                // The change log can name a node twice; its effects were already brought up to date in this call.
                continue;
            }

            RecalcNode(tree, node, null, true, tree.OldStyleOf(node), now);
            touched |= had || _effects.ContainsKey(key);
        }

        SortEvents();
        return touched;
    }

    // One element of Recalc. `previous` is the tree the page showed before (a full rebuild); otherwise, when `useOld` is set, `oldStyle`
    // is the node's style before the change (a restyle in place).
    private void RecalcNode(StyledTree tree, int i, StyledTree? previous, bool useOld, StyleId oldStyle, double now)
    {
        if (tree.KindOf(i) != (byte)StyledNodeKind.Element)
        {
            return;
        }

        int key = KeyOf(tree, i);
        _effects.TryGetValue(key, out ElementEffects? e);
        ref readonly ComputedValues after = ref tree.Styles[tree.StyleOf(i)];
        bool wantsTransitions = HasTransitionsConfigured(in after);
        bool wantsAnimations = after.HasAnimations;
        if (e is null && !wantsTransitions && !wantsAnimations)
        {
            return;
        }

        if (e is null)
        {
            if (_effects.Count >= MaxAnimatedElements)
            {
                return;
            }

            e = new ElementEffects { Key = key };
            _effects[key] = e;
        }

        e.Stamp = _stamp;
        e.NodeIndex = i;
        bool hadRow = e.HasRow && _sameTable;
        ComputedValues baseValues = after;
        e.Base = baseValues;

        ComputedValues before = default;
        bool hasBefore = false;
        if (previous is not null)
        {
            int prevNode = MapNode(tree, previous, i);
            if (prevNode >= 0)
            {
                before = previous.Styles[previous.StyleOf(prevNode)];
                hasBefore = true;
            }
        }
        else if (useOld)
        {
            before = tree.Styles[oldStyle];
            hasBefore = true;
        }

        if (after.Display.IsNone)
        {
            // CSS Transitions 2 §3.1 / CSS Display 4 §2.7: an element with display: none runs no animations and no transitions, except
            // a transition of display itself, which keeps the element shown until it ends.
            CancelAllExceptDisplayTransition(e, now);
            if (hasBefore && wantsTransitions)
            {
                _onlyDisplay = true;
                UpdateTransitions(tree, e, in before, true, in baseValues, wantsTransitions, now);
                _onlyDisplay = false;
            }

            FinishRecalc(tree, e, i, hadRow, now);
            return;
        }

        UpdateAnimations(tree, e, i, in baseValues, now);
        UpdateTransitions(tree, e, in before, hasBefore, in baseValues, wantsTransitions, now);
        FinishRecalc(tree, e, i, hadRow, now);
    }

    private void FinishRecalc(StyledTree tree, ElementEffects e, int node, bool hadRow, double now)
    {
        if (e.IsEmpty)
        {
            // Nothing left to animate: the element shows its cascaded style. Rows of a table that is still in use go back for reuse; the
            // restyle that led here already gave the element and its followers their cascaded styles.
            if (e.HasRow && _sameTable)
            {
                ReleaseRows(tree.Styles, e);
            }

            e.HasRow = false;
            _active.Remove(e);
            _effects.Remove(e.Key);
            return;
        }

        StyleTable table = tree.Styles;
        if (!hadRow)
        {
            if (!AllocateRow(table, in e.Base, out StyleId row))
            {
                tree.Diagnostics.Add(DiagnosticKind.LimitExceeded, "style table full; element left unanimated");
                CancelAll(e, now);
                _effects.Remove(e.Key);
                return;
            }

            e.Row = row;
            e.HasRow = true;
        }
        else
        {
            // The row keeps what the last tick wrote; the cascaded values of the properties no effect touches may have changed.
            table.MutableRow(e.Row) = e.Base;
            ReleaseFollowerRows(table, e);
        }

        RebuildTouched(e);
        BuildFollowers(tree, e, node);
        tree.ReplaceStyle(node, e.Row);
        if (!_active.Contains(e))
        {
            _active.Add(e);
        }

        SampleElement(e, now, true);
        AddChanged(node);
    }

    private static void ReleaseFollowerRows(StyleTable table, ElementEffects e)
    {
        foreach (StyleId row in e.FollowerRows)
        {
            table.ReleasePrivate(row);
        }

        e.FollowerRows = [];
        e.FollowerNodes = [];
    }

    private static void ReleaseRows(StyleTable table, ElementEffects e)
    {
        ReleaseFollowerRows(table, e);
        table.ReleasePrivate(e.Row);
    }

    private void AddChanged(int node)
    {
        if (_changedCount == _changedNodes.Length)
        {
            Array.Resize(ref _changedNodes, _changedNodes.Length * 2);
        }

        _changedNodes[_changedCount++] = node;
    }

    // ------------------------------------------------------------------------------------------------------------ tick

    /// <summary>
    /// Moves every live transition and animation to <paramref name="now"/>, updates the animated style rows in place and queues events.
    /// </summary>
    /// <returns>The most expensive kind of change made; <see cref="ChangedNodes"/> lists the elements.</returns>
    public MotionLevel Tick(double now)
    {
        now = EffectTiming.ClampTime(now);
        _changedCount = 0;
        _compositorCount = 0;
        MotionLevel level = MotionLevel.None;
        for (int a = _active.Count - 1; a >= 0; a--)
        {
            ElementEffects e = _active[a];
            MotionLevel l = SampleElement(e, now, false);
            if (l == MotionLevel.Compositor)
            {
                if (_compositorCount == _compositorNodes.Length)
                {
                    Array.Resize(ref _compositorNodes, _compositorCount * 2);
                }

                _compositorNodes[_compositorCount++] = e.NodeIndex;
                if (level < MotionLevel.Compositor)
                {
                    level = MotionLevel.Compositor;
                }
            }
            else if (l != MotionLevel.None)
            {
                AddChanged(e.NodeIndex);
                if (l >= MotionLevel.Paint)
                {
                    // Descendants that follow an animated inherited value changed with it and take part in repaint and relayout.
                    int[] followers = e.FollowerNodes;
                    for (int f = 0; f < followers.Length; f++)
                    {
                        AddChanged(followers[f]);
                    }
                }

                if (l > level)
                {
                    level = l;
                }
            }

            if (!IsLive(e))
            {
                _active.RemoveAt(a);
            }
        }

        if (level == MotionLevel.Compositor)
        {
            // Only transform and opacity moved: they are the changed nodes.
            (_changedNodes, _compositorNodes) = (_compositorNodes, _changedNodes);
            _changedCount = _compositorCount;
            _compositorCount = 0;
        }

        SortEvents();
        return level;
    }

    private static bool IsLive(ElementEffects e)
    {
        if (e.Transitions.Count > 0)
        {
            return true;
        }

        foreach (AnimationInstance a in e.Animations)
        {
            if (!a.Finished)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------------------------ events

    private void Emit(MotionEventKind kind, int key, string name, double elapsedMs, double scheduled, int order)
    {
        if (_eventCount >= MaxPendingEvents)
        {
            return;
        }

        if (_eventCount == _events.Length)
        {
            Array.Resize(ref _events, _events.Length * 2);
        }

        _events[_eventCount++] = new PendingMotionEvent { Kind = kind, Key = key, Name = name, ElapsedSeconds = elapsedMs / 1000, ScheduledTime = scheduled, Order = order };
    }

    // Events are dispatched in the order of the time they were scheduled for, ties by the composite order of the effect, then kind and
    // element, so the order is total and does not depend on the sort algorithm (CSS Animations 1 §4.2, CSS Transitions 1 §6). The sort
    // is O(n log n) in place; a hostile page can queue tens of thousands of events in one frame.
    private static readonly Comparison<PendingMotionEvent> EventOrder = CompareEvents;

    private void SortEvents()
    {
        if (_eventCount > 1)
        {
            new Span<PendingMotionEvent>(_events, 0, _eventCount).Sort(EventOrder);
        }
    }

    private static int CompareEvents(PendingMotionEvent a, PendingMotionEvent b)
    {
        int c = a.ScheduledTime.CompareTo(b.ScheduledTime);
        if (c != 0)
        {
            return c;
        }

        c = a.Order.CompareTo(b.Order);
        if (c != 0)
        {
            return c;
        }

        c = ((int)a.Kind).CompareTo((int)b.Kind);
        return c != 0 ? c : a.Key.CompareTo(b.Key);
    }
}
