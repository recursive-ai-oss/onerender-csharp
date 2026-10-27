using System.Text;
using OneRender.Painting;

namespace OneRender.Style;

internal static partial class ComputedValueText
{
    private static readonly string[] BlendNames =
    [
        "normal", "multiply", "screen", "overlay", "darken", "lighten", "color-dodge", "color-burn", "hard-light", "soft-light",
        "difference", "exclusion", "hue", "saturation", "color", "luminosity", "plus-lighter",
    ];

    private static readonly string[] TransformBoxNames = ["content-box", "border-box", "fill-box", "stroke-box", "view-box"];

    private static string? EffectsText(PropertyId id, in ComputedValues v)
    {
        switch (id)
        {
            case PropertyId.Transform: return v.Transform.Text;
            case PropertyId.Translate: return v.Translate.Text;
            case PropertyId.Rotate: return v.Rotate.Text;
            case PropertyId.Scale: return v.Scale.Text;
            case PropertyId.TransformOrigin: return PositionText(v.TransformOrigin) + (v.TransformOriginZ != 0 ? " " + CssText.Px(v.TransformOriginZ) : string.Empty);
            case PropertyId.TransformBox: return TransformBoxNames[(int)v.TransformBox];
            case PropertyId.Perspective: return v.Perspective > 0 ? CssText.Px(v.Perspective) : "none";
            case PropertyId.PerspectiveOrigin: return PositionText(v.PerspectiveOrigin);
            case PropertyId.TransformStyle: return v.TransformStyle == TransformStyleKind.Flat ? "flat" : "preserve-3d";
            case PropertyId.BackfaceVisibility: return v.BackfaceHidden ? "hidden" : "visible";
            case PropertyId.Filter: return v.Filter.Text;
            case PropertyId.BackdropFilter: return v.BackdropFilter.Text;
            case PropertyId.ClipPath: return v.ClipPath.Text;
            case PropertyId.MaskImage: return JoinEach(v.Mask.Images, ImageText);
            case PropertyId.MaskMode: return JoinEach(v.Mask.Modes, m => m == MaskModeKind.Alpha ? "alpha" : m == MaskModeKind.Luminance ? "luminance" : "match-source");
            case PropertyId.MaskRepeat: return JoinEach(v.Mask.Repeats, r => r.X + " " + r.Y);
            case PropertyId.MaskPosition:
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < v.Mask.PositionsX.Length; i++)
                    {
                        sb.Append(i > 0 ? ", " : string.Empty).Append(AxisText(v.Mask.PositionsX[i])).Append(' ').Append(AxisText(v.Mask.PositionsY[Math.Min(i, v.Mask.PositionsY.Length - 1)]));
                    }

                    return sb.ToString();
                }

            case PropertyId.MaskSize: return JoinEach(v.Mask.Sizes, s => s.Kind + " " + s.Width.ToCss() + " " + s.Height.ToCss());
            case PropertyId.MaskOrigin: return JoinEach(v.Mask.Origins, b => b.ToString());
            case PropertyId.MaskClip: return JoinEach(v.Mask.Clips, b => b.ToString());
            case PropertyId.MaskComposite: return JoinEach(v.Mask.Composites, c => c.ToString().ToLowerInvariant());
            case PropertyId.MixBlendMode: return BlendNames[(int)v.MixBlendMode];
            case PropertyId.Isolation: return v.Isolate ? "isolate" : "auto";
            case PropertyId.BackgroundBlendMode: return JoinEach(v.BackgroundBlendModes, b => BlendNames[(int)b]);
            default: return null;
        }
    }
}
