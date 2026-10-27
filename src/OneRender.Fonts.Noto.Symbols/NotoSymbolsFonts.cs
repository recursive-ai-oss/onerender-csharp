using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Symbols fonts for OneRender: the Noto Sans Symbols, Noto Sans Symbols 2 release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoSymbolsFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoSymbolsFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Symbols",
        0,
        [
            new(typeof(NotoSymbolsFonts).Assembly, "Noto Sans Symbols", 400, FontSlant.Normal, "NotoSansSymbols-Regular.ttf", 145508),
            new(typeof(NotoSymbolsFonts).Assembly, "Noto Sans Symbols", 700, FontSlant.Normal, "NotoSansSymbols-Bold.ttf", 146136),
            new(typeof(NotoSymbolsFonts).Assembly, "Noto Sans Symbols 2", 400, FontSlant.Normal, "NotoSansSymbols2-Regular.ttf", 671568),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
