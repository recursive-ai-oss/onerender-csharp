using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Sinhala fonts for OneRender: the Noto Sans Sinhala release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoSinhalaFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoSinhalaFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Sinhala",
        0,
        [
            new(typeof(NotoSinhalaFonts).Assembly, "Noto Sans Sinhala", 400, FontSlant.Normal, "NotoSansSinhala-Regular.ttf", 90296),
            new(typeof(NotoSinhalaFonts).Assembly, "Noto Sans Sinhala", 700, FontSlant.Normal, "NotoSansSinhala-Bold.ttf", 88508),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
