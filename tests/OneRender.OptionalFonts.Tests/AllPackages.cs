using OneRender.Fonts;
using OneRender.Fonts.Noto;

namespace OneRender.FontPackageTests;

// Liberation covers nearly all of Hebrew, so the Hebrew sample ends in U+05EF (yod triangle), which only the Noto face has.
// Every optional package and a sample string in its script. Generated from the package list; keep in step with src/OneRender.Fonts.*.
internal static class AllPackages
{
    public static readonly (string Id, OptionalFontPackage Package, string Sample)[] Scripts =
    [
        ("RecursiveAI.OneRender.Fonts.Noto.Armenian", NotoArmenianFonts.Package, "\u0540\u0561\u0575\u0561\u057D\u057F\u0561\u0576"),
        ("RecursiveAI.OneRender.Fonts.Noto.Hebrew", NotoHebrewFonts.Package, "\u05E9\u05DC\u05D5\u05DD \u05EF"),
        ("RecursiveAI.OneRender.Fonts.Noto.Arabic", NotoArabicFonts.Package, "\u0645\u0631\u062D\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645"),
        ("RecursiveAI.OneRender.Fonts.Noto.Syriac", NotoSyriacFonts.Package, "\u072B\u0720\u0721\u0710"),
        ("RecursiveAI.OneRender.Fonts.Noto.Thaana", NotoThaanaFonts.Package, "\u078B\u07A8\u0788\u07AC\u0780\u07A8"),
        ("RecursiveAI.OneRender.Fonts.Noto.Nko", NotoNkoFonts.Package, "\u07D2\u07DE\u07CF"),
        ("RecursiveAI.OneRender.Fonts.Noto.Devanagari", NotoDevanagariFonts.Package, "\u0928\u092E\u0938\u094D\u0924\u0947 \u0926\u0941\u0928\u093F\u092F\u093E"),
        ("RecursiveAI.OneRender.Fonts.Noto.Bengali", NotoBengaliFonts.Package, "\u09AC\u09BE\u0982\u09B2\u09BE \u09AD\u09BE\u09B7\u09BE"),
        ("RecursiveAI.OneRender.Fonts.Noto.Gurmukhi", NotoGurmukhiFonts.Package, "\u0A2A\u0A70\u0A1C\u0A3E\u0A2C\u0A40"),
        ("RecursiveAI.OneRender.Fonts.Noto.Gujarati", NotoGujaratiFonts.Package, "\u0A97\u0AC1\u0A9C\u0AB0\u0ABE\u0AA4\u0AC0"),
        ("RecursiveAI.OneRender.Fonts.Noto.Oriya", NotoOriyaFonts.Package, "\u0B13\u0B21\u0B3C\u0B3F\u0B06"),
        ("RecursiveAI.OneRender.Fonts.Noto.Tamil", NotoTamilFonts.Package, "\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD"),
        ("RecursiveAI.OneRender.Fonts.Noto.Telugu", NotoTeluguFonts.Package, "\u0C24\u0C46\u0C32\u0C41\u0C17\u0C41"),
        ("RecursiveAI.OneRender.Fonts.Noto.Kannada", NotoKannadaFonts.Package, "\u0C95\u0CA8\u0CCD\u0CA8\u0CA1"),
        ("RecursiveAI.OneRender.Fonts.Noto.Malayalam", NotoMalayalamFonts.Package, "\u0D2E\u0D32\u0D2F\u0D3E\u0D33\u0D02"),
        ("RecursiveAI.OneRender.Fonts.Noto.Sinhala", NotoSinhalaFonts.Package, "\u0DC3\u0DD2\u0D82\u0DC4\u0DBD"),
        ("RecursiveAI.OneRender.Fonts.Noto.Thai", NotoThaiFonts.Package, "\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35\u0E0A\u0E32\u0E27\u0E42\u0E25\u0E01"),
        ("RecursiveAI.OneRender.Fonts.Noto.Lao", NotoLaoFonts.Package, "\u0EAA\u0EB0\u0E9A\u0EB2\u0E8D\u0E94\u0EB5"),
        ("RecursiveAI.OneRender.Fonts.Noto.Tibetan", NotoTibetanFonts.Package, "\u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51"),
        ("RecursiveAI.OneRender.Fonts.Noto.Myanmar", NotoMyanmarFonts.Package, "\u1019\u103C\u1014\u103A\u1019\u102C"),
        ("RecursiveAI.OneRender.Fonts.Noto.Georgian", NotoGeorgianFonts.Package, "\u10E5\u10D0\u10E0\u10D7\u10E3\u10DA\u10D8"),
        ("RecursiveAI.OneRender.Fonts.Noto.Ethiopic", NotoEthiopicFonts.Package, "\u12A0\u121B\u122D\u129B"),
        ("RecursiveAI.OneRender.Fonts.Noto.Cherokee", NotoCherokeeFonts.Package, "\u13E3\u13B3\u13A9"),
        ("RecursiveAI.OneRender.Fonts.Noto.Khmer", NotoKhmerFonts.Package, "\u1781\u17D2\u1798\u17C2\u179A"),
        ("RecursiveAI.OneRender.Fonts.Noto.Mongolian", NotoMongolianFonts.Package, "\u182E\u1823\u1829\u182D\u1823\u182F"),
        ("RecursiveAI.OneRender.Fonts.Noto.Balinese", NotoBalineseFonts.Package, "\u1B29\u1B2E\u1B36"),
        ("RecursiveAI.OneRender.Fonts.Noto.Javanese", NotoJavaneseFonts.Package, "\uA997\uA9AE"),
        ("RecursiveAI.OneRender.Fonts.Noto.Symbols", NotoSymbolsFonts.Package, "\u2318\u2325\u238B\u2801\u2803"),
        ("RecursiveAI.OneRender.Fonts.Noto.Math", NotoMathFonts.Package, "\u27E6\u22B8\u2A0C\u27E7"),
    ];

    public static void RegisterAll()
    {
        foreach ((_, OptionalFontPackage p, _) in Scripts)
        {
            OptionalFonts.Register(p);
        }

        OneRender.Fonts.Cjk.CjkFonts.Register();
        OneRender.Fonts.Emoji.EmojiFonts.Register();
    }
}
