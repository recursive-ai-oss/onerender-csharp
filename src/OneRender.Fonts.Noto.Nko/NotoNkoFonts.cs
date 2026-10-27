using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Nko fonts for OneRender: the Noto Sans NKo release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoNkoFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoNkoFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Nko",
        0,
        [
            new(typeof(NotoNkoFonts).Assembly, "Noto Sans NKo", 400, FontSlant.Normal, "NotoSansNKo-Regular.ttf", 24608),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
