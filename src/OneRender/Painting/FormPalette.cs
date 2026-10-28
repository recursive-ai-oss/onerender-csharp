namespace OneRender.Painting;

/// <summary>
/// The colors Chrome 156 paints native form controls with, under the light and the dark color scheme (CSS Color Adjust 1 §2). The
/// specifications leave the look of a native widget to the user agent; every value here was read off screenshots of Chrome for Testing
/// with the probe pages in <c>testdata/forms-probes/scheme</c> (<c>docs/forms.md</c> lists them with the evidence).
/// </summary>
internal sealed class FormPalette
{
    private static PaintColor Gray(int v, float a = 1f) => new(v / 255f, v / 255f, v / 255f, a);

    private static PaintColor Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    /// <summary>The palette of the light scheme.</summary>
    public static readonly FormPalette Light = new(dark: false);

    /// <summary>The palette of the dark scheme.</summary>
    public static readonly FormPalette Dark = new(dark: true);

    /// <summary>The palette of a scheme.</summary>
    public static FormPalette For(bool dark) => dark ? Dark : Light;

    private FormPalette(bool dark)
    {
        IsDark = dark;
        Face = dark ? Gray(59) : Gray(255);
        Border = dark ? Gray(133) : Gray(118);
        DisabledFace = dark ? Gray(59) : Gray(248);
        DisabledBorder = dark ? Gray(98) : Gray(209);
        DisabledAccent = dark ? Gray(117) : Gray(209);
        DisabledMark = dark ? Gray(59) : Gray(237);
        AutoAccent = dark ? Rgb(153, 200, 255) : Rgb(0, 117, 255);
        MeterFill = dark ? Gray(59) : Gray(239);
        DisabledTrackFill = dark ? Gray(54) : Gray(250);
        DisabledTrackBorder = dark ? Gray(98) : Gray(210);
        DisabledRangeAccent = dark ? Gray(117) : Gray(203);
        MeterBorder = dark ? Gray(133) : Gray(203);
        MeterOptimum = dark ? Rgb(116, 179, 116) : Rgb(16, 124, 16);
        MeterSuboptimum = dark ? Rgb(242, 200, 18) : Rgb(255, 185, 0);
        MeterBad = dark ? Rgb(233, 143, 109) : Rgb(216, 59, 1);
        ResizerDark = dark ? Gray(23) : Gray(102);
        ResizerLight = dark ? Gray(176) : Gray(255);
        FocusInner = dark ? Gray(238) : Gray(16);
        FocusOuter = dark ? Gray(16) : Gray(255);
        MarkOnLight = Gray(59);
        MarkOnDark = Gray(255);
    }

    /// <summary>True for the dark palette.</summary>
    public bool IsDark { get; }

    /// <summary>The face of a field, a drop-down, a checkbox and a radio button (the system color <c>Field</c>).</summary>
    public PaintColor Face { get; }

    /// <summary>The 1 px border of a field, a checkbox and a radio button.</summary>
    public PaintColor Border { get; }

    /// <summary>The face of a disabled checkbox or radio button.</summary>
    public PaintColor DisabledFace { get; }

    /// <summary>The border of a disabled checkbox or radio button.</summary>
    public PaintColor DisabledBorder { get; }

    /// <summary>The fill of a disabled checked checkbox or radio button; <c>accent-color</c> does not apply to a disabled control.</summary>
    public PaintColor DisabledAccent { get; }

    /// <summary>The check mark of a disabled checked checkbox.</summary>
    public PaintColor DisabledMark { get; }

    /// <summary>The accent of <c>accent-color: auto</c>.</summary>
    public PaintColor AutoAccent { get; }

    /// <summary>The unfilled part of a meter's track.</summary>
    public PaintColor MeterFill { get; }

    /// <summary>The track of a disabled range.</summary>
    public PaintColor DisabledTrackFill { get; }

    /// <summary>The border of a disabled range track.</summary>
    public PaintColor DisabledTrackBorder { get; }

    /// <summary>The filled part and the thumb of a disabled range.</summary>
    public PaintColor DisabledRangeAccent { get; }

    /// <summary>The border of a meter's track.</summary>
    public PaintColor MeterBorder { get; }

    /// <summary>A meter whose value is in the optimum region.</summary>
    public PaintColor MeterOptimum { get; }

    /// <summary>A meter whose value is in the suboptimum region.</summary>
    public PaintColor MeterSuboptimum { get; }

    /// <summary>A meter whose value is in the even-less-good region.</summary>
    public PaintColor MeterBad { get; }

    /// <summary>The darker of the two diagonal strokes of a textarea's resize handle.</summary>
    public PaintColor ResizerDark { get; }

    /// <summary>The lighter stroke of a resize handle, one pixel to the right of the darker.</summary>
    public PaintColor ResizerLight { get; }

    /// <summary>The 2 px ring of <c>outline: auto</c> when the outline color is left at its default.</summary>
    public PaintColor FocusInner { get; }

    /// <summary>The 1 px ring just outside the focus ring.</summary>
    public PaintColor FocusOuter { get; }

    /// <summary>The mark on an accent that is light enough for it (a check mark, the bar of an indeterminate box).</summary>
    public PaintColor MarkOnLight { get; }

    /// <summary>The mark on an accent dark enough for white.</summary>
    public PaintColor MarkOnDark { get; }

    // WCAG 2.x relative luminance of an sRGB color given in 0 to 1.
    private static double Linear(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    /// <summary>The relative luminance (WCAG 2.x) of a color.</summary>
    public static double Luminance(PaintColor c) => (0.2126 * Linear(c.R)) + (0.7152 * Linear(c.G)) + (0.0722 * Linear(c.B));

    private static double Contrast(double a, double b) => a > b ? (a + 0.05) / (b + 0.05) : (b + 0.05) / (a + 0.05);

    // The two looks a range track, progress bar and check mark can take (measured from Chrome 156 with grey and saturated accents): the
    // scheme picks one, and a custom accent that has less than 3:1 contrast with it flips to the other, whatever the scheme.
    private static readonly double LightTrackLuminance = Luminance(Gray(239));
    private static readonly double DarkTrackLuminance = Luminance(Gray(59));

    /// <summary>
    /// Whether the track of a range or progress bar takes its dark look (<c>#3b3b3b</c> face, <c>#858585</c> border and ring) for this accent.
    /// The light scheme prefers the light track (<c>#efefef</c>) and uses the dark one when the accent's contrast with it is under 3;
    /// the dark scheme prefers the dark track and uses the light one when the accent's contrast with it is under 3.
    /// </summary>
    public bool TrackIsDark(PaintColor accent)
    {
        double l = Luminance(accent);
        return IsDark ? Contrast(l, DarkTrackLuminance) >= 3.0 : Contrast(l, LightTrackLuminance) < 3.0;
    }

    /// <summary>The unfilled part of a range track or progress bar for an accent.</summary>
    public PaintColor TrackFillFor(PaintColor accent) => TrackIsDark(accent) ? Gray(59) : Gray(239);

    /// <summary>The border of a range track or progress bar for an accent.</summary>
    public PaintColor TrackBorderFor(PaintColor accent) => TrackIsDark(accent) ? Gray(133) : Gray(178);

    /// <summary>
    /// Whether the mark drawn on <paramref name="accent"/> is white. The light scheme prefers a white mark, the dark one the dark mark
    /// (<c>#3b3b3b</c>); a mark whose contrast with the accent is under 3 is swapped for the other.
    /// </summary>
    public bool MarkIsWhite(PaintColor accent)
    {
        double l = Luminance(accent);
        return IsDark ? Contrast(l, DarkTrackLuminance) < 3.0 : Contrast(l, 1.0) >= 3.0;
    }

    /// <summary>The mark color on an accent-colored box.</summary>
    public PaintColor MarkOn(PaintColor accent) => MarkIsWhite(accent) ? MarkOnDark : MarkOnLight;

    /// <summary>
    /// The 1 px ring that the border of a range track or progress bar leaves over its filled part. Measured from Chrome 156: half the accent
    /// and half <c>#767676</c> (8-bit channels, rounded down) on a light track, <c>#858585</c> on a dark one.
    /// </summary>
    public PaintColor FilledRing(PaintColor accent)
    {
        if (TrackIsDark(accent))
        {
            return Gray(133);
        }

        return new PaintColor(((ToByte(accent.R) + 118) >> 1) / 255f, ((ToByte(accent.G) + 118) >> 1) / 255f, ((ToByte(accent.B) + 118) >> 1) / 255f, 1f);
    }

    private static int ToByte(float c) => Math.Clamp((int)MathF.Round(c * 255f), 0, 255);
}
