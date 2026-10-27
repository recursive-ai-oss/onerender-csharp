using OneRender.Style;

namespace OneRender.Animation;

/// <summary>The events CSS Transitions 1 §6 and CSS Animations 1 §4.? define, as the controller queues them for dispatch.</summary>
internal enum MotionEventKind : byte
{
    /// <summary><c>transitionrun</c>.</summary>
    TransitionRun,

    /// <summary><c>transitionstart</c>.</summary>
    TransitionStart,

    /// <summary><c>transitionend</c>.</summary>
    TransitionEnd,

    /// <summary><c>transitioncancel</c>.</summary>
    TransitionCancel,

    /// <summary><c>animationstart</c>.</summary>
    AnimationStart,

    /// <summary><c>animationiteration</c>.</summary>
    AnimationIteration,

    /// <summary><c>animationend</c>.</summary>
    AnimationEnd,

    /// <summary><c>animationcancel</c>.</summary>
    AnimationCancel,
}

/// <summary>An event waiting to be dispatched. <see cref="Name"/> is the property name or the animation name.</summary>
internal struct PendingMotionEvent
{
    public MotionEventKind Kind;
    public int Key;
    public string Name;
    public double ElapsedSeconds;
    public double ScheduledTime;
    public int Order;
}

/// <summary>A running or delayed CSS transition on one property of one element (CSS Transitions 1 §3).</summary>
internal sealed class TransitionInstance
{
    public PropertyId Property;
    public ComputedValues From;
    public ComputedValues To;
    public ComputedValues ReversingAdjustedStart;
    public double ReversingShorteningFactor = 1;
    public double StartTime;
    public double Duration; // ms
    public double Delay; // ms
    public EasingFunction Easing = EasingFunction.Ease;
    public bool AllowDiscrete;
    public bool Interpolable;
    public bool RunSent;
    public bool StartSent;
    public bool EndSent;
    public double LastProgress = double.NaN;
    public EffectPhase LastPhase = EffectPhase.Before;
    public int Order;
    public InterpolationScratch Scratch = new();

    /// <summary>The time since the transition's start time.</summary>
    public double LocalTime(double now) => now - StartTime;
}

/// <summary>One property of an animation: its keyframes in offset order (CSS Animations 1 §4.2, with the implicit 0% and 100% filled in).</summary>
internal sealed class PropertyTrack
{
    public PropertyId Property;
    public double[] Offsets = [];
    public int[] Values = []; // index into the animation's keyframe values, or -1 for the element's underlying value
    public EasingFunction?[] Easings = [];
}

/// <summary>A CSS animation on one element: the keyframes resolved for it and the timing state (CSS Animations 1 §3).</summary>
internal sealed class AnimationInstance
{
    public string Name = string.Empty;
    public int ListIndex;
    public PropertyTrack[] Tracks = [];
    public ComputedValues[] KeyframeValues = [];
    public double StartTime;
    public double Delay;
    public double Duration;
    public double Iterations = 1;
    public AnimationDirectionKind Direction;
    public AnimationFillKind Fill;
    public EasingFunction Easing = EasingFunction.Ease;
    public bool Paused;
    public double HoldTime;
    public bool Seen;
    public bool StartSent;
    public bool EndSent;
    public double LastIteration = double.NaN;
    public double LastProgress = double.NaN;
    public EffectPhase LastPhase = EffectPhase.Idle;
    public bool Finished;
    public InterpolationScratch Scratch = new();
    public bool Compositor;
    public MotionLevel Level = MotionLevel.Layout;
    public int Order;

    /// <summary>The local time of the animation at <paramref name="now"/>, frozen while paused.</summary>
    public double LocalTime(double now) => Paused ? HoldTime : now - StartTime;
}

/// <summary>Everything the animation controller tracks for one element.</summary>
internal sealed class ElementEffects
{
    public int Key;
    public int Stamp;
    public ComputedValues Base;
    public StyleId Row;
    public bool HasRow;
    public int NodeIndex;
    public List<TransitionInstance> Transitions = [];
    public List<AnimationInstance> Animations = [];
    public bool ChangedCompositor;
    public bool ChangedOther;
    public PropertyId[] Touched = [];
    public double ReferenceWidth;
    public double ReferenceHeight;

    public bool IsEmpty => Transitions.Count == 0 && Animations.Count == 0;
}
