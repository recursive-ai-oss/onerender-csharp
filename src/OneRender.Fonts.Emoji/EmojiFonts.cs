using OneRender.Fonts;

namespace OneRender.Fonts.Emoji;

/// <summary>
/// Noto Color Emoji for OneRender: the upstream CBDT/CBLC color bitmap font (the build with an empty glyf table, which every TrueType reader accepts), embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class EmojiFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(EmojiFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Emoji",
        1,
        [
            new(typeof(EmojiFonts).Assembly, "Noto Color Emoji", 400, FontSlant.Normal, "NotoColorEmoji_WindowsCompatible.ttf", 10682364),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
