using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Animation;

internal sealed partial class AnimationController
{
    // ------------------------------------------------------------------------------------------------------------ sampling

    private static MotionLevel LevelOf(PropertyId id)
    {
        switch (id)
        {
            case PropertyId.Transform:
            case PropertyId.Translate:
            case PropertyId.Rotate:
            case PropertyId.Scale:
            case PropertyId.Opacity:
            case PropertyId.TransformOrigin:
                return MotionLevel.Compositor;
            case PropertyId.Color:
            case PropertyId.BackgroundColor:
            case PropertyId.BorderTopColor:
            case PropertyId.BorderRightColor:
            case PropertyId.BorderBottomColor:
            case PropertyId.BorderLeftColor:
            case PropertyId.OutlineColor:
            case PropertyId.OutlineWidth:
            case PropertyId.OutlineOffset:
            case PropertyId.TextDecorationColor:
            case PropertyId.ColumnRuleColor:
            case PropertyId.BoxShadow:
            case PropertyId.TextShadow:
            case PropertyId.Filter:
            case PropertyId.BackdropFilter:
            case PropertyId.ClipPath:
            case PropertyId.Visibility:
            case PropertyId.BorderTopLeftRadius:
            case PropertyId.BorderTopRightRadius:
            case PropertyId.BorderBottomRightRadius:
            case PropertyId.BorderBottomLeftRadius:
            case PropertyId.ObjectPosition:
            case PropertyId.BackgroundImage:
            case PropertyId.BackgroundPositionX:
            case PropertyId.BackgroundPositionY:
            case PropertyId.BackgroundSize:
            case PropertyId.BackgroundRepeat:
            case PropertyId.MixBlendMode:
            case PropertyId.PointerEvents:
                return MotionLevel.Paint;
            default:
                return MotionLevel.Layout;
        }
    }

    private static bool IsTransformProperty(PropertyId id) => id is PropertyId.Transform or PropertyId.Translate or PropertyId.Rotate or PropertyId.Scale or PropertyId.TransformOrigin;

    private void RebuildTouched(ElementEffects e)
    {
        _propStampValue++;
        var list = new List<PropertyId>(8);
        void Add(PropertyId p)
        {
            if (_propStamp[(int)p] != _propStampValue)
            {
                _propStamp[(int)p] = _propStampValue;
                list.Add(p);
            }
        }

        foreach (TransitionInstance t in e.Transitions)
        {
            Add(t.Property);
        }

        foreach (AnimationInstance a in e.Animations)
        {
            foreach (PropertyTrack track in a.Tracks)
            {
                Add(track.Property);
            }
        }

        e.Touched = list.ToArray();
    }

    private static (bool Transform, bool Opacity) FlagsOf(ElementEffects e)
    {
        bool tr = false, op = false;
        foreach (PropertyId p in e.Touched)
        {
            tr |= p is PropertyId.Transform or PropertyId.Translate or PropertyId.Rotate or PropertyId.Scale;
            op |= p == PropertyId.Opacity;
        }

        return (tr, op);
    }

    /// <summary>Writes the animated values at <paramref name="now"/> into the element's row and queues the events that passed.</summary>
    private MotionLevel SampleElement(ElementEffects e, double now, bool initial)
    {
        StyleTable table = _table!;
        ref ComputedValues row = ref table.MutableRow(e.Row);
        MotionLevel level = initial ? MotionLevel.Layout : MotionLevel.None;

        // Start from the cascaded value of every property an effect drives, then lay the effects over it in composite order.
        PropertyId[] touched = e.Touched;
        for (int i = 0; i < touched.Length; i++)
        {
            table.CopyProperty(touched[i], ref row, in e.Base);
        }

        (bool tr, bool op) = FlagsOf(e);
        if (row.AnimatesTransform != tr || row.AnimatesOpacity != op)
        {
            row.AnimatesTransform = tr;
            row.AnimatesOpacity = op;
            if (!initial && level < MotionLevel.Paint)
            {
                level = MotionLevel.Paint;
            }
        }

        var ctx = new InterpolationContext(e.ReferenceWidth, e.ReferenceHeight, e.Base.Color);
        bool structural = false;
        for (int i = 0; i < e.Transitions.Count; i++)
        {
            TransitionInstance t = e.Transitions[i];
            MotionLevel l = SampleTransition(table, e, t, now, ref row, in ctx, out bool done);
            if (l > level)
            {
                level = l;
            }

            if (done)
            {
                e.Transitions.RemoveAt(i--);
                structural = true;
            }
        }

        for (int i = 0; i < e.Animations.Count; i++)
        {
            AnimationInstance a = e.Animations[i];
            MotionLevel l = SampleAnimation(table, e, a, now, ref row, in ctx);
            if (l > level)
            {
                level = l;
            }
        }

        if (structural)
        {
            RebuildTouched(e);
            (bool tr2, bool op2) = FlagsOf(e);
            if (row.AnimatesTransform != tr2 || row.AnimatesOpacity != op2)
            {
                row.AnimatesTransform = tr2;
                row.AnimatesOpacity = op2;
                if (level < MotionLevel.Paint)
                {
                    level = MotionLevel.Paint;
                }
            }
        }

        return level;
    }

    private MotionLevel SampleTransition(StyleTable table, ElementEffects e, TransitionInstance t, double now, ref ComputedValues row, in InterpolationContext ctx, out bool done)
    {
        done = false;
        double local = t.LocalTime(now);
        EffectSample s = EffectTiming.Sample(local, t.Delay, t.Duration, 1, AnimationDirectionKind.Normal, AnimationFillKind.Both);
        double activeMs = t.Duration;
        if (!t.RunSent)
        {
            t.RunSent = true;
            Emit(MotionEventKind.TransitionRun, e.Key, PropertyIdName(t.Property), Math.Max(Math.Min(-t.Delay, activeMs), 0), t.StartTime, t.Order);
        }

        if (s.Phase != EffectPhase.Before && !t.StartSent)
        {
            t.StartSent = true;
            Emit(MotionEventKind.TransitionStart, e.Key, PropertyIdName(t.Property), Math.Max(Math.Min(-t.Delay, activeMs), 0), t.StartTime + Math.Max(t.Delay, 0), t.Order);
        }

        double eased;
        if (s.Phase == EffectPhase.After)
        {
            eased = 1;
            if (!t.EndSent)
            {
                t.EndSent = true;
                Emit(MotionEventKind.TransitionEnd, e.Key, PropertyIdName(t.Property), activeMs, t.StartTime + t.Delay + activeMs, t.Order);
            }

            done = true;
        }
        else
        {
            eased = t.Easing.Evaluate(s.Phase == EffectPhase.Before ? 0 : s.Progress, s.Reversed);
        }

        MotionLevel level = MotionLevel.None;
        bool progressChanged = !t.LastProgress.Equals(eased) || t.LastPhase != s.Phase;
        t.LastProgress = eased;
        t.LastPhase = s.Phase;
        if (progressChanged)
        {
            level = LevelOf(t.Property);
        }

        if (done)
        {
            // The end value is the cascaded value, which the reset already wrote; nothing to interpolate.
            return level == MotionLevel.None ? MotionLevel.None : level;
        }

        PropertyAnimator.Interpolate(table, t.Property, in t.From, in t.To, eased, ref row, in ctx, t.Scratch);
        return level;
    }

    private MotionLevel SampleAnimation(StyleTable table, ElementEffects e, AnimationInstance a, double now, ref ComputedValues row, in InterpolationContext ctx)
    {
        double local = a.LocalTime(now);
        EffectSample s = EffectTiming.Sample(local, a.Delay, a.Duration, a.Iterations, a.Direction, a.Fill);
        double active = EffectTiming.ActiveDuration(a.Duration, a.Iterations);
        double startAt = a.StartTime + a.Delay;
        if (s.Phase != EffectPhase.Before && !a.StartSent)
        {
            a.StartSent = true;
            Emit(MotionEventKind.AnimationStart, e.Key, a.Name, Math.Max(Math.Min(-a.Delay, active), 0), startAt, a.Order);
            a.LastIteration = s.Phase == EffectPhase.Active ? s.Iteration : double.NaN;
        }
        else if (s.Phase == EffectPhase.Active && a.StartSent && !double.IsNaN(a.LastIteration) && s.Iteration != a.LastIteration)
        {
            // CSS Animations 1 §4.? : animationiteration when the current iteration changes while active; elapsed time is the iteration boundary.
            Emit(MotionEventKind.AnimationIteration, e.Key, a.Name, s.Iteration * a.Duration, startAt + (s.Iteration * a.Duration), a.Order);
            a.LastIteration = s.Iteration;
        }
        else if (s.Phase == EffectPhase.Active && double.IsNaN(a.LastIteration))
        {
            a.LastIteration = s.Iteration;
        }

        if (s.Phase == EffectPhase.After && !a.EndSent)
        {
            a.EndSent = true;
            Emit(MotionEventKind.AnimationEnd, e.Key, a.Name, active, startAt + active, a.Order);
        }

        if (s.Phase == EffectPhase.After)
        {
            a.Finished = true;
        }
        else
        {
            a.Finished = false;
        }

        MotionLevel level = MotionLevel.None;
        bool changed = !a.LastProgress.Equals(s.Progress) || a.LastPhase != s.Phase || !s.InEffect;
        if (s.InEffect)
        {
            changed = !a.LastProgress.Equals(s.Progress) || a.LastPhase != s.Phase;
        }

        a.LastProgress = s.InEffect ? s.Progress : double.NaN;
        a.LastPhase = s.Phase;
        if (!s.InEffect)
        {
            return changed && !double.IsNaN(a.LastProgress) ? a.Level : MotionLevel.None;
        }

        if (changed)
        {
            level = a.Level;
        }

        for (int i = 0; i < a.Tracks.Length; i++)
        {
            SampleTrack(table, e, a, a.Tracks[i], s, ref row, in ctx);
        }

        return level;
    }

    private static void SampleTrack(StyleTable table, ElementEffects e, AnimationInstance a, PropertyTrack track, in EffectSample s, ref ComputedValues row, in InterpolationContext ctx)
    {
        double[] offsets = track.Offsets;
        int last = offsets.Length - 1;
        double p = s.Progress;
        int i = 0;
        while (i < last - 1 && offsets[i + 1] <= p)
        {
            i++;
        }

        double o0 = offsets[i], o1 = offsets[i + 1];
        double local = o1 > o0 ? (p - o0) / (o1 - o0) : 1;
        EasingFunction easing = track.Easings[i] ?? a.Easing;
        double eased = easing.Evaluate(local, s.Reversed);
        int fi = track.Values[i], ti = track.Values[i + 1];
        ref readonly ComputedValues from = ref (fi < 0 ? ref e.Base : ref a.KeyframeValues[fi]);
        ref readonly ComputedValues to = ref (ti < 0 ? ref e.Base : ref a.KeyframeValues[ti]);
        PropertyAnimator.Interpolate(table, track.Property, in from, in to, eased, ref row, in ctx, a.Scratch);
    }

    private static string PropertyIdName(PropertyId id) => Properties.NameOf(id);

    // ------------------------------------------------------------------------------------------------------------ cancel

    private void CancelAll(ElementEffects e, double now)
    {
        foreach (TransitionInstance t in e.Transitions)
        {
            CancelTransition(e, t, now);
        }

        e.Transitions.Clear();
        foreach (AnimationInstance a in e.Animations)
        {
            CancelAnimation(e, a, now);
        }

        e.Animations.Clear();
        e.Touched = [];
    }

    private void CancelTransition(ElementEffects e, TransitionInstance t, double now)
    {
        if (t.RunSent && !t.EndSent)
        {
            double local = t.LocalTime(now);
            Emit(MotionEventKind.TransitionCancel, e.Key, PropertyIdName(t.Property), Math.Max(Math.Min(local - t.Delay, t.Duration), 0), now, t.Order);
        }
    }

    private void CancelAnimation(ElementEffects e, AnimationInstance a, double now)
    {
        if (a.LastPhase != EffectPhase.Idle && !a.EndSent)
        {
            double local = a.LocalTime(now);
            double active = EffectTiming.ActiveDuration(a.Duration, a.Iterations);
            Emit(MotionEventKind.AnimationCancel, e.Key, a.Name, Math.Max(Math.Min(local - a.Delay, active), 0), now, a.Order);
        }
    }
}
