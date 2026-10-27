using OneRender.Style;
namespace OneRender.Animation;

/// <summary>Where an animation effect is relative to its active interval (Web Animations 1 §4.5.1).</summary>
internal enum EffectPhase : byte
{
    /// <summary>The local time is before the active interval.</summary>
    Before,

    /// <summary>The local time is inside the active interval.</summary>
    Active,

    /// <summary>The local time is at or after the end of the active interval.</summary>
    After,

    /// <summary>No local time: the effect is not on a timeline (cancelled).</summary>
    Idle,
}

/// <summary>The result of sampling an effect at a local time.</summary>
internal readonly struct EffectSample
{
    public EffectSample(EffectPhase phase, bool inEffect, double progress, double iteration, bool reversed)
    {
        Phase = phase;
        InEffect = inEffect;
        Progress = progress;
        Iteration = iteration;
        Reversed = reversed;
    }

    /// <summary>The phase.</summary>
    public EffectPhase Phase { get; }

    /// <summary>True when the effect applies a value: active, or before/after with a fill (Web Animations 1 §4.5.?).</summary>
    public bool InEffect { get; }

    /// <summary>The directed progress within the iteration, 0 to 1 (before the easing of a transition; keyframe intervals ease on their own).</summary>
    public double Progress { get; }

    /// <summary>The current iteration index (0-based); infinity once an infinite animation is in the after phase, which cannot happen on a timeline.</summary>
    public double Iteration { get; }

    /// <summary>True when the iteration runs backwards (for the step-function before flag).</summary>
    public bool Reversed { get; }
}

/// <summary>
/// The timing model of Web Animations 1 §4 as CSS Animations 1 §3 and CSS Transitions 1 §3 use it. Times are in milliseconds.
/// Inputs are bounded (<see cref="MaxTime"/>, <see cref="MaxIterations"/>) so no arithmetic overflows to a non-finite number.
/// </summary>
internal static class EffectTiming
{
    /// <summary>Largest delay or duration used, in ms (about 31 years); larger values are clamped.</summary>
    public const double MaxTime = 1e12;

    /// <summary>Largest finite iteration count used; infinity stays infinity.</summary>
    public const double MaxIterations = 1e9;

    /// <summary>Clamps a time in ms into the supported range; NaN becomes 0.</summary>
    public static double ClampTime(double ms) => double.IsNaN(ms) ? 0 : Math.Clamp(ms, -MaxTime, MaxTime);

    /// <summary>The active duration: duration times iteration count, 0 when either is 0 (Web Animations 1 §4.3.? "calculating the active duration").</summary>
    public static double ActiveDuration(double duration, double iterations)
    {
        if (duration <= 0 || iterations <= 0)
        {
            return 0;
        }

        return double.IsPositiveInfinity(iterations) ? double.PositiveInfinity : duration * Math.Min(iterations, MaxIterations);
    }

    /// <summary>The end time of the effect: delay plus active duration, not below 0.</summary>
    public static double EndTime(double delay, double duration, double iterations) => Math.Max(delay + ActiveDuration(duration, iterations), 0);

    /// <summary>Samples an effect.</summary>
    /// <param name="localTime">The time since the animation started, in ms (may be negative before a start).</param>
    /// <param name="delay">The delay in ms.</param>
    /// <param name="duration">The iteration duration in ms, at least 0.</param>
    /// <param name="iterations">The iteration count, at least 0, infinity allowed.</param>
    /// <param name="direction">The playback direction of each iteration.</param>
    /// <param name="fill">The fill mode.</param>
    public static EffectSample Sample(double localTime, double delay, double duration, double iterations, AnimationDirectionKind direction, AnimationFillKind fill)
    {
        double active = ActiveDuration(duration, iterations);
        double end = Math.Max(delay + active, 0);
        double beforeBoundary = Math.Max(Math.Min(delay, end), 0);
        double afterBoundary = Math.Max(Math.Min(delay + active, end), 0);

        EffectPhase phase;
        if (localTime < beforeBoundary)
        {
            phase = EffectPhase.Before;
        }
        else if (localTime >= afterBoundary)
        {
            phase = EffectPhase.After;
        }
        else
        {
            phase = EffectPhase.Active;
        }

        // Active time (Web Animations 1 §4.5.4): unresolved outside the interval without a matching fill.
        double activeTime;
        switch (phase)
        {
            case EffectPhase.Before:
                if (fill is AnimationFillKind.Backwards or AnimationFillKind.Both)
                {
                    activeTime = Math.Max(localTime - delay, 0);
                    break;
                }

                return new EffectSample(phase, false, 0, 0, false);
            case EffectPhase.Active:
                activeTime = localTime - delay;
                break;
            default:
                if (fill is AnimationFillKind.Forwards or AnimationFillKind.Both)
                {
                    activeTime = Math.Max(Math.Min(localTime - delay, active), 0);
                    break;
                }

                return new EffectSample(phase, false, 0, 0, false);
        }

        // Overall progress (§4.7.? "calculating the overall progress").
        double overall;
        if (duration <= 0)
        {
            overall = phase == EffectPhase.Before ? 0 : Math.Min(iterations, MaxIterations);
        }
        else
        {
            overall = activeTime / duration;
        }

        // Simple iteration progress.
        double simple;
        if (double.IsInfinity(overall))
        {
            simple = 0;
        }
        else
        {
            simple = overall - Math.Floor(overall);
        }

        if (simple == 0 && phase != EffectPhase.Before && activeTime == active && iterations != 0)
        {
            simple = 1;
        }

        double iteration;
        if (phase == EffectPhase.After && double.IsPositiveInfinity(iterations))
        {
            iteration = double.PositiveInfinity;
        }
        else if (simple == 1 && !double.IsInfinity(overall))
        {
            iteration = Math.Floor(overall) - 1;
        }
        else
        {
            iteration = Math.Floor(overall);
        }

        if (iteration < 0 || double.IsNaN(iteration))
        {
            iteration = 0;
        }

        // Directed progress (§4.7.?).
        bool reverseIteration;
        bool odd = !double.IsInfinity(iteration) && (iteration % 2) == 1;
        switch (direction)
        {
            case AnimationDirectionKind.Reverse: reverseIteration = true; break;
            case AnimationDirectionKind.Alternate: reverseIteration = odd; break;
            case AnimationDirectionKind.AlternateReverse: reverseIteration = !odd; break;
            default: reverseIteration = false; break;
        }

        double directed = reverseIteration ? 1 - simple : simple;
        return new EffectSample(phase, true, directed, iteration, reverseIteration);
    }
}
