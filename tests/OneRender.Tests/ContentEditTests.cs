using System.Linq;
using System.Text;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.TestSupport;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>Text and class changes on an interactive page equal a page created from the equivalent HTML.</summary>
public class ContentEditTests
{
    private const int Items = 10;

    private static readonly string[] ClassPool = ["a", "b", "c", "hide", "wide", "big", "pad", "tall"];
    private static readonly string[] Words = ["7", "x", " ", "Level 12 Ranger", "a much longer label that has to wrap across several lines of the narrow box", "\u00e9\u00e8", "0", "Two  spaces and\ttab"];

    private const string GeneratedCss = ".c::before{content:'>'}.pad::after{content:'<'}";

    private const string Css =
        "body{margin:0;font:14px Liberation Sans}.item{margin:2px;border:1px solid #888}.a{color:#c00}.b{background:#cde;padding:4px}" +
        ".c{border:2px solid #080}.hide{display:none}.wide{width:300px}.big{font-size:22px}.pad{padding:7px}.tall{height:40px}" +
        ".a + .b{margin-left:9px}.big > span{font-weight:bold}.item:hover{background:#eee}#scroller{height:60px;overflow:auto;width:200px}" +
        ".row{display:flex;gap:4px}.row .item{flex:1}input{width:80px}";

    private static string Html(string[] classes, string[] texts, bool generated = false)
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>").Append(Css).Append(generated ? GeneratedCss : string.Empty).Append("</style></head><body>");
        sb.Append("<div class=row>");
        for (int i = 0; i < Items; i++)
        {
            if (i == 5)
            {
                sb.Append("</div><input id=inp value=hello><div id=scroller><div style='height:200px'></div>");
            }

            sb.Append("<div id=i").Append(i).Append(" class=\"").Append(classes[i]).Append("\"><span id=t").Append(i).Append('>')
              .Append(System.Net.WebUtility.HtmlEncode(texts[i])).Append("</span></div>");
        }

        sb.Append("</div><p id=tail>after</p></body></html>");
        return sb.ToString();
    }

    private static string Describe(InteractivePage p) => p.Page.Styled.Dump() + "\n--\n" + p.Page.Boxes.Dump() + "\n--\n" + p.Page.Fragments.Dump();

    private static void AssertSameList(InteractivePage a, InteractivePage b, string where)
    {
        var la = new DisplayList();
        var lb = new DisplayList();
        a.Page.Paint(la);
        b.Page.Paint(lb);
        Assert.True(la.Count == lb.Count, where + ": display lists differ in length " + la.Count + " vs " + lb.Count);
        for (int k = 0; k < la.Count; k++)
        {
            // Ids come from arena slots, which an edit may allocate differently from a fresh build; what they draw must match.
            Assert.True(la[k].Kind == lb[k].Kind && la[k].ContentHash == lb[k].ContentHash && la[k].ContextHash == lb[k].ContextHash && la[k].RootBounds == lb[k].RootBounds, where + ": item " + k + " " + la[k].Kind + " " + la[k].RootBounds + " vs " + lb[k].Kind + " " + lb[k].RootBounds + " ids " + la[k].Id + "/" + lb[k].Id);
        }
    }

    private static HtmlPageOptions Options() => new(FontProviders.CreateDefault()) { ViewportWidth = 420, ViewportHeight = 3000 };

    private static void Settle(InteractivePage p)
    {
        // The same pointer, focus and scroll on both pages.
        p.Focus(Ix.Id(p, "inp"));
        p.ScrollElementTo(Ix.Id(p, "scroller"), 0, 20);
        p.HandlePointer(Ix.Move(30, 20, 2));
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(3, true, false)]
    [InlineData(4, true, false)]
    [InlineData(5, false, true)]
    [InlineData(6, false, true)]
    public void RandomEditsEqualAPageBuiltFromTheEditedHtml(int seed, bool rebuildInstead, bool generated)
    {
        var rng = new Random(seed);
        string[] classes = new string[Items];
        string[] texts = new string[Items];
        for (int i = 0; i < Items; i++)
        {
            classes[i] = i % 3 == 0 ? "item a" : "item";
            texts[i] = "item " + i;
        }

        HtmlPageOptions options = Options();
        InteractivePage p = InteractivePage.Create(Html(classes, texts, generated), options);
        p.DisableInPlaceRestyle = rebuildInstead;
        Settle(p);
        var log = new List<string>();
        int edits = 0;
        for (int step = 0; step < 700; step++)
        {
            int i = rng.Next(Items);
            Node item = Ix.Id(p, "i" + i);
            string what;
            int kind = rng.Next(9);
            if (kind < 3)
            {
                string w = Words[rng.Next(Words.Length)];
                what = $"text {i} '{w}'";
                p.SetTextContent(Ix.Id(p, "t" + i), w);
                texts[i] = w;
            }
            else if (kind < 6)
            {
                string token = ClassPool[rng.Next(ClassPool.Length)];
                bool wasThere = Array.IndexOf(classes[i].Split(' ', StringSplitOptions.RemoveEmptyEntries), token) >= 0;
                what = $"toggle {i} {token}";
                p.ToggleClass(item, token);
                var parts = classes[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (wasThere)
                {
                    parts.Remove(token);
                }
                else
                {
                    parts.Add(token);
                }

                classes[i] = string.Join(' ', parts);
            }
            else if (kind < 7)
            {
                string token = ClassPool[rng.Next(ClassPool.Length)];
                what = $"add {i} {token}";
                p.AddClass(item, token);
                var parts = classes[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (!parts.Contains(token))
                {
                    parts.Add(token);
                }

                classes[i] = string.Join(' ', parts);
            }
            else if (kind < 8)
            {
                string token = ClassPool[rng.Next(ClassPool.Length)];
                what = $"remove {i} {token}";
                p.RemoveClass(item, token);
                var parts = classes[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                parts.Remove(token);
                classes[i] = string.Join(' ', parts);
            }
            else
            {
                int k1 = rng.Next(ClassPool.Length), k2 = (k1 + 1 + rng.Next(ClassPool.Length - 1)) % ClassPool.Length;
                string v = "item " + ClassPool[k1] + " " + ClassPool[k2];
                what = $"set {i} '{v}'";
                p.SetClassName(item, v);
                classes[i] = v;
            }

            edits++;
            log.Add(what);
            Assert.Equal(classes[i], p.GetClassName(item));
            Assert.Equal(texts[i], p.GetTextContent(Ix.Id(p, "t" + i)));
            // Every 4th step (and always at the end) compare with a page created from the edited HTML; the rest only mutate.
            if (step % 4 != 0 && step != 699)
            {
                continue;
            }

            InteractivePage fresh = InteractivePage.Create(Html(classes, texts, generated), options);
            Settle(fresh);
            string a = Describe(p), b = Describe(fresh);
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                string[] x = a.Split('\n'), y = b.Split('\n');
                int n = 0;
                while (n < x.Length && n < y.Length && x[n] == y[n])
                {
                    n++;
                }

                Assert.Fail($"seed {seed} step {step} after [{string.Join("; ", log.TakeLast(6))}]: line {n}\n edited: {(n < x.Length ? x[n] : "(end)")}\n fresh:  {(n < y.Length ? y[n] : "(end)")}");
            }

            AssertSameList(p, fresh, $"seed {seed} step {step} scrollTop {p.Layout.ScrollTop(Ix.Id(p, "scroller"))} vs {fresh.Layout.ScrollTop(Ix.Id(fresh, "scroller"))} hover {Ix.IdOf(p.HoveredElement)} vs {Ix.IdOf(fresh.HoveredElement)} focus {Ix.IdOf(p.FocusedElement)} vs {Ix.IdOf(fresh.FocusedElement)}");
            Assert.Equal(fresh.Layout.GetBoundingClientRect(Ix.Id(fresh, "tail")), p.Layout.GetBoundingClientRect(Ix.Id(p, "tail")));
        }

        Assert.True(edits >= 700);
    }

    [Fact]
    public void FocusHoverScrollAndFormStateSurviveEdits()
    {
        string[] classes = new string[Items];
        string[] texts = new string[Items];
        for (int i = 0; i < Items; i++)
        {
            classes[i] = "item";
            texts[i] = "item " + i;
        }

        InteractivePage p = InteractivePage.Create(Html(classes, texts), Options());
        Node inp = Ix.Id(p, "inp");
        Settle(p);
        p.SetValue(inp, "typed");
        Node hovered = p.HoveredElement;
        double scrollTop = p.Layout.ScrollTop(Ix.Id(p, "scroller"));
        Assert.True(scrollTop > 0);
        Assert.False(hovered.IsNull);
        var clicks = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "i8")), "click", _ => clicks.Add("i8"));

        HtmlPage page = p.Page;
        p.SetTextContent(Ix.Id(p, "t6"), "changed");
        Assert.Same(page, p.Page); // text on a plain element is updated in place
        p.AddClass(Ix.Id(p, "i7"), "big");
        Assert.Same(page, p.Page); // so is a class change that creates and removes no boxes
        p.SetTextContent(Ix.Id(p, "t8"), "changed too");
        p.ToggleClass(Ix.Id(p, "i9"), "wide");

        Assert.True(p.FocusedElement.Equals(inp));
        Assert.Equal("typed", p.GetValue(inp));
        Assert.True(p.HoveredElement.Equals(hovered));
        Assert.Equal(scrollTop, p.Layout.ScrollTop(Ix.Id(p, "scroller")));
        Assert.Equal("changed", p.GetTextContent(Ix.Id(p, "t6")));
        Assert.Equal("item big", p.GetClassName(Ix.Id(p, "i7")));
        p.Click(Ix.Id(p, "i8"), 10);
        Assert.Equal(["i8"], clicks);
    }

    [Fact]
    public void BadArgumentsAreRefusedWithoutChangingThePage()
    {
        InteractivePage p = InteractivePage.Create("<body><p id=a>x</p><p id=b></p><p id=c>one <b>two</b></p></body>", Options());
        string before = Describe(p);
        Assert.Throws<ArgumentException>(() => p.SetTextContent(Ix.Id(p, "a"), string.Empty));
        Assert.Throws<InvalidOperationException>(() => p.SetTextContent(Ix.Id(p, "b"), "text"));
        Assert.Throws<InvalidOperationException>(() => p.SetTextContent(Ix.Id(p, "c"), "text"));
        Assert.Throws<ArgumentException>(() => p.AddClass(Ix.Id(p, "a"), "two words"));
        Assert.Throws<ArgumentException>(() => p.AddClass(Ix.Id(p, "a"), string.Empty));
        Assert.Throws<ArgumentException>(() => p.SetTextContent(default, "x"));
        InteractivePage other = InteractivePage.Create("<body><p id=a>x</p></body>", Options());
        Assert.Throws<ArgumentException>(() => p.AddClass(Ix.Id(other, "a"), "k"));
        Assert.Equal(InputOutcome.None, p.SetTextContent(Ix.Id(p, "a"), "x"));
        Assert.Equal(InputOutcome.None, p.RemoveClass(Ix.Id(p, "a"), "none"));
        Assert.Equal(before, Describe(p));
    }

    [Fact]
    public void ClassListSemantics()
    {
        InteractivePage p = InteractivePage.Create("<body><p id=a class=' x  y '>t</p></body>", Options());
        Node a = Ix.Id(p, "a");
        p.AddClass(a, "z");
        Assert.Equal("x  y z".Replace("  ", " "), p.GetClassName(a));
        p.RemoveClass(a, "x");
        Assert.Equal("y z", p.GetClassName(a));
        p.ToggleClass(a, "y", force: true);
        Assert.Equal("y z", p.GetClassName(a));
        p.ToggleClass(a, "y", force: false);
        Assert.Equal("z", p.GetClassName(a));
        p.SetClassName(a, string.Empty);
        Assert.Equal(string.Empty, p.GetClassName(a));
    }

    [Fact]
    public void TextAndClassChangesOnTheHudPageEqualAFreshPageAndAreMeasured()
    {
        const string Rule = ".hl{color:#f00 !important;font-size:15px !important}";
        string html = GameUiScene.Html().Replace("</style>", Rule + "</style>", StringComparison.Ordinal);
        var options = new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height };
        InteractivePage p = InteractivePage.Create(html, options);
        Node level = Ix.Id(p, "level");
        string[] labels = ["Level 7", "Level 42 Ranger", "Level 100 Grand Ranger of the North"];
        int n = 0;
        void Text() => p.SetTextContent(level, labels[n++ % labels.Length]);
        long textBytes = Allocation.Measure(Text, warmup: 6, iterations: 30) / 30;
        void Toggle() => p.ToggleClass(level, "hl");
        long classBytes = Allocation.Measure(Toggle, warmup: 6, iterations: 30) / 30;
        TestContext.Current.SendDiagnosticMessage($"HUD page, per change: text {textBytes} B, class toggle {classBytes} B");

        // The same two edits as source changes of a fresh page.
        InteractivePage a = InteractivePage.Create(html, options);
        Assert.Equal(InputOutcome.Relaid, a.SetTextContent(Ix.Id(a, "level"), "Level 7") & InputOutcome.Relaid);
        Assert.Equal(InputOutcome.Relaid, a.AddClass(Ix.Id(a, "level"), "hl") & InputOutcome.Relaid);
        string edited = html.Replace(">Level 42 Ranger<", ">Level 7<", StringComparison.Ordinal).Replace("<span id=\"level\" style=", "<span id=\"level\" class=\"hl\" style=", StringComparison.Ordinal);
        Assert.NotEqual(html, edited);
        InteractivePage b = InteractivePage.Create(edited, options);
        Assert.Equal(Describe(b), Describe(a));
        AssertSameList(a, b, "hud");
    }
}
