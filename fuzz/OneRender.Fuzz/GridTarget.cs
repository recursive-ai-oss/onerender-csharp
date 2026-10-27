using System;
using System.Text;
using OneRender.Image;

namespace OneRender.Fuzz;

/// <summary>
/// Builds a random grid page from a byte string: a container (sometimes nested, sometimes subgridded) with random track
/// templates, areas, auto-placement, line-based placement with huge and negative numbers, alignment, gaps, spans, orders, absolutely
/// positioned children and replaced items, then lays it out through the whole pipeline. No exception may escape, every fragment
/// rectangle must be finite with a non-negative size, and the layout must finish.
/// </summary>
internal static class GridTarget
{
    private static readonly string[] Tracks =
    [
        "auto", "1fr", "2fr", "0fr", "0.5fr", "10px", "0", "50%", "120%", "min-content", "max-content", "minmax(10px,1fr)", "minmax(0,1fr)",
        "minmax(auto,30px)", "minmax(min-content,max-content)", "fit-content(40px)", "fit-content(50%)", "minmax(20%,2fr)", "1e9px", "calc(10px + 5%)",
        "repeat(2,20px)", "repeat(auto-fill,minmax(30px,1fr))", "repeat(auto-fit,40px)", "repeat(3,[a] 1fr [b])", "[x] 10px [y]", "subgrid", "subgrid [p] [q]",
        "repeat(100000,1px)", "repeat(auto-fill,1px)", "[a] [b] auto [a]", "minmax(1fr,2fr)", "-5px", "nonsense", "repeat(0,10px)", "repeat(2,repeat(2,1px))",
    ];

    private static readonly string[] Lines =
    [
        "auto", "1", "2", "-1", "-2", "0", "span 2", "span 99999", "99999999", "-99999999", "2147483647", "-2147483648", "a", "b", "x", "p", "a 2", "span a", "head",
        "span 0", "1 / 3", "2 / span 2", "-1 / 1", "1 / -1", "head / main",
    ];

    private static readonly string[] Align = ["normal", "stretch", "start", "end", "center", "baseline", "last baseline", "safe end", "unsafe center", "space-between", "space-around", "space-evenly", "left", "right", "self-end", "flex-start"];

    private static readonly string[] Sizes = ["auto", "0", "10px", "37px", "50%", "999999px", "-1px", "min-content", "max-content", "fit-content", "stretch", "calc(100% - 7px)", "1e12px"];

    private static readonly string[] Areas =
    [
        "none", "'a a' 'b c'", "'head head head' 'side main main'", "'. a' 'a a'", "'a b' 'b a'", "'x'", "'a a' 'a'",
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
        var sb = new StringBuilder(2048);
        sb.Append("<!DOCTYPE html><html><head><style>body{margin:0}div,i,u,img{box-sizing:").Append(r.Next(2) == 0 ? "border-box" : "content-box").Append("}</style></head><body>");
        int depth = 1 + r.Next(4);
        for (int d = 0; d < depth; d++)
        {
            Container(sb, r, d > 0);
        }

        Items(sb, r, 1 + r.Next(24));
        for (int d = 0; d < depth; d++)
        {
            sb.Append("</div>");
        }

        sb.Append("</body></html>");
        string html = sb.ToString();
        HtmlPage page;
        try
        {
            page = HtmlPage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100 + r.Next(5) * 300, ViewportHeight = 300 });
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

            // Pre-order walk without recursion.
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

    private static void Container(StringBuilder sb, Reader r, bool nested)
    {
        sb.Append("<div style=\"display:").Append(r.Next(8) == 0 ? "inline-grid" : "grid");
        if (r.Next(2) == 0)
        {
            sb.Append(";grid-template-columns:").Append(List(r, 4));
        }

        if (r.Next(2) == 0)
        {
            sb.Append(";grid-template-rows:").Append(List(r, 4));
        }

        if (r.Next(3) == 0)
        {
            sb.Append(";grid-template-areas:").Append(r.Pick(Areas));
        }

        if (r.Next(3) == 0)
        {
            sb.Append(";grid-auto-flow:").Append(r.Pick(["row", "column", "row dense", "column dense", "dense"]));
        }

        if (r.Next(3) == 0)
        {
            sb.Append(";grid-auto-rows:").Append(r.Pick(Tracks)).Append(";grid-auto-columns:").Append(r.Pick(Tracks));
        }

        if (r.Next(3) == 0)
        {
            sb.Append(";gap:").Append(r.Pick(Sizes)).Append(' ').Append(r.Pick(Sizes));
        }

        if (r.Next(2) == 0)
        {
            sb.Append(";justify-items:").Append(r.Pick(Align)).Append(";align-items:").Append(r.Pick(Align));
        }

        if (r.Next(3) == 0)
        {
            sb.Append(";justify-content:").Append(r.Pick(Align)).Append(";align-content:").Append(r.Pick(Align));
        }

        if (r.Next(3) == 0)
        {
            sb.Append(";width:").Append(r.Pick(Sizes)).Append(";height:").Append(r.Pick(Sizes));
        }

        if (r.Next(4) == 0)
        {
            sb.Append(";padding:").Append(r.Next(9)).Append("px;border:").Append(r.Next(4)).Append("px solid;direction:").Append(r.Next(2) == 0 ? "rtl" : "ltr");
        }

        if (r.Next(4) == 0)
        {
            sb.Append(";position:relative");
        }

        if (nested)
        {
            Placement(sb, r);
        }

        sb.Append("\">");
        if (r.More)
        {
            Items(sb, r, r.Next(6));
        }
    }

    private static string List(Reader r, int max)
    {
        var sb = new StringBuilder();
        int n = 1 + r.Next(max);
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(r.Pick(Tracks));
        }

        return sb.ToString();
    }

    private static void Placement(StringBuilder sb, Reader r)
    {
        if (r.Next(2) == 0)
        {
            sb.Append(";grid-column:").Append(r.Pick(Lines));
        }

        if (r.Next(2) == 0)
        {
            sb.Append(";grid-row:").Append(r.Pick(Lines));
        }

        if (r.Next(8) == 0)
        {
            sb.Append(";grid-area:").Append(r.Pick(["a", "b", "head", "1 / 1 / 3 / 3", "main"]));
        }

        if (r.Next(6) == 0)
        {
            sb.Append(";order:").Append(r.Next(5) - 2);
        }
    }

    private static void Items(StringBuilder sb, Reader r, int count)
    {
        for (int i = 0; i < count && r.More; i++)
        {
            int kind = r.Next(10);
            sb.Append(kind == 9 ? "<img src=x.png style=\"" : "<i style=\"display:block");
            if (r.Next(2) == 0)
            {
                sb.Append(";width:").Append(r.Pick(Sizes));
            }

            if (r.Next(2) == 0)
            {
                sb.Append(";height:").Append(r.Pick(Sizes));
            }

            Placement(sb, r);
            if (r.Next(4) == 0)
            {
                sb.Append(";justify-self:").Append(r.Pick(Align)).Append(";align-self:").Append(r.Pick(Align));
            }

            if (r.Next(5) == 0)
            {
                sb.Append(";margin:").Append(r.Pick(["auto", "3px", "-4px", "5%", "0 auto", "auto 0"]));
            }

            if (r.Next(8) == 0)
            {
                sb.Append(";min-width:").Append(r.Pick(Sizes)).Append(";max-height:").Append(r.Pick(Sizes));
            }

            if (r.Next(8) == 0)
            {
                sb.Append(";aspect-ratio:").Append(r.Pick(["1", "2/1", "0", "1/0"])).Append(";overflow:hidden");
            }

            if (r.Next(7) == 0)
            {
                sb.Append(";position:").Append(r.Pick(["absolute", "relative", "fixed"])).Append(";left:").Append(r.Next(20)).Append("px;right:").Append(r.Next(20)).Append("px");
            }

            sb.Append(kind == 9 ? "\">" : "\"><u style=\"display:block;height:" + r.Next(30) + "px;width:" + r.Next(60) + "px\"></u></i>");
        }
    }

    /// <summary>Seed inputs: a few byte strings that reach different features.</summary>
    public static int WriteSeeds(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var rng = new Random(13);
        for (int i = 0; i < 24; i++)
        {
            byte[] b = new byte[64 + rng.Next(200)];
            rng.NextBytes(b);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "seed" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)), b);
        }

        return 0;
    }
}
