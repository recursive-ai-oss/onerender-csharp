using System.Buffers.Binary;
using OneFont;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Text;

namespace OneRender.Forms;

/// <summary>
/// The widget sizes of form controls. HTML §15.5 leaves the intrinsic size of a control to the user agent; these rules reproduce what
/// Chrome 156 does at the default form-control fonts and at other sizes, measured black-box with the probe pages in
/// <c>testdata/forms-probes</c> (see <c>docs/forms.md</c> for each rule and its evidence).
/// </summary>
internal static class FormMetrics
{
    /// <summary>The width Chrome reserves for a scrollbar inside a textarea whose <c>overflow</c> can scroll; it is added to the column width whether or not a bar shows.</summary>
    public const double ScrollbarWidth = 15;

    /// <summary>Rounds half away from zero, the way the glyph metrics are rounded to whole pixels.</summary>
    public static double Round(double x) => Math.Round(x, MidpointRounding.AwayFromZero);

    /// <summary>The primary face of a style, or null when no face is available.</summary>
    public static FontFace? PrimaryFace(IFontProvider? fonts, in ComputedValues v)
    {
        if (fonts is null)
        {
            return null;
        }

        try
        {
            return fonts.Resolve(in v).PrimaryFace?.Face;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The line spacing of the primary face at the style's size: ascent, descent and, when asked, line gap, each rounded to whole
    /// pixels before they are added. Probe: at 13.33 px Arial the text field's line is 15 px, at 16 px it is 18 px, at 30 px it is 34 px.
    /// </summary>
    public static double LineSpacing(IFontProvider? fonts, in ComputedValues v, bool withGap)
    {
        if (fonts is null || !(v.FontSize > 0))
        {
            return Round(v.FontSize * (withGap ? 1.15 : 1.1));
        }

        FontMetricsInfo m = fonts.GetMetrics(in v);
        double h = Round(m.Ascent) + Round(m.Descent);
        return withGap ? h + Round(m.LineGap) : h;
    }

    /// <summary>
    /// The average character width the <c>size</c> and <c>cols</c> attributes multiply: the face's OS/2 average at the style's size,
    /// kept when its fraction is below one half and rounded up otherwise. Probe: Liberation Sans gives 6, 8, 12 px at 10, 13.33, 20 px
    /// and 9.27 and 17.39 px at 16 and 30 px.
    /// </summary>
    public static double AverageCharWidth(IFontProvider? fonts, in ComputedValues v)
    {
        FontFace? face = PrimaryFace(fonts, in v);
        double size = v.FontSize;
        double avg = 0.5 * size;
        if (face is not null && face.UnitsPerEm > 0)
        {
            ReadOnlyMemory<byte> os2 = face.GetTable("OS/2");
            if (os2.Length >= 4)
            {
                avg = BinaryPrimitives.ReadInt16BigEndian(os2.Span[2..]) / (double)face.UnitsPerEm * size;
            }
            else
            {
                avg = fonts!.GetMetrics(in v).ZeroAdvance;
            }
        }

        return avg - Math.Floor(avg) >= 0.5 ? Math.Ceiling(avg) : avg;
    }

    /// <summary>The widest a glyph of the face can be at the style's size: the face's bounding box width.</summary>
    public static double MaxCharWidth(IFontProvider? fonts, in ComputedValues v)
    {
        FontFace? face = PrimaryFace(fonts, in v);
        if (face is null || face.UnitsPerEm <= 0)
        {
            return 2 * v.FontSize;
        }

        // head table: xMin at byte 36, xMax at byte 40 (OpenType head).
        ReadOnlySpan<byte> head = face.GetTable("head").Span;
        if (head.Length < 44)
        {
            return 2 * v.FontSize;
        }

        int xMin = BinaryPrimitives.ReadInt16BigEndian(head[36..]);
        int xMax = BinaryPrimitives.ReadInt16BigEndian(head[40..]);
        return (xMax - xMin) / (double)face.UnitsPerEm * v.FontSize;
    }

    /// <summary>
    /// The content width of a single-line text field with <paramref name="size"/> characters: (size − 1) average widths plus the
    /// widest glyph. Probe (Liberation Sans 13.33 px): size 1, 2, 10, 20, 40 give 25, 33, 97, 177, 337 px.
    /// </summary>
    public static double TextFieldWidth(IFontProvider? fonts, in ComputedValues v, int size)
    {
        double a = AverageCharWidth(fonts, in v);
        return Math.Floor(((size - 1) * a) + MaxCharWidth(fonts, in v) + 0.75);
    }

    /// <summary>The content width of a textarea with <paramref name="cols"/> columns, with room for a vertical scrollbar when <paramref name="scrollbar"/> is true.</summary>
    public static double TextAreaWidth(IFontProvider? fonts, in ComputedValues v, int cols, bool scrollbar) =>
        Round(cols * AverageCharWidth(fonts, in v)) + (scrollbar ? ScrollbarWidth : 0);

    /// <summary>The used line height of a style in px: its <c>line-height</c> when that is not <c>normal</c>, otherwise the rounded line spacing of the face.</summary>
    public static double UsedLineHeight(IFontProvider? fonts, in ComputedValues v)
    {
        Length lh = v.LineHeight;
        return lh.Kind switch
        {
            LengthKind.Number => lh.Value * v.FontSize,
            LengthKind.Px => lh.Value,
            LengthKind.Percent => lh.Value / 100 * v.FontSize,
            LengthKind.Calc => lh.Resolve(v.FontSize),
            _ => LineSpacing(fonts, in v, withGap: true),
        };
    }

    /// <summary>A padding or border length as px; percentages and keywords count as zero, because the control's size is fixed before layout knows a containing block.</summary>
    public static double Px(Length l) => l.Kind == LengthKind.Px ? l.Value : 0;

    /// <summary>The sum of padding and border on one axis, in px.</summary>
    public static double Extra(in ComputedValues v, bool horizontal) => horizontal
        ? Px(v.PaddingLeft) + Px(v.PaddingRight) + v.BorderLeftWidth + v.BorderRightWidth
        : Px(v.PaddingTop) + Px(v.PaddingBottom) + v.BorderTopWidth + v.BorderBottomWidth;
}
