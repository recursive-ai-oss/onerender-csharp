using OneRender.Style;
using System.Diagnostics.CodeAnalysis;
using OneFont;

namespace OneRender.Text;

/// <summary>
/// The host's way to supply font files for <c>@font-face</c> rules. The engine never fetches: it asks the resolver, which
/// may read a file, a bundle, a cache or the network, however the host likes.
/// </summary>
public interface IFontResourceResolver
{
    /// <summary>Returns the bytes of a font resource.</summary>
    /// <param name="url">The URL exactly as written in the <c>src</c> descriptor (it may be relative).</param>
    /// <param name="baseUrl">The location of the stylesheet that holds the rule, or null. Resolve <paramref name="url"/> against it.</param>
    /// <returns>The file contents, or null when the resource does not exist or cannot be read.</returns>
    byte[]? LoadFont(string url, string? baseUrl);
}

/// <summary>Maps generic keywords and well-known family names to a registered family. The fonts package supplies one for its fonts.</summary>
public interface IFontFamilyAliases
{
    /// <summary>Resolves a name.</summary>
    /// <param name="name">A family name or generic keyword (<c>sans-serif</c>, <c>system-ui</c>, <c>Arial</c>); compare ASCII case-insensitively.</param>
    /// <param name="family">The registered family that stands in for it.</param>
    /// <returns>True when there is a mapping.</returns>
    bool TryResolve(ReadOnlySpan<char> name, [NotNullWhen(true)] out string? family);
}

/// <summary>A font installed on the host system, as reported by an <see cref="ISystemFontProvider"/>.</summary>
public sealed class SystemFontFace
{
    private readonly Func<byte[]?> _load;

    /// <summary>Describes an installed face.</summary>
    /// <param name="family">The family name.</param>
    /// <param name="weight">The weight, 1 to 1000.</param>
    /// <param name="style">The style kind.</param>
    /// <param name="stretch">The stretch as a percentage.</param>
    /// <param name="fullName">The full font name, used by <c>local()</c>; may be null.</param>
    /// <param name="postScriptName">The PostScript name, used by <c>local()</c>; may be null.</param>
    /// <param name="load">Reads the font file; returns null when it cannot be read.</param>
    public SystemFontFace(string family, float weight, FontStyleKind style, float stretch, string? fullName, string? postScriptName, Func<byte[]?> load)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(load);
        Family = family;
        Weight = weight;
        Style = style;
        Stretch = stretch;
        FullName = fullName;
        PostScriptName = postScriptName;
        _load = load;
    }

    /// <summary>The family name.</summary>
    public string Family { get; }

    /// <summary>The weight.</summary>
    public float Weight { get; }

    /// <summary>The style kind.</summary>
    public FontStyleKind Style { get; }

    /// <summary>The stretch as a percentage.</summary>
    public float Stretch { get; }

    /// <summary>The full name.</summary>
    public string? FullName { get; }

    /// <summary>The PostScript name.</summary>
    public string? PostScriptName { get; }

    internal byte[]? Load() => _load();
}

/// <summary>
/// Opt-in access to the fonts installed on the host. Off by default: output that uses only the packaged fonts is identical on
/// every machine, and a provider gives that up. The host implements this; the engine never reads the file system itself.
/// </summary>
public interface ISystemFontProvider
{
    /// <summary>Lists the installed faces of a family.</summary>
    /// <param name="family">The family name as written in <c>font-family</c>.</param>
    /// <returns>The faces; empty when the family is not installed.</returns>
    IReadOnlyList<SystemFontFace> GetFaces(string family);

    /// <summary>Finds an installed face by full name or PostScript name, for <c>local()</c>.</summary>
    /// <param name="name">The name inside <c>local()</c>.</param>
    /// <returns>The face, or null.</returns>
    SystemFontFace? FindByName(string name);

    /// <summary>Finds an installed face that has a glyph for a character none of the requested families covers.</summary>
    /// <param name="codePoint">The Unicode scalar value.</param>
    /// <returns>A face, or null.</returns>
    SystemFontFace? FindFallback(int codePoint);
}

internal enum FaceOrigin : byte
{
    Registered,
    WebFont,
    System,
}

internal sealed class FaceEntry
{
    private enum LoadState : byte
    {
        Unloaded,
        Loaded,
        Failed,
    }

    private LoadState _state;
    private byte[]? _data;
    private FontFaceRule? _rule;
    private SystemFontFace? _system;
    private FontMetricsBase? _metrics;

    public FaceEntry(int id, string family, FaceOrigin origin)
    {
        Id = id;
        Family = family;
        Origin = origin;
    }

    public int Id { get; }

    public string Family { get; }

    public FaceOrigin Origin { get; }

    public float WeightMin = 400;
    public float WeightMax = 400;
    public float StretchMin = 100;
    public float StretchMax = 100;
    public FontStyleKind Style;
    public float ObliqueMin = 14;
    public float ObliqueMax = 14;
    public CodePointRange[]? Ranges;
    public float SizeAdjust = 1;
    public float AscentOverride = float.NaN;
    public float DescentOverride = float.NaN;
    public float LineGapOverride = float.NaN;
    public FontDisplay Display;

    public FontFace? Face { get; private set; }

    public static FaceEntry FromFace(int id, string family, FontFace face, float weight, FontStyleKind style, float stretch)
    {
        var e = new FaceEntry(id, family, FaceOrigin.Registered) { Face = face, _state = LoadState.Loaded };
        e.SetDescriptors(weight, style, stretch);
        return e;
    }

    public static FaceEntry FromBytes(int id, string family, byte[] data, float weight, FontStyleKind style, float stretch)
    {
        var e = new FaceEntry(id, family, FaceOrigin.Registered) { _data = data };
        e.SetDescriptors(weight, style, stretch);
        return e;
    }

    public static FaceEntry FromRule(int id, FontFaceRule rule)
    {
        var e = new FaceEntry(id, rule.Family, FaceOrigin.WebFont)
        {
            _rule = rule,
            WeightMin = rule.WeightMin,
            WeightMax = rule.WeightMax,
            StretchMin = rule.StretchMin,
            StretchMax = rule.StretchMax,
            Style = rule.Style,
            ObliqueMin = rule.ObliqueMin,
            ObliqueMax = rule.ObliqueMax,
            SizeAdjust = rule.SizeAdjust,
            AscentOverride = rule.AscentOverride,
            DescentOverride = rule.DescentOverride,
            LineGapOverride = rule.LineGapOverride,
            Display = rule.Display,
        };
        if (rule.UnicodeRanges.Count > 0)
        {
            e.Ranges = [.. rule.UnicodeRanges];
        }

        return e;
    }

    public static FaceEntry FromSystem(int id, SystemFontFace face)
    {
        var e = new FaceEntry(id, face.Family, FaceOrigin.System) { _system = face };
        e.SetDescriptors(face.Weight, face.Style, face.Stretch);
        return e;
    }

    private void SetDescriptors(float weight, FontStyleKind style, float stretch)
    {
        WeightMin = WeightMax = Math.Clamp(weight, 1, 1000);
        StretchMin = StretchMax = stretch > 0 ? stretch : 100;
        Style = style;
    }

    public bool InRange(int cp)
    {
        if (Ranges is null)
        {
            return true;
        }

        foreach (CodePointRange r in Ranges)
        {
            if (cp >= r.Start && cp <= r.End)
            {
                return true;
            }
        }

        return false;
    }

    // Loads the font on first use. A failure is sticky: the entry is skipped from then on and the owner starts matching afresh.
    public bool EnsureLoaded(FontCatalog owner)
    {
        if (_state == LoadState.Loaded)
        {
            return true;
        }

        if (_state == LoadState.Failed)
        {
            return false;
        }

        FontFace? face = owner.LoadFace(this, _data, _rule, _system);
        _data = null;
        if (face is null)
        {
            _state = LoadState.Failed;
            owner.NoteFaceFailed();
            return false;
        }

        Face = face;
        _state = LoadState.Loaded;
        return true;
    }

    public bool IsFailed => _state == LoadState.Failed;

    public FontMetricsInfo GetMetrics(float size)
    {
        FontFace face = Face ?? throw new InvalidOperationException("The face has not been loaded.");
        _metrics ??= FontMetricsBase.Read(face);
        FontMetricsBase m = _metrics;
        float scale = size * SizeAdjust / m.UnitsPerEm;
        float ascent = float.IsNaN(AscentOverride) ? m.Ascent * scale : AscentOverride * size;
        float descent = float.IsNaN(DescentOverride) ? m.Descent * scale : DescentOverride * size;
        float gap = float.IsNaN(LineGapOverride) ? m.LineGap * scale : LineGapOverride * size;
        return new FontMetricsInfo(
            size, ascent, descent, gap,
            m.XHeight * scale, m.CapHeight * scale,
            m.UnderlinePosition * scale, m.UnderlineThickness * scale,
            m.StrikeoutPosition * scale, m.StrikeoutThickness * scale,
            m.ZeroAdvance * scale, m.IdeographicAdvance * scale);
    }
}

// Unscaled metrics read once per face, in design units.
internal sealed class FontMetricsBase
{
    public int UnitsPerEm;
    public float Ascent;
    public float Descent;
    public float LineGap;
    public float XHeight;
    public float CapHeight;
    public float UnderlinePosition;
    public float UnderlineThickness;
    public float StrikeoutPosition;
    public float StrikeoutThickness;
    public float ZeroAdvance;
    public float IdeographicAdvance;

    public static FontMetricsBase Read(FontFace face)
    {
        OneFont.FontMetrics fm = face.Metrics;
        int asc = fm.Ascender;
        int desc = -fm.Descender;
        int gap = fm.LineGap;
        if (asc == 0 && desc == 0)
        {
            // Neither the horizontal header nor the typographic metrics gave anything: use the Windows metrics (OS/2 usWinAscent/usWinDescent).
            ReadOnlySpan<byte> os2 = face.GetTable("OS/2").Span;
            if (os2.Length >= 78)
            {
                asc = (os2[74] << 8) | os2[75];
                desc = (os2[76] << 8) | os2[77];
            }
        }

        int upem = Math.Max(fm.UnitsPerEm, 1);
        var m = new FontMetricsBase
        {
            UnitsPerEm = upem,
            Ascent = asc,
            Descent = Math.Max(desc, 0),
            LineGap = Math.Max(gap, 0),
            UnderlinePosition = -fm.UnderlinePosition,
            UnderlineThickness = fm.UnderlineThickness,
            StrikeoutPosition = fm.StrikeoutPosition,
            StrikeoutThickness = fm.StrikeoutSize,
            XHeight = fm.XHeight > 0 ? fm.XHeight : GlyphTop(face, 'x'),
            CapHeight = fm.CapHeight > 0 ? fm.CapHeight : GlyphTop(face, 'H'),
        };
        int zero = face.GetGlyphIndex('0');
        m.ZeroAdvance = zero != 0 ? face.GetAdvance(zero) : 0;
        int water = face.GetGlyphIndex(0x6C34);
        m.IdeographicAdvance = water != 0 ? face.GetAdvance(water) : 0;
        if (m.UnderlineThickness <= 0)
        {
            m.UnderlineThickness = upem / 14f;
        }

        if (m.StrikeoutThickness <= 0)
        {
            m.StrikeoutThickness = m.UnderlineThickness;
        }

        if (m.StrikeoutPosition == 0)
        {
            m.StrikeoutPosition = m.XHeight / 2;
        }

        return m;
    }

    // Older fonts leave the OS/2 height fields at zero; measure the glyph's outline instead.
    private static float GlyphTop(FontFace face, char c)
    {
        int g = face.GetGlyphIndex(c);
        if (g == 0)
        {
            return 0;
        }

        try
        {
            GlyphOutline outline = face.GetOutline(g);
            return outline.IsEmpty ? 0 : outline.TightBox().MaxY;
        }
        catch (FontFormatException)
        {
            return 0;
        }
    }
}
