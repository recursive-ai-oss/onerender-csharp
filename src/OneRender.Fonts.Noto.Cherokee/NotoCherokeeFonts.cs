using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Cherokee fonts for OneRender: the Noto Sans Cherokee release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoCherokeeFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoCherokeeFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Cherokee",
        0,
        [
            new(typeof(NotoCherokeeFonts).Assembly, "Noto Sans Cherokee", 400, FontSlant.Normal, "NotoSansCherokee-Regular.ttf", 65168),
            new(typeof(NotoCherokeeFonts).Assembly, "Noto Sans Cherokee", 700, FontSlant.Normal, "NotoSansCherokee-Bold.ttf", 76784),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
