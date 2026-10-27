using OneRender.Fonts;

namespace OneRender.Fonts.Cjk;

/// <summary>
/// Noto Sans CJK for OneRender: Regular and Bold collections holding the Japanese, Korean, Simplified Chinese, Traditional Chinese and Hong Kong faces, embedded byte-for-byte.
/// </summary>
/// <remarks>
/// An application that references this package registers it automatically at start-up. Call <see cref="Register"/> when the
/// reference is not enough (a library that loads the package on demand) or to use the faces without the packaged text service.
/// Provenance and hashes are in <c>fonts/MANIFEST.tsv</c> (<see cref="OptionalFontPackage.OpenManifest"/>).
/// </remarks>
public static class CjkFonts
{
    /// <summary>The package's faces and licence.</summary>
    public static OptionalFontPackage Package { get; } = new(
        typeof(CjkFonts).Assembly,
        "RecursiveAI.OneRender.Fonts.Cjk",
        0,
        [
            new(typeof(CjkFonts).Assembly, "Noto Sans CJK JP", 400, FontSlant.Normal, "NotoSansCJK-Regular.ttc", 19484784),
            new(typeof(CjkFonts).Assembly, "Noto Sans CJK KR", 400, FontSlant.Normal, "NotoSansCJK-Regular.ttc", 19484784, faceIndex: 1),
            new(typeof(CjkFonts).Assembly, "Noto Sans CJK SC", 400, FontSlant.Normal, "NotoSansCJK-Regular.ttc", 19484784, faceIndex: 2),
            new(typeof(CjkFonts).Assembly, "Noto Sans CJK TC", 400, FontSlant.Normal, "NotoSansCJK-Regular.ttc", 19484784, faceIndex: 3),
            new(typeof(CjkFonts).Assembly, "Noto Sans CJK HK", 400, FontSlant.Normal, "NotoSansCJK-Regular.ttc", 19484784, faceIndex: 4),
            new(typeof(CjkFonts).Assembly, "Noto Sans CJK JP", 700, FontSlant.Normal, "NotoSansCJK-Bold.ttc", 20050760),
            new(typeof(CjkFonts).Assembly, "Noto Sans CJK KR", 700, FontSlant.Normal, "NotoSansCJK-Bold.ttc", 20050760, faceIndex: 1),
            new(typeof(CjkFonts).Assembly, "Noto Sans CJK SC", 700, FontSlant.Normal, "NotoSansCJK-Bold.ttc", 20050760, faceIndex: 2),
            new(typeof(CjkFonts).Assembly, "Noto Sans CJK TC", 700, FontSlant.Normal, "NotoSansCJK-Bold.ttc", 20050760, faceIndex: 3),
            new(typeof(CjkFonts).Assembly, "Noto Sans CJK HK", 700, FontSlant.Normal, "NotoSansCJK-Bold.ttc", 20050760, faceIndex: 4),
        ]);

    /// <summary>Registers the package with <see cref="OptionalFonts"/> so the packaged text service falls back to its faces. Safe to call more than once.</summary>
    public static void Register() => OptionalFonts.Register(Package);
}
