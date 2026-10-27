using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Georgian fonts for OneRender: the Noto Sans Georgian release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoGeorgianFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoGeorgianFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Georgian",
        0,
        [
            new(typeof(NotoGeorgianFonts).Assembly, "Noto Sans Georgian", 400, FontSlant.Normal, "NotoSansGeorgian-Regular.ttf", 35300),
            new(typeof(NotoGeorgianFonts).Assembly, "Noto Sans Georgian", 700, FontSlant.Normal, "NotoSansGeorgian-Bold.ttf", 35820),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
