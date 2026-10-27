using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Arabic fonts for OneRender: the Noto Sans Arabic release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoArabicFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoArabicFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Arabic",
        0,
        [
            new(typeof(NotoArabicFonts).Assembly, "Noto Sans Arabic", 400, FontSlant.Normal, "NotoSansArabic-Regular.ttf", 142140),
            new(typeof(NotoArabicFonts).Assembly, "Noto Sans Arabic", 700, FontSlant.Normal, "NotoSansArabic-Bold.ttf", 141852),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
