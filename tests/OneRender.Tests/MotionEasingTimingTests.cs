using OneRender.Animation;
using OneRender.Interaction;
using OneRender.Style;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>Easing functions (CSS Easing 1) and the timing model (Web Animations 1 §4).</summary>
public class EasingTests
{
    [Fact]
    public void CubicBezierKeywordsMatchTheirCurves()
    {
        EasingFunction ease = EasingFunction.Ease;
        Assert.Equal(0, ease.Evaluate(0));
        Assert.Equal(1, ease.Evaluate(1));
        // cubic-bezier(0.25, 0.1, 0.25, 1) at x = 0.5 is about 0.8024 (the curve is steeper than linear early on).
        Assert.InRange(ease.Evaluate(0.5), 0.795, 0.81);
        Assert.InRange(EasingFunction.EaseIn.Evaluate(0.5), 0.31, 0.33);
        Assert.InRange(EasingFunction.EaseOut.Evaluate(0.5), 0.67, 0.69);
        Assert.InRange(EasingFunction.EaseInOut.Evaluate(0.5), 0.499, 0.501);
    }

    [Fact]
    public void CubicBezierExtrapolatesAlongTheEndTangents()
    {
        EasingFunction? e = EasingFunction.CubicBezier(0.5, 1.5, 0.5, -0.5);
        Assert.NotNull(e);
        // Start tangent slope y1/x1 = 3, so progress -0.1 gives -0.3.
        Assert.Equal(-0.3, e.Evaluate(-0.1), 6);
        Assert.Equal(1 + (3 * 0.1), e.Evaluate(1.1), 6);
    }

    [Fact]
    public void CubicBezierRejectsXOutsideZeroToOne()
    {
        Assert.Null(EasingFunction.CubicBezier(-0.1, 0, 0.5, 1));
        Assert.Null(EasingFunction.CubicBezier(0.5, 0, 1.5, 1));
        Assert.Null(EasingFunction.CubicBezier(double.NaN, 0, 0.5, 1));
    }

    [Theory]
    [InlineData(4, 0, 0.0, 0.0)]
    [InlineData(4, 0, 0.25, 0.25)]
    [InlineData(4, 0, 0.99, 0.75)]
    [InlineData(4, 0, 1.0, 1.0)]
    [InlineData(4, 1, 0.0, 0.25)]
    [InlineData(4, 1, 0.5, 0.75)]
    [InlineData(4, 2, 0.0, 0.0)]
    [InlineData(4, 2, 0.5, 2.0 / 3)]
    [InlineData(4, 2, 1.0, 1.0)]
    [InlineData(2, 3, 0.0, 1.0 / 3)]
    [InlineData(2, 3, 0.5, 2.0 / 3)]
    [InlineData(2, 3, 1.0, 1.0)]
    public void StepsFollowTheJumpTerms(int count, int jump, double input, double expected)
    {
        EasingFunction? e = EasingFunction.Step(count, (StepPosition)jump);
        Assert.NotNull(e);
        Assert.Equal(expected, e.Evaluate(input), 9);
    }

    [Fact]
    public void StepsBeforeFlagPicksTheLowerSideOfAJump()
    {
        EasingFunction e = EasingFunction.Step(2, StepPosition.JumpEnd)!;
        Assert.Equal(0.5, e.Evaluate(0.5, before: false));
        Assert.Equal(0.0, e.Evaluate(0.5, before: true));
    }

    [Fact]
    public void StepsRejectInvalidCounts()
    {
        Assert.Null(EasingFunction.Step(0, StepPosition.JumpEnd));
        Assert.Null(EasingFunction.Step(1, StepPosition.JumpNone));
        Assert.Null(EasingFunction.Step(EasingFunction.MaxSteps + 1, StepPosition.JumpEnd));
    }

    [Fact]
    public void LinearFillsMissingInputsAndInterpolates()
    {
        // linear(0, .5 10% 20%, 1, .5 50%, 1): the CSS Easing 1 §2.1 example, serialized with every input.
        EasingFunction? e = EasingFunction.FromStops([0, 0.5, 0.5, 1, 0.5, 1], [double.NaN, 0.1, 0.2, double.NaN, 0.5, double.NaN]);
        Assert.NotNull(e);
        Assert.Equal("linear(0 0%, 0.5 10%, 0.5 20%, 1 35%, 0.5 50%, 1 100%)", e.Text);
        Assert.Equal(0.25, e.Evaluate(0.05), 9);
        Assert.Equal(0.5, e.Evaluate(0.15), 9);
        Assert.Equal(1.0, e.Evaluate(0.35), 9);
        Assert.Equal(0.75, e.Evaluate(0.425), 9);
    }

    [Fact]
    public void LinearExtrapolatesAlongTheEndSegments()
    {
        EasingFunction e = EasingFunction.FromStops([0.2, 0.8], [0.25, 0.75])!;
        // The end segments have slope 0.6 / 0.5 = 1.2, so 0.05 of input either side moves the output by 0.06.
        Assert.Equal(0.2 - 0.06, e.Evaluate(0.2), 9);
        Assert.Equal(0.8 + 0.06, e.Evaluate(0.8), 9);
    }

    [Fact]
    public void LinearNeverDecreasesItsInputs()
    {
        // An input below an earlier one is raised to it, so the function has a jump instead of going backwards.
        EasingFunction e = EasingFunction.FromStops([0, 1, 0, 1], [0, 0.6, 0.4, 1])!;
        Assert.Equal("linear(0 0%, 1 60%, 0 60%, 1 100%)", e.Text);
    }
}

public class EffectTimingTests
{
    [Fact]
    public void PhasesFollowDelayAndDuration()
    {
        Assert.Equal(EffectPhase.Before, EffectTiming.Sample(500, 1000, 2000, 1, AnimationDirectionKind.Normal, AnimationFillKind.Backwards).Phase);
        Assert.Equal(EffectPhase.Active, EffectTiming.Sample(1500, 1000, 2000, 1, AnimationDirectionKind.Normal, AnimationFillKind.None).Phase);
        Assert.Equal(EffectPhase.After, EffectTiming.Sample(3000, 1000, 2000, 1, AnimationDirectionKind.Normal, AnimationFillKind.Forwards).Phase);
    }

    [Fact]
    public void WithoutFillTheEffectOnlyAppliesWhileActive()
    {
        Assert.False(EffectTiming.Sample(500, 1000, 2000, 1, AnimationDirectionKind.Normal, AnimationFillKind.None).InEffect);
        Assert.True(EffectTiming.Sample(500, 1000, 2000, 1, AnimationDirectionKind.Normal, AnimationFillKind.Backwards).InEffect);
        Assert.False(EffectTiming.Sample(3000, 1000, 2000, 1, AnimationDirectionKind.Normal, AnimationFillKind.Backwards).InEffect);
        Assert.True(EffectTiming.Sample(3000, 1000, 2000, 1, AnimationDirectionKind.Normal, AnimationFillKind.Forwards).InEffect);
    }

    [Fact]
    public void ProgressRepeatsPerIterationAndEndsAtOne()
    {
        EffectSample mid = EffectTiming.Sample(2500, 0, 1000, 3, AnimationDirectionKind.Normal, AnimationFillKind.None);
        Assert.Equal(0.5, mid.Progress, 9);
        Assert.Equal(2, mid.Iteration);
        EffectSample end = EffectTiming.Sample(3000, 0, 1000, 3, AnimationDirectionKind.Normal, AnimationFillKind.Forwards);
        Assert.Equal(1.0, end.Progress, 9);
        Assert.Equal(2, end.Iteration);
    }

    [Fact]
    public void FractionalIterationCountEndsPartWayThrough()
    {
        EffectSample end = EffectTiming.Sample(2500, 0, 1000, 2.5, AnimationDirectionKind.Normal, AnimationFillKind.Forwards);
        Assert.Equal(0.5, end.Progress, 9);
    }

    [Theory]
    [InlineData(0, 0, 0.25)]
    [InlineData(1, 0, 0.75)]
    [InlineData(2, 0, 0.25)]
    [InlineData(2, 1, 0.75)]
    [InlineData(3, 0, 0.75)]
    [InlineData(3, 1, 0.25)]
    public void DirectionPicksTheSideOfEachIteration(int dir, int iteration, double expected)
    {
        EffectSample s = EffectTiming.Sample((iteration * 1000) + 250, 0, 1000, double.PositiveInfinity, (AnimationDirectionKind)dir, AnimationFillKind.None);
        Assert.Equal(expected, s.Progress, 9);
        Assert.Equal(iteration, s.Iteration);
    }

    [Fact]
    public void ZeroDurationJumpsStraightToTheEnd()
    {
        EffectSample s = EffectTiming.Sample(0, 0, 0, 1, AnimationDirectionKind.Normal, AnimationFillKind.Forwards);
        Assert.Equal(EffectPhase.After, s.Phase);
        Assert.Equal(1.0, s.Progress, 9);
    }

    [Fact]
    public void ExtremeInputsStayFinite()
    {
        EffectSample s = EffectTiming.Sample(EffectTiming.MaxTime, -EffectTiming.MaxTime, EffectTiming.MaxTime, EffectTiming.MaxIterations, AnimationDirectionKind.Alternate, AnimationFillKind.Both);
        Assert.True(double.IsFinite(s.Progress));
        Assert.True(s.Progress is >= 0 and <= 1);
        EffectSample inf = EffectTiming.Sample(1e12, 0, 1, double.PositiveInfinity, AnimationDirectionKind.Normal, AnimationFillKind.None);
        Assert.True(double.IsFinite(inf.Progress));
    }
}
