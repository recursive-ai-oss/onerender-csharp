using System.Text;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.TestSupport;
using Xunit;

namespace OneRender.Tests;

/// <summary>An interactive page that restyles in place must equal one that builds the page again after every input.</summary>
public class HtmlIncrementalTests
{
    private const string FocusPage =
        "<!DOCTYPE html><html><head><style>body{margin:0;font:14px Liberation Sans}a{color:#06c}a:hover{color:#c60;text-decoration:underline}" +
        "button{width:80px;height:30px;margin:4px}button:focus{outline:2px solid #f0f;background:#ffd}button:hover{background:#def;width:120px}" +
        ".row{display:flex;gap:6px}.row:hover{background:#eee;padding:6px}.dd{display:none}.m:hover .dd{display:block}" +
        "li:hover{color:red}ul{margin:0}</style></head><body>" +
        "<div class=row><button id=b1>One</button><button id=b2>Two</button><a href='#x' id=a1>link</a></div>" +
        "<div class=m>Menu<div class=dd><p>Item 1</p><p>Item 2</p></div></div>" +
        "<ul><li>First</li><li>Second</li><li>Third <b>bold</b></li></ul>" +
        "<div class=row><button id=b3>Three</button><input id=i1 value=x></div></body></html>";

    private const string FormPage =
        "<!DOCTYPE html><html><head><style>body{margin:0;font:14px Liberation Sans}input.cb{appearance:none;width:18px;height:18px;border:2px solid #333;margin:6px;display:inline-block}" +
        "input.cb:hover{border-color:#f0f}input.cb:checked{background:#06c;width:24px}input.cb:checked + label{color:#c00}input.cb:indeterminate{background:#999}" +
        "input.cb:focus{outline:2px solid #0f0}input:disabled{opacity:.4}fieldset:disabled{background:#eee}label{margin-right:10px}.g:has(:checked){background:#eef}</style></head><body>" +
        "<div class=g><input id=a class=cb type=checkbox><label for=a>A</label><input id=b class=cb type=checkbox checked><label for=b>B</label></div>" +
        "<div class=g><input class=cb type=radio name=r><input class=cb type=radio name=r><input class=cb type=radio name=r checked></div>" +
        "<fieldset disabled><input class=cb type=checkbox><input class=cb type=radio name=q></fieldset><button>ok</button></body></html>";

    private static string Describe(InteractivePage p) => p.Page.Styled.Dump() + "\n--\n" + p.Page.Boxes.Dump() + "\n--\n" + p.Page.Fragments.Dump();

    private static void Drive(string html, int w, int h, int seed, int steps)
    {
        var fonts = FontProviders.CreateDefault();
        var options = new HtmlPageOptions(fonts) { ViewportWidth = w, ViewportHeight = h };
        InteractivePage fast = InteractivePage.Create(html, options);
        InteractivePage slow = InteractivePage.Create(html, options);
        slow.DisableInPlaceRestyle = true;
        var rng = new Random(seed);
        double t = 0;
        for (int i = 0; i < steps; i++)
        {
            t += 16;
            string what;
            int kind = rng.Next(10);
            double x = rng.Next(w), y = rng.Next(h);
            if (kind < 6)
            {
                what = $"move {x},{y}";
                fast.HandlePointer(Ix.Move(x, y, t));
                slow.HandlePointer(Ix.Move(x, y, t));
            }
            else if (kind < 8)
            {
                what = $"click {x},{y}";
                fast.HandlePointer(Ix.Down(x, y, t));
                slow.HandlePointer(Ix.Down(x, y, t));
                fast.HandlePointer(Ix.Up(x, y, t + 1));
                slow.HandlePointer(Ix.Up(x, y, t + 1));
            }
            else if (kind < 9)
            {
                what = "tab";
                fast.HandleKey(Ix.Key("Tab"));
                slow.HandleKey(Ix.Key("Tab"));
            }
            else
            {
                what = "wheel";
                var wheel = new WheelInput { X = x, Y = y, DeltaY = rng.Next(-80, 80), Time = t };
                fast.HandleWheel(wheel);
                slow.HandleWheel(wheel);
            }

            string a = Describe(fast), b = Describe(slow);
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                string[] x1 = a.Split('\n'), y1 = b.Split('\n');
                int n = 0;
                while (n < x1.Length && n < y1.Length && x1[n] == y1[n])
                {
                    n++;
                }

                throw new Xunit.Sdk.XunitException($"seed {seed} step {i} ({what}): line {n}\n in place: {(n < x1.Length ? x1[n] : "(end)")}\n rebuilt:  {(n < y1.Length ? y1[n] : "(end)")}");
            }
        }
    }

    [Fact]
    public void StateFlipsOnAFocusPageEqualARebuild()
    {
        for (int seed = 1; seed <= 12; seed++)
        {
            Drive(FocusPage, 400, 300, seed, 150);
        }
    }

    [Fact]
    public void StateFlipsOnTheGameUiPageEqualARebuild()
    {
        for (int seed = 1; seed <= 3; seed++)
        {
            Drive(GameUiScene.Html(), GameUiScene.Width, GameUiScene.Height, seed, 120);
        }
    }

    [Fact]
    public void StateFlipsAndClicksOnAFormPageEqualARebuild()
    {
        for (int seed = 1; seed <= 12; seed++)
        {
            Drive(FormPage, 300, 200, seed, 150);
        }
    }
}
