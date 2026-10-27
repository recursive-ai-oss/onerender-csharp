using System.Linq;
using System.Net;
using System.Text;
using OneRender.Forms;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>Random edit sequences: the page that applied every edit in place must lay out exactly like a page built from the resulting values.</summary>
public class TextEditingEqualityTests
{
    private static readonly string[] Ids = ["a", "b", "c", "d"];

    private static string Html(string[] shown)
    {
        string Enc(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>body{margin:4px;font:14px Liberation Sans}input{width:120px;margin:2px}textarea{width:150px;height:60px;margin:2px}p{margin:2px}</style></head><body>");
        sb.Append("<p>before</p><input id=a value=\"").Append(Enc(shown[0])).Append("\" placeholder='first'>");
        sb.Append("<input id=b type=password value=\"").Append(Enc(shown[1])).Append("\" maxlength=12>");
        sb.Append("<textarea id=c placeholder='notes here'>\n").Append(Enc(shown[2])).Append("</textarea>");
        sb.Append("<input id=d value=\"").Append(Enc(shown[3])).Append("\" style='width:60px'><p>after</p></body></html>");
        return sb.ToString();
    }

    private static string Shown(InteractivePage p, string id)
    {
        StyledNode n = p.Page.Styled.FindBySource(Ix.Id(p, id).Index);
        return TextControls.ValueNode(n).Text.ToString();
    }

    private static readonly string[] Words = ["a", "b", " ", "x", "hello", "\u00e9", "\U0001F600", "w w", "-", "long-word-without-breaks"];

    private static void Drive(int seed, int steps)
    {
        var options = new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 420, ViewportHeight = 300 };
        InteractivePage p = InteractivePage.Create(Html(["hello world", "secret", "line one\nline two", ""]), options);
        var rng = new Random(seed);
        var log = new List<string>();
        double t = 0;
        p.Focus(Ix.Id(p, "a"));
        for (int i = 0; i < steps; i++)
        {
            t += 37;
            int k = rng.Next(22);
            Node focus = p.FocusedElement;
            string op;
            switch (k)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                    op = "type";
                    foreach (char c in Words[rng.Next(Words.Length)])
                    {
                        p.HandleKey(new KeyInput { Key = c.ToString(), Down = true, Time = t });
                    }

                    break;
                case 4: op = "Backspace"; p.HandleKey(new KeyInput { Key = "Backspace", Down = true, Time = t }); break;
                case 5: op = "Delete"; p.HandleKey(new KeyInput { Key = "Delete", Down = true, Time = t }); break;
                case 6: op = "Enter"; p.HandleKey(new KeyInput { Key = "Enter", Down = true, Time = t }); break;
                case 7:
                case 8:
                {
                    string[] keys = ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"];
                    string key = keys[rng.Next(keys.Length)];
                    KeyModifiers m = (KeyModifiers)(rng.Next(3) switch { 0 => 0, 1 => (int)KeyModifiers.Shift, _ => (int)KeyModifiers.Control });
                    op = key + "/" + m;
                    p.HandleKey(new KeyInput { Key = key, Down = true, Time = t, Modifiers = m });
                    break;
                }

                case 9: op = "selectall"; p.SelectAll(); break;
                case 10: op = "undo"; p.HandleKey(new KeyInput { Key = "z", Down = true, Time = t, Modifiers = KeyModifiers.Control }); break;
                case 11: op = "redo"; p.HandleKey(new KeyInput { Key = "y", Down = true, Time = t, Modifiers = KeyModifiers.Control }); break;
                case 12:
                {
                    op = "ime";
                    p.CompositionUpdate("ni", time: t);
                    if (rng.Next(2) == 0)
                    {
                        p.CompositionUpdate("nih", time: t);
                    }

                    if (rng.Next(3) == 0)
                    {
                        // Leave the composition open for a step to compare the preedit too.
                        break;
                    }

                    p.CompositionEnd(rng.Next(2) == 0 ? "\u4f60\u597d" : string.Empty, t);
                    break;
                }

                case 13:
                    op = "setvalue";
                    if (!focus.IsNull && (Ids.Contains(focus.GetAttribute("id"))))
                    {
                        p.SetValue(focus, Words[rng.Next(Words.Length)] + Words[rng.Next(Words.Length)]);
                    }

                    break;
                case 14:
                case 15:
                {
                    op = "focus";
                    p.Focus(Ix.Id(p, Ids[rng.Next(Ids.Length)]), rng.Next(2) == 0 ? FocusReason.Keyboard : FocusReason.Script);
                    break;
                }

                case 16:
                {
                    Layout.Rect r = p.Layout.GetBoundingClientRect(Ix.Id(p, Ids[rng.Next(Ids.Length)]));
                    double x = r.X + (rng.NextDouble() * r.Width), y = r.Y + (rng.NextDouble() * r.Height);
                    op = $"click {x:0.#},{y:0.#}";
                    p.HandlePointer(Ix.Down(x, y, t));
                    p.HandlePointer(Ix.Move(x + 20, y, t + 5, MouseButtons.Primary));
                    p.HandlePointer(Ix.Up(x + 20, y, t + 9));
                    break;
                }

                default:
                    op = "advance";
                    p.Advance(t);
                    break;
            }

            log.Add(op);
            string[] shown = new string[4];
            for (int c = 0; c < 4; c++)
            {
                shown[c] = Shown(p, Ids[c]);
            }

            // The textarea's baked text starts with a line break the parser drops, so a leading break in the value survives.
            InteractivePage fresh = InteractivePage.Create(Html(shown), options);
            string a = p.Page.Fragments.Dump(), b = fresh.Page.Fragments.Dump();
            if (a != b)
            {
                Assert.Fail($"seed {seed} step {i} after [{string.Join("; ", log.TakeLast(8))}]\nincremental:\n{a}\nfresh:\n{b}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EditedPagesLayOutLikePagesBuiltFromTheirValues(int seed) => Drive(seed, 250);
}
