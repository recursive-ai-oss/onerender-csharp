using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Telugu fonts for OneRender: the Noto Sans Telugu release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoTeluguFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoTeluguFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Telugu",
        0,
        [
            new(typeof(NotoTeluguFonts).Assembly, "Noto Sans Telugu", 400, FontSlant.Normal, "NotoSansTelugu-Regular.ttf", 165212),
            new(typeof(NotoTeluguFonts).Assembly, "Noto Sans Telugu", 700, FontSlant.Normal, "NotoSansTelugu-Bold.ttf", 165564),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
