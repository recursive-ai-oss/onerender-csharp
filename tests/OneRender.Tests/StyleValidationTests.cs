using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// An invalid declaration is dropped while the cascade collects declarations, so an earlier valid one wins
/// (CSS Syntax 3 §5.4.6, CSS Cascade 4 §4.3), and a value that is invalid only at computed-value time is <c>unset</c>
/// (CSS Variables 1 §3.2).
/// </summary>
public class StyleValidationTests
{
    private static StyledNode Anchor(Pipeline p)
    {
        for (int i = 0; i < p.Styled.Count; i++)
        {
            var n = new StyledNode(p.Styled, i);
            if (n.Kind == StyledNodeKind.Element && n.TagName == "a")
            {
                return n;
            }
        }

        throw new InvalidOperationException("no <a>");
    }

    private static string Dump(string css, out int diagnostics)
    {
        Pipeline p = Pipeline.FromHtml("<style>" + css + "</style><a>x</a>", userAgentSheet: false);
        StyledNode a = Anchor(p);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Properties.Count; i++)
        {
            sb.Append(ComputedValueText.Get((PropertyId)i, in a.Values)).Append('|');
        }

        diagnostics = p.Styled.Diagnostics.Items.Count;
        return sb.ToString();
    }

    // Each row: property, a valid value that changes the computed style, an invalid value for it.
    [Theory]
    // lengths
    [InlineData("width", "10px", "10")]
    [InlineData("min-height", "4px", "-4px")]
    [InlineData("margin-left", "5px", "5")]
    [InlineData("padding-top", "3px", "-3px")]
    [InlineData("border-top-width", "6px;border-top-style:solid", "-1px")]
    [InlineData("line-height", "20px", "-1px")]
    [InlineData("letter-spacing", "2px", "2")]
    [InlineData("text-indent", "7px", "7")]
    [InlineData("top", "7px", "7")]
    // colors
    [InlineData("color", "red", "bogus")]
    [InlineData("border-top-color", "red", "bogus")]
    // keywords and numbers
    [InlineData("float", "left", "sideways")]
    [InlineData("clear", "both", "sideways")]
    [InlineData("box-sizing", "border-box", "padding-box")]
    [InlineData("text-align", "center", "middle")]
    [InlineData("direction", "rtl", "up")]
    [InlineData("text-transform", "uppercase", "shout")]
    [InlineData("vertical-align", "middle", "centre")]
    [InlineData("z-index", "3", "3px")]
    [InlineData("display", "block", "bogus")]
    [InlineData("position", "absolute", "bogus")]
    [InlineData("overflow-x", "hidden", "bogus")]
    // flexbox
    [InlineData("flex-direction", "column", "sideways")]
    [InlineData("flex-wrap", "wrap", "sideways")]
    [InlineData("flex-grow", "2", "-1")]
    [InlineData("flex-basis", "10px", "10")]
    [InlineData("order", "3", "3px")]
    [InlineData("align-items", "center", "bogus")]
    [InlineData("justify-content", "center", "bogus")]
    [InlineData("column-gap", "9px", "9")]
    [InlineData("flex", "2 1 10px", "1 2 3 4")]
    [InlineData("flex-flow", "column wrap", "column bogus")]
    [InlineData("place-items", "center", "bogus")]
    // grid
    [InlineData("grid-template-columns", "100px 1fr", "100px 1x")]
    [InlineData("grid-template-areas", "'a b'", "a")]
    [InlineData("grid-auto-flow", "column", "diagonal")]
    [InlineData("grid-column", "2 / 4", "2 / 4 / 5")]
    [InlineData("grid-area", "1 / 2 / 3 / 4", "1 / 2 / 3 / 4 / 5")]
    [InlineData("grid-template", "'a a' 10px / 1fr", "'a a' 10px / bogus")]
    [InlineData("grid", "auto-flow / 1fr 1fr", "auto-flow / bogus")]
    // the shorthands OneHtml keeps as written
    [InlineData("white-space", "pre", "pre foo")]
    [InlineData("white-space", "nowrap", "bogus")]
    [InlineData("border-radius", "5px 6px", "5px 6x")]
    [InlineData("border-radius", "5px / 10px", "5px / bogus")]
    [InlineData("overflow", "hidden", "bogus")]
    [InlineData("columns", "2 100px", "2 2 2")]
    [InlineData("column-rule", "2px solid", "2px bogus")]
    [InlineData("break-before", "page", "pagey")]
    public void AnEarlierValidDeclarationBeatsALaterInvalidOne(string property, string valid, string invalid)
    {
        string baseline = Dump("a{}", out _);
        string expected = Dump("a{" + property + ":" + valid + "}", out int d0);
        Assert.NotEqual(baseline, expected); // the valid value does something
        Assert.Equal(0, d0);

        string actual = Dump("a{" + property + ":" + valid + "} a{" + property + ":" + invalid + "}", out int d1);
        Assert.Equal(expected, actual);
        Assert.Equal(0, d1); // dropped by the cascade, not reported as an ignored value later
    }

    [Fact]
    public void AnInvalidImportantDeclarationDoesNotOverrideAValidOne()
    {
        // The dropped declaration does not count as !important (Cascade 4 §4.3).
        string expected = Dump("a{width:10px}", out _);
        string actual = Dump("a{width:10px} a{width:bogus !important}", out int d);
        Assert.Equal(expected, actual);
        Assert.Equal(0, d);
    }

    [Fact]
    public void AnInvalidInlineStyleKeepsTheSheetValue()
    {
        Pipeline p = Pipeline.FromHtml("<style>a{width:10px}</style><a style='width:bogus'>x</a>", userAgentSheet: false);
        Assert.Equal(10, Anchor(p).Values.Width.Value);
    }

    [Fact]
    public void AValueThatIsInvalidAtComputedValueTimeIsUnset()
    {
        // CSS Variables 1 §3.2: the declaration took part in the cascade, so the earlier width does not come back.
        Pipeline p = Pipeline.FromHtml("<style>a{--v:bogus} a{width:10px} a{width:var(--v)}</style><a>x</a>", userAgentSheet: false);
        Assert.True(Anchor(p).Values.Width.IsAuto);
        Assert.Contains(p.Styled.Diagnostics.Items, i => i.Kind == DiagnosticKind.InvalidValue);
    }

    [Fact]
    public void ValidShorthandWithVarStillWorks()
    {
        Pipeline p = Pipeline.FromHtml("<style>a{--r:4px} a{border-radius:var(--r)}</style><a>x</a>", userAgentSheet: false);
        Assert.Equal(4, Anchor(p).Values.BorderTopLeftRadius.Horizontal.Value);
    }

    [Fact]
    public void EveryPropertyOneHtmlDoesNotTypeIsCheckedInTheCascade()
    {
        // OneHtml types these itself, so the validator is not asked about them and their invalid values still
        // fall back to unset with a diagnostic.
        string[] typed =
        [
            "display", "position", "top", "right", "bottom", "left", "overflow-x", "overflow-y", "visibility", "font-size",
            "font-weight", "font-style", "font-family",
        ];
        var failures = new List<string>();
        for (int i = 0; i < Properties.Count; i++)
        {
            string name = Properties.NameOf((PropertyId)i);
            if (Array.IndexOf(typed, name) >= 0 || name.StartsWith("--", StringComparison.Ordinal))
            {
                continue; // custom properties accept any value, so nothing is invalid
            }

            Dump("a{" + name + ":~bogus 1 2 (}", out int d);
            if (d != 0)
            {
                failures.Add(name);
            }
        }

        Assert.True(failures.Count == 0, "reported at style time: " + string.Join(", ", failures));
    }
}
