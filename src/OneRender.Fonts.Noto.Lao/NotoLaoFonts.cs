using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Lao fonts for OneRender: the Noto Sans Lao release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoLaoFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoLaoFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Lao",
        0,
        [
            new(typeof(NotoLaoFonts).Assembly, "Noto Sans Lao", 400, FontSlant.Normal, "NotoSansLao-Regular.ttf", 21720),
            new(typeof(NotoLaoFonts).Assembly, "Noto Sans Lao", 700, FontSlant.Normal, "NotoSansLao-Bold.ttf", 21508),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
