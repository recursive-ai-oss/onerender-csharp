using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Devanagari fonts for OneRender: the Noto Sans Devanagari release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoDevanagariFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoDevanagariFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Devanagari",
        0,
        [
            new(typeof(NotoDevanagariFonts).Assembly, "Noto Sans Devanagari", 400, FontSlant.Normal, "NotoSansDevanagari-Regular.ttf", 184228),
            new(typeof(NotoDevanagariFonts).Assembly, "Noto Sans Devanagari", 700, FontSlant.Normal, "NotoSansDevanagari-Bold.ttf", 183412),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
