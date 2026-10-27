using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Thai fonts for OneRender: the Noto Sans Thai release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoThaiFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoThaiFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Thai",
        0,
        [
            new(typeof(NotoThaiFonts).Assembly, "Noto Sans Thai", 400, FontSlant.Normal, "NotoSansThai-Regular.ttf", 20960),
            new(typeof(NotoThaiFonts).Assembly, "Noto Sans Thai", 700, FontSlant.Normal, "NotoSansThai-Bold.ttf", 20600),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
