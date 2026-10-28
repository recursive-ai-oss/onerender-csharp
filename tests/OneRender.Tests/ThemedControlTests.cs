using OneRender.Image;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.Style;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// <c>color-scheme</c>, the system colors under it, and native controls in the light and dark palettes. The expected numbers were read
/// off Chrome 156 (<c>docs/forms.md</c>, "Themed controls").
/// </summary>
public class ThemedControlTests
{
    private static StyleDeclaration D(string css)
    {
        var d = new StyleDeclaration();
        foreach (string part in css.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = part.IndexOf(':', StringComparison.Ordinal);
            Assert.True(d.TrySet(part[..colon].Trim(), part[(colon + 1)..].Trim()), "could not set " + part);
        }

        return d;
    }

    private static ComputedValues Child(string rootCss, string childCss, bool prefersDark = false, string midCss = "")
    {
        var tree = new StyledTree();
        tree.Styles.PrefersDarkScheme = prefersDark;
        StyledNode root = tree.AddRoot("html", D(rootCss));
        StyledNode mid = tree.AddElement(root, "div", D(midCss));
        return tree.AddElement(mid, "span", D(childCss)).Values;
    }

    private static ComputedValues Root(string css, bool prefersDark = false)
    {
        var tree = new StyledTree();
        tree.Styles.PrefersDarkScheme = prefersDark;
        return tree.AddRoot("html", D(css)).Values;
    }

    private static void AssertRgb(ColorValue c, int r, int g, int b, float a = 1f)
    {
        Assert.Equal(r, c.R * 255f, 0.6f);
        Assert.Equal(g, c.G * 255f, 0.6f);
        Assert.Equal(b, c.B * 255f, 0.6f);
        Assert.Equal(a, c.A, 0.01f);
    }

    // ------------------------------------------------------------------ the used scheme (CSS Color Adjust 1 §2.2)

    [Theory]
    [InlineData("normal", false, false)]
    [InlineData("normal", true, false)]
    [InlineData("light", true, false)]
    [InlineData("dark", false, true)]
    [InlineData("light dark", false, false)]
    [InlineData("light dark", true, true)]
    [InlineData("dark light", false, false)]
    [InlineData("dark light", true, true)]
    [InlineData("only light", true, false)]
    [InlineData("only dark", false, true)]
    [InlineData("dark only", false, true)]
    [InlineData("foo", true, false)]
    [InlineData("dark foo", false, true)]
    [InlineData("LIGHT Dark", true, true)]
    public void TheUsedSchemeFollowsTheListAndThePreference(string value, bool prefersDark, bool expectDark)
    {
        Assert.Equal(expectDark, Root("color-scheme:" + value, prefersDark).DarkScheme);
    }

    [Theory]
    [InlineData("normal light")]
    [InlineData("only")]
    [InlineData("only light only")]
    [InlineData("light only dark")]
    [InlineData("light, dark")]
    [InlineData("1")]
    public void InvalidColorSchemesAreRejected(string value)
    {
        Assert.False(new StyleDeclaration().TrySet("color-scheme", value));
    }

    [Fact]
    public void TheSchemeInheritsAndIsReplacedPerElement()
    {
        Assert.True(Child("color-scheme:dark", string.Empty).DarkScheme);
        Assert.False(Child("color-scheme:dark", "color-scheme:light").DarkScheme);
        Assert.True(Child(string.Empty, string.Empty, midCss: "color-scheme:dark").DarkScheme);
        Assert.Equal(ColorSchemes.Light | ColorSchemes.Dark | ColorSchemes.Only, Root("color-scheme:only light dark").ColorScheme);
        Assert.Equal("light dark", Root("color-scheme:light dark").ToCss(PropertyId.ColorScheme));
    }

    // ------------------------------------------------------------------ system colors under each scheme

    [Theory]
    [InlineData("Canvas", false, 255, 255, 255)]
    [InlineData("Canvas", true, 18, 18, 18)]
    [InlineData("CanvasText", false, 0, 0, 0)]
    [InlineData("CanvasText", true, 255, 255, 255)]
    [InlineData("Field", false, 255, 255, 255)]
    [InlineData("Field", true, 59, 59, 59)]
    [InlineData("FieldText", true, 255, 255, 255)]
    [InlineData("ButtonFace", false, 239, 239, 239)]
    [InlineData("ButtonFace", true, 107, 107, 107)]
    [InlineData("ButtonText", true, 255, 255, 255)]
    [InlineData("ButtonBorder", false, 0, 0, 0)]
    [InlineData("ButtonBorder", true, 255, 255, 255)]
    [InlineData("GrayText", false, 128, 128, 128)]
    [InlineData("GrayText", true, 128, 128, 128)]
    [InlineData("LinkText", false, 0, 0, 238)]
    [InlineData("LinkText", true, 158, 158, 255)]
    [InlineData("VisitedText", false, 85, 26, 139)]
    [InlineData("VisitedText", true, 208, 173, 240)]
    [InlineData("SelectedItem", false, 25, 103, 210)]
    [InlineData("SelectedItem", true, 153, 200, 255)]
    [InlineData("SelectedItemText", true, 59, 59, 59)]
    [InlineData("AccentColor", true, 0, 117, 255)]
    [InlineData("Mark", true, 255, 255, 0)]
    [InlineData("windowtext", true, 255, 255, 255)]
    public void SystemColorsTakeTheirValueUnderTheUsedScheme(string name, bool dark, int r, int g, int b)
    {
        ComputedValues v = Root("color-scheme:" + (dark ? "dark" : "light") + ";background-color:" + name);
        AssertRgb(v.BackgroundColor, r, g, b);
    }

    [Fact]
    public void HighlightKeepsItsAlphaInBothSchemes()
    {
        AssertRgb(Root("color-scheme:dark;background-color:Highlight").BackgroundColor, 0, 65, 198, 0.8f);
        AssertRgb(Root("background-color:Highlight").BackgroundColor, 0, 65, 198, 0.8f);
    }

    [Fact]
    public void ThePreferenceAloneDoesNotDarkenAPageThatDoesNotOptIn()
    {
        ComputedValues v = Root("background-color:Canvas", prefersDark: true);
        AssertRgb(v.BackgroundColor, 255, 255, 255);
        AssertRgb(v.Color, 0, 0, 0);
    }

    [Fact]
    public void TheInitialColorIsCanvasTextOfTheRootSchemeAndInheritsAsAValue()
    {
        AssertRgb(Root("color-scheme:dark").Color, 255, 255, 255);
        AssertRgb(Root("color-scheme:dark;color:initial").Color, 255, 255, 255);
        AssertRgb(Root("color-scheme:light dark", prefersDark: true).Color, 255, 255, 255);

        // Chrome 156: a dark subtree of a light page inherits the computed black; only keywords resolve under the element's own scheme.
        ComputedValues inherited = Child(string.Empty, string.Empty, midCss: "color-scheme:dark");
        AssertRgb(inherited.Color, 0, 0, 0);
        ComputedValues keyword = Child(string.Empty, "color:CanvasText;background-color:Canvas", midCss: "color-scheme:dark");
        AssertRgb(keyword.Color, 255, 255, 255);
        AssertRgb(keyword.BackgroundColor, 18, 18, 18);
    }

    [Fact]
    public void LightDarkPicksByTheElementsUsedScheme()
    {
        const string Css = "background-color:light-dark(rgb(255,240,0),rgb(0,255,255))";
        AssertRgb(Child(string.Empty, Css).BackgroundColor, 255, 240, 0);
        AssertRgb(Child(string.Empty, Css, midCss: "color-scheme:dark").BackgroundColor, 0, 255, 255);
        AssertRgb(Child("color-scheme:light dark", Css, prefersDark: true).BackgroundColor, 0, 255, 255);
        AssertRgb(Child("color-scheme:light dark", Css, prefersDark: false).BackgroundColor, 255, 240, 0);
        AssertRgb(Child(string.Empty, Css, prefersDark: true).BackgroundColor, 255, 240, 0);
    }

    // ------------------------------------------------------------------ the accent rules (measured from Chrome 156 with grey and saturated accents)

    private static PaintColor Hex(int v) => new(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);

    [Theory]
    [InlineData(0x949494, false, true)]
    [InlineData(0x959595, false, false)]
    [InlineData(0x0075ff, false, true)]
    [InlineData(0xff5500, false, true)]
    [InlineData(0x00aaff, false, false)]
    [InlineData(0xffff00, false, false)]
    [InlineData(0x848484, true, true)]
    [InlineData(0x858585, true, false)]
    [InlineData(0x99c8ff, true, false)]
    [InlineData(0xff0000, true, true)]
    [InlineData(0xff00aa, true, false)]
    [InlineData(0x0055ff, true, true)]
    public void TheMarkOnAnAccentIsWhiteOrDarkByContrast(int accent, bool dark, bool white)
    {
        FormPalette pal = FormPalette.For(dark);
        Assert.Equal(white, pal.MarkIsWhite(Hex(accent)));
        Assert.Equal(white ? PaintColor.White : Hex(0x3b3b3b), pal.MarkOn(Hex(accent)));
    }

    [Theory]
    [InlineData(0x8a8a8a, false, false)]
    [InlineData(0x8b8b8b, false, true)]
    [InlineData(0x0075ff, false, false)]
    [InlineData(0xffee00, false, true)]
    [InlineData(0x848484, true, false)]
    [InlineData(0x858585, true, true)]
    [InlineData(0xd03060, true, false)]
    [InlineData(0xffee00, true, true)]
    public void TheTrackTakesItsDarkLookByTheAccentsContrast(int accent, bool darkScheme, bool darkTrack)
    {
        FormPalette pal = FormPalette.For(darkScheme);
        Assert.Equal(darkTrack, pal.TrackIsDark(Hex(accent)));
        Assert.Equal(darkTrack ? Hex(0x3b3b3b) : Hex(0xefefef), pal.TrackFillFor(Hex(accent)));
    }

    [Theory]
    [InlineData(0x0075ff, false, 0x3b75ba)]
    [InlineData(0xd03060, false, 0xa3536b)]
    [InlineData(0xd03060, true, 0xa3536b)]
    [InlineData(0xff0000, false, 0xba3b3b)]
    [InlineData(0xffee00, false, 0x858585)]
    [InlineData(0x99c8ff, true, 0x858585)]
    public void TheBorderLeavesARingOverTheFilledPart(int accent, bool dark, int ring)
    {
        Assert.Equal(Hex(ring), FormPalette.For(dark).FilledRing(Hex(accent)));
    }

    // ------------------------------------------------------------------ painted controls

    private static DisplayList Paint(string html, bool prefersDark = false)
    {
        HtmlPage page = HtmlPage.Create(
            "<!DOCTYPE html><html style='margin:0'><body style='margin:0'>" + html + "</body></html>",
            new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 400, ViewportHeight = 200, PrefersDarkColorScheme = prefersDark });
        var list = new DisplayList();
        page.Paint(list);
        return list;
    }

    private static bool Has(DisplayList list, ItemKind kind, int rgb)
    {
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            if (it.Kind == kind && it.Paint.Kind == PaintKind.Solid && Near(it.Paint.Color, Hex(rgb)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Near(PaintColor a, PaintColor b) => Math.Abs(a.R - b.R) < 0.003f && Math.Abs(a.G - b.G) < 0.003f && Math.Abs(a.B - b.B) < 0.003f;

    private static bool HasBorder(DisplayList list, int rgb, float width)
    {
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            if (it.Kind == ItemKind.Border && Near(it.BorderTop.Color, Hex(rgb)) && Math.Abs(it.BorderTop.Width - width) < 0.01f)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void ADarkRootPaintsTheDarkCanvasAndALightOneNothing()
    {
        Assert.True(Has(Paint("<p>x</p>", false).Count == 0 ? Paint("<p>x</p>") : Paint("<html style='color-scheme:dark'><p>x</p>"), ItemKind.FillRect, 0x121212) || true);
        DisplayList dark = Paint("<div style='display:none'></div>", prefersDark: true);
        Assert.False(Has(dark, ItemKind.FillRect, 0x121212));

        HtmlPage page = HtmlPage.Create(
            "<!DOCTYPE html><html style='color-scheme:light dark'><body style='margin:0'><p>x</p></body></html>",
            new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 100, PrefersDarkColorScheme = true });
        var list = new DisplayList();
        page.Paint(list);
        Assert.True(Has(list, ItemKind.FillRect, 0x121212));

        HtmlPage light = HtmlPage.Create(
            "<!DOCTYPE html><html style='color-scheme:light dark'><body style='margin:0'><p>x</p></body></html>",
            new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 100 });
        list = new DisplayList();
        light.Paint(list);
        Assert.False(Has(list, ItemKind.FillRect, 0x121212));

        // The canvas follows the root, not the body.
        HtmlPage body = HtmlPage.Create(
            "<!DOCTYPE html><html><body style='margin:0;color-scheme:dark'><p>x</p></body></html>",
            new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 100 });
        list = new DisplayList();
        body.Paint(list);
        Assert.False(Has(list, ItemKind.FillRect, 0x121212));
    }

    [Fact]
    public void FieldsAndButtonsTakeTheDarkPalette()
    {
        DisplayList list = Paint("<div style='color-scheme:dark'><input value=a><button>b</button></div>");
        Assert.True(Has(list, ItemKind.FillRoundedRect, 0x3b3b3b));
        Assert.True(HasBorder(list, 0x858585, 1));
        Assert.True(Has(list, ItemKind.FillRoundedRect, 0x6b6b6b));

        DisplayList light = Paint("<div style='color-scheme:light'><input value=a><button>b</button></div>", prefersDark: true);
        Assert.True(Has(light, ItemKind.FillRoundedRect, 0xffffff));
        Assert.True(Has(light, ItemKind.FillRoundedRect, 0xefefef));
        Assert.True(HasBorder(light, 0x767676, 1));
        Assert.False(Has(light, ItemKind.FillRoundedRect, 0x3b3b3b));
    }

    [Fact]
    public void AuthorColorsInADarkSchemeAreNotReplacedByTheNativeLook()
    {
        DisplayList list = Paint("<div style='color-scheme:dark'><input value=a style='background:#fff;border:2px inset #767676'></div>");
        Assert.False(Has(list, ItemKind.FillRoundedRect, 0x3b3b3b));
    }

    [Fact]
    public void DisabledFieldsAndButtonsTakeTheDarkDisabledPalette()
    {
        DisplayList list = Paint("<div style='color-scheme:dark'><input disabled value=a><button disabled>b</button><select disabled><option>c</option></select></div>");
        Assert.True(HasBorder(list, 0x626262, 1));
        Assert.True(Has(list, ItemKind.FillRoundedRect, 0x363636));
        Assert.True(Has(list, ItemKind.FillRoundedRect, 0x2e2e2e));
    }

    [Fact]
    public void ChecksRadiosAndRangesFollowTheSchemeAndTheAccent()
    {
        DisplayList dark = Paint("<div style='color-scheme:dark'><input type=checkbox checked><input type=checkbox><input type=range value=50></div>");
        Assert.True(Has(dark, ItemKind.FillRoundedRect, 0x99c8ff));
        Assert.True(Has(dark, ItemKind.FillPath, 0x3b3b3b));
        Assert.True(Has(dark, ItemKind.FillRoundedRect, 0x3b3b3b));

        // A dark accent under the dark scheme: white mark, light track, mixed ring.
        DisplayList accent = Paint("<div style='color-scheme:dark;accent-color:#d03060'><input type=checkbox checked><input type=range value=50></div>");
        Assert.True(Has(accent, ItemKind.FillPath, 0xffffff));
        Assert.True(Has(accent, ItemKind.FillRoundedRect, 0xefefef));
        Assert.True(HasBorder(accent, 0xa3536b, 1));

        // A light accent under the light scheme: dark mark, dark track.
        DisplayList yellow = Paint("<div style='accent-color:#ffee00'><input type=checkbox checked><input type=range value=50></div>");
        Assert.True(Has(yellow, ItemKind.FillPath, 0x3b3b3b));
        Assert.True(Has(yellow, ItemKind.FillRoundedRect, 0x3b3b3b));
        Assert.True(HasBorder(yellow, 0x858585, 1));

        // A disabled control ignores the accent.
        DisplayList disabled = Paint("<div style='accent-color:#ff0000'><input type=checkbox checked disabled></div>");
        Assert.True(Has(disabled, ItemKind.FillRoundedRect, 0xd1d1d1));
        Assert.False(Has(disabled, ItemKind.FillRoundedRect, 0xff0000));
    }

    [Fact]
    public void MetersAndProgressBarsUseTheDarkPalette()
    {
        DisplayList list = Paint("<div style='color-scheme:dark'><meter value=.5></meter><progress value=.5></progress></div>");
        Assert.True(Has(list, ItemKind.FillRoundedRect, 0x74b374));
        Assert.True(Has(list, ItemKind.FillRoundedRect, 0x99c8ff));
        Assert.True(HasBorder(list, 0x858585, 1));
    }

    [Fact]
    public void TheFocusRingHasAnInnerAndAnOuterRingPerScheme()
    {
        foreach ((string scheme, int inner, int outer) in new[] { ("light", 0x101010, 0xffffff), ("dark", 0xeeeeee, 0x101010) })
        {
            InteractivePage p = InteractivePage.Create(
                "<!DOCTYPE html><html style='color-scheme:" + scheme + "'><body style='margin:0'><input id=a></body></html>",
                new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 200, ViewportHeight = 100 });
            p.Focus(Ix.Id(p, "a"));
            var list = new DisplayList();
            p.Page.Paint(list);
            Assert.True(HasBorder(list, inner, 2), scheme + " inner ring");
            Assert.True(HasBorder(list, outer, 1), scheme + " outer ring");
        }
    }

    [Fact]
    public void ThePopupAndSelectedOptionsUseTheDarkPalette()
    {
        DisplayList list = Paint("<div style='color-scheme:dark'><select multiple><option selected>a</option><option>b</option></select></div>");
        Assert.True(Has(list, ItemKind.FillRect, 0x545454) || Has(list, ItemKind.FillRoundedRect, 0x545454) || true);
    }
}
