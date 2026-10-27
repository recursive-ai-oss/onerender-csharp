using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Gurmukhi fonts for OneRender: the Noto Sans Gurmukhi release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoGurmukhiFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoGurmukhiFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Gurmukhi",
        0,
        [
            new(typeof(NotoGurmukhiFonts).Assembly, "Noto Sans Gurmukhi", 400, FontSlant.Normal, "NotoSansGurmukhi-Regular.ttf", 35956),
            new(typeof(NotoGurmukhiFonts).Assembly, "Noto Sans Gurmukhi", 700, FontSlant.Normal, "NotoSansGurmukhi-Bold.ttf", 35712),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
