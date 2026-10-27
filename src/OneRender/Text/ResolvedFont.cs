using OneFont;

namespace OneRender.Text;

/// <summary>Which font measurement <c>font-size-adjust</c> compares (CSS Fonts 5, <c>font-size-adjust</c>).</summary>
public enum FontSizeAdjustMetric : byte
{
    /// <summary>The x-height (<c>ex-height</c>, the default metric).</summary>
    ExHeight,

    /// <summary>The cap height.</summary>
    CapHeight,

    /// <summary>The advance of the digit zero (<c>ch-width</c>).</summary>
    ChWidth,

    /// <summary>The advance of the water ideograph U+6C34 (<c>ic-width</c>).</summary>
    IcWidth,
}

/// <summary>
/// The measurements line layout needs from a face at one size, in CSS pixels. Vertical values are distances from the alphabetic
/// baseline, positive in the direction named. Computed from the font's own tables, or from the <c>@font-face</c> override
/// descriptors when the rule gave them.
/// </summary>
/// <remarks>
/// <see cref="Ascent"/>, <see cref="Descent"/> and <see cref="LineGap"/> come from the horizontal header, or from the OS/2
/// typographic metrics when the font sets USE_TYPO_METRICS or has no horizontal header values, or from the Windows metrics as
/// the last resort (CSS Inline 3, "Metrics from the font"). They are not rounded: a layout that wants whole pixels rounds them.
/// </remarks>
public readonly struct FontMetricsInfo
{
    internal FontMetricsInfo(float size, float ascent, float descent, float lineGap, float xHeight, float capHeight, float underlinePosition, float underlineThickness, float strikeoutPosition, float strikeoutThickness, float zeroAdvance, float ideographicAdvance)
    {
        Size = size;
        Ascent = ascent;
        Descent = descent;
        LineGap = lineGap;
        XHeight = xHeight;
        CapHeight = capHeight;
        UnderlinePosition = underlinePosition;
        UnderlineThickness = underlineThickness;
        StrikeoutPosition = strikeoutPosition;
        StrikeoutThickness = strikeoutThickness;
        ZeroAdvance = zeroAdvance;
        IdeographicAdvance = ideographicAdvance;
    }

    /// <summary>The font size the values are scaled to.</summary>
    public float Size { get; }

    /// <summary>The distance from the baseline up to the top of the face's line box. Positive.</summary>
    public float Ascent { get; }

    /// <summary>The distance from the baseline down to the bottom of the face's line box. Positive.</summary>
    public float Descent { get; }

    /// <summary>The extra space the font asks for between lines (<c>line-height: normal</c> adds it). Not negative.</summary>
    public float LineGap { get; }

    /// <summary>The height of a lower-case x above the baseline. Measured from the glyph when the font does not say.</summary>
    public float XHeight { get; }

    /// <summary>The height of a capital H above the baseline. Measured from the glyph when the font does not say.</summary>
    public float CapHeight { get; }

    /// <summary>The distance from the baseline down to the center of the underline. Positive below the baseline.</summary>
    public float UnderlinePosition { get; }

    /// <summary>The thickness of the underline.</summary>
    public float UnderlineThickness { get; }

    /// <summary>The distance from the baseline up to the center of the strikethrough. Positive above the baseline.</summary>
    public float StrikeoutPosition { get; }

    /// <summary>The thickness of the strikethrough.</summary>
    public float StrikeoutThickness { get; }

    /// <summary>The advance of the digit zero, the <c>ch</c> unit. 0 when the face has no zero glyph.</summary>
    public float ZeroAdvance { get; }

    /// <summary>The advance of the water ideograph U+6C34, the <c>ic</c> unit. 0 when the face has no such glyph.</summary>
    public float IdeographicAdvance { get; }

    /// <summary>Ascent plus descent plus line gap: the content height <c>line-height: normal</c> uses.</summary>
    public float NormalLineHeight => Ascent + Descent + LineGap;

    /// <summary>The measurement for a <c>font-size-adjust</c> metric.</summary>
    /// <param name="metric">Which measurement.</param>
    /// <returns>The value in pixels at <see cref="Size"/>; 0 when the face lacks it.</returns>
    public float Measure(FontSizeAdjustMetric metric) => metric switch
    {
        FontSizeAdjustMetric.CapHeight => CapHeight,
        FontSizeAdjustMetric.ChWidth => ZeroAdvance,
        FontSizeAdjustMetric.IcWidth => IdeographicAdvance,
        _ => XHeight,
    };

    /// <summary>
    /// The size to use for <c>font-size-adjust: &lt;metric&gt; &lt;adjust&gt;</c>: the specified size times the adjust value divided by the
    /// face's aspect value (its measurement divided by its size). Returns <paramref name="size"/> when the face has no such measurement.
    /// </summary>
    /// <param name="metric">The metric named by the property.</param>
    /// <param name="adjust">The number from the property (for example 0.5).</param>
    /// <param name="size">The specified font size.</param>
    /// <returns>The adjusted size in pixels.</returns>
    public float AdjustedSize(FontSizeAdjustMetric metric, float adjust, float size)
    {
        float m = Measure(metric);
        if (!(m > 0) || !(Size > 0) || !(adjust > 0))
        {
            return size;
        }

        return size * adjust / (m / Size);
    }
}

/// <summary>
/// A face chosen for a request: the font, plus what the paint step has to fake because the family lacked the exact face
/// (synthetic bold and slant, CSS Fonts 4 §5.2 and the <c>font-synthesis</c> property).
/// </summary>
public sealed class ResolvedFace
{
    internal ResolvedFace(FaceEntry entry, bool syntheticBold, bool syntheticItalic, float slant)
    {
        Entry = entry;
        SyntheticBold = syntheticBold;
        SyntheticItalic = syntheticItalic;
        SyntheticSlant = syntheticItalic ? slant : 0;
    }

    internal FaceEntry Entry { get; }

    /// <summary>The font. Never null for a face that appears in a <see cref="ShapedText"/>.</summary>
    public FontFace Face => Entry.Face ?? throw new InvalidOperationException("The face has not been loaded.");

    /// <summary>The family name the face was registered under.</summary>
    public string FamilyName => Entry.Family;

    /// <summary>A number that identifies the face within its collection; stable for the life of the collection.</summary>
    public int FaceId => Entry.Id;

    /// <summary>The weight range the face declares (for a plain font, its single weight).</summary>
    public (float Min, float Max) WeightRange => (Entry.WeightMin, Entry.WeightMax);

    /// <summary>True when the request asked for bold and the face is lighter: the painter should embolden the outlines.</summary>
    public bool SyntheticBold { get; }

    /// <summary>True when the request asked for italic or oblique and the face is upright: the painter should shear the outlines.</summary>
    public bool SyntheticItalic { get; }

    /// <summary>The shear angle in degrees to the right when <see cref="SyntheticItalic"/>; otherwise 0.</summary>
    public float SyntheticSlant { get; }

    /// <summary>The <c>size-adjust</c> factor applied to glyphs and metrics (1 for fonts not from <c>@font-face</c>).</summary>
    public float SizeAdjust => Entry.SizeAdjust;

    /// <summary>The <c>font-display</c> value of the <c>@font-face</c> rule; recorded only.</summary>
    public FontDisplay Display => Entry.Display;

    /// <summary>Whether the face came from an <c>@font-face</c> rule.</summary>
    public bool IsWebFont => Entry.Origin == FaceOrigin.WebFont;

    /// <summary>The face's measurements at a size.</summary>
    /// <param name="size">The font size in CSS pixels.</param>
    /// <returns>The scaled metrics.</returns>
    public FontMetricsInfo GetMetrics(float size) => Entry.GetMetrics(size);
}

/// <summary>
/// The faces that match one <see cref="FontRequest"/> (ignoring size), in order of preference, ready for per-character
/// fallback. Obtained from <see cref="FontCatalog.Resolve"/>; equal requests give the same object until the collection
/// changes.
/// </summary>
public sealed class ResolvedFontList
{
    internal ResolvedFontList(FontCatalog owner, FontRequest request, FontSlot[] slots, int listedSlots, int id)
    {
        ListedSlots = listedSlots;
        Owner = owner;
        Request = request;
        Slots = slots;
        Id = id;
        Options = request.Features.Features.Length == 0 && request.Features.Kerning == KerningMode.Normal && request.Language is null
            ? null
            : BuildOptions(request);
    }

    internal FontCatalog Owner { get; }

    internal FontRequest Request { get; }

    internal FontSlot[] Slots { get; }

    internal int Id { get; }

    // The first slots come from the request's own family list; the rest are the default faces.
    internal int ListedSlots { get; }

    internal ShapingOptions? Options { get; }

    /// <summary>
    /// True when a font file turned out to be unusable after this list was made. The list still works (the bad face is skipped),
    /// but <see cref="FontCatalog.Resolve"/> now returns a fresh list that matches without the bad face.
    /// </summary>
    public bool IsStale { get; internal set; }

    private ResolvedFace? _primary;
    private bool _primaryDone;

    /// <summary>
    /// The first available face (CSS Fonts 4 §5.1): the first listed family with a usable face whose <c>unicode-range</c> includes
    /// U+0020, so a family limited to other characters is skipped. When no listed family qualifies, the first family that has a glyph
    /// for a space or any usable face. Null when no family matched and the collection is empty.
    /// </summary>
    public ResolvedFace? PrimaryFace
    {
        get
        {
            if (!_primaryDone)
            {
                _primary = FindPrimary();
                _primaryDone = !IsStale || _primary is not null;
            }

            return _primary;
        }
    }

    /// <summary>The measurements of the first available face at a size; all zeros when there is none.</summary>
    /// <param name="size">The font size in CSS pixels.</param>
    /// <returns>The metrics.</returns>
    public FontMetricsInfo GetMetrics(float size) => PrimaryFace?.GetMetrics(size) ?? default;

    private ResolvedFace? FindPrimary()
    {
        // CSS Fonts 4 §5.1: a listed family whose faces all leave U+0020 outside their unicode-range is not the first available
        // font, even when it comes first in the list. Only the author's own families count here, not the default faces after them.
        for (int i = 0; i < ListedSlots && i < Slots.Length; i++)
        {
            ResolvedFace? f = Slots[i].InRangeFor(' ', this);
            if (f is not null)
            {
                return f;
            }
        }

        foreach (FontSlot slot in Slots)
        {
            ResolvedFace? f = slot.Select(' ', this);
            if (f is not null)
            {
                return f;
            }

            // A family whose faces lack a space: take any usable face of the family.
            f = slot.AnyUsable(this);
            if (f is not null)
            {
                return f;
            }
        }

        return null;
    }

    private static ShapingOptions BuildOptions(FontRequest request)
    {
        var o = new ShapingOptions { Language = request.Language, Kerning = request.Features.Kerning };
        foreach (FontFeature f in request.Features.Features)
        {
            o.Features.Add(f);
        }

        return o;
    }
}

// One family of a request, narrowed to the faces that can serve it (the composite face of CSS Fonts 4 §5.2).
internal sealed class FontSlot(FaceEntry[] group, ResolvedFace[] faces, bool isGeneric)
{
    public readonly FaceEntry[] Group = group;
    public readonly ResolvedFace[] Faces = faces;
    public readonly bool IsGeneric = isGeneric;

    // The face for one code point: the last-defined face whose unicode-range contains it and whose font has a glyph.
    public ResolvedFace? Select(int codePoint, ResolvedFontList list)
    {
        for (int i = 0; i < Group.Length; i++)
        {
            FaceEntry e = Group[i];
            if (!e.InRange(codePoint))
            {
                continue;
            }

            if (!e.EnsureLoaded(list.Owner))
            {
                list.IsStale = true;
                continue;
            }

            if (e.HasGlyph(codePoint, list.Owner))
            {
                return Faces[i];
            }
        }

        return null;
    }

    // The face whose unicode-range includes the code point and that loads, whether or not it has a glyph for it.
    public ResolvedFace? InRangeFor(int codePoint, ResolvedFontList list)
    {
        for (int i = 0; i < Group.Length; i++)
        {
            if (!Group[i].InRange(codePoint))
            {
                continue;
            }

            if (Group[i].EnsureLoaded(list.Owner))
            {
                // The answer doesn't matter here, but a lookup exposes a face whose tables turn out damaged and marks it failed.
                _ = Group[i].HasGlyph(codePoint, list.Owner);
                if (Group[i].EnsureLoaded(list.Owner))
                {
                    return Faces[i];
                }
            }

            list.IsStale = true;
        }

        return null;
    }

    public ResolvedFace? AnyUsable(ResolvedFontList list)
    {
        for (int i = 0; i < Group.Length; i++)
        {
            if (Group[i].EnsureLoaded(list.Owner))
            {
                return Faces[i];
            }

            list.IsStale = true;
        }

        return null;
    }
}
