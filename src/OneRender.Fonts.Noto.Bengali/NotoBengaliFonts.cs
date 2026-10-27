using OneRender.Fonts;

namespace OneRender.Fonts.Noto;

/// <summary>
/// Noto Bengali fonts for OneRender: the Noto Sans Bengali release files, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class NotoBengaliFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(NotoBengaliFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Noto.Bengali",
        0,
        [
            new(typeof(NotoBengaliFonts).Assembly, "Noto Sans Bengali", 400, FontSlant.Normal, "NotoSansBengali-Regular.ttf", 103704),
            new(typeof(NotoBengaliFonts).Assembly, "Noto Sans Bengali", 700, FontSlant.Normal, "NotoSansBengali-Bold.ttf", 102440),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
