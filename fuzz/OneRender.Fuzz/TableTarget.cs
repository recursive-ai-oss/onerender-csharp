using System;
using System.Text;
using OneRender.Image;

namespace OneRender.Fuzz;

/// <summary>
/// Builds a random table page from a byte string: HTML tables and display:table-* boxes mixed freely (so anonymous table objects are
/// generated), column groups and columns with spans and widths, captions on both sides, header and footer groups, cells with huge
/// column and row spans, percentage, fixed and auto widths, separate and collapsing borders with conflicting styles, collapsed rows
/// and columns, vertical alignment, empty-cells, fixed layout and nested tables. The page is laid out through the whole pipeline: no
/// exception may escape, every fragment rectangle must be finite with a non-negative size, and the layout must finish.
/// </summary>
internal static class TableTarget
{
    private static readonly string[] Widths = ["auto", "0", "10px", "37px", "50%", "100%", "150%", "999999px", "min-content", "max-content", "calc(100% - 7px)", "1e12px", "-1px"];
    private static readonly string[] Borders = ["none", "1px solid #000", "3px double red", "hidden", "5px dashed blue", "2px ridge", "0.5px dotted", "100px solid", "thick groove", "2px inset"];
    private static readonly string[] Aligns = ["baseline", "top", "middle", "bottom", "sub", "super", "text-top", "10px"];
    private static readonly string[] Displays = ["table-cell", "table-row", "table-row-group", "table-header-group", "table-footer-group", "table-column", "table-column-group", "table-caption", "table", "inline-table", "block", "inline", "flex"];
    private static readonly string[] Words = ["x", "word", "a longer run of words that wraps", "", "longunbrokenwordlongunbrokenwordlongunbrokenword"];

    private sealed class Reader(ReadOnlySpan<byte> data)
    {
        private readonly byte[] _data = data.ToArray();
        private int _pos;

        public int Next(int n) => n <= 0 ? 0 : (_pos < _data.Length ? _data[_pos++] : 0) % n;

        public string Pick(string[] set) => set[Next(set.Length)];

        public int Span() => Next(7) switch { 0 => 0, 1 => 2, 2 => 3, 3 => 1000, 4 => 1001, 5 => 99999999, _ => 1 };
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        var r = new Reader(data);
        var sb = new StringBuilder(4096);
        sb.Append("<!DOCTYPE html><html><head><style>body{margin:0}td,th{padding:").Append(r.Next(4)).Append("px}table{border-collapse:")
            .Append(r.Next(2) == 0 ? "collapse" : "separate").Append(";border-spacing:").Append(r.Next(5)).Append("px ").Append(r.Next(5)).Append("px}")
            .Append("</style></head><body>");
        Table(sb, r, 0);
        sb.Append("</body></html>");
        string html = sb.ToString();
        HtmlPage page;
        try
        {
            page = HtmlPage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100 + (r.Next(5) * 300), ViewportHeight = 300 });
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
        if (r.Next(3) == 0)
        {
            sb.Append("width:").Append(r.Pick(Widths)).Append(';');
        }

        if (r.Next(4) == 0)
        {
            sb.Append("height:").Append(r.Pick(Widths)).Append(';');
        }

        if (r.Next(3) == 0)
        {
            sb.Append("border:").Append(r.Pick(Borders)).Append(';');
        }

        if (r.Next(6) == 0)
        {
            sb.Append("border-left:").Append(r.Pick(Borders)).Append(";border-bottom:").Append(r.Pick(Borders)).Append(';');
        }

        if (r.Next(4) == 0)
        {
            sb.Append("vertical-align:").Append(r.Pick(Aligns)).Append(';');
        }

        if (r.Next(8) == 0)
        {
            sb.Append("visibility:collapse;");
        }

        if (r.Next(8) == 0)
        {
            sb.Append("display:").Append(r.Pick(Displays)).Append(';');
        }

        if (r.Next(10) == 0)
        {
            sb.Append("empty-cells:hide;");
        }

        if (r.Next(10) == 0)
        {
            sb.Append("position:").Append(r.Next(2) == 0 ? "absolute" : "relative").Append(';');
        }

        if (r.Next(10) == 0)
        {
            sb.Append("float:left;");
        }

        sb.Append('"');
    }

    private static void Table(StringBuilder sb, Reader r, int depth)
    {
        sb.Append("<table");
        Style(sb, r);
        if (r.Next(5) == 0)
        {
            sb.Append(" cellspacing=").Append(r.Next(9)).Append(" cellpadding=").Append(r.Next(9)).Append(" width=").Append(r.Next(2) == 0 ? "100%" : "300");
        }

        if (r.Next(5) == 0)
        {
            sb.Append(" style=\"table-layout:fixed;width:").Append(r.Pick(Widths)).Append('"');
        }

        sb.Append('>');
        if (r.Next(3) == 0)
        {
            sb.Append("<caption");
            Style(sb, r);
            sb.Append(r.Next(2) == 0 ? " style=\"caption-side:bottom\">" : ">").Append(r.Pick(Words)).Append("</caption>");
        }

        if (r.Next(3) == 0)
        {
            sb.Append("<colgroup");
            Style(sb, r);
            sb.Append(" span=").Append(r.Span()).Append('>');
            for (int i = r.Next(4); i > 0; i--)
            {
                sb.Append("<col");
                Style(sb, r);
                sb.Append(" span=").Append(r.Span()).Append('>');
            }

            sb.Append("</colgroup>");
        }

        int groups = 1 + r.Next(3);
        for (int g = 0; g < groups; g++)
        {
            string tag = r.Next(5) == 0 ? "thead" : r.Next(5) == 0 ? "tfoot" : "tbody";
            bool wrap = r.Next(5) != 0;
            if (wrap)
            {
                sb.Append('<').Append(tag);
                Style(sb, r);
                sb.Append('>');
            }

            for (int row = 1 + r.Next(6); row > 0; row--)
            {
                sb.Append("<tr");
                Style(sb, r);
                sb.Append('>');
                for (int c = r.Next(7); c > 0; c--)
                {
                    sb.Append(r.Next(6) == 0 ? "<th" : "<td");
                    Style(sb, r);
                    if (r.Next(3) == 0)
                    {
                        sb.Append(" colspan=").Append(r.Span());
                    }

                    if (r.Next(3) == 0)
                    {
                        sb.Append(" rowspan=").Append(r.Span());
                    }

                    sb.Append('>');
                    if (depth < 3 && r.Next(8) == 0)
                    {
                        Table(sb, r, depth + 1);
                    }
                    else if (r.Next(10) == 0)
                    {
                        // Stray table parts inside a cell are wrapped in anonymous boxes.
                        sb.Append("<div style=\"display:table-cell\">cell</div><div style=\"display:table-row\"></div>");
                    }
                    else
                    {
                        sb.Append(r.Pick(Words));
                    }

                    sb.Append("</td>");
                }

                sb.Append("</tr>");
            }

            if (wrap)
            {
                sb.Append("</").Append(tag).Append('>');
            }
        }

        sb.Append("</table>");
    }

    public static int WriteSeeds(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var rng = new Random(29);
        for (int i = 0; i < 24; i++)
        {
            byte[] b = new byte[64 + rng.Next(300)];
            rng.NextBytes(b);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "seed" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)), b);
        }

        return 0;
    }
}
