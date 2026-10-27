using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Math fonts for OneRender: the Noto Sans Math release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoMathFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoMathFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Math",
        0,
        [
            new(typeof(NotoMathFonts).Assembly, "Noto Sans Math", 400, FontSlant.Normal, "NotoSansMath-Regular.ttf", 657440),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
