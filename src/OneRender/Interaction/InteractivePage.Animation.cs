using OneRender.Animation;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.Tree;
using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

public sealed partial class InteractivePage
{
    private double _motionNow;
    private TransitionEvent? _transitionEvent;
    private AnimationEvent? _animationEvent;
    private int[] _patchBoxes = new int[64];
    private int _patchCount;
    private MotionLevel _lastMotion;

    /// <summary>
    /// The page's animation clock: the largest time passed to <see cref="Advance"/> or carried by an input, in the host's milliseconds.
    /// Starts at 0, or at <see cref="InteractionOptions.AnimationStartTime"/>.
    /// </summary>
    public double AnimationTime => _motionNow;

    /// <summary>True while a transition or animation still changes over time; the host should keep calling <see cref="Advance"/> every frame.</summary>
    public bool IsAnimating => _page.Motion is { HasLiveEffects: true };

    /// <summary>
    /// Moves CSS transitions and animations to <paramref name="nowMs"/> and brings the page up to date. The engine reads no clock: the host
    /// passes the time of the frame it is about to draw, from any monotonic source, and uses the same source for the times of its inputs.
    /// A time earlier than the last one is treated as the last one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Events (<c>transitionrun</c>, <c>animationstart</c>, ...) are dispatched through <see cref="Events"/> before the method returns, in the
    /// order of the times they were scheduled for. Animated values change the page like this:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Only <c>transform</c>, the individual transform properties or <c>opacity</c> changed: the result has
    /// <see cref="InputOutcome.Animated"/> and not <see cref="InputOutcome.Relaid"/>. Call <see cref="PatchAnimations"/> to update the display list in place;
    /// nothing is laid out again and the call allocates nothing.</description></item>
    /// <item><description>A paint-only property changed (a color, a shadow, a filter): <see cref="InputOutcome.Animated"/> with
    /// <see cref="InputOutcome.Relaid"/>; paint the page again. Layout is not run.</description></item>
    /// <item><description>A property that affects layout changed: the boxes and fragments are rebuilt in place and the result has both flags.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="nowMs">The time of the frame in milliseconds.</param>
    /// <param name="cancellationToken">Cancels a relayout.</param>
    /// <returns>What changed on the page.</returns>
    /// <exception cref="OperationCanceledException">Cancelled while laying out.</exception>
    public InputOutcome Advance(double nowMs, CancellationToken cancellationToken = default)
    {
        if (double.IsNaN(nowMs))
        {
            nowMs = _motionNow;
        }

        _motionNow = Math.Max(_motionNow, EffectTiming.ClampTime(nowMs));
        _lastTime = Math.Max(_lastTime, _motionNow);
        AnimationController? motion = _page.Motion;
        _patchCount = 0;
        _lastMotion = MotionLevel.None;
        if (motion is null)
        {
            return InputOutcome.None;
        }

        MotionLevel level = motion.Tick(_motionNow);
        _lastMotion = level;
        InputOutcome outcome = InputOutcome.None;
        switch (level)
        {
            case MotionLevel.Compositor:
                CollectPatchBoxes(motion);
                outcome = InputOutcome.Animated;
                break;
            case MotionLevel.Paint:
                outcome = InputOutcome.Animated | InputOutcome.Relaid;
                _generation++;
                break;
            case MotionLevel.Layout:
                RelayoutInPlace(cancellationToken);
                outcome = InputOutcome.Animated | InputOutcome.Relaid;
                break;
        }

        DispatchMotionEvents(motion);
        return outcome;
    }

    private void RelayoutInPlace(CancellationToken ct)
    {
        HtmlPage page = _page;
        page.Boxes.Build(ct);
        ILayoutEngine engine = _options.Layout ?? new BlockLayoutEngine();
        engine.Layout(page.Boxes, new LayoutInput(_options.ViewportWidth, _options.ViewportHeight, _options.Fonts), page.Fragments, ct);
        _hits.Rebuild();
        _generation++;
    }

    private void CollectPatchBoxes(AnimationController motion)
    {
        ReadOnlySpan<int> nodes = motion.ChangedNodes;
        if (_patchBoxes.Length < nodes.Length)
        {
            _patchBoxes = new int[Math.Max(nodes.Length, _patchBoxes.Length * 2)];
        }

        BoxTree boxes = _page.Boxes;
        StyledTree styled = _page.Styled;
        for (int i = 0; i < nodes.Length; i++)
        {
            Box b = boxes.PrincipalBox(styled.Make(nodes[i]));
            if (!b.IsNull)
            {
                _patchBoxes[_patchCount++] = b.Index;
            }
        }
    }

    /// <summary>
    /// Brings a painted display list up to date with the last <see cref="Advance"/> when it reported only transform and opacity changes:
    /// the matrices and group opacities of the animated elements are replaced in place and the damage lands in the list's damage region. It
    /// allocates nothing and does no layout. After a call to <see cref="Advance"/> that also reported <see cref="InputOutcome.Relaid"/>,
    /// paint the page again instead.
    /// </summary>
    /// <param name="painter">The painter that painted <paramref name="list"/>.</param>
    /// <param name="list">The retained list holding the last painted frame.</param>
    /// <returns>The number of items patched.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public int PatchAnimations(Painter painter, RetainedDisplayList list)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(list);
        return _patchCount == 0 ? 0 : painter.PatchAnimated(list, new ReadOnlySpan<int>(_patchBoxes, 0, _patchCount));
    }

    private void DispatchMotionEvents(AnimationController motion)
    {
        ReadOnlySpan<PendingMotionEvent> pending = motion.PendingEvents;
        if (pending.Length == 0)
        {
            return;
        }

        // Copy first: a listener may call back into the page, which may queue more.
        PendingMotionEvent[] batch = pending.ToArray();
        motion.ClearEvents();
        Document doc = _page.Document;
        foreach (PendingMotionEvent p in batch)
        {
            if (p.Key < 0)
            {
                continue;
            }

            Node target = doc.GetNode(p.Key);
            if (target.IsNull)
            {
                continue;
            }

            bool transition = p.Kind <= MotionEventKind.TransitionCancel;
            string type = p.Kind switch
            {
                MotionEventKind.TransitionRun => "transitionrun",
                MotionEventKind.TransitionStart => "transitionstart",
                MotionEventKind.TransitionEnd => "transitionend",
                MotionEventKind.TransitionCancel => "transitioncancel",
                MotionEventKind.AnimationStart => "animationstart",
                MotionEventKind.AnimationIteration => "animationiteration",
                MotionEventKind.AnimationEnd => "animationend",
                _ => "animationcancel",
            };
            if (transition)
            {
                TransitionEvent e = _transitionEvent ??= new TransitionEvent(type);
                e.Reset(type, true, false, _lastTime);
                e.PropertyName = p.Name;
                e.ElapsedTime = p.ElapsedSeconds;
                _events.Dispatch(e, EventTarget.Of(target));
            }
            else
            {
                AnimationEvent e = _animationEvent ??= new AnimationEvent(type);
                e.Reset(type, true, false, _lastTime);
                e.AnimationName = p.Name;
                e.ElapsedTime = p.ElapsedSeconds;
                _events.Dispatch(e, EventTarget.Of(target));
            }
        }
    }
}
