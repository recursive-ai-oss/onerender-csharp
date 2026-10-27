using System;
using System.Text;
using OneRender.Image;

namespace OneRender.Fuzz;

/// <summary>
/// Builds a random flow page from a byte string: nested blocks, inline boxes and text with floats, clear, margins (negative
/// ones too), padding and borders, sizes and their limits, positioning, overflow, white space modes, text transforms, line
/// breaks, font sizes and families, alignment and direction, then lays it out through the whole pipeline. No exception may
/// escape, every fragment rectangle must be finite with a non-negative size, and the layout must finish.
/// </summary>
internal static class FlowTarget
{
    private static readonly string[] Lengths = ["0", "1px", "7px", "20px", "63px", "150px", "10%", "50%", "100%", "120%", "auto", "-3px", "1e7px", "3em", "calc(50% - 9px)", "min-content", "max-content", "fit-content"];

    private static readonly string[] Margins = ["0", "5px", "17px", "-9px", "-40px", "auto", "10%", "2em", "0 auto", "30px 0 -10px", "1e6px"];

    private static readonly string[] Words =
    [
        "alpha", "beta gamma", "  spaced   out  ", "line\none\n\ntwo", "tab\tbed", "straße", "don't stop", "MiXeD cAsE", "\u05e9\u05dc\u05d5\u05dd \u05e2\u05d5\u05dc\u05dd", "\u0645\u0631\u062d\u0628\u0627",
        "\u4e2d\u6587\u6587\u5b57", "averyveryverylongunbreakablewordthatgoesonandonandonandon", "a-b-c d.e/f", "\u00a0nbsp\u00a0", "x", "",
    ];

    private static readonly string[] Styles =
    [
        "white-space:pre", "white-space:pre-wrap", "white-space:pre-line", "white-space:nowrap", "white-space:break-spaces", "text-transform:uppercase", "text-transform:capitalize",
        "text-transform:lowercase", "font-family:monospace", "font-family:monospace,monospace", "font-size:2em", "font-size:x-large", "font-size:70%", "font-size:larger",
        "font-weight:bolder", "text-align:center", "text-align:right", "text-align:justify", "line-height:0", "line-height:2", "line-height:30px", "direction:rtl", "word-break:break-all",
        "overflow-wrap:anywhere", "letter-spacing:2px", "word-spacing:5px", "text-indent:2em", "text-indent:-3em", "vertical-align:top", "vertical-align:middle", "vertical-align:5px", "tab-size:2",
    ];

    private sealed class Reader(ReadOnlySpan<byte> data)
    {
        private readonly byte[] _data = data.ToArray();
        private int _pos;

        public bool More => _pos < _data.Length;

        public int Next(int n) => n <= 0 ? 0 : (_pos < _data.Length ? _data[_pos++] : 0) % n;

        public string Pick(string[] set) => set[Next(set.Length)];
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        var r = new Reader(data);
        var sb = new StringBuilder(4096);
        sb.Append("<!DOCTYPE html><html><head><style>");
        if (r.Next(2) == 0)
        {
            sb.Append("body{margin:0}");
        }

        sb.Append("</style></head><body");
        Style(sb, r);
        sb.Append('>');
        int budget = 60;
        Content(sb, r, 0, ref budget);
        sb.Append("</body></html>");
        string html = sb.ToString();
        HtmlPage page;
        try
        {
            page = HtmlPage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 50 + r.Next(6) * 250, ViewportHeight = 400 });
        }
        catch (ArgumentException)
        {
            return;
        }

        var fragments = page.Fragments;
        OneRender.Layout.Fragment f = fragments.Root;
        while (!f.IsNull)
        {
            var b = fragments.AbsoluteBorderBox(f);
            if (!double.IsFinite(b.X) || !double.IsFinite(b.Y) || !double.IsFinite(b.Width) || !double.IsFinite(b.Height) || b.Width < -1e-6 || b.Height < -1e-6)
            {
                throw new InvalidOperationException("non-finite or negative box " + b + " in " + html);
            }

            OneRender.Layout.Fragment next = f.FirstChild;
            while (next.IsNull && !f.IsNull)
            {
                next = f.NextSibling;
                if (next.IsNull)
                {
                    f = f.Parent;
                }
            }

            f = next;
        }
    }

    private static void Style(StringBuilder sb, Reader r)
    {
        sb.Append(" style=\"");
        int n = r.Next(5);
        for (int i = 0; i < n; i++)
        {
            switch (r.Next(14))
            {
                case 0: sb.Append("margin:").Append(r.Pick(Margins)).Append(';'); break;
                case 1: sb.Append("padding:").Append(r.Next(12)).Append("px;"); break;
                case 2: sb.Append("border:").Append(r.Next(5)).Append("px solid;"); break;
                case 3: sb.Append("width:").Append(r.Pick(Lengths)).Append(';'); break;
                case 4: sb.Append("height:").Append(r.Pick(Lengths)).Append(';'); break;
                case 5: sb.Append("min-height:").Append(r.Pick(Lengths)).Append(";max-width:").Append(r.Pick(Lengths)).Append(';'); break;
                case 6: sb.Append("float:").Append(r.Pick(["left", "right", "none", "inline-start"])).Append(';'); break;
                case 7: sb.Append("clear:").Append(r.Pick(["left", "right", "both", "none", "inline-end"])).Append(';'); break;
                case 8: sb.Append("position:").Append(r.Pick(["relative", "absolute", "fixed", "sticky", "static"])).Append(";top:").Append(r.Next(30)).Append("px;left:").Append(r.Next(30)).Append("px;"); break;
                case 9: sb.Append("overflow:").Append(r.Pick(["hidden", "auto", "scroll", "clip", "visible"])).Append(';'); break;
                case 10: sb.Append("display:").Append(r.Pick(["block", "inline-block", "inline", "flow-root", "none", "list-item", "inline"])).Append(';'); break;
                case 11: sb.Append("box-sizing:border-box;"); break;
                case 12: sb.Append(r.Pick(Styles)).Append(';'); break;
                default: sb.Append("z-index:").Append(r.Next(5) - 2).Append(';'); break;
            }
        }

        sb.Append('"');
    }

    // Recursion here is the generator's, bounded by depth; the engine's own handling is what is under test.
    private static void Content(StringBuilder sb, Reader r, int depth, ref int budget)
    {
        int n = 1 + r.Next(5);
        for (int i = 0; i < n && r.More && budget > 0; i++)
        {
            budget--;
            switch (r.Next(9))
            {
                case 0:
                case 1:
                    sb.Append("<div");
                    Style(sb, r);
                    sb.Append('>');
                    if (depth < 6)
                    {
                        Content(sb, r, depth + 1, ref budget);
                    }

                    sb.Append("</div>");
                    break;
                case 2:
                    sb.Append("<span");
                    Style(sb, r);
                    sb.Append('>');
                    if (depth < 6)
                    {
                        Content(sb, r, depth + 1, ref budget);
                    }

                    sb.Append("</span>");
                    break;
                case 3:
                    sb.Append("<p");
                    Style(sb, r);
                    sb.Append('>').Append(r.Pick(Words)).Append("</p>");
                    break;
                case 4:
                    sb.Append("<pre").Append('>').Append(r.Pick(Words)).Append("</pre>");
                    break;
                case 5:
                    sb.Append("<br>");
                    break;
                case 6:
                    sb.Append("<div style=\"clear:").Append(r.Pick(["left", "right", "both"])).Append(";margin:").Append(r.Pick(Margins)).Append("\"></div>");
                    break;
                case 7:
                    sb.Append("<img width=").Append(r.Next(80)).Append(" height=").Append(r.Next(80)).Append('>');
                    break;
                default:
                    sb.Append(r.Pick(Words));
                    break;
            }
        }
    }

    /// <summary>Seed inputs: a few byte strings that reach different features.</summary>
    public static int WriteSeeds(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var rng = new Random(29);
        for (int i = 0; i < 32; i++)
        {
            byte[] b = new byte[64 + rng.Next(400)];
            rng.NextBytes(b);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "seed" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)), b);
        }

        return 0;
    }
}
