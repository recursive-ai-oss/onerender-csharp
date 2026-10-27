using OneRender.Painting;

namespace OneRender.Style;

// Computed values of transforms, filters, masking and compositing (Transforms 1 and 2, Filter Effects 1 and 2, Masking 1, Compositing 1).
public partial struct ComputedValues
{
    /// <summary>True when any of the transform properties does something (the element has a transform, Transforms 1 §2).</summary>
    internal readonly bool HasTransform => !Transform.IsNone || !Translate.IsNone || !Rotate.IsNone || !Scale.IsNone;

    /// <summary>True when the element has a transform or a perspective: it then is a stacking context and the containing block of fixed and absolute boxes (Transforms 1 §2, Transforms 2 §10).</summary>
    internal readonly bool CreatesTransformContext => HasTransform || Perspective > 0;

    /// <summary>True when the element paints as a stacking context because of opacity, transforms, filters, masking or compositing (Compositing 1 §3, Transforms 1 §2, Filter Effects 1 §3, Masking 1 §5 and §6).</summary>
    internal readonly bool CreatesEffectContext =>
        Opacity < 1.0 || CreatesTransformContext || TransformStyle == TransformStyleKind.Preserve3D || !Filter.IsNone || !BackdropFilter.IsNone
        || ClipPath.Kind != ClipPathKind.None || Mask.HasImages || MixBlendMode != BlendMode.Normal || Isolate;

    private static void InitEffects(ref ComputedValues v)
    {
        v.Transform = TransformSpec.None;
        v.Translate = TransformSpec.None;
        v.Rotate = TransformSpec.None;
        v.Scale = TransformSpec.None;
        v.TransformOrigin = PositionSpec.Center;
        v.TransformBox = TransformBoxKind.ViewBox;
        v.PerspectiveOrigin = PositionSpec.Center;
        v.Filter = FilterSpec.None;
        v.BackdropFilter = FilterSpec.None;
        v.ClipPath = ClipPathSpec.None;
        v.Mask = MaskSpec.None;
        v.BackgroundBlendModes = [BlendMode.Normal];
    }

    private static bool EffectsEqual(in ComputedValues a, in ComputedValues b) =>
        a.Transform.Equals(b.Transform) &&
        a.Translate.Equals(b.Translate) &&
        a.Rotate.Equals(b.Rotate) &&
        a.Scale.Equals(b.Scale) &&
        a.TransformOrigin.Equals(b.TransformOrigin) &&
        a.TransformOriginZ.Equals(b.TransformOriginZ) &&
        a.TransformBox == b.TransformBox &&
        a.Perspective.Equals(b.Perspective) &&
        a.PerspectiveOrigin.Equals(b.PerspectiveOrigin) &&
        a.TransformStyle == b.TransformStyle &&
        a.BackfaceHidden == b.BackfaceHidden &&
        a.Filter.Equals(b.Filter) &&
        a.BackdropFilter.Equals(b.BackdropFilter) &&
        a.ClipPath.Equals(b.ClipPath) &&
        a.Mask.Equals(b.Mask) &&
        a.MixBlendMode == b.MixBlendMode &&
        a.Isolate == b.Isolate &&
        a.BackgroundBlendModes.AsSpan().SequenceEqual(b.BackgroundBlendModes);

    private static void EffectsHash(ref HashCode h, in ComputedValues v)
    {
        h.Add(v.Transform);
        h.Add(v.Translate);
        h.Add(v.Rotate);
        h.Add(v.Scale);
        h.Add(v.Perspective);
        h.Add(v.Filter);
        h.Add(v.BackdropFilter);
        h.Add(v.ClipPath);
        h.Add(v.MixBlendMode);
        h.Add(v.Isolate);
    }
}
