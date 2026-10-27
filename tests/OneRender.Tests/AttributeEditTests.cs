using System.Linq;
using System.Text;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.TestSupport;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>Attribute edits on an interactive page equal a page created from the edited HTML, with the same focus and pointer.</summary>
public class AttributeEditTests
{
    private static readonly string[] Ids = ["fs", "t1", "c1", "b1", "r1", "r2", "r3", "s1", "ta", "a1", "n1", "d1"];
    private static readonly string[] Disableable = ["fs", "t1", "c1", "b1", "r1", "r2", "r3", "s1", "ta"];
    private static readonly string[] Words = ["abc", "7", "a longer value", ""];

    // The model: per element, its attributes in document order (an edit that adds one appends it, as the editor does).
    private sealed class Model
    {
        public readonly Dictionary<string, List<KeyValuePair<string, string>>> Attrs = [];

        public Model()
        {
            foreach (string id in Ids)
            {
                Attrs[id] = [];
            }

            Attrs["t1"].Add(new("type", "text"));
            Attrs["t1"].Add(new("value", "hello"));
            Attrs["c1"].Add(new("type", "checkbox"));
            Attrs["b1"].Add(new("type", "button"));
            foreach (string r in new[] { "r1", "r2", "r3" })
            {
                Attrs[r].Add(new("type", "radio"));
                Attrs[r].Add(new("name", "g"));
            }

            Attrs["n1"].Add(new("type", "text"));
            Attrs["n1"].Add(new("value", "12"));
            Attrs["a1"].Add(new("href", "#x"));
        }

        public string? Get(string id, string name) => Attrs[id].Where(p => p.Key == name).Select(p => (string?)p.Value).FirstOrDefault();

        public void Set(string id, string name, string value)
        {
            List<KeyValuePair<string, string>> l = Attrs[id];
            int i = l.FindIndex(p => p.Key == name);
            if (i >= 0)
            {
                l[i] = new(name, value);
            }
            else
            {
                l.Add(new(name, value));
            }
        }

        public void Remove(string id, string name) => Attrs[id].RemoveAll(p => p.Key == name);

        private string A(string id)
        {
            var sb = new StringBuilder(" id=" + id);
            foreach (var p in Attrs[id])
            {
                sb.Append(' ').Append(p.Key).Append("=\"").Append(System.Net.WebUtility.HtmlEncode(p.Value)).Append('"');
            }

            return sb.ToString();
        }

        public string Html() =>
            "<!DOCTYPE html><html><head><style>body{margin:0;font:14px Liberation Sans}button,input,select,textarea{margin:2px}[hidden]{display:none}" +
            "a{color:#06c}a:hover{color:#c60}button:hover{background:#def}input:focus,button:focus{outline:2px solid #f0f}.dis{color:#999}</style></head><body>" +
            "<fieldset" + A("fs") + "><legend>Group</legend><input" + A("t1") + "><input" + A("c1") + "><button" + A("b1") + ">One</button></fieldset>" +
            "<p><input" + A("r1") + "><input" + A("r2") + "><input" + A("r3") + "></p>" +
            "<select" + A("s1") + "><option>a</option><option selected>b</option></select><textarea" + A("ta") + ">text</textarea>" +
            "<a" + A("a1") + ">link</a><input" + A("n1") + "><div" + A("d1") + ">div</div><p>tail</p></body></html>";
    }

    private static string Describe(InteractivePage p) => p.Page.Styled.Dump() + "\n--\n" + p.Page.Boxes.Dump() + "\n--\n" + p.Page.Fragments.Dump();

    private static HtmlPageOptions Options() => new(FontProviders.CreateDefault()) { ViewportWidth = 400, ViewportHeight = 3000 };

    private static string Ids2(InteractivePage p) => string.Join(",", p.GetTabOrder().Select(Ix.IdOf));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RandomAttributeEditsEqualAPageBuiltFromTheEditedHtml(int seed)
    {
        var rng = new Random(seed);
        var model = new Model();
        HtmlPageOptions options = Options();
        InteractivePage p = InteractivePage.Create(model.Html(), options);
        var log = new List<string>();
        double px = -1, py = -1;
        for (int step = 0; step < 600; step++)
        {
            string what;
            int kind = rng.Next(16);
            switch (kind)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                {
                    string id = Disableable[rng.Next(Disableable.Length)];
                    bool on = model.Get(id, "disabled") is not null;
                    what = (on ? "enable " : "disable ") + id;
                    if (on)
                    {
                        p.RemoveAttribute(Ix.Id(p, id), "disabled");
                        model.Remove(id, "disabled");
                    }
                    else
                    {
                        p.SetAttribute(Ix.Id(p, id), "disabled", string.Empty);
                        model.Set(id, "disabled", string.Empty);
                    }

                    break;
                }

                case 4:
                {
                    string id = new[] { "b1", "d1", "t1" }[rng.Next(3)];
                    bool on = model.Get(id, "hidden") is not null;
                    what = (on ? "show " : "hide ") + id;
                    if (on)
                    {
                        p.RemoveAttribute(Ix.Id(p, id), "hidden");
                        model.Remove(id, "hidden");
                    }
                    else
                    {
                        p.SetAttribute(Ix.Id(p, id), "hidden", string.Empty);
                        model.Set(id, "hidden", string.Empty);
                    }

                    break;
                }

                case 5:
                {
                    string id = new[] { "c1", "r1", "r2", "r3" }[rng.Next(4)];
                    bool on = model.Get(id, "checked") is not null;
                    what = (on ? "uncheck " : "check ") + id;
                    if (on)
                    {
                        p.RemoveAttribute(Ix.Id(p, id), "checked");
                        model.Remove(id, "checked");
                    }
                    else
                    {
                        p.SetAttribute(Ix.Id(p, id), "checked", string.Empty);
                        model.Set(id, "checked", string.Empty);
                    }

                    break;
                }

                case 6:
                {
                    string id = rng.Next(2) == 0 ? "t1" : "n1";
                    string v = Words[rng.Next(Words.Length)];
                    what = $"value {id} '{v}'";
                    if (p.FocusedElement.Equals(Ix.Id(p, id)))
                    {
                        // The caret and selection of a focused field are interaction state, not markup: a fresh page picks its own.
                        what += " (skipped, focused)";
                        break;
                    }

                    p.SetAttribute(Ix.Id(p, id), "value", v);
                    model.Set(id, "value", v);
                    break;
                }

                case 7:
                {
                    // The type of a focused control cannot change; the page says so and nothing happens.
                    string next = model.Get("n1", "type") == "text" ? "number" : "text";
                    what = "type n1 " + next;
                    if (p.FocusedElement.Equals(Ix.Id(p, "n1")))
                    {
                        Assert.Throws<InvalidOperationException>(() => p.SetAttribute(Ix.Id(p, "n1"), "type", next));
                    }
                    else
                    {
                        p.SetAttribute(Ix.Id(p, "n1"), "type", next);
                        model.Set("n1", "type", next);
                    }

                    break;
                }

                case 8:
                {
                    string v = new[] { "display:none", "width:120px", "color:red;background:#eee", "" }[rng.Next(4)];
                    what = $"style b1 '{v}'";
                    if (v.Length == 0)
                    {
                        p.RemoveAttribute(Ix.Id(p, "b1"), "style");
                        model.Remove("b1", "style");
                    }
                    else
                    {
                        p.SetAttribute(Ix.Id(p, "b1"), "style", v);
                        model.Set("b1", "style", v);
                    }

                    break;
                }

                case 9:
                {
                    bool on = model.Get("a1", "href") is not null;
                    what = on ? "href off" : "href on";
                    if (on)
                    {
                        p.RemoveAttribute(Ix.Id(p, "a1"), "href");
                        model.Remove("a1", "href");
                    }
                    else
                    {
                        p.SetAttribute(Ix.Id(p, "a1"), "href", "#y");
                        model.Set("a1", "href", "#y");
                    }

                    break;
                }

                case 10:
                {
                    string id = rng.Next(2) == 0 ? "d1" : "a1";
                    string v = new[] { "-1", "0", "2", "" }[rng.Next(4)];
                    what = $"tabindex {id} '{v}'";
                    if (v.Length == 0)
                    {
                        p.RemoveAttribute(Ix.Id(p, id), "tabindex");
                        model.Remove(id, "tabindex");
                    }
                    else
                    {
                        p.SetAttribute(Ix.Id(p, id), "tabindex", v);
                        model.Set(id, "tabindex", v);
                    }

                    break;
                }

                case 11:
                {
                    string v = new[] { "g", "h" }[rng.Next(2)];
                    what = "name r3 " + v;
                    p.SetAttribute(Ix.Id(p, "r3"), "name", v);
                    model.Set("r3", "name", v);
                    break;
                }

                case 12:
                {
                    string v = new[] { "dis", "dis big", "" }[rng.Next(3)];
                    what = $"class t1 '{v}'";
                    p.SetAttribute(Ix.Id(p, "t1"), "class", v);
                    model.Set("t1", "class", v);
                    break;
                }

                case 13:
                case 14:
                    what = "tab";
                    p.HandleKey(Ix.Key("Tab", m: rng.Next(3) == 0 ? KeyModifiers.Shift : KeyModifiers.None));
                    break;
                default:
                {
                    px = rng.Next(400);
                    py = rng.Next(200);
                    what = $"move {px},{py}";
                    p.HandlePointer(Ix.Move(px, py, step));
                    break;
                }
            }

            log.Add(what);
            if (step % 3 != 0 && step != 599)
            {
                continue;
            }

            InteractivePage fresh = InteractivePage.Create(model.Html(), options);
            string focusId = Ix.IdOf(p.FocusedElement);
            if (!p.FocusedElement.IsNull)
            {
                fresh.Focus(Ix.Id(fresh, focusId), FocusReason.Keyboard);
            }

            if (px >= 0)
            {
                fresh.HandlePointer(Ix.Move(px, py, 1000 + step));
            }

            string where = $"seed {seed} step {step} after [{string.Join("; ", log.TakeLast(5))}]";
            Assert.True(focusId == Ix.IdOf(fresh.FocusedElement), where + $": focus {focusId} vs {Ix.IdOf(fresh.FocusedElement)}");
            Assert.True(Ids2(p) == Ids2(fresh), where + $": tab order {Ids2(p)} vs {Ids2(fresh)}");
            Assert.True(Ix.IdOf(p.HoveredElement) == Ix.IdOf(fresh.HoveredElement), where + ": hover");
            string a = Describe(p), b = Describe(fresh);
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                string[] x = a.Split('\n'), y = b.Split('\n');
                int n = 0;
                while (n < x.Length && n < y.Length && x[n] == y[n])
                {
                    n++;
                }

                Assert.Fail($"{where}: line {n}\n edited: {(n < x.Length ? x[n] : "(end)")}\n fresh:  {(n < y.Length ? y[n] : "(end)")}");
            }

            var la = new DisplayList();
            var lb = new DisplayList();
            p.Page.Paint(la);
            fresh.Page.Paint(lb);
            Assert.True(la.Count == lb.Count, where + ": display list length " + la.Count + " vs " + lb.Count + " focus=" + focusId);
            for (int k = 0; k < la.Count; k++)
            {
                Assert.True(la[k].Kind == lb[k].Kind && la[k].ContentHash == lb[k].ContentHash && la[k].ContextHash == lb[k].ContextHash && la[k].RootBounds == lb[k].RootBounds, where + ": item " + k + " " + la[k].Kind + "/" + lb[k].Kind + " " + la[k].RootBounds + " vs " + lb[k].RootBounds + " focus=" + focusId);
            }

            // click() on the button runs its listeners on both pages unless it is disabled.
            int cp = 0, cf = 0;
            p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "b1")), "click", _ => cp++, new EventListenerOptions { Once = true });
            fresh.Events.AddEventListener(EventTarget.Of(Ix.Id(fresh, "b1")), "click", _ => cf++);
            p.Click(Ix.Id(p, "b1"), 5000 + step);
            fresh.Click(Ix.Id(fresh, "b1"), 5000 + step);
            Assert.True(cp == cf, where + $": click handling {cp} vs {cf}");
        }
    }

    [Fact]
    public void DisabledFollowsEverywhere()
    {
        string html = "<body><button id=b>x</button><input id=r1 type=radio name=g checked><input id=r2 type=radio name=g><input id=t><fieldset id=fs><input id=in></fieldset></body>";
        InteractivePage p = InteractivePage.Create(html, Options());
        Node b = Ix.Id(p, "b");
        Assert.Equal(["b", "r1", "t", "in"], p.GetTabOrder().Select(Ix.IdOf).ToArray());
        p.Focus(b);
        Assert.True(p.FocusedElement.Equals(b));
        p.SetAttribute(b, "disabled", string.Empty);
        Assert.True(p.FocusedElement.IsNull); // focus fixup
        Assert.True(Ids2(p) == "r1,t,in", Ids2(p) + " checked r1=" + p.IsChecked(Ix.Id(p, "r1")) + " r2=" + p.IsChecked(Ix.Id(p, "r2")) + " attr=" + p.GetAttribute(Ix.Id(p, "r1"), "checked"));
        Assert.False(p.Focus(b) != InputOutcome.None);
        p.SetAttribute(Ix.Id(p, "r1"), "disabled", string.Empty);
        Assert.Equal(["r2", "t", "in"], p.GetTabOrder().Select(Ix.IdOf).ToArray()); // the group's stop moves to an enabled radio
        p.SetAttribute(Ix.Id(p, "fs"), "disabled", string.Empty);
        Assert.Equal(["r2", "t"], p.GetTabOrder().Select(Ix.IdOf).ToArray());
        p.RemoveAttribute(Ix.Id(p, "fs"), "disabled");
        p.RemoveAttribute(b, "disabled");
        Assert.Equal(["b", "r2", "t", "in"], p.GetTabOrder().Select(Ix.IdOf).ToArray());
        Assert.Equal("disabled", p.GetAttribute(Ix.Id(p, "r1"), "DISABLED") is null ? null : "disabled");
        Assert.Null(p.GetAttribute(b, "disabled"));
    }

    [Fact]
    public void RefusalsAndNoOps()
    {
        InteractivePage p = InteractivePage.Create("<html><head><style>p{color:red}</style></head><body><p id=a title=t>x</p><svg id=s><rect/></svg><input id=i></body></html>", Options());
        Node a = Ix.Id(p, "a");
        Node i = Ix.Id(p, "i");
        p.Focus(i);
        string before = Describe(p);
        Assert.Equal(InputOutcome.None, p.SetAttribute(a, "title", "t"));
        Assert.Equal(InputOutcome.None, p.RemoveAttribute(a, "lang"));
        Assert.Throws<ArgumentException>(() => p.SetAttribute(a, "bad name", "v"));
        Assert.Throws<ArgumentException>(() => p.SetAttribute(a, string.Empty, "v"));
        Assert.Throws<ArgumentException>(() => p.SetAttribute(default, "title", "v"));
        Assert.Throws<InvalidOperationException>(() => p.SetAttribute(Ix.Id(p, "s"), "width", "5"));
        Assert.Throws<InvalidOperationException>(() => p.SetAttribute(p.Page.Document.DocumentElement.QuerySelector("style"), "media", "print"));
        Assert.Throws<InvalidOperationException>(() => p.SetAttribute(i, "type", "number"));
        Assert.Equal(before, Describe(p));
        Assert.Equal("t", p.GetAttribute(a, "title"));
        // Attributes keep working after others changed; class takes its own path.
        p.SetAttribute(a, "class", "k");
        Assert.Equal("k", p.GetClassName(a));
        Assert.Equal("k", p.GetAttribute(a, "class"));
        p.RemoveAttribute(a, "class");
        Assert.Null(p.GetAttribute(a, "class"));
    }

    [Fact]
    public void UserStateSurvivesAnAttributeEditOfAnotherControl()
    {
        InteractivePage p = InteractivePage.Create("<body><input id=c type=checkbox><input id=t value=a><button id=b></button></body>", Options());
        p.SetChecked(Ix.Id(p, "c"), true);
        p.Focus(Ix.Id(p, "t"));
        p.SetValue(Ix.Id(p, "t"), "typed");
        p.Blur();
        p.SetAttribute(Ix.Id(p, "b"), "disabled", string.Empty);
        Assert.True(p.IsChecked(Ix.Id(p, "c")));
        Assert.Equal("typed", p.GetValue(Ix.Id(p, "t")));
    }

    [Fact]
    public void AttributeChangesOnTheHudPageAreMeasured()
    {
        var options = new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height };
        InteractivePage p = InteractivePage.Create(GameUiScene.Html(), options);
        Node level = Ix.Id(p, "level");
        bool on = false;
        void Toggle()
        {
            on = !on;
            if (on)
            {
                p.SetAttribute(level, "title", "x");
            }
            else
            {
                p.RemoveAttribute(level, "title");
            }
        }

        long bytes = Allocation.Measure(Toggle, warmup: 6, iterations: 30) / 30;
        TestContext.Current.SendDiagnosticMessage($"HUD page, per attribute change: {bytes} B");
        Assert.True(bytes < 8_000_000);
    }

    [Fact]
    public void SetValueOnAControlThatWasNeverFocused()
    {
        InteractivePage p = InteractivePage.Create("<body><input id=t value=a><textarea id=ta>x</textarea></body>", Options());
        Node t = Ix.Id(p, "t");
        Assert.True(p.FocusedElement.IsNull);
        p.SetValue(t, "typed");
        p.SetValue(Ix.Id(p, "ta"), "many\nlines");
        Assert.Equal("typed", p.GetValue(t));
        Assert.True(p.FocusedElement.IsNull);
        InteractivePage fresh = InteractivePage.Create("<body><input id=t value=typed><textarea id=ta>many\nlines</textarea></body>", Options());
        Assert.Equal(Describe(fresh), Describe(p));
    }
}
