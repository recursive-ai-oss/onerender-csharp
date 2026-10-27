using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Khmer fonts for OneRender: the Noto Sans Khmer release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoKhmerFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoKhmerFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Khmer",
        0,
        [
            new(typeof(NotoKhmerFonts).Assembly, "Noto Sans Khmer", 400, FontSlant.Normal, "NotoSansKhmer-Regular.ttf", 67784),
            new(typeof(NotoKhmerFonts).Assembly, "Noto Sans Khmer", 700, FontSlant.Normal, "NotoSansKhmer-Bold.ttf", 67976),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
