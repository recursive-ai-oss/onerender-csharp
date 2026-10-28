using System.Text;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.TestSupport;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// The selectors that read form state (<c>:checked</c>, <c>:indeterminate</c>, <c>:disabled</c>, <c>:enabled</c>, <c>:read-write</c>) follow what
/// the user or the host changed (HTML §4.16.3), and the restyled page equals a page parsed with that state in its attributes.
/// </summary>
public class FormStateStyleTests
{
    private const string Css =
        "input.cb{appearance:none;width:16px;height:16px;border:2px solid #333;background:#fff;margin:4px;display:inline-block}" +
        "input.cb:checked{background:#06c;border-color:#fc0;width:20px}" +
        "input.cb:indeterminate{background:#999;height:10px}" +
        "input.cb:disabled{opacity:.5}" +
        "input.cb:enabled{outline:1px solid #0a0}" +
        "input.cb:checked + label{color:#c00;font-weight:bold}" +
        "label{display:inline-block;margin:0 6px}" +
        "li:has(:checked){background:#eef;padding-left:10px}" +
        "fieldset{margin:2px;padding:2px}" +
        "input.t:read-write{background:#efe}input.t:disabled{background:#ccc}";

    private sealed record Model(string[] Checkable, string[] Radios, string[] Disableable);

    private static readonly Model M = new(
        ["c1", "c2", "c3", "c4", "r1", "r2", "r3", "r4"],
        ["r1", "r2", "r3"],
        ["c1", "r1", "fs", "c3", "t1", "r4"]);

    // The page's markup for a given state; the same function builds the page under test and the oracle.
    private static string Body(HashSet<string> on, HashSet<string> off)
    {
        string A(string id) => (on.Contains(id) ? " checked" : string.Empty) + (off.Contains(id) ? " disabled" : string.Empty);
        var sb = new StringBuilder();
        sb.Append("<div><input id=c1 class=cb type=checkbox").Append(A("c1")).Append("><input id=c2 class=cb type=checkbox").Append(A("c2")).Append("></div>");
        sb.Append("<div><input id=r1 class=cb type=radio name=g").Append(A("r1")).Append("><input id=r2 class=cb type=radio name=g").Append(A("r2"))
            .Append("><input id=r3 class=cb type=radio name=g").Append(A("r3")).Append("></div>");
        sb.Append("<fieldset id=fs").Append(A("fs")).Append("><input id=c3 class=cb type=checkbox").Append(A("c3")).Append("><input id=t1 class=t").Append(A("t1")).Append("></fieldset>");
        sb.Append("<div><input id=c4 class=cb type=checkbox").Append(A("c4")).Append("><label for=c4>Label</label><input id=r4 class=cb type=radio").Append(A("r4")).Append("></div>");
        sb.Append("<ul><li id=li1><input id=c5 class=cb type=checkbox").Append(A("c5")).Append("></li><li id=li2>plain</li></ul>");
        return sb.ToString();
    }

    private static string Html(HashSet<string> on, HashSet<string> off) =>
        Ix.Head.Replace("</style>", Css + "</style>", StringComparison.Ordinal) + Body(on, off) + "</body></html>";

    private static InteractivePage Make(HashSet<string> on, HashSet<string> off) =>
        InteractivePage.Create(Html(on, off), new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 500, ViewportHeight = 400 });

    private static string Describe(InteractivePage p) => p.Page.Styled.Dump() + "\n--\n" + p.Page.Boxes.Dump() + "\n--\n" + p.Page.Fragments.Dump();

    private static void AssertEqualsFresh(InteractivePage p, HashSet<string> off, string what)
    {
        p.Blur();
        var on = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in (string[])[.. M.Checkable, "c5"])
        {
            if (p.IsChecked(Ix.Id(p, id)))
            {
                on.Add(id);
            }
        }

        InteractivePage fresh = Make(on, off);
        string a = Describe(p), b = Describe(fresh);
        if (!string.Equals(a, b, StringComparison.Ordinal))
        {
            string[] x = a.Split('\n'), y = b.Split('\n');
            int n = 0;
            while (n < x.Length && n < y.Length && x[n] == y[n])
            {
                n++;
            }

            throw new Xunit.Sdk.XunitException($"{what}: line {n}\n live:  {(n < x.Length ? x[n] : "(end)")}\n fresh: {(n < y.Length ? y[n] : "(end)")}");
        }
    }

    private static (double X, double Y) Center(InteractivePage p, string id)
    {
        Layout.Rect r = p.Layout.GetBoundingClientRect(Ix.Id(p, id));
        return (r.X + (r.Width / 2), r.Y + (r.Height / 2));
    }

    private static void Click(InteractivePage p, string id, double t)
    {
        (double x, double y) = Center(p, id);
        p.HandlePointer(Ix.Move(x, y, t));
        p.HandlePointer(Ix.Down(x, y, t + 1));
        p.HandlePointer(Ix.Up(x, y, t + 2));
    }

    private static string Background(InteractivePage p, string id)
    {
        StyledNode n = p.Page.Styled.FindBySource(Ix.Id(p, id).Index);
        return p.Page.Styled.Styles[p.Page.Styled.StyleOf(n.Index)].BackgroundColor.ToString();
    }

    [Fact]
    public void AClickOnAStyledCheckboxRestylesItAndItsSibling()
    {
        var none = new HashSet<string>();
        InteractivePage p = Make(none, none);
        string before = Background(p, "c1");
        Click(p, "c1", 0);
        Assert.True(p.IsChecked(Ix.Id(p, "c1")));
        Assert.NotEqual(before, Background(p, "c1"));
        AssertEqualsFresh(p, none, "checked by click");
        Click(p, "c4", 10);
        AssertEqualsFresh(p, none, "label sibling and :has");
        Click(p, "c5", 20);
        AssertEqualsFresh(p, none, ":has(:checked)");
        Click(p, "c1", 30);
        Assert.Equal(before, Background(p, "c1"));
        AssertEqualsFresh(p, none, "unchecked by click");
    }

    [Fact]
    public void ARadioGroupFollowsClicksAndArrowKeys()
    {
        var none = new HashSet<string>();
        InteractivePage p = Make(none, none);
        Click(p, "r1", 0);
        AssertEqualsFresh(p, none, "r1 by click");
        Click(p, "r3", 10);
        Assert.False(p.IsChecked(Ix.Id(p, "r1")));
        AssertEqualsFresh(p, none, "r3 by click");
        p.Focus(Ix.Id(p, "r3"), FocusReason.Keyboard);
        p.HandleKey(Ix.Key("ArrowUp"));
        AssertEqualsFresh(p, none, "arrow up");
        p.HandleKey(Ix.Key("ArrowDown"));
        p.HandleKey(Ix.Key("ArrowDown"));
        AssertEqualsFresh(p, none, "arrow down, wrapping");
    }

    [Fact]
    public void SetCheckedAndSetIndeterminateRestyle()
    {
        var none = new HashSet<string>();
        InteractivePage p = Make(none, none);
        string plain = Background(p, "c2");
        p.SetChecked(Ix.Id(p, "c2"), true);
        AssertEqualsFresh(p, none, "SetChecked");
        Assert.NotEqual(plain, Background(p, "c2"));
        p.SetIndeterminate(Ix.Id(p, "c1"), true);
        Assert.True(p.IsIndeterminate(Ix.Id(p, "c1")));
        Assert.Contains("153, 153, 153", Background(p, "c1"), StringComparison.Ordinal);
        p.SetIndeterminate(Ix.Id(p, "c1"), false);
        Assert.Equal(plain, Background(p, "c1"));
        // A click clears the indeterminate state and checks the box (HTML §4.10.5.1.15).
        p.SetIndeterminate(Ix.Id(p, "c1"), true);
        Click(p, "c1", 0);
        Assert.False(p.IsIndeterminate(Ix.Id(p, "c1")));
        Assert.True(p.IsChecked(Ix.Id(p, "c1")));
        AssertEqualsFresh(p, none, "click on an indeterminate box");
    }

    [Fact]
    public void DisabledFollowsAttributeEditsAndAFieldset()
    {
        var off = new HashSet<string>();
        InteractivePage p = Make(new HashSet<string>(), off);
        p.SetAttribute(Ix.Id(p, "c1"), "disabled", string.Empty);
        off.Add("c1");
        AssertEqualsFresh(p, off, "disabled attribute");
        p.SetAttribute(Ix.Id(p, "fs"), "disabled", string.Empty);
        off.Add("fs");
        AssertEqualsFresh(p, off, "disabled fieldset disables c3 and t1");
        Click(p, "c3", 0);
        Assert.False(p.IsChecked(Ix.Id(p, "c3")));
        p.RemoveAttribute(Ix.Id(p, "fs"), "disabled");
        off.Remove("fs");
        AssertEqualsFresh(p, off, "fieldset enabled again");
        p.RemoveAttribute(Ix.Id(p, "c1"), "disabled");
        off.Remove("c1");
        AssertEqualsFresh(p, off, "attribute removed");
    }

    [Fact]
    public void ARandomSequenceOfChangesEqualsAFreshPage()
    {
        for (int seed = 1; seed <= 12; seed++)
        {
            var rng = new Random(seed);
            var off = new HashSet<string>(StringComparer.Ordinal);
            InteractivePage p = Make(new HashSet<string>(StringComparer.Ordinal), off);
            double t = 0;
            for (int step = 0; step < 80; step++)
            {
                t += 20;
                string what;
                switch (rng.Next(6))
                {
                    case 0:
                    case 1:
                    {
                        string id = M.Checkable[rng.Next(M.Checkable.Length)];
                        what = "click " + id;
                        if (!off.Contains(id) && !(IsInDisabledFieldset(id) && off.Contains("fs")))
                        {
                            Click(p, id, t);
                        }

                        break;
                    }

                    case 2:
                    {
                        string id = M.Checkable[rng.Next(M.Checkable.Length)];
                        bool v = rng.Next(2) == 0;
                        what = "SetChecked " + id + " " + v;
                        p.SetChecked(Ix.Id(p, id), v);
                        break;
                    }

                    case 3:
                    {
                        string id = M.Disableable[rng.Next(M.Disableable.Length)];
                        what = "toggle disabled " + id;
                        if (off.Remove(id))
                        {
                            p.RemoveAttribute(Ix.Id(p, id), "disabled");
                        }
                        else
                        {
                            off.Add(id);
                            p.SetAttribute(Ix.Id(p, id), "disabled", string.Empty);
                        }

                        break;
                    }

                    case 4:
                    {
                        string id = M.Radios[rng.Next(M.Radios.Length)];
                        what = "arrow from " + id;
                        if (!off.Contains(id))
                        {
                            p.Focus(Ix.Id(p, id), FocusReason.Keyboard);
                            p.HandleKey(Ix.Key(rng.Next(2) == 0 ? "ArrowDown" : "ArrowUp"));
                        }

                        break;
                    }

                    default:
                        what = "space on c2";
                        p.Focus(Ix.Id(p, "c2"), FocusReason.Keyboard);
                        p.HandleKey(Ix.Key(" "));
                        p.HandleKey(Ix.Key(" ", false));
                        break;
                }

                AssertEqualsFresh(p, off, $"seed {seed} step {step} ({what})");
            }
        }
    }

    private static bool IsInDisabledFieldset(string id) => id is "c3";

    // Form state goes through the live cascade: restyling a styled checkbox, its sibling and its list item allocates nothing in steady
    // state. (A pointer click also builds its DOM event objects, which this test leaves out by setting the state directly.)
    [Fact]
    public void AFormStateChangeOnAStyledCheckboxAllocatesNothingInSteadyState()
    {
        var none = new HashSet<string>();
        InteractivePage p = Make(none, none);
        Node box = Ix.Id(p, "c5"), other = Ix.Id(p, "c4");
        long Round(int count)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++)
            {
                p.SetChecked(box, (i & 1) == 0);
                p.SetChecked(other, (i & 1) == 0);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Round(300);
        long best = long.MaxValue;
        for (int pass = 0; pass < 3 && best != 0; pass++)
        {
            best = Math.Min(best, Round(100));
        }

        Assert.True(best == 0, "bytes per 200 form state changes: " + best);
    }

    // Hover and focus on the HTML path go through the live cascade too.
    [Fact]
    public void AHoverChangeOnAnHtmlPageAllocatesNothingInSteadyState()
    {
        var none = new HashSet<string>();
        InteractivePage p = InteractivePage.Create(
            Ix.Head.Replace("</style>", Css + "div.h{height:30px}div.h:hover{background:#def;padding-left:4px}input.cb:hover{background:#fdd}</style>", StringComparison.Ordinal)
            + "<div class=h id=h1>one</div><div class=h id=h2>two</div><input id=c1 class=cb type=checkbox></body></html>",
            new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 500, ViewportHeight = 400 });
        _ = none;
        (double ax, double ay) = Center(p, "h1");
        (double bx, double by) = Center(p, "h2");
        (double cx, double cy) = Center(p, "c1");
        long Round(int count, ref double t)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++)
            {
                p.HandlePointer(Ix.Move(ax, ay, t += 1));
                p.HandlePointer(Ix.Move(bx, by, t += 1));
                p.HandlePointer(Ix.Move(cx, cy, t += 1));
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        double time = 0;
        Round(300, ref time);
        long best = long.MaxValue;
        for (int pass = 0; pass < 3 && best != 0; pass++)
        {
            best = Math.Min(best, Round(100, ref time));
        }

        Assert.True(best == 0, "bytes per 300 hover changes: " + best);
    }

    [Fact]
    public void AHoverChangeOnTheHudPageAllocatesNothingInSteadyState() => CheckHud();

    private static void CheckHud([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", "..", "testdata", "game-ui", "hud.html"));
        InteractivePage p = InteractivePage.Create(System.IO.File.ReadAllText(path), new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height });
        string[] ids = ["h31", "h34", "h37", "h40", "h43"];
        var points = new (double X, double Y)[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            points[i] = Center(p, ids[i]);
        }

        long Round(int count, ref double t)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++)
            {
                (double x, double y) = points[i % points.Length];
                p.HandlePointer(Ix.Move(x, y, t += 1));
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        double time = 0;
        Round(500, ref time);
        long best = long.MaxValue;
        for (int pass = 0; pass < 3 && best != 0; pass++)
        {
            best = Math.Min(best, Round(200, ref time));
        }

        Assert.True(best == 0, "bytes per 200 hover changes on the HUD page: " + best);
    }

    [Fact]
    public void TheSelectionHighlightTakesItsColorsFromTheSelectionPseudoElement()
    {
        InteractivePage p = Ix.Page("<input id=t value='hello'><input id=u value='plain'>", 300, 100, "#t::selection{background-color:#ff0000;color:#00ff00}");
        p.Focus(Ix.Id(p, "t"), FocusReason.Keyboard);
        Forms.TextEditView v = p.Page.Form.ActiveView!;
        Assert.Equal(new PaintColor(1f, 0f, 0f, 1f), v.SelectionBackground);
        Assert.Equal(new PaintColor(0f, 1f, 0f, 1f), v.SelectionForeground);
        // A field with no rule keeps the default highlight.
        p.Focus(Ix.Id(p, "u"), FocusReason.Keyboard);
        Assert.True(p.Page.Form.ActiveView!.SelectionForeground.IsTransparent);
    }
}
