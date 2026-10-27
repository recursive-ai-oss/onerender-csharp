using System.Text;

namespace OneRender.Style;

// Serialization of the paint properties, as getComputedStyle gives them.
internal static partial class ComputedValueText
{
    private static readonly string[] RepeatNames = ["repeat", "no-repeat", "space", "round"];
    private static readonly string[] BoxNames = ["border-box", "padding-box", "content-box", "text", "border-area"];
    private static readonly string[] AttachmentNames = ["scroll", "fixed", "local"];
    private static readonly string[] DecorationStyleNames = ["solid", "double", "dotted", "dashed", "wavy"];
    private static readonly string[] ObjectFitNames = ["fill", "contain", "cover", "none", "scale-down"];
    private static readonly string[] BorderRepeatNames = ["stretch", "repeat", "round", "space"];

    private static string? PaintText(PropertyId id, in ComputedValues v)
    {
        switch (id)
        {
            case PropertyId.BackgroundImage: return JoinEach(v.Background.Images, ImageText);
            case PropertyId.BackgroundRepeat: return JoinEach(v.Background.Repeats, r => r.X == r.Y ? RepeatNames[(int)r.X] : r is (BackgroundRepeatKind.Repeat, BackgroundRepeatKind.NoRepeat) ? "repeat-x" : r is (BackgroundRepeatKind.NoRepeat, BackgroundRepeatKind.Repeat) ? "repeat-y" : RepeatNames[(int)r.X] + " " + RepeatNames[(int)r.Y]);
            case PropertyId.BackgroundPositionX: return JoinEach(v.Background.PositionsX, AxisText);
            case PropertyId.BackgroundPositionY: return JoinEach(v.Background.PositionsY, AxisText);
            case PropertyId.BackgroundSize: return JoinEach(v.Background.Sizes, s => s.Kind == BackgroundSizeKind.Cover ? "cover" : s.Kind == BackgroundSizeKind.Contain ? "contain" : s.Height.IsAuto ? s.Width.ToCss() : s.Width.ToCss() + " " + s.Height.ToCss());
            case PropertyId.BackgroundOrigin: return JoinEach(v.Background.Origins, b => BoxNames[(int)b]);
            case PropertyId.BackgroundClip: return JoinEach(v.Background.Clips, b => BoxNames[(int)b]);
            case PropertyId.BackgroundAttachment: return JoinEach(v.Background.Attachments, a => AttachmentNames[(int)a]);
            case PropertyId.BorderImageSource: return ImageText(v.BorderImage.Source);
            case PropertyId.BorderImageSlice: return Sides(v.BorderImage.Slice) + (v.BorderImage.Fill ? " fill" : string.Empty);
            case PropertyId.BorderImageWidth: return Sides(v.BorderImage.Width);
            case PropertyId.BorderImageOutset: return Sides(v.BorderImage.Outset);
            case PropertyId.BorderImageRepeat: return v.BorderImage.RepeatX == v.BorderImage.RepeatY ? BorderRepeatNames[(int)v.BorderImage.RepeatX] : BorderRepeatNames[(int)v.BorderImage.RepeatX] + " " + BorderRepeatNames[(int)v.BorderImage.RepeatY];
            case PropertyId.BoxShadow: return ShadowsText(v.BoxShadow, true);
            case PropertyId.TextShadow: return ShadowsText(v.TextShadow, false);
            case PropertyId.OutlineWidth: return CssText.Px(v.OutlineWidth);
            case PropertyId.OutlineStyle: return v.OutlineAuto ? "auto" : BorderStyles[(int)v.OutlineStyle];
            case PropertyId.OutlineColor: return v.OutlineColor.ToCss();
            case PropertyId.OutlineOffset: return CssText.Px(v.OutlineOffset);
            case PropertyId.TextDecorationLine: return LineText(v.TextDecorationLine);
            case PropertyId.TextDecorationStyle: return DecorationStyleNames[(int)v.TextDecorationStyle];
            case PropertyId.TextDecorationColor: return v.TextDecorationColor.ToCss();
            case PropertyId.TextDecorationThickness: return v.TextDecorationThickness.Kind == LengthKind.Normal ? "from-font" : v.TextDecorationThickness.ToCss();
            case PropertyId.TextUnderlineOffset: return v.TextUnderlineOffset.ToCss();
            case PropertyId.TextUnderlinePosition: return v.TextUnderlinePosition switch { TextUnderlinePositionKind.FromFont => "from-font", TextUnderlinePositionKind.Under => "under", _ => "auto" };
            case PropertyId.ObjectFit: return ObjectFitNames[(int)v.ObjectFit];
            case PropertyId.ObjectPosition: return PositionText(v.ObjectPosition);
            case PropertyId.ImageRendering: return v.ImageRendering switch { ImageRenderingKind.Smooth => "smooth", ImageRenderingKind.CrispEdges => "crisp-edges", ImageRenderingKind.Pixelated => "pixelated", _ => "auto" };
            case PropertyId.ListStyleImage: return ImageText(v.ListStyleImage);
            default: return EffectsText(id, in v);
        }
    }

    private static string JoinEach<T>(T[] items, Func<T, string> f)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(f(items[i]));
        }

        return sb.ToString();
    }

    private static string Sides(Length[] four)
    {
        // The shortest form that gives the same four sides (CSSOM, serializing a CSS value).
        string t = four[0].ToCss(), r = four[1].ToCss(), b = four[2].ToCss(), l = four[3].ToCss();
        if (l != r)
        {
            return t + " " + r + " " + b + " " + l;
        }

        if (b != t)
        {
            return t + " " + r + " " + b;
        }

        return r != t ? t + " " + r : t;
    }

    private static string AxisText(PositionAxis a)
    {
        if (!a.FromEnd)
        {
            return a.Offset.ToCss();
        }

        return a.Offset.Kind == LengthKind.Percent ? CssText.Number(100 - a.Offset.Value) + "%" : "calc(100% - " + a.Offset.ToCss() + ")";
    }

    private static string PositionText(PositionSpec p) => AxisText(p.X) + " " + AxisText(p.Y);

    private static string LineText(TextDecorationLines l)
    {
        if (l == TextDecorationLines.None)
        {
            return "none";
        }

        var parts = new List<string>(3);
        if ((l & TextDecorationLines.Underline) != 0)
        {
            parts.Add("underline");
        }

        if ((l & TextDecorationLines.Overline) != 0)
        {
            parts.Add("overline");
        }

        if ((l & TextDecorationLines.LineThrough) != 0)
        {
            parts.Add("line-through");
        }

        return string.Join(' ', parts);
    }

    private static string ImageText(ImageSpec i) => i.Kind switch
    {
        ImageSourceKind.Url => "url(\"" + i.Url + "\")",
        ImageSourceKind.Gradient => GradientText(i.Gradient!),
        _ => "none",
    };

    private static string GradientText(GradientSpec g)
    {
        var sb = new StringBuilder();
        sb.Append(g.Repeating ? "repeating-" : string.Empty);
        sb.Append(g.Kind switch { GradientKind.Linear => "linear-gradient(", GradientKind.Radial => "radial-gradient(", _ => "conic-gradient(" });
        bool first = true;
        foreach (GradientStopSpec s in g.Stops)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            if (!s.IsHint)
            {
                sb.Append(s.Color.ToCss());
                if (!s.Position.IsAuto)
                {
                    sb.Append(' ');
                }
            }

            if (!s.Position.IsAuto)
            {
                sb.Append(s.Position.Kind == LengthKind.Number ? CssText.Number(s.Position.Value * 180 / Math.PI) + "deg" : s.Position.ToCss());
            }
        }

        return sb.Append(')').ToString();
    }

    private static string ShadowsText(ShadowList list, bool box)
    {
        if (list.Count == 0)
        {
            return "none";
        }

        return JoinEach(list.Items, s =>
            s.Color.ToCss() + " " + CssText.Px(s.OffsetX) + " " + CssText.Px(s.OffsetY) + " " + CssText.Px(s.Blur)
            + (box ? " " + CssText.Px(s.Spread) : string.Empty) + (s.Inset ? " inset" : string.Empty));
    }
}
