using System.IO;

namespace OneRender.Fonts;

/// <summary>
/// The fonts this package ships (Liberation Sans, Serif and Mono; Carlito; Caladea) and the family-name mapping
/// that makes common web and Office names resolve to them. Output that uses these fonts is identical on every
/// platform because the bytes come from the package, never from the system.
/// </summary>
public static class DefaultFonts
{
    private const string Sans = "Liberation Sans";
    private const string Serif = "Liberation Serif";
    private const string Mono = "Liberation Mono";
    private const string Carlito = "Carlito";
    private const string Caladea = "Caladea";

    // Every face, grouped by family so GetFaces can return a slice. Within a family: regular, bold, italic, bold italic.
    private static readonly FontFaceInfo[] s_faces =
    [
        new(Sans, 400, FontSlant.Normal, "LiberationSans-Regular.ttf", "Liberation", 410712),
        new(Sans, 700, FontSlant.Normal, "LiberationSans-Bold.ttf", "Liberation", 414456),
        new(Sans, 400, FontSlant.Italic, "LiberationSans-Italic.ttf", "Liberation", 415816),
        new(Sans, 700, FontSlant.Italic, "LiberationSans-BoldItalic.ttf", "Liberation", 408996),
        new(Serif, 400, FontSlant.Normal, "LiberationSerif-Regular.ttf", "Liberation", 393576),
        new(Serif, 700, FontSlant.Normal, "LiberationSerif-Bold.ttf", "Liberation", 370096),
        new(Serif, 400, FontSlant.Italic, "LiberationSerif-Italic.ttf", "Liberation", 375632),
        new(Serif, 700, FontSlant.Italic, "LiberationSerif-BoldItalic.ttf", "Liberation", 376772),
        new(Mono, 400, FontSlant.Normal, "LiberationMono-Regular.ttf", "Liberation", 319508),
        new(Mono, 700, FontSlant.Normal, "LiberationMono-Bold.ttf", "Liberation", 307996),
        new(Mono, 400, FontSlant.Italic, "LiberationMono-Italic.ttf", "Liberation", 281536),
        new(Mono, 700, FontSlant.Italic, "LiberationMono-BoldItalic.ttf", "Liberation", 284068),
        new(Carlito, 400, FontSlant.Normal, "Carlito-Regular.ttf", "Carlito", 628032),
        new(Carlito, 700, FontSlant.Normal, "Carlito-Bold.ttf", "Carlito", 682468),
        new(Carlito, 400, FontSlant.Italic, "Carlito-Italic.ttf", "Carlito", 615236),
        new(Carlito, 700, FontSlant.Italic, "Carlito-BoldItalic.ttf", "Carlito", 808508),
        new(Caladea, 400, FontSlant.Normal, "Caladea-Regular.ttf", "Caladea", 81600),
        new(Caladea, 700, FontSlant.Normal, "Caladea-Bold.ttf", "Caladea", 84492),
        new(Caladea, 400, FontSlant.Italic, "Caladea-Italic.ttf", "Caladea", 83780),
        new(Caladea, 700, FontSlant.Italic, "Caladea-BoldItalic.ttf", "Caladea", 83356),
    ];

    // Generic families and well-known names. Keys are lower-case ASCII; matching lower-cases the input ordinally.
    // The family names themselves are included so a resolved name resolves to itself.
    private static readonly (string Key, string Family)[] s_names =
    [
        ("sans-serif", Sans),
        ("serif", Serif),
        ("monospace", Mono),
        ("system-ui", Sans),
        ("ui-sans-serif", Sans),
        ("ui-serif", Serif),
        ("ui-monospace", Mono),
        // No bundled script or display faces: these two fall back to a serif so text stays readable.
        ("cursive", Serif),
        ("fantasy", Serif),
        ("arial", Sans),
        ("helvetica", Sans),
        ("times new roman", Serif),
        ("times", Serif),
        ("courier new", Mono),
        ("courier", Mono),
        ("calibri", Carlito),
        ("cambria", Caladea),
        ("liberation sans", Sans),
        ("liberation serif", Serif),
        ("liberation mono", Mono),
        ("carlito", Carlito),
        ("caladea", Caladea),
    ];

    /// <summary>All 20 shipped faces, grouped by family (Liberation Sans, Serif, Mono, Carlito, Caladea), each in the order regular, bold, italic, bold italic. The list is shared and read-only.</summary>
    public static IReadOnlyList<FontFaceInfo> Faces => s_faces;

    /// <summary>The five shipped family names, in the order of <see cref="Faces"/>.</summary>
    public static IReadOnlyList<string> Families { get; } = [Sans, Serif, Mono, Carlito, Caladea];

    /// <summary>
    /// Maps a CSS generic family (<c>serif</c>, <c>sans-serif</c>, <c>monospace</c>, <c>system-ui</c>, <c>ui-sans-serif</c>,
    /// <c>ui-serif</c>, <c>ui-monospace</c>, <c>cursive</c>, <c>fantasy</c>), a well-known alias (Arial, Helvetica, Times New Roman,
    /// Times, Courier New, Courier, Calibri, Cambria) or a shipped family name to the shipped family that stands in for it.
    /// Matching is ordinal and ASCII case-insensitive; the name is not trimmed and quotes are not removed.
    /// Allocates nothing.
    /// </summary>
    /// <param name="name">The family name to look up.</param>
    /// <param name="family">The shipped family name, one of <see cref="Families"/>; null when there is no match.</param>
    /// <returns>True when <paramref name="name"/> maps to a shipped family.</returns>
    public static bool TryResolveFamily(ReadOnlySpan<char> name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? family)
    {
        foreach ((string key, string target) in s_names)
        {
            if (EqualsAsciiIgnoreCase(name, key))
            {
                family = target;
                return true;
            }
        }

        family = null;
        return false;
    }

    /// <summary>Same as <see cref="TryResolveFamily(ReadOnlySpan{char}, out string?)"/> for a string; a null name never matches.</summary>
    /// <param name="name">The family name to look up.</param>
    /// <param name="family">The shipped family name; null when there is no match.</param>
    /// <returns>True when <paramref name="name"/> maps to a shipped family.</returns>
    public static bool TryResolveFamily(string? name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? family)
    {
        if (name is null)
        {
            family = null;
            return false;
        }

        return TryResolveFamily(name.AsSpan(), out family);
    }

    /// <summary>
    /// The faces of a shipped family, as a slice of <see cref="Faces"/> (no allocation).
    /// The name must be one of <see cref="Families"/>; use <see cref="TryResolveFamily(string?, out string?)"/> first for aliases.
    /// Comparison is ordinal and ASCII case-insensitive.
    /// </summary>
    /// <param name="family">A shipped family name.</param>
    /// <returns>The four faces of the family, or an empty span when the name is not a shipped family.</returns>
    public static ReadOnlySpan<FontFaceInfo> GetFaces(ReadOnlySpan<char> family)
    {
        for (int i = 0; i < s_faces.Length; i++)
        {
            if (EqualsAsciiIgnoreCase(family, s_faces[i].Family))
            {
                int end = i + 1;
                while (end < s_faces.Length && ReferenceEquals(s_faces[end].Family, s_faces[i].Family))
                {
                    end++;
                }

                return s_faces.AsSpan(i, end - i);
            }
        }

        return default;
    }

    /// <summary>
    /// Opens the licence text (SIL Open Font License 1.1, with the copyright lines) that covers a shipped family.
    /// Same stream semantics as <see cref="FontFaceInfo.Open"/>.
    /// </summary>
    /// <param name="family">A shipped family name, as in <see cref="Families"/> (ASCII case-insensitive).</param>
    /// <returns>A new read-only stream over the licence file.</returns>
    /// <exception cref="ArgumentException"><paramref name="family"/> is not a shipped family.</exception>
    public static Stream OpenLicense(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        ReadOnlySpan<FontFaceInfo> faces = GetFaces(family);
        if (faces.IsEmpty)
        {
            throw new ArgumentException("Not a shipped font family: " + family, nameof(family));
        }

        return OpenResource(faces[0].LicenseResourceName);
    }

    /// <summary>
    /// Opens the provenance manifest: tab-separated lines giving, for every shipped file, its upstream URL and SHA-256.
    /// Lines starting with <c>#</c> are comments. Same stream semantics as <see cref="FontFaceInfo.Open"/>.
    /// </summary>
    /// <returns>A new read-only stream over the UTF-8 manifest.</returns>
    public static Stream OpenManifest() => OpenResource("fonts/MANIFEST.tsv");

    internal static Stream OpenResource(string name) =>
        typeof(DefaultFonts).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException("Embedded font resource is missing: " + name);

    private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<char> a, string lowerAsciiOrMixed)
    {
        if (a.Length != lowerAsciiOrMixed.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (AsciiLower(a[i]) != AsciiLower(lowerAsciiOrMixed[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
}
