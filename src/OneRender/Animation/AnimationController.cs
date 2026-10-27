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
internal sealed partial class AnimationController
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
    private StyledTree? _tree;
    private StyleTable? _table;
    private readonly int[] _propStamp = new int[Properties.Count];
    private int _propStampValue;

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
    public void Recalc(StyledTree tree, StyledTree? previous, double now)
    {
        ArgumentNullException.ThrowIfNull(tree);
        now = EffectTiming.ClampTime(now);
        _tree = tree;
        StyleTable table = tree.Styles;
        bool sameTable = ReferenceEquals(_table, table);
        _table = table;
        _stamp++;
        _changedCount = 0;
        for (int i = 0; i < tree.Count; i++)
        {
            if (tree.KindOf(i) != (byte)StyledNodeKind.Element)
            {
                continue;
            }

            int key = KeyOf(tree, i);
            _effects.TryGetValue(key, out ElementEffects? e);
            ref readonly ComputedValues after = ref tree.Styles[tree.StyleOf(i)];
            bool wantsTransitions = HasTransitionsConfigured(in after);
            bool wantsAnimations = after.HasAnimations;
            if (e is null && !wantsTransitions && !wantsAnimations)
            {
                continue;
            }

            if (e is null)
            {
                if (_effects.Count >= MaxAnimatedElements)
                {
                    continue;
                }

                e = new ElementEffects { Key = key };
                _effects[key] = e;
            }

            e.Stamp = _stamp;
            e.NodeIndex = i;
            bool hadRow = e.HasRow && sameTable;
            ComputedValues baseValues = after;
            e.Base = baseValues;

            if (after.Display.IsNone)
            {
                CancelAll(e, now);
                FinishRecalc(tree, e, i, hadRow, now);
                continue;
            }

            UpdateAnimations(tree, e, i, in baseValues, now);
            if (previous is not null)
            {
                UpdateTransitions(tree, previous, e, i, in baseValues, wantsTransitions, now);
            }
            else
            {
                CancelTransitionsNotIn(e, in baseValues, now);
            }

            FinishRecalc(tree, e, i, hadRow, now);
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

    private void FinishRecalc(StyledTree tree, ElementEffects e, int node, bool hadRow, double now)
    {
        if (e.IsEmpty)
        {
            // Nothing left to animate: the element shows its cascaded style.
            e.HasRow = false;
            _active.Remove(e);
            _effects.Remove(e.Key);
            return;
        }

        StyleTable table = tree.Styles;
        if (!hadRow)
        {
            if (!table.TryAllocatePrivate(in e.Base, out StyleId row))
            {
                tree.Diagnostics.Add(DiagnosticKind.LimitExceeded, "style table full; element left unanimated");
                CancelAll(e, now);
                _effects.Remove(e.Key);
                return;
            }

            e.Row = row;
            e.HasRow = true;
        }

        RebuildTouched(e);
        tree.ReplaceStyle(node, e.Row);
        if (!_active.Contains(e))
        {
            _active.Add(e);
        }

        SampleElement(e, now, true);
        AddChanged(node);
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
        MotionLevel level = MotionLevel.None;
        for (int a = _active.Count - 1; a >= 0; a--)
        {
            ElementEffects e = _active[a];
            MotionLevel l = SampleElement(e, now, false);
            if (l != MotionLevel.None)
            {
                AddChanged(e.NodeIndex);
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

    // Events are dispatched in the order of the time they were scheduled for, ties by the composite order of the effect (CSS Animations 1
    // §4.? "Event dispatch", CSS Transitions 1 §6). A stable insertion sort, since the batches are small.
    private void SortEvents()
    {
        for (int i = 1; i < _eventCount; i++)
        {
            PendingMotionEvent cur = _events[i];
            int j = i - 1;
            while (j >= 0 && Before(in cur, in _events[j]))
            {
                _events[j + 1] = _events[j];
                j--;
            }

            _events[j + 1] = cur;
        }
    }

    private static bool Before(in PendingMotionEvent a, in PendingMotionEvent b)
    {
        if (a.ScheduledTime != b.ScheduledTime)
        {
            return a.ScheduledTime < b.ScheduledTime;
        }

        return a.Order < b.Order;
    }
}
