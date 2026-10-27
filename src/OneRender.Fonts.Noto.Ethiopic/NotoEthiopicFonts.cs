using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Ethiopic fonts for OneRender: the Noto Sans Ethiopic release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoEthiopicFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoEthiopicFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Ethiopic",
        0,
        [
            new(typeof(NotoEthiopicFonts).Assembly, "Noto Sans Ethiopic", 400, FontSlant.Normal, "NotoSansEthiopic-Regular.ttf", 289244),
            new(typeof(NotoEthiopicFonts).Assembly, "Noto Sans Ethiopic", 700, FontSlant.Normal, "NotoSansEthiopic-Bold.ttf", 288828),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
