using OneFont;

namespace OneRender.Text;

/// <summary>The kind of <c>font-style</c> value (CSS Fonts 4 §2.3).</summary>
public enum FontStyleKind : byte
{
    /// <summary>Upright.</summary>
    Normal = 0,

    /// <summary>Italic.</summary>
    Italic = 1,

    /// <summary>Oblique at an angle given beside the kind.</summary>
    Oblique = 2,
}

/// <summary>CSS generic font families and the <c>system-ui</c> family (CSS Fonts 4 §2.1.1).</summary>
public enum GenericFamily : byte
{
    /// <summary>Not a generic family: the name is a family name.</summary>
    None = 0,

    /// <summary><c>serif</c>.</summary>
    Serif,

    /// <summary><c>sans-serif</c>.</summary>
    SansSerif,

    /// <summary><c>monospace</c>.</summary>
    Monospace,

    /// <summary><c>cursive</c>.</summary>
    Cursive,

    /// <summary><c>fantasy</c>.</summary>
    Fantasy,

    /// <summary><c>system-ui</c>.</summary>
    SystemUi,

    /// <summary><c>ui-serif</c>.</summary>
    UiSerif,

    /// <summary><c>ui-sans-serif</c>.</summary>
    UiSansSerif,

    /// <summary><c>ui-monospace</c>.</summary>
    UiMonospace,

    /// <summary><c>ui-rounded</c>.</summary>
    UiRounded,

    /// <summary><c>math</c>.</summary>
    Math,

    /// <summary><c>emoji</c>.</summary>
    Emoji,

    /// <summary><c>fangsong</c>.</summary>
    Fangsong,
}

/// <summary>Which faces the engine may fake when a family has no matching one (the <c>font-synthesis</c> property of CSS Fonts 4).</summary>
[Flags]
public enum FontSynthesis : byte
{
    /// <summary>Nothing is synthesized.</summary>
    None = 0,

    /// <summary>A bold face may be faked by emboldening a lighter one (<c>font-synthesis-weight: auto</c>).</summary>
    Weight = 1,

    /// <summary>An italic or oblique face may be faked by slanting an upright one (<c>font-synthesis-style: auto</c>).</summary>
    Style = 2,

    /// <summary>Small caps may be faked. Recorded for the layout; the text service does not change glyphs for it.</summary>
    SmallCaps = 4,

    /// <summary>Sub- and superscripts may be faked. Recorded for the layout.</summary>
    Position = 8,

    /// <summary>Everything (<c>font-synthesis: weight style small-caps position</c>, the initial value).</summary>
    All = 15,
}

/// <summary>One entry of a <c>font-family</c> list: a family name or a generic family.</summary>
public readonly struct FontFamilyName : IEquatable<FontFamilyName>
{
    /// <summary>Creates a named family. The name is matched case-insensitively and exactly (no trimming).</summary>
    /// <param name="name">The family name without quotes.</param>
    public FontFamilyName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Generic = GenericFamily.None;
    }

    private FontFamilyName(GenericFamily generic)
    {
        Name = GenericKeyword(generic);
        Generic = generic;
    }

    /// <summary>The family name, or the keyword of a generic family (for example <c>sans-serif</c>).</summary>
    public string Name { get; }

    /// <summary>The generic family, or <see cref="GenericFamily.None"/> for a named family.</summary>
    public GenericFamily Generic { get; }

    /// <summary>Creates a generic family entry.</summary>
    /// <param name="generic">The generic family; must not be <see cref="GenericFamily.None"/>.</param>
    /// <returns>The entry.</returns>
    public static FontFamilyName OfGeneric(GenericFamily generic)
    {
        if (generic == GenericFamily.None)
        {
            throw new ArgumentException("A generic family is required.", nameof(generic));
        }

        return new FontFamilyName(generic);
    }

    /// <summary>The CSS keyword of a generic family; an empty string for <see cref="GenericFamily.None"/>.</summary>
    /// <param name="generic">The family.</param>
    /// <returns>The keyword.</returns>
    public static string GenericKeyword(GenericFamily generic) => generic switch
    {
        GenericFamily.Serif => "serif",
        GenericFamily.SansSerif => "sans-serif",
        GenericFamily.Monospace => "monospace",
        GenericFamily.Cursive => "cursive",
        GenericFamily.Fantasy => "fantasy",
        GenericFamily.SystemUi => "system-ui",
        GenericFamily.UiSerif => "ui-serif",
        GenericFamily.UiSansSerif => "ui-sans-serif",
        GenericFamily.UiMonospace => "ui-monospace",
        GenericFamily.UiRounded => "ui-rounded",
        GenericFamily.Math => "math",
        GenericFamily.Emoji => "emoji",
        GenericFamily.Fangsong => "fangsong",
        _ => string.Empty,
    };

    /// <summary>Finds the generic family a keyword names (ASCII case-insensitive).</summary>
    /// <param name="keyword">An unquoted identifier.</param>
    /// <returns>The family, or <see cref="GenericFamily.None"/>.</returns>
    public static GenericFamily GenericFromKeyword(ReadOnlySpan<char> keyword)
    {
        for (GenericFamily g = GenericFamily.Serif; g <= GenericFamily.Fangsong; g++)
        {
            if (keyword.Equals(GenericKeyword(g), StringComparison.OrdinalIgnoreCase))
            {
                return g;
            }
        }

        return GenericFamily.None;
    }

    /// <inheritdoc/>
    public bool Equals(FontFamilyName other) => Generic == other.Generic && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FontFamilyName f && Equals(f);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine((int)Generic, Name is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Name));

    /// <inheritdoc/>
    public override string ToString() => Name ?? string.Empty;

    /// <summary>Equality.</summary>
    /// <param name="left">A family.</param>
    /// <param name="right">A family.</param>
    /// <returns>Whether they are equal.</returns>
    public static bool operator ==(FontFamilyName left, FontFamilyName right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    /// <param name="left">A family.</param>
    /// <param name="right">A family.</param>
    /// <returns>Whether they differ.</returns>
    public static bool operator !=(FontFamilyName left, FontFamilyName right) => !left.Equals(right);
}

/// <summary>
/// The OpenType features and kerning mode a run is shaped with: the result of <c>font-kerning</c>, the <c>font-variant-*</c>
/// properties and <c>font-feature-settings</c> (CSS Fonts 4 §6, §7). Immutable. Ligatures and kerning are on unless turned off.
/// </summary>
public sealed class FontFeatureSettings : IEquatable<FontFeatureSettings>
{
    private readonly FontFeature[] _features;
    private readonly int _hash;

    /// <summary>The default: kerning on, the font's default features, nothing else.</summary>
    public static FontFeatureSettings Default { get; } = new([], KerningMode.Normal);

    /// <summary>Creates settings from explicit features. Later entries replace earlier ones for the same tag.</summary>
    /// <param name="features">The features in order; may be empty.</param>
    /// <param name="kerning">Whether pair kerning is applied. <c>font-kerning: none</c> is <see cref="KerningMode.None"/>.</param>
    public FontFeatureSettings(ReadOnlySpan<FontFeature> features, KerningMode kerning)
    {
        _features = features.ToArray();
        Kerning = kerning;
        var h = new HashCode();
        h.Add((int)kerning);
        foreach (FontFeature f in _features)
        {
            h.Add(f.Tag, StringComparer.Ordinal);
            h.Add(f.Value);
        }

        _hash = h.ToHashCode();
    }

    /// <summary>The features, in application order.</summary>
    public ReadOnlySpan<FontFeature> Features => _features;

    /// <summary>The kerning mode.</summary>
    public KerningMode Kerning { get; }

    /// <summary>
    /// Builds the settings from the CSS text of the properties that choose features. A null or empty text is the property's
    /// initial value (<c>normal</c>/<c>auto</c>). Unknown or malformed values are ignored, as an invalid declaration would be.
    /// Variant properties apply first and <c>font-feature-settings</c> last, so it wins (CSS Fonts 4 §7.1).
    /// </summary>
    /// <param name="kerning"><c>font-kerning</c>: <c>auto</c>, <c>normal</c> or <c>none</c>.</param>
    /// <param name="ligatures"><c>font-variant-ligatures</c>.</param>
    /// <param name="caps"><c>font-variant-caps</c>.</param>
    /// <param name="numeric"><c>font-variant-numeric</c>.</param>
    /// <param name="eastAsian"><c>font-variant-east-asian</c>.</param>
    /// <param name="position"><c>font-variant-position</c>.</param>
    /// <param name="featureSettings"><c>font-feature-settings</c>, for example <c>"smcp", "liga" 0, "ss01"</c>.</param>
    /// <returns>The settings; <see cref="Default"/> when nothing changes.</returns>
    public static FontFeatureSettings FromCss(string? kerning = null, string? ligatures = null, string? caps = null, string? numeric = null, string? eastAsian = null, string? position = null, string? featureSettings = null)
        => FontVariantParser.Build(kerning, ligatures, caps, numeric, eastAsian, position, featureSettings);

    /// <inheritdoc/>
    public bool Equals(FontFeatureSettings? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || _hash != other._hash || Kerning != other.Kerning || _features.Length != other._features.Length)
        {
            return false;
        }

        for (int i = 0; i < _features.Length; i++)
        {
            if (!_features[i].Equals(other._features[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as FontFeatureSettings);

    /// <inheritdoc/>
    public override int GetHashCode() => _hash;
}

/// <summary>
/// The computed font properties a run of text is matched and shaped with. Immutable; create one per computed style and reuse it,
/// because matching and the shaped-run cache recognise equal requests quickly. The <see cref="Size"/> is not part of matching.
/// </summary>
public sealed class FontRequest : IEquatable<FontRequest>
{
    /// <summary>The most family names a request may hold. Longer lists are refused.</summary>
    public const int MaxFamilies = 64;

    private readonly FontFamilyName[] _families;
    private readonly int _styleHash;

    /// <summary>Creates a request.</summary>
    /// <param name="families">The <c>font-family</c> list in order; may be empty (the default faces are used).</param>
    /// <param name="size">The font size in CSS pixels; finite and above 0.</param>
    /// <param name="weight">The <c>font-weight</c> from 1 to 1000. Default 400.</param>
    /// <param name="style">The <c>font-style</c> kind. Default normal.</param>
    /// <param name="obliqueAngle">The angle in degrees for <see cref="FontStyleKind.Oblique"/>, from -90 to 90. Default 14; also the slant used when italic is faked.</param>
    /// <param name="stretch">The <c>font-stretch</c> as a percentage of normal, above 0. Default 100.</param>
    /// <param name="synthesis">What may be faked. Default everything.</param>
    /// <param name="features">Features and kerning; <see cref="FontFeatureSettings.Default"/> when null.</param>
    /// <param name="language">A BCP 47 language tag (the <c>lang</c> of the text), used to choose language-specific glyphs; null for none.</param>
    /// <exception cref="ArgumentException">A value is out of range, or the family list is longer than <see cref="MaxFamilies"/>.</exception>
    public FontRequest(ReadOnlySpan<FontFamilyName> families, float size, float weight = 400, FontStyleKind style = FontStyleKind.Normal, float obliqueAngle = 14, float stretch = 100, FontSynthesis synthesis = FontSynthesis.All, FontFeatureSettings? features = null, string? language = null)
    {
        if (families.Length > MaxFamilies)
        {
            throw new ArgumentException("Too many font families.", nameof(families));
        }

        if (!(size > 0) || float.IsInfinity(size) || !(weight >= 1 && weight <= 1000) || !(stretch > 0) || float.IsInfinity(stretch) || !(obliqueAngle >= -90 && obliqueAngle <= 90))
        {
            throw new ArgumentException("A font property is out of range.");
        }

        _families = families.ToArray();
        Size = size;
        Weight = weight;
        Style = style;
        ObliqueAngle = style == FontStyleKind.Oblique ? obliqueAngle : 0;
        Stretch = stretch;
        Synthesis = synthesis;
        Features = features ?? FontFeatureSettings.Default;
        Language = string.IsNullOrEmpty(language) ? null : language;

        var h = new HashCode();
        foreach (FontFamilyName f in _families)
        {
            h.Add(f);
        }

        h.Add(weight);
        h.Add((int)style);
        h.Add(ObliqueAngle);
        h.Add(stretch);
        h.Add((int)synthesis);
        h.Add(Features);
        h.Add(Language, StringComparer.OrdinalIgnoreCase);
        _styleHash = h.ToHashCode();
    }

    /// <summary>The <c>font-family</c> list.</summary>
    public ReadOnlySpan<FontFamilyName> Families => _families;

    /// <summary>The font size in CSS pixels.</summary>
    public float Size { get; }

    /// <summary>The weight, 1 to 1000.</summary>
    public float Weight { get; }

    /// <summary>The style kind.</summary>
    public FontStyleKind Style { get; }

    /// <summary>The oblique angle in degrees (0 for normal).</summary>
    public float ObliqueAngle { get; }

    /// <summary>The stretch as a percentage of normal.</summary>
    public float Stretch { get; }

    /// <summary>What may be faked.</summary>
    public FontSynthesis Synthesis { get; }

    /// <summary>Features and kerning.</summary>
    public FontFeatureSettings Features { get; }

    /// <summary>The language tag, or null.</summary>
    public string? Language { get; }

    /// <summary>The slant, in degrees, applied when an italic or oblique face has to be faked: the requested angle, or 14 for italic.</summary>
    public float SyntheticSlant => Style == FontStyleKind.Oblique ? ObliqueAngle : 14;

    internal int StyleHash => _styleHash;

    /// <summary>The same request at another size.</summary>
    /// <param name="size">The new size in CSS pixels.</param>
    /// <returns>A request equal to this one in everything but size.</returns>
    public FontRequest WithSize(float size) => new(_families, size, Weight, Style, SyntheticSlant, Stretch, Synthesis, Features, Language);

    /// <summary>Whether two requests match fonts and shape identically, ignoring size.</summary>
    /// <param name="other">The other request.</param>
    /// <returns>True when the style parts are equal.</returns>
    public bool StyleEquals(FontRequest? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || _styleHash != other._styleHash || _families.Length != other._families.Length || Weight != other.Weight || Style != other.Style
            || Stretch != other.Stretch || Synthesis != other.Synthesis || !string.Equals(Language, other.Language, StringComparison.OrdinalIgnoreCase)
            || !Features.Equals(other.Features) || ObliqueAngle != other.ObliqueAngle)
        {
            return false;
        }

        for (int i = 0; i < _families.Length; i++)
        {
            if (!_families[i].Equals(other._families[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public bool Equals(FontRequest? other) => StyleEquals(other) && other!.Size == Size;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as FontRequest);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_styleHash, Size);
}
