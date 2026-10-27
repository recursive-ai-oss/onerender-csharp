using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Thaana fonts for OneRender: the Noto Sans Thaana release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoThaanaFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoThaanaFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Thaana",
        0,
        [
            new(typeof(NotoThaanaFonts).Assembly, "Noto Sans Thaana", 400, FontSlant.Normal, "NotoSansThaana-Regular.ttf", 27308),
            new(typeof(NotoThaanaFonts).Assembly, "Noto Sans Thaana", 700, FontSlant.Normal, "NotoSansThaana-Bold.ttf", 27000),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
