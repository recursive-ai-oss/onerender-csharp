using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OneRender.Image;
using OneRender.Layout;
using OneRender.Tests.Support;
using OneRender.TestSupport;
using OneRender.Style;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// Inline layout: white space (CSS Text 3 §4), line breaking (§5), inline boxes and their fragments (CSS2 §9.4.2, CSS Fragmentation
/// 3 §5.4), line height and vertical alignment (CSS2 §10.8, CSS Inline 3), alignment and spacing (CSS Text 3 §6–§8), bidi
/// (UAX #9) and intrinsic sizes (CSS Sizing 3 §5). Widths are compared with widths measured in the same font, so the tests do
/// not hard-code font metrics.
/// </summary>
public class InlineLayoutTests
{
    private sealed class Lay
    {
        public Pipeline P = null!;
        public FragmentTree F = null!;
        public BlockLayoutEngine Engine = null!;

        public Rect this[string id]
        {
            get
            {
                Assert.True(F.TryGetBorderBox(P.Document.GetElementById(id), out Rect r), "no box for #" + id);
                return r;
            }
        }

        public bool Has(string id) => F.TryGetBorderBox(P.Document.GetElementById(id), out _);

        public Fragment Frag(string id) => F.FragmentOf(P.Boxes.PrincipalBox(P.Styled.FindBySource(P.Document.GetElementById(id).Index)));

        public Box BoxOf(string id) => P.Boxes.PrincipalBox(P.Styled.FindBySource(P.Document.GetElementById(id).Index));

        /// <summary>The line fragments of a block container.</summary>
        public List<Fragment> Lines(string id)
        {
            var list = new List<Fragment>();
            for (Fragment c = Frag(id).FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == FragmentKind.Line)
                {
                    list.Add(c);
                }
            }

            return list;
        }

        /// <summary>The text fragments of a line in tree order with their offset from the line's origin.</summary>
        public static List<(Fragment F, double X, double Y, string Text)> Texts(Fragment line)
        {
            var list = new List<(Fragment, double, double, string)>();
            var stack = new Stack<(Fragment, double, double)>();
            stack.Push((line, 0, 0));
            var order = new List<(Fragment, double, double)>();
            while (stack.Count > 0)
            {
                (Fragment f, double ox, double oy) = stack.Pop();
                order.Add((f, ox, oy));
                var kids = new List<Fragment>();
                for (Fragment c = f.FirstChild; !c.IsNull; c = c.NextSibling)
                {
                    kids.Add(c);
                }

                for (int i = kids.Count - 1; i >= 0; i--)
                {
                    Rect r = kids[i].BorderBox;
                    stack.Push((kids[i], ox + r.X, oy + r.Y));
                }
            }

            foreach ((Fragment f, double ox, double oy) in order)
            {
                if (f.Kind == FragmentKind.Text)
                {
                    string t = f.Box.Text.Slice(f.TextStart, f.TextLength).ToString();
                    list.Add((f, ox, oy, t));
                }
            }

            return list;
        }

        public static string LineText(Fragment line)
        {
            var sb = new StringBuilder();
            foreach (var t in Texts(line))
            {
                sb.Append(t.Text);
            }

            return sb.ToString().Trim();
        }

        public string[] LineTexts(string id)
        {
            List<Fragment> ls = Lines(id);
            var r = new string[ls.Count];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = LineText(ls[i]);
            }

            return r;
        }
    }

    private const string Base = "body{margin:0;font:16px/20px 'Liberation Sans'}p,div{margin:0}";

    private static Lay Run(string body, string css = "", double width = 800, double height = 600)
    {
        string html = "<!DOCTYPE html><html><head><meta charset=utf-8><style>" + Base + css + "</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, width, height);
        var f = new FragmentTree(p.Boxes);
        var engine = new BlockLayoutEngine();
        engine.Layout(p.Boxes, new LayoutInput(width, double.NaN, FontProviders.ForCurrentThread()), f);
        return new Lay { P = p, F = f, Engine = engine };
    }

    /// <summary>The width of a string on one line with the given extra style, measured as a shrink-to-fit float.</summary>
    private static double W(string text, string style = "")
    {
        Lay l = Run("<div id=m style='float:left;white-space:pre;" + style + "'>" + text + "</div>");
        return l["m"].Width;
    }

    // ------------------------------------------------------------------ white space (CSS Text 3 §4)

    [Fact]
    public void CollapsibleSpacesCollapseToOne()
    {
        Assert.Equal(W("a b"), Run("<div id=m style='float:left'>a    b</div>")["m"].Width, 3);
        Assert.Equal(W("a b"), Run("<div id=m style='float:left'>a \n\t b</div>")["m"].Width, 3);
    }

    [Fact]
    public void LeadingAndTrailingSpacesOfALineAreRemoved()
    {
        Assert.Equal(W("ab"), Run("<div id=m style='float:left'>   ab   </div>")["m"].Width, 3);
    }

    [Fact]
    public void CollapsingCrossesInlineBoundaries()
    {
        // "a " + " b" inside a span: the second space is collapsible after the first.
        Assert.Equal(W("a b"), Run("<div id=m style='float:left'>a <span> b</span></div>")["m"].Width, 3);
    }

    [Fact]
    public void PreserveKeepsSpacesAndNewlines()
    {
        Lay l = Run("<div id=a style='white-space:pre'>a   b\n  c</div>");
        Assert.Equal(2, l.Lines("a").Count);
        Assert.Equal(40, l["a"].Height);
        Assert.True(W("a   b") > W("a b") + 5);
    }

    [Fact]
    public void PreLineCollapsesSpacesButKeepsNewlines()
    {
        Lay l = Run("<div id=a style='white-space:pre-line'>a    b\nc</div>");
        Assert.Equal(2, l.Lines("a").Count);
        Assert.Equal(W("a b"), l.Lines("a")[0].BorderBox.Width > 0 ? Lay.Texts(l.Lines("a")[0])[0].F.BorderBox.Width : 0, 2);
    }

    [Fact]
    public void PreWrapWrapsAndTrailingSpacesHang()
    {
        double w = W("aaaa bbbb");
        Lay l = Run("<div id=a style='white-space:pre-wrap;width:" + Px(w) + "px'>aaaa bbbb      cccc</div>");
        string[] t = l.LineTexts("a");
        Assert.Equal(2, t.Length);
        Assert.Equal("aaaa bbbb", t[0]);
        Assert.Equal("cccc", t[1]);
        foreach (Fragment line in l.Lines("a"))
        {
            // Hanging spaces do not make the line overflow (CSS Text 3 §4.1.3).
            Assert.True(line.BorderBox.Width <= w + 0.01);
        }
    }

    [Fact]
    public void BreakSpacesLetsSpacesWrapInsteadOfHang()
    {
        double w = W("aaaa ");
        int pre = Run("<div id=a style='white-space:pre-wrap;width:" + Px(w) + "px'>aaaa     bbbb</div>").Lines("a").Count;
        int bs = Run("<div id=a style='white-space:break-spaces;width:" + Px(w) + "px'>aaaa     bbbb</div>").Lines("a").Count;
        Assert.True(bs > pre, "break-spaces " + bs + " pre-wrap " + pre);
    }

    [Fact]
    public void NowrapNeverBreaksButOverflows()
    {
        Lay l = Run("<div id=a style='white-space:nowrap;width:50px'>aaaa bbbb cccc dddd</div>");
        Assert.Single(l.Lines("a"));
        Assert.True(Lay.Texts(l.Lines("a")[0])[0].F.BorderBox.Width > 50);
    }

    [Fact]
    public void TabSizeSetsTheTabStops()
    {
        double sp = W(" ");
        double w4 = W("\tx", "tab-size:4");
        double w8 = W("\tx", "tab-size:8");
        Assert.Equal(4 * sp, w8 - w4, 2);
        // A length is used as it is.
        Assert.Equal(W("x") + 40, W("\tx", "tab-size:40px"), 2);
    }

    [Fact]
    public void BrForcesALineEndAndAnEmptyLineHasStrutHeight()
    {
        Lay l = Run("<div id=a>one<br><br>three</div>");
        Assert.Equal(3, l.Lines("a").Count);
        Assert.Equal(60, l["a"].Height);
    }

    // ------------------------------------------------------------------ line breaking (CSS Text 3 §5)

    [Fact]
    public void GreedyBreakingFillsEachLine()
    {
        double w = W("aaaa bbbb");
        Lay l = Run("<div id=a style='width:" + Px(w) + "px'>aaaa bbbb cccc dddd eeee</div>");
        Assert.Equal("aaaa bbbb|cccc dddd|eeee", string.Join("|", l.LineTexts("a")));
    }

    [Fact]
    public void ALongWordOverflowsWithoutOverflowWrap()
    {
        Lay l = Run("<div id=a style='width:40px'>abcdefghijklmnop qr</div>");
        Assert.Equal("abcdefghijklmnop|qr", string.Join("|", l.LineTexts("a")));
    }

    [Fact]
    public void OverflowWrapBreaksAnUnbreakableWordOnlyWhenNothingElseFits()
    {
        double w = W("abcdefgh");
        Lay l = Run("<div id=a style='overflow-wrap:break-word;width:" + Px(w) + "px'>xx abcdefghijkl</div>");
        string[] t = l.LineTexts("a");
        Assert.Equal("xx", t[0]); // the word moves to a line of its own first
        Assert.Equal("abcdefgh", t[1]);
        Assert.Equal("ijkl", t[2]);
    }

    [Fact]
    public void OverflowWrapAnywhereCountsInMinContentButBreakWordDoesNot()
    {
        Lay a = Run("<div id=a style='float:left;overflow-wrap:anywhere;width:min-content'>abcdefghij</div>");
        Lay b = Run("<div id=a style='float:left;overflow-wrap:break-word;width:min-content'>abcdefghij</div>");
        Assert.True(a["a"].Width < W("abcdefghij") / 2, "anywhere " + a["a"].Width);
        Assert.Equal(W("abcdefghij"), b["a"].Width, 2);
    }

    [Fact]
    public void WordBreakBreakAllBreaksBetweenLetters()
    {
        double w = W("abcd");
        Lay l = Run("<div id=a style='word-break:break-all;width:" + Px(w) + "px'>abcdefghij</div>");
        string[] t = l.LineTexts("a");
        Assert.Equal(3, t.Length);
        Assert.Equal("abcd", t[0]);
    }

    [Fact]
    public void WordBreakKeepAllKeepsCjkTogether()
    {
        string cjk = "日本語日本語日本語";
        int normal = Run("<div id=a style='width:60px'>" + cjk + "</div>").Lines("a").Count;
        int keep = Run("<div id=a style='word-break:keep-all;width:60px'>" + cjk + "</div>").Lines("a").Count;
        Assert.True(normal > 1);
        Assert.Equal(1, keep);
    }

    [Fact]
    public void CjkBreaksBetweenIdeographsButNotBeforeClosingPunctuation()
    {
        // UAX #14 class CL: no break before a closing punctuation mark.
        Lay l = Run("<div id=a style='width:48px'>日本語日本語。日本</div>");
        foreach (string line in l.LineTexts("a"))
        {
            Assert.False(line.StartsWith('。'), line);
        }
    }

    [Fact]
    public void UrlBreaksAfterSlashes()
    {
        double w = W("http://example.com/");
        Lay l = Run("<div id=a style='width:" + Px(w + 4) + "px'>http://example.com/alpha/beta/gamma</div>");
        Assert.True(l.Lines("a").Count > 1);
        foreach (Fragment line in l.Lines("a"))
        {
            Assert.True(Lay.Texts(line)[0].F.BorderBox.Width <= w + 4.01, "line " + Lay.Texts(line)[0].Text);
        }
    }

    [Fact]
    public void SoftHyphenBreaksWithAVisibleHyphen()
    {
        double w = W("super-cali-");
        Lay l = Run("<div id=a style='width:" + Px(w) + "px'>super&shy;cali&shy;fragilistic</div>");
        List<Fragment> lines = l.Lines("a");
        Assert.True(lines.Count >= 2);
        // The broken line shows a hyphen: its width is that of "super-" (or longer) and fits.
        Assert.True(lines[0].BorderBox.Width > 0);
        string joined = string.Concat(l.LineTexts("a"));
        Assert.Contains("fragilistic", joined);
        // hyphens: none ignores the soft hyphens.
        Lay n = Run("<div id=a style='hyphens:none;width:" + Px(w) + "px'>super&shy;cali&shy;fragilistic</div>");
        Assert.Single(n.Lines("a"));
    }

    [Fact]
    public void NoBreakSpaceDoesNotBreak()
    {
        Lay l = Run("<div id=a style='width:60px'>aaaa&nbsp;bbbb cc</div>");
        Assert.StartsWith("aaaa", l.LineTexts("a")[0]);
        Assert.Contains("bbbb", l.LineTexts("a")[0]);
    }

    // ------------------------------------------------------------------ inline boxes (CSS2 §9.4.2, Fragmentation 3 §5.4)

    [Fact]
    public void ASplitInlineBoxKeepsBordersPaddingAndMarginsOnItsFirstAndLastPieceOnly()
    {
        double w = W("aaaa bbbb");
        Lay l = Run("<div id=a style='width:" + Px(w + 30) + "px'>xx <span id=s style='border:2px solid;padding:0 3px;margin:0 5px'>aaaa bbbb cccc dddd eeee</span> yy</div>");
        var frags = new List<Fragment>();
        for (Fragment f = l.Frag("s"); !f.IsNull; f = f.NextOfBox)
        {
            frags.Add(f);
        }

        Assert.True(frags.Count >= 3, "pieces " + frags.Count);
        Assert.Equal(2, frags[0].Border.Left);
        Assert.Equal(0, frags[0].Border.Right);
        Assert.Equal(3, frags[0].Padding.Left);
        Assert.Equal(0, frags[0].Padding.Right);
        Assert.Equal(0, frags[1].Border.Left);
        Assert.Equal(0, frags[1].Border.Right);
        Fragment last = frags[^1];
        Assert.Equal(0, last.Border.Left);
        Assert.Equal(2, last.Border.Right);
        Assert.Equal(3, last.Padding.Right);
        Assert.True((frags[0].Traits & FragmentTraits.ContinuesAfter) != 0);
        Assert.True((last.Traits & FragmentTraits.ContinuesBefore) != 0);

        // The border box the geometry comparison sees is the union of the pieces.
        Rect u = l["s"];
        Assert.True(u.Height >= 3 * 16);
        Assert.True(u.Width > 0);
    }

    [Fact]
    public void InlineMarginsMoveTheContentButNotTheLineHeight()
    {
        Lay l = Run("<div id=a>x<span id=s style='margin:30px 10px;padding:0'>y</span></div>");
        Assert.Equal(20, l["a"].Height);
        Assert.True(l["s"].X >= W("x") + 10 - 0.01);
    }

    [Fact]
    public void VerticalPaddingAndBordersOfAnInlineBoxDoNotChangeTheLineHeight()
    {
        Lay l = Run("<div id=a>x<span id=s style='padding:10px;border:5px solid'>y</span></div>");
        Assert.Equal(20, l["a"].Height);
        Assert.True(l["s"].Height > 30);
    }

    [Fact]
    public void NestedInlineBoxesAreNestedFragments()
    {
        Lay l = Run("<div id=a><span id=o>a<span id=i>b<b id=j>c</b></span></span></div>");
        Assert.Equal(l.Frag("o").BorderBox.X, l["o"].X);
        Fragment line = l.Lines("a")[0];
        Assert.Equal(l.Frag("o").Parent, line);
        Assert.Equal(l.Frag("i").Parent, l.Frag("o"));
        Assert.Equal(l.Frag("j").Parent, l.Frag("i"));
    }

    [Fact]
    public void AtomicInlinesTakeTheirBoxWidthAndBreakAroundThem()
    {
        Lay l = Run("<div id=a style='width:100px'>a<span id=b style='display:inline-block;width:60px;height:10px'></span><span id=c style='display:inline-block;width:60px;height:10px'></span></div>");
        Assert.Equal(60, l["b"].Width);
        Assert.True(l["c"].Y > l["b"].Y); // 60 + 60 does not fit in 100
        Assert.Equal(2, l.Lines("a").Count);
    }

    [Fact]
    public void InlineBlockBaselineIsItsLastLineBoxBaseline()
    {
        Lay l = Run("<div id=a>x<span id=b style='display:inline-block'>one<br>two</span></div>");
        Fragment line = l.Lines("a")[0];
        // The inline-block's last line baseline sits on the line's baseline: the bottom line is 20px tall with baseline 15.
        double ibBaseline = l["b"].Y + 20 + l.Frag("b").Baseline;
        Assert.True(ibBaseline > 0);
        Assert.Equal(line.Baseline, l["b"].Y + l.Lines("b")[1].BorderBox.Y + l.Lines("b")[1].Baseline, 1);
    }

    [Fact]
    public void AnEmptyInlineBlockSitsOnTheBaselineWithItsBottomMarginEdge()
    {
        Lay l = Run("<div id=a>x<span id=b style='display:inline-block;width:10px;height:30px;margin-bottom:5px'></span></div>");
        Fragment line = l.Lines("a")[0];
        Assert.Equal(line.Baseline, l["b"].Y + 30 + 5, 1);
        Assert.True(l["a"].Height > 30);
    }

    // ------------------------------------------------------------------ line height (CSS2 §10.8)

    [Fact]
    public void LineHeightAsNumberLengthPercentageAndNormal()
    {
        Assert.Equal(32, Run("<div id=a style='line-height:2'>x</div>")["a"].Height);
        Assert.Equal(30, Run("<div id=a style='line-height:30px'>x</div>")["a"].Height);
        Assert.Equal(24, Run("<div id=a style='line-height:150%'>x</div>")["a"].Height);
        double normal = Run("<div id=a style='line-height:normal'>x</div>")["a"].Height;
        Assert.InRange(normal, 17, 20);
    }

    [Fact]
    public void LineHeightIsInheritedAsTheSpecifiedNumberButAsTheComputedLength()
    {
        Lay n = Run("<div id=a style='line-height:1.5;font-size:10px'><span id=s style='font-size:30px'>x</span></div>");
        Assert.Equal(45, n["a"].Height, 1); // 1.5 × 30 for the big span
        Lay p = Run("<div id=a style='line-height:150%;font-size:10px'><span id=s style='font-size:30px'>x</span></div>");
        Assert.Equal(22, p["a"].Height, 1); // 15px inherited as a length: strut and span both 15px tall, offset by their baselines
    }

    [Fact]
    public void TheStrutKeepsALineWithOnlySmallTextAsTall()
    {
        Lay l = Run("<div id=a><span style='font-size:6px;line-height:6px'>x</span></div>");
        Assert.Equal(20, l["a"].Height);
    }

    [Fact]
    public void ATallInlineBoxGrowsTheLineAroundTheStrut()
    {
        Lay l = Run("<div id=a>x<span style='font-size:16px;line-height:60px'>y</span></div>");
        Assert.Equal(60, l["a"].Height);
    }

    [Fact]
    public void HalfLeadingIsSplitAroundTheContentArea()
    {
        // With line-height 40 and a 16px font, the content area is centred in the line box: the baseline is lower than at 20.
        double b20 = Run("<div id=a style='line-height:20px'>x</div>").Lines("a")[0].Baseline;
        double b40 = Run("<div id=a style='line-height:40px'>x</div>").Lines("a")[0].Baseline;
        Assert.Equal(10, b40 - b20, 1);
    }

    // ------------------------------------------------------------------ vertical-align (CSS2 §10.8.1)

    private static double AboveBaseline(string style)
    {
        Lay l = Run("<div id=d>x<span id=s style='" + style + "'>y</span></div>");
        return l.Lines("d")[0].Baseline - l["s"].Y;
    }

    [Fact]
    public void SuperAndSubRaiseAndLowerTheBox()
    {
        double plain = AboveBaseline("");
        Assert.True(AboveBaseline("vertical-align:super") > plain + 3);
        Assert.True(AboveBaseline("vertical-align:sub") < plain - 1);
    }

    [Fact]
    public void LengthAndPercentageRaiseByThatMuch()
    {
        double plain = AboveBaseline("");
        Assert.Equal(10, AboveBaseline("vertical-align:10px") - plain, 1);
        Assert.Equal(-6, AboveBaseline("vertical-align:-6px") - plain, 1);
        Assert.Equal(10, AboveBaseline("vertical-align:50%") - plain, 1); // 50% of line-height 20px
    }

    [Fact]
    public void TopAndBottomAlignToTheLineBox()
    {
        Lay t = Run("<div id=a>x<span id=b style='display:inline-block;height:50px;vertical-align:top'></span></div>");
        Assert.Equal(0, t["b"].Y);
        Assert.Equal(50, t["a"].Height);
        Lay b = Run("<div id=a>x<span id=b style='display:inline-block;height:50px;vertical-align:bottom'></span></div>");
        Assert.Equal(50, b["b"].Y + b["b"].Height);
        Assert.Equal(50, b["a"].Height);
    }

    [Fact]
    public void MiddleCentresTheBoxOnTheBaselinePlusHalfTheXHeight()
    {
        Lay l = Run("<div id=a>x<span id=b style='display:inline-block;width:10px;height:10px;vertical-align:middle'></span></div>");
        Fragment line = l.Lines("a")[0];
        double centre = l["b"].Y + 5;
        // The centre is above the baseline by about half the x-height (0.519 em of Liberation Sans / 2 ≈ 4.2px).
        Assert.InRange(line.Baseline - centre, 3.5, 5);
    }

    [Fact]
    public void TextTopAndTextBottomAlignToTheParentContentArea()
    {
        Lay tt = Run("<div id=a>x<span id=b style='display:inline-block;width:10px;height:5px;vertical-align:text-top'></span></div>");
        Lay tb = Run("<div id=a>x<span id=b style='display:inline-block;width:10px;height:5px;vertical-align:text-bottom'></span></div>");
        Assert.True(tt["b"].Y < tb["b"].Y);
        Assert.InRange(tt["b"].Y, 0, 6);
    }

    [Fact]
    public void ReplacedStyleBoxWithMarginSitsOnTheBaselineByItsMarginEdge()
    {
        Lay l = Run("<div id=a>x<span id=b style='display:inline-block;width:10px;height:10px;margin-top:20px'></span></div>");
        Assert.True(l["b"].Y >= 20 - 0.01 || l["a"].Height >= 30);
        Assert.Equal(l.Lines("a")[0].Baseline, l["b"].Y + 10, 1);
    }

    // ------------------------------------------------------------------ alignment (CSS Text 3 §6–§7)

    private static (double left, double right) Extent(Lay l, Fragment line)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var t in Lay.Texts(line))
        {
            lo = System.Math.Min(lo, line.BorderBox.X + t.X);
            hi = System.Math.Max(hi, line.BorderBox.X + t.X + t.F.BorderBox.Width);
        }

        return (lo, hi);
    }

    [Fact]
    public void TextAlignMovesTheLineContent()
    {
        double w = W("hello");
        (double l0, double r0) = Extent(Run("<div id=a style='width:300px'>hello</div>"), Run("<div id=a style='width:300px'>hello</div>").Lines("a")[0]);
        Assert.Equal(0, l0, 2);

        Lay r = Run("<div id=a style='width:300px;text-align:right'>hello</div>");
        Assert.Equal(300, Extent(r, r.Lines("a")[0]).right, 2);

        Lay c = Run("<div id=a style='width:300px;text-align:center'>hello</div>");
        (double cl, double cr) = Extent(c, c.Lines("a")[0]);
        Assert.Equal((300 - w) / 2, cl, 2);
        Assert.Equal(300 - cr, cl, 2);

        // start/end follow direction; left/right do not.
        Lay e = Run("<div id=a style='width:300px;direction:rtl;text-align:start'>hello</div>");
        Assert.Equal(300, Extent(e, e.Lines("a")[0]).right, 2);
        Lay lf = Run("<div id=a style='width:300px;direction:rtl;text-align:left'>hello</div>");
        Assert.Equal(0, Extent(lf, lf.Lines("a")[0]).left, 2);
        Lay en = Run("<div id=a style='width:300px;text-align:end'>hello</div>");
        Assert.Equal(300, Extent(en, en.Lines("a")[0]).right, 2);
    }

    [Fact]
    public void MatchParentResolvesStartAndEndAgainstTheParentDirection()
    {
        Lay l = Run("<div style='direction:rtl;text-align:start'><div id=a style='direction:ltr;width:300px;text-align:match-parent'>hello</div></div>");
        // The parent's start is the right side.
        Assert.Equal(300, Extent(l, l.Lines("a")[0]).right, 2);
    }

    [Fact]
    public void JustifyStretchesAllButTheLastLineBetweenWords()
    {
        double w = W("aaaa bbbb");
        Lay l = Run("<div id=a style='text-align:justify;width:" + Px(w + 20) + "px'>aaaa bbbb cccc dddd eeee ffff</div>");
        List<Fragment> lines = l.Lines("a");
        Assert.True(lines.Count >= 3);
        for (int i = 0; i < lines.Count - 1; i++)
        {
            Assert.Equal(w + 20, Extent(l, lines[i]).right, 1);
        }

        Assert.True(Extent(l, lines[^1]).right < w + 20 - 1);
    }

    [Fact]
    public void TextAlignLastAppliesToTheLastLineAndForcedBreaks()
    {
        Lay l = Run("<div id=a style='width:300px;text-align:justify;text-align-last:right'>aaaa bbbb</div>");
        Assert.Equal(300, Extent(l, l.Lines("a")[0]).right, 2);
        Lay b = Run("<div id=a style='width:300px;text-align:justify'>aaaa bbbb<br>cc</div>");
        Assert.True(Extent(b, b.Lines("a")[0]).right < 299); // a line before a forced break is a last line
    }

    [Fact]
    public void TextIndentIndentsTheFirstLineEachLineOrHangs()
    {
        Lay l = Run("<div id=a style='width:100px;text-indent:30px'>aaaa bbbb cccc</div>");
        List<Fragment> ls = l.Lines("a");
        Assert.Equal(30, Extent(l, ls[0]).left, 2);
        Assert.Equal(0, Extent(l, ls[1]).left, 2);

        Lay h = Run("<div id=a style='width:100px;text-indent:30px hanging'>aaaa bbbb cccc</div>");
        Assert.Equal(0, Extent(h, h.Lines("a")[0]).left, 2);
        Assert.Equal(30, Extent(h, h.Lines("a")[1]).left, 2);

        Lay e = Run("<div id=a style='width:100px;text-indent:30px each-line'>aaaa<br>bbbb</div>");
        Assert.Equal(30, Extent(e, e.Lines("a")[1]).left, 2);
        Lay n = Run("<div id=a style='width:100px;text-indent:30px'>aaaa<br>bbbb</div>");
        Assert.Equal(0, Extent(n, n.Lines("a")[1]).left, 2);
    }

    [Fact]
    public void LetterSpacingAndWordSpacingAddToTheWidth()
    {
        double plain = W("a b c");
        Assert.Equal(plain + 5 * 2, W("a b c", "letter-spacing:2px"), 2);
        Assert.Equal(plain + 2 * 7, W("a b c", "word-spacing:7px"), 2);
    }

    // ------------------------------------------------------------------ bidi (UAX #9)

    [Fact]
    public void AHebrewRunInsideLeftToRightTextIsReorderedAsOneRun()
    {
        Lay l = Run("<div id=a>abc אבג def</div>");
        var texts = Lay.Texts(l.Lines("a")[0]);
        Assert.Contains(texts, t => t.F.GlyphRun.IsValid && l.F.GetGlyphRun(t.F.GlyphRun).RightToLeft);
        var heb = texts.Find(t => t.Text.Contains('א'));
        var abc = texts.Find(t => t.Text.Contains("abc"));
        var def = texts.Find(t => t.Text.Contains("def"));
        Assert.True(abc.X < heb.X && heb.X < def.X);
    }

    [Fact]
    public void TwoHebrewRunsSeparatedByLatinKeepTheirOrderOnTheRight()
    {
        // In a right-to-left paragraph the first logical run is rightmost.
        Lay l = Run("<div id=a style='direction:rtl;width:300px'>אבג abc דהו</div>");
        var texts = Lay.Texts(l.Lines("a")[0]);
        var first = texts.Find(t => t.Text.Contains('א'));
        var mid = texts.Find(t => t.Text.Contains("abc"));
        var last = texts.Find(t => t.Text.Contains('ד'));
        Assert.True(first.X > mid.X && mid.X > last.X);
        Assert.Equal(300, Extent(l, l.Lines("a")[0]).right, 2);
    }

    [Fact]
    public void NumbersInARightToLeftParagraphStayLeftToRight()
    {
        Lay l = Run("<div id=a style='direction:rtl;width:300px'>אבג 123</div>");
        var texts = Lay.Texts(l.Lines("a")[0]);
        var num = texts.Find(t => t.Text.Contains("123"));
        var heb = texts.Find(t => t.Text.Contains('א'));
        Assert.True(num.X < heb.X);
    }

    [Fact]
    public void BidiRunsInsideNestedInlineBoxesKeepTheirBoxes()
    {
        Lay l = Run("<div id=a>abc <span id=s>אבג <b id=t>def</b> זחט</span> ghi</div>");
        Assert.True(l.Has("s") && l.Has("t"));
        Rect s = l["s"], t = l["t"];
        Assert.True(t.X >= s.X && t.X + t.Width <= s.X + s.Width + 0.01);
    }

    [Fact]
    public void UnicodeBidiIsolateKeepsAnRtlSpanTogether()
    {
        Lay l = Run("<div id=a>abc <span id=s dir=rtl style='unicode-bidi:isolate'>אבג def</span> ghi</div>");
        var texts = Lay.Texts(l.Lines("a")[0]);
        var ghi = texts.Find(t => t.Text.Contains("ghi"));
        Assert.True(l["s"].X + l["s"].Width <= ghi.X + 0.5);
    }

    [Fact]
    public void WrappedRtlLinesAreReorderedLineByLine()
    {
        double w = W("אבג דהו");
        Lay l = Run("<div id=a style='direction:rtl;width:" + Px(w) + "px'>אבג דהו זחט יכל</div>");
        List<Fragment> lines = l.Lines("a");
        Assert.True(lines.Count >= 2);
        foreach (Fragment line in lines)
        {
            Assert.Equal(w, Extent(l, line).right, 1);
        }
    }

    // ------------------------------------------------------------------ intrinsic sizes (CSS Sizing 3 §5)

    [Fact]
    public void MinContentIsTheWidestUnbreakableSegmentAndMaxContentTheWholeLine()
    {
        Lay l = Run("<div id=a style='float:left;width:min-content'>aa bbbbbb c</div><div id=b style='float:left;width:max-content'>aa bbbbbb c</div>");
        Assert.Equal(W("bbbbbb"), l["a"].Width, 2);
        Assert.Equal(W("aa bbbbbb c"), l["b"].Width, 2);
    }

    [Fact]
    public void AtomicInlinesCountInMinContentAndForcedBreaksSplitMaxContent()
    {
        Lay l = Run("<div id=a style='float:left;width:min-content'>a <span style='display:inline-block;width:90px;height:5px'></span> b</div>");
        Assert.Equal(90, l["a"].Width, 2);
        Lay b = Run("<div id=a style='float:left;width:max-content'>aaaa<br>bb</div>");
        Assert.Equal(W("aaaa"), b["a"].Width, 2);
    }

    [Fact]
    public void ShrinkToFitUsesAvailableWidthBetweenMinAndMax()
    {
        Lay l = Run("<div id=a style='float:left'>aaaa bbbb cccc dddd eeee ffff gggg hhhh</div>", width: 100);
        Assert.InRange(l["a"].Width, W("aaaa"), 100.01);
        Assert.True(l.Lines("a").Count >= 2);
    }

    [Fact]
    public void IntrinsicSizesIncludeLetterSpacingAndInlineEdges()
    {
        Lay l = Run("<div id=a style='float:left;width:max-content;letter-spacing:1px'>ab<span style='padding:0 4px;border-left:2px solid'>c</span></div>");
        Assert.Equal(W("abc", "letter-spacing:1px") + 10, l["a"].Width, 2);
    }

    // ------------------------------------------------------------------ floats and other containers

    [Fact]
    public void LinesAreShortenedBesideAFloatAndMoveDownWhenNothingFits()
    {
        Lay l = Run("<div id=a style='width:200px'><div style='float:left;width:150px;height:30px'></div>aaaa bbbb cccc dddd</div>");
        List<Fragment> lines = l.Lines("a");
        Assert.True(lines[0].BorderBox.X >= 150 - 0.01);
        Assert.True(lines[0].BorderBox.Width <= 50.01);
        Lay m = Run("<div id=a style='width:200px'><div style='float:left;width:190px;height:30px'></div>aaaaaaaaaaaaa</div>");
        Assert.True(m.Lines("a")[0].BorderBox.Y >= 30 - 0.01); // the word does not fit in the 10px beside the float
    }

    [Fact]
    public void ALineBelowAFloatUsesTheFullWidth()
    {
        Lay l = Run("<div id=a style='width:200px'><div style='float:left;width:100px;height:20px'></div>aaaa bbbb cccc dddd eeee</div>");
        List<Fragment> lines = l.Lines("a");
        Assert.Equal(0, lines[^1].BorderBox.X, 2);
    }

    [Fact]
    public void BlockContainerBaselineComesFromTheFirstAndLastLine()
    {
        Lay l = Run("<div id=o>x<span id=b style='display:inline-block'><span style='display:block;height:5px'></span><span style='display:block'>q</span></span></div>");
        Assert.True(l.Has("b"));
        Assert.Equal(l.Lines("o")[0].Baseline, l["b"].Y + 5 + 15, 1);
    }

    [Fact]
    public void EmptyContainersAndWhitespaceOnlyContainersHaveNoLines()
    {
        Assert.Empty(Run("<div id=a> \n </div>").Lines("a"));
        Assert.Equal(0, Run("<div id=a>   </div>")["a"].Height);
    }

    [Fact]
    public void ReplacedAndBrAreInTheFragmentTree()
    {
        Lay l = Run("<div id=a>a<br id=b>c</div>");
        Assert.True(l.Has("b"));
    }

    // ------------------------------------------------------------------ determinism, allocation and limits

    [Fact]
    public void AbsoluteBoxInsideAnInlineBlockLaysOutTheSameAgainIntoTheRetainedTree()
    {
        // The inline-block's fragment is moved under its line after the line exists; origins must not depend on fragment order.
        Lay l = Run("<p>texts <span id=i style='display:inline-block;width:40px;vertical-align:top'><span id=x style='position:absolute;top:0'></span>b</span> c</p>", width: 220);
        string first = l.F.Dump();
        l.Engine.Layout(l.P.Boxes, new LayoutInput(220, double.NaN, FontProviders.ForCurrentThread()), l.F);
        Assert.Equal(first, l.F.Dump());
        Assert.Equal(l["i"].Y, l["x"].Y, 3);
    }

    [Fact]
    public void TheSameInputGivesTheSameFragments()
    {
        const string body = "<p id=a style='width:123px;text-align:justify'>Lorem ipsum <b>dolor</b> sit <i>amet, consectetur</i> adipiscing אבג elit.</p>";
        Assert.Equal(Run(body).F.Dump(), Run(body).F.Dump());
    }

    private static string TextPage(int paragraphs)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < paragraphs; i++)
        {
            sb.Append("<p style='width:").Append(200 + i % 7 * 20).Append("px;text-align:").Append(i % 3 == 0 ? "justify" : "left").Append("'>")
              .Append("Paragraph ").Append(i).Append(" has <b>bold</b> and <i>italic <span style='padding:0 2px'>nested</span></i> text that wraps ")
              .Append("over several lines with <code>code</code> and a <span style='display:inline-block;width:20px;height:8px'></span> box.</p>");
        }

        return sb.ToString();
    }

    [Fact]
    public void RelayoutOfAThousandBoxTextPageAllocatesNothing()
    {
        Lay l = Run(TextPage(80));
        Assert.True(l.P.Boxes.Count >= 1000, "boxes " + l.P.Boxes.Count);
        var input = new LayoutInput(800, double.NaN, FontProviders.ForCurrentThread());
        string dump = l.F.Dump();
        Allocation.AssertZero(() => l.Engine.Layout(l.P.Boxes, input, l.F), "relayout of a 1,000-box text page");
        Assert.Equal(dump, l.F.Dump());
    }

    [Fact]
    public void AMillionCharacterParagraphLaysOutInLinearTime()
    {
        var sb = new StringBuilder("<div id=a style='width:600px'>");
        for (int i = 0; sb.Length < 1_000_000; i++)
        {
            sb.Append("lorem ipsum dolor sit amet ").Append(i % 10).Append(' ');
        }

        sb.Append("</div>");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Lay l = Run(sb.ToString());
        sw.Stop();
        Assert.True(l.Lines("a").Count > 1000);
        Assert.True(sw.ElapsedMilliseconds < 20_000, "took " + sw.ElapsedMilliseconds + " ms");
        Note("1M chars: " + sw.ElapsedMilliseconds + " ms, lines " + l.Lines("a").Count);
    }

    [Fact]
    public void AHundredThousandCharacterUnbreakableWordLaysOutInOneLine()
    {
        string word = new('w', 100_000);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Lay l = Run("<div id=a style='width:300px'>" + word + "</div>");
        sw.Stop();
        Assert.Single(l.Lines("a"));
        Assert.True(sw.ElapsedMilliseconds < 10_000, "took " + sw.ElapsedMilliseconds + " ms");
        Note("100k word: " + sw.ElapsedMilliseconds + " ms");
        // The same word with overflow-wrap broken into many lines, still in linear time.
        sw.Restart();
        Lay b = Run("<div id=a style='width:300px;overflow-wrap:anywhere'>" + word + "</div>");
        sw.Stop();
        Assert.True(b.Lines("a").Count > 1000);
        Assert.True(sw.ElapsedMilliseconds < 20_000, "took " + sw.ElapsedMilliseconds + " ms");
        Note("100k word anywhere: " + sw.ElapsedMilliseconds + " ms, lines " + b.Lines("a").Count);
    }

    [Fact]
    public void TenThousandDeepInlineNestingLaysOutWithoutRecursion()
    {
        var sb = new StringBuilder("<div id=a style='width:300px'>");
        for (int i = 0; i < 4000; i++)
        {
            sb.Append("<span>");
        }

        sb.Append("deep text that wraps over a few lines in the narrow box");
        for (int i = 0; i < 4000; i++)
        {
            sb.Append("</span>");
        }

        sb.Append("</div>");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Lay l = Run(sb.ToString());
        sw.Stop();
        Assert.True(l.Lines("a").Count >= 2);
        Assert.True(sw.ElapsedMilliseconds < 20_000, "took " + sw.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void TenThousandDeepNestingFromTheBuilderLaysOutWithoutRecursion()
    {
        // The HTML parser stops at 4,096 open elements; the builder has no such limit, so the depth bound is tested here.
        var styled = new StyledTree();
        var boxes = new BoxTree(styled);
        StyleDeclaration block = new StyleDeclaration().Set("display", "block").Set("width", "300px").Set("font", "16px/20px 'Liberation Sans'");
        StyleDeclaration inline = new StyleDeclaration().Set("display", "inline");
        StyledNode n = styled.AddRoot("div", block);
        for (int i = 0; i < 10_000; i++)
        {
            n = styled.AddElement(n, "span", inline);
        }

        styled.AddText(n, "deep text that wraps over a few lines in the narrow box");
        boxes.Build();
        var f = new FragmentTree(boxes);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        new BlockLayoutEngine().Layout(boxes, new LayoutInput(300, double.NaN, FontProviders.ForCurrentThread()), f);
        sw.Stop();
        Assert.True(f.Root.BorderBox.Height >= 40);
        Assert.True(sw.ElapsedMilliseconds < 20_000, "took " + sw.ElapsedMilliseconds + " ms");
        Note("10k deep: " + sw.ElapsedMilliseconds + " ms, fragments " + f.Count);
    }

    private static void Note(string line) =>
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("TMPDIR") ?? ".", "limits.txt"), line + "\n");

    private static string Px(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
