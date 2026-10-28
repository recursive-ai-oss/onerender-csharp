using RecursiveAI.OneHtml;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Animation;

internal sealed partial class AnimationController
{
    // ------------------------------------------------------------------------------------------------------------ animations

    /// <summary>Matches <c>animation-name</c> and its companions against the running animations (CSS Animations 1 §3.2).</summary>
    private void UpdateAnimations(StyledTree tree, ElementEffects e, int node, in ComputedValues after, double now)
    {
        foreach (AnimationInstance a in e.Animations)
        {
            a.Seen = false;
        }

        string?[] names = after.AnimationName;
        int count = Math.Min(names.Length, MaxAnimationsPerElement);
        for (int i = 0; i < count; i++)
        {
            string? name = names[i];
            if (name is null)
            {
                continue;
            }

            AnimationInstance? inst = null;
            foreach (AnimationInstance a in e.Animations)
            {
                if (!a.Seen && string.Equals(a.Name, name, StringComparison.Ordinal))
                {
                    inst = a;
                    break;
                }
            }

            double duration = Pick(after.AnimationDuration, i);
            double durationMs = double.IsNaN(duration) ? 0 : EffectTiming.ClampTime(duration * 1000);
            double delayMs = EffectTiming.ClampTime(Pick(after.AnimationDelay, i) * 1000);
            double iterations = Pick(after.AnimationIterationCount, i);
            iterations = double.IsPositiveInfinity(iterations) ? iterations : Math.Min(Math.Max(iterations, 0), EffectTiming.MaxIterations);
            AnimationDirectionKind direction = Pick(after.AnimationDirection, i);
            AnimationFillKind fill = Pick(after.AnimationFillMode, i);
            EasingFunction easing = Pick(after.AnimationTimingFunction, i);
            bool running = Pick(after.AnimationPlayState, i);

            if (inst is null)
            {
                ComputedStyle? cascade = e.Animations.Count >= MaxAnimationsPerElement ? null : CascadeStyleOf(tree, node);
                IReadOnlyList<ResolvedKeyframes?>? lists = cascade is null ? null : _styles.GetAnimationKeyframes(cascade);
                if (lists is null || i >= lists.Count || lists[i] is not { Keyframes.Count: > 0 } resolved)
                {
                    continue;
                }

                inst = new AnimationInstance { Name = name, StartTime = now, Order = _order++ };
                ResolveKeyframes(tree, e, node, inst, resolved, cascade!, in after);
                if (inst.Tracks.Length == 0 && !HasTimingEffect(inst))
                {
                    // A rule with no animatable declarations still runs (its events fire), just without values.
                }

                e.Animations.Add(inst);
            }

            inst.Seen = true;
            inst.ListIndex = i;
            inst.Delay = delayMs;
            inst.Duration = durationMs;
            inst.Iterations = iterations;
            inst.Direction = direction;
            inst.Fill = fill;
            inst.Easing = easing;
            if (running && inst.Paused)
            {
                inst.Paused = false;
                inst.StartTime = now - inst.HoldTime;
            }
            else if (!running && !inst.Paused)
            {
                inst.Paused = true;
                inst.HoldTime = now - inst.StartTime;
            }

            // A change of timing can bring a finished animation back to life (CSS Animations 1 §3.? "animation-iteration-count changes").
            inst.Finished = false;
            inst.EndSent = false;
        }

        for (int i = e.Animations.Count - 1; i >= 0; i--)
        {
            AnimationInstance a = e.Animations[i];
            if (!a.Seen)
            {
                CancelAnimation(e, a, now);
                e.Animations.RemoveAt(i);
            }
        }

        // Composite order among animations is the order of animation-name (later wins), so keep them in list order.
        e.Animations.Sort(static (x, y) => x.ListIndex.CompareTo(y.ListIndex));
    }

    private static bool HasTimingEffect(AnimationInstance inst) => inst.Duration > 0 || inst.Delay != 0;

    private static T Pick<T>(T[] list, int index) => list[index % list.Length];

    // The element's source node and computed style in the cascade the page is styled from; the style is null without a cascade.
    private ComputedStyle? CascadeStyleOf(StyledTree tree, int node)
    {
        if (_styles.IsNull || tree.SourceDocument is null)
        {
            return null;
        }

        int source = tree.SourceIndexOf(node);
        RecursiveAI.OneHtml.Node n = source < 0 ? default : _styles.Document.GetNode(source);
        return n.IsNull ? null : _styles.GetStyle(n);
    }

    /// <summary>
    /// Turns the resolved keyframes of one animation into what sampling reads: each keyframe's declared values become typed values
    /// against the element's own font size and parent, then one track per property is assembled. A value that is the element's own value
    /// (the keyframes OneHtml adds at 0% and 100%, or an explicit value equal to it) stays "the underlying value" and is read from the
    /// element's base style at each tick (CSS Animations 1 §4.2).
    /// </summary>
    private static void ResolveKeyframes(StyledTree tree, ElementEffects e, int node, AnimationInstance inst, ResolvedKeyframes resolved, ComputedStyle cascade, in ComputedValues after)
    {
        StyleTable table = tree.Styles;
        int parentNode = tree.ParentOf(node);
        StyleId parent = parentNode >= 0 ? tree.StyleOf(parentNode) : default;
        bool hasParent = parentNode >= 0;
        ComponentValueList fontSize = CssTree.ParseComponentValueList(CssText.Px(after.FontSize)).Values;
        var source = new KeyframeSource();
        IReadOnlyList<ResolvedKeyframe> frames = resolved.Keyframes;
        int count = Math.Min(frames.Count, MaxKeyframes);
        var values = new ComputedValues[count];
        var declared = new bool[count * Properties.Count];
        var explicitValue = new bool[count * Properties.Count];
        var easings = new EasingFunction?[count];
        for (int b = 0; b < count; b++)
        {
            ResolvedKeyframe frame = frames[b];
            var decl = new StyleDeclaration();
            foreach (KeyframeValue v in frame.Values)
            {
                if (!Properties.TryGet(v.Property, out PropertyId pid))
                {
                    continue;
                }

                string text = v.Value;
                if (frame.IsImplicit || (!v.InvalidAtComputedValueTime && text.Equals(cascade.GetComputedValue(v.Property), StringComparison.Ordinal)))
                {
                    // The element's own value: read from the base style when sampled.
                    if (PropertyAnimator.IsAnimatable(pid))
                    {
                        declared[(b * Properties.Count) + (int)pid] = true;
                    }

                    continue;
                }

                // The declaration store treats unset as "not declared", but in a keyframe it is a value: inherit or initial by property (CSS Animations 1 §4.2).
                if (v.InvalidAtComputedValueTime || text.Trim().Equals("unset", StringComparison.OrdinalIgnoreCase))
                {
                    text = Properties.IsInherited(pid) ? "inherit" : "initial";
                }

                decl.TrySet(v.Property, text);
            }

            source.Reset(decl, fontSize);
            values[b] = table.ComputeDetached(source, parent, hasParent);
            easings[b] = frame.TimingFunction.Equals(resolved.TimingFunction, StringComparison.Ordinal) ? null : StyleComputer.ParseEasing(frame.TimingFunction);
            for (int i = 0; i < Properties.Count; i++)
            {
                if (decl.Get((PropertyId)i, out _) != ValueOrigin.Unset && PropertyAnimator.IsAnimatable((PropertyId)i))
                {
                    declared[(b * Properties.Count) + i] = true;
                    explicitValue[(b * Properties.Count) + i] = true;
                }
            }
        }

        inst.KeyframeValues = values;

        // Properties in property order, so the result does not depend on hash or declaration order.
        var tracks = new List<PropertyTrack>();
        var offsets = new List<double>(count + 2);
        var idx = new List<int>(count + 2);
        var eas = new List<EasingFunction?>(count + 2);
        MotionLevel level = MotionLevel.Compositor;
        for (int p = 0; p < Properties.Count; p++)
        {
            var id = (PropertyId)p;
            if (!PropertyAnimator.IsAnimatable(id) || IsImportantWinner(cascade, id))
            {
                continue;
            }

            offsets.Clear();
            idx.Clear();
            eas.Clear();
            for (int b = 0; b < count; b++)
            {
                if (!declared[(b * Properties.Count) + p])
                {
                    continue;
                }

                offsets.Add(frames[b].Offset);
                idx.Add(explicitValue[(b * Properties.Count) + p] ? b : -1);
                eas.Add(easings[b]);
            }

            if (offsets.Count == 0)
            {
                continue;
            }

            if (offsets[0] != 0)
            {
                offsets.Insert(0, 0);
                idx.Insert(0, -1);
                eas.Insert(0, null);
            }

            if (offsets[^1] != 1)
            {
                offsets.Add(1);
                idx.Add(-1);
                eas.Add(null);
            }

            tracks.Add(new PropertyTrack { Property = id, Offsets = offsets.ToArray(), Values = idx.ToArray(), Easings = eas.ToArray() });
            MotionLevel l = LevelOf(id);
            if (l > level)
            {
                level = l;
            }
        }

        inst.Tracks = tracks.ToArray();
        inst.Level = level;
        inst.Compositor = level == MotionLevel.Compositor;
    }

    // CSS Cascade 4 §6.4.1: important declarations of the author, user and user-agent origins beat animations, so an animation does
    // not drive a property whose winning declaration is !important.
    private static bool IsImportantWinner(ComputedStyle cascade, PropertyId id) =>
        cascade.GetSource(Properties.NameOf(id)) is { IsImportant: true };

    /// <summary>Declarations of one keyframe as the computer's input; font-size is the element's, so <c>em</c> in a keyframe means the element's em.</summary>
    private sealed class KeyframeSource : OneRender.Style.DeclarationSource
    {
        private StyleDeclaration? _declaration;
        private ComponentValueList _fontSize;

        public void Reset(StyleDeclaration declaration, ComponentValueList fontSize)
        {
            _declaration = declaration;
            _fontSize = fontSize;
        }

        public override ValueOrigin Get(PropertyId id, out ComponentValueList value)
        {
            ValueOrigin origin = _declaration is null ? ValueOrigin.Unset : _declaration.Get(id, out value);
            if (_declaration is not null && origin != ValueOrigin.Unset)
            {
                _declaration.Get(id, out value);
                return origin;
            }

            if (id == PropertyId.FontSize)
            {
                value = _fontSize;
                return ValueOrigin.Specified;
            }

            value = default;
            return ValueOrigin.Unset;
        }
    }

    // ------------------------------------------------------------------------------------------------------------ transitions

    private void CancelTransitionsNotIn(ElementEffects e, in ComputedValues after, double now)
    {
        for (int i = e.Transitions.Count - 1; i >= 0; i--)
        {
            TransitionInstance t = e.Transitions[i];
            if (!TransitionListed(in after, t.Property))
            {
                CancelTransition(e, t, now);
                e.Transitions.RemoveAt(i);
            }
        }
    }

    // Whether property id appears in the computed transition-property list (CSS Transitions 1 §2.1).
    private static bool TransitionListed(in ComputedValues v, PropertyId id)
    {
        foreach (TransitionTarget t in v.TransitionProperty)
        {
            switch (t.Kind)
            {
                case TransitionTargetKind.All:
                    return PropertyAnimator.IsAnimatable(id);
                case TransitionTargetKind.Property when t.Property == id:
                    return true;
                case TransitionTargetKind.Shorthand when TransitionShorthands.TryExpand(t.Name, out PropertyId[] longs) && Array.IndexOf(longs, id) >= 0:
                    return true;
            }
        }

        return false;
    }

    /// <summary>Starts, reverses and cancels transitions for one element at a style change (CSS Transitions 1 §3.2 and §3.3).</summary>
    private void UpdateTransitions(StyledTree tree, ElementEffects e, in ComputedValues before, bool hasBefore, in ComputedValues after, bool configured, double now)
    {
        CancelTransitionsNotIn(e, in after, now);
        if (!configured || !hasBefore)
        {
            return;
        }

        // Transitions start from the before-change style: what the page last showed, with running transitions and animations at their
        // current values (the caller passes the node's style before the change).
        StyleTable table = tree.Styles;
        TransitionTarget[] targets = after.TransitionProperty;

        // The last entry that names a property decides its timing (CSS Transitions 1 §2.1), so walk the list backwards and let the first
        // writer win. Properties are visited in property order afterwards so the start order does not depend on the list.
        _propStampValue++;
        int[] entryOf = _entryScratch ??= new int[Properties.Count];
        for (int ti = targets.Length - 1; ti >= 0; ti--)
        {
            TransitionTarget t = targets[ti];
            switch (t.Kind)
            {
                case TransitionTargetKind.All:
                    for (int p = 0; p < Properties.Count; p++)
                    {
                        Mark(p, ti, entryOf);
                    }

                    break;
                case TransitionTargetKind.Property:
                    Mark((int)t.Property, ti, entryOf);
                    break;
                case TransitionTargetKind.Shorthand:
                    if (TransitionShorthands.TryExpand(t.Name, out PropertyId[] longs))
                    {
                        foreach (PropertyId l in longs)
                        {
                            Mark((int)l, ti, entryOf);
                        }
                    }

                    break;
            }
        }

        for (int p = 0; p < Properties.Count; p++)
        {
            if (_propStamp[p] != _propStampValue)
            {
                continue;
            }

            var id = (PropertyId)p;
            if (_onlyDisplay && id != PropertyId.Display)
            {
                continue;
            }

            int ti = entryOf[p];
            bool allowDiscrete = Pick(after.TransitionBehavior, ti);
            if (!PropertyAnimator.IsAnimatable(id) || (!PropertyAnimator.IsInterpolable(id) && !allowDiscrete))
            {
                continue;
            }

            double durationMs = EffectTiming.ClampTime(Pick(after.TransitionDuration, ti) * 1000);
            double delayMs = EffectTiming.ClampTime(Pick(after.TransitionDelay, ti) * 1000);
            EasingFunction easing = Pick(after.TransitionTimingFunction, ti);
            ConsiderTransition(table, e, id, in before, in after, durationMs, delayMs, easing, allowDiscrete, now);
        }
    }

    private int[]? _entryScratch;

    private void Mark(int p, int entry, int[] entryOf)
    {
        // Walking backwards: the first mark (the last entry in the list) stays.
        if (_propStamp[p] != _propStampValue)
        {
            _propStamp[p] = _propStampValue;
            entryOf[p] = entry;
        }
    }

    private static int MapNode(StyledTree tree, StyledTree previous, int node)
    {
        int source = tree.SourceIndexOf(node);
        if (source >= 0)
        {
            StyledNode n = previous.FindBySource(source);
            return n.IsNull ? -1 : n.Index;
        }

        return node < previous.Count && previous.KindOf(node) == tree.KindOf(node) && string.Equals(previous.TagNameOf(node), tree.TagNameOf(node), StringComparison.Ordinal) ? node : -1;
    }

    private void ConsiderTransition(StyleTable table, ElementEffects e, PropertyId id, in ComputedValues before, in ComputedValues after, double durationMs, double delayMs,
        EasingFunction easing, bool allowDiscrete, double now)
    {
        TransitionInstance? running = null;
        int runningIndex = -1;
        for (int i = 0; i < e.Transitions.Count; i++)
        {
            if (e.Transitions[i].Property == id)
            {
                running = e.Transitions[i];
                runningIndex = i;
                break;
            }
        }

        bool same = PropertyAnimator.ValuesEqual(table, id, in before, in after);
        double combined = Math.Max(durationMs, 0) + delayMs;
        if (running is null)
        {
            if (same || combined <= 0 || !Transitionable(id, in before, in after, allowDiscrete))
            {
                return;
            }

            StartTransition(e, id, in before, in after, durationMs, delayMs, easing, allowDiscrete, now, 1, in before);
            return;
        }

        // A transition is already running on this property.
        if (PropertyAnimator.ValuesEqual(table, id, in running.To, in after))
        {
            // CSS Transitions 1 §3.2: its end value is already the after-change value; leave it alone.
            return;
        }

        if (PropertyAnimator.ValuesEqual(table, id, in before, in after) || combined <= 0 || !Transitionable(id, in before, in after, allowDiscrete))
        {
            // The current value already equals the new value, or the pair cannot transition: cancel and show the new value.
            CancelTransition(e, running, now);
            e.Transitions.RemoveAt(runningIndex);
            return;
        }

        if (PropertyAnimator.ValuesEqual(table, id, in running.ReversingAdjustedStart, in after))
        {
            // Reversing a transition part-way (CSS Transitions 1 §3.3): the new one covers only the distance already travelled.
            double oldProgress = running.Easing.Evaluate(Math.Clamp(EffectTiming.Sample(running.LocalTime(now), running.Delay, running.Duration, 1, AnimationDirectionKind.Normal, AnimationFillKind.Both).Progress, 0, 1));
            double factor = Math.Abs((oldProgress * running.ReversingShorteningFactor) + (1 - running.ReversingShorteningFactor));
            factor = Math.Clamp(factor, 0, 1);
            double newDelay = delayMs >= 0 ? delayMs : factor * delayMs;
            double newDuration = factor * durationMs;
            ComputedValues adjusted = running.To;
            CancelTransition(e, running, now);
            e.Transitions.RemoveAt(runningIndex);
            StartTransition(e, id, in before, in after, newDuration, newDelay, easing, allowDiscrete, now, factor, in adjusted);
            return;
        }

        CancelTransition(e, running, now);
        e.Transitions.RemoveAt(runningIndex);
        StartTransition(e, id, in before, in after, durationMs, delayMs, easing, allowDiscrete, now, 1, in before);
    }

    private static bool Transitionable(PropertyId id, in ComputedValues from, in ComputedValues to, bool allowDiscrete) =>
        PropertyAnimator.IsInterpolable(id) && PropertyAnimator.CanInterpolate(id, in from, in to) ? true : allowDiscrete;

    private void StartTransition(ElementEffects e, PropertyId id, in ComputedValues from, in ComputedValues to, double durationMs, double delayMs, EasingFunction easing,
        bool allowDiscrete, double now, double shorteningFactor, in ComputedValues reversingAdjustedStart)
    {
        if (e.Transitions.Count >= MaxTransitionsPerElement)
        {
            return;
        }

        var t = new TransitionInstance
        {
            Property = id,
            From = from,
            To = to,
            ReversingAdjustedStart = reversingAdjustedStart,
            ReversingShorteningFactor = shorteningFactor,
            StartTime = now,
            Duration = Math.Max(durationMs, 0),
            Delay = delayMs,
            Easing = easing,
            AllowDiscrete = allowDiscrete,
            Order = _order++,
        };
        e.Transitions.Add(t);
    }
}
