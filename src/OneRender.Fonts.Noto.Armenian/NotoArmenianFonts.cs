using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Armenian fonts for OneRender: the Noto Sans Armenian release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoArmenianFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoArmenianFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Armenian",
        0,
        [
            new(typeof(NotoArmenianFonts).Assembly, "Noto Sans Armenian", 400, FontSlant.Normal, "NotoSansArmenian-Regular.ttf", 18772),
            new(typeof(NotoArmenianFonts).Assembly, "Noto Sans Armenian", 700, FontSlant.Normal, "NotoSansArmenian-Bold.ttf", 18752),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
