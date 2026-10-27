using System;
using System.Text;
using OneRender.Image;
using OneRender.Paged;
using OneRender.Painting;

namespace OneRender.Fuzz;

/// <summary>
/// Builds a random paged document from a byte string: <c>@page</c> rules with sizes, margins, selectors and margin boxes whose content
/// uses counters, strings, targets and leaders, named pages, forced and avoided breaks of every kind, tables with repeated headers and
/// footers, <c>box-decoration-break</c>, reversed counters, <c>display: contents</c>, long unbreakable text and huge values. The document is
/// paginated and every page is painted, with its margin layer: no exception but the library's typed ones may escape, pages are
/// bounded, and page geometry stays finite.
/// </summary>
internal static class PagedTarget
{
    private static readonly string[] Sizes = ["A4", "letter landscape", "200px 100px", "0", "1px", "999999px", "auto", "landscape", "-5px", "10cm 3in", "b4 portrait", "1e9px"];
    private static readonly string[] Margins = ["0", "10px", "50%", "9999px", "-3px", "1em 2em", "auto", "20mm 5mm 30mm", "calc(1px + 2%)"];
    private static readonly string[] Selectors = ["", ":first", ":left", ":right", ":blank", " named", " named:first", ":nth(2)", ":bogus", " a, b"];
    private static readonly string[] Boxes = ["top-left-corner", "top-left", "top-center", "top-right", "top-right-corner", "left-top", "left-middle", "left-bottom", "right-top", "right-middle", "right-bottom", "bottom-left-corner", "bottom-left", "bottom-center", "bottom-right", "bottom-right-corner", "nonsense"];
    private static readonly string[] Contents =
    [
        "counter(page)", "counter(page) ' of ' counter(pages)", "string(chap)", "string(chap, last) string(other, first-except)", "'text'", "leader('.') counter(page)",
        "counters(page, '.')", "counter(page, upper-roman)", "target-counter(url(#a), page)", "element(x)", "attr(title)", "content(text)", "none", "normal",
        "counter(", "'unclosed", "counter(page, bogus-style)", "string(", "leader(", "url(x.png)", "counter(inherit)", "open-quote close-quote",
    ];
    private static readonly string[] Breaks = ["auto", "page", "left", "right", "recto", "verso", "avoid", "avoid-page", "always", "column", "region", "bogus"];
    private static readonly string[] Names = ["auto", "named", "other", "a", "1"];
    private static readonly string[] Heights = ["0", "10px", "100px", "300px", "5000px", "50%", "auto", "1e9px", "-1px"];
    private static readonly string[] Texts = ["x", "word", "a longer run of words that wraps across the line and keeps going and going", "", "longunbrokenwordlongunbrokenwordlongunbrokenwordlongunbrokenword"];

    private sealed class Reader(ReadOnlySpan<byte> data)
    {
        private readonly byte[] _data = data.ToArray();
        private int _pos;

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
        sb.Append("<!DOCTYPE html><html><head><style>html,body{margin:0}");
        int rules = 1 + r.Next(4);
        for (int i = 0; i < rules; i++)
        {
            sb.Append("@page").Append(r.Pick(Selectors)).Append('{');
            if (r.Next(2) == 0)
            {
                sb.Append("size:").Append(r.Pick(Sizes)).Append(';');
            }

            if (r.Next(2) == 0)
            {
                sb.Append("margin:").Append(r.Pick(Margins)).Append(';');
            }

            int boxes = r.Next(4);
            for (int b = 0; b < boxes; b++)
            {
                sb.Append('@').Append(r.Pick(Boxes)).Append("{content:").Append(r.Pick(Contents)).Append(r.Next(3) == 0 ? ";width:" + r.Pick(Margins) : "").Append(";}");
            }

            sb.Append('}');
        }

        if (r.Next(3) == 0)
        {
            sb.Append("h2{string-set:chap content(text)}h3{string-set:other attr(id), chap 'x'}");
        }

        if (r.Next(3) == 0)
        {
            sb.Append("div{counter-increment:c}div::before{content:counter(c) '. '}ol{counter-reset:reversed(c) 7}");
        }

        sb.Append("table{border-collapse:collapse}td,th{padding:0;height:").Append(r.Next(30)).Append("px}");
        sb.Append("</style></head><body>");
        Content(sb, r, 0, 0);
        sb.Append("</body></html>");
        string html = sb.ToString();
        PagedDocument doc;
        try
        {
            doc = PagedDocument.Create(html, new PagedOptions(FontProviders.CreateDefault()) { MaxPages = 40, DefaultPage = new PageGeometry(300, 200, 10, 10, 10, 10) });
        }
        catch (ArgumentException)
        {
            return;
        }

        if (doc.PageCount < 1 || doc.PageCount > 40)
        {
            throw new InvalidOperationException("page count " + doc.PageCount + " in " + html);
        }

        var list = new DisplayList();
        doc.PaintContent(list, null, r.Next(2) == 0);
        for (int i = 0; i < doc.PageCount; i++)
        {
            PageInfo p = doc.GetPage(i);
            PageGeometry g = p.Geometry;
            if (!double.IsFinite(g.Width) || !double.IsFinite(g.Height) || g.Width <= 0 || g.Height <= 0 || !double.IsFinite(g.MarginTop + g.MarginRight + g.MarginBottom + g.MarginLeft))
            {
                throw new InvalidOperationException("page " + i + " geometry in " + html);
            }

            if (i < 6)
            {
                doc.GetPageLayer(i);
            }
        }
    }

    private static void Content(StringBuilder sb, Reader r, int depth, int count)
    {
        int n = 1 + r.Next(4);
        for (int i = 0; i < n && sb.Length < 6000; i++)
        {
            switch (r.Next(9))
            {
                case 0:
                    sb.Append("<h2 id=\"a\">").Append(r.Pick(Texts)).Append("</h2>");
                    break;
                case 1:
                    sb.Append("<h3 id=\"h").Append(count).Append("\">").Append(r.Pick(Texts)).Append("</h3>");
                    break;
                case 2:
                {
                    sb.Append("<table><thead><tr><th>H</th></tr></thead><tbody>");
                    int rows = r.Next(40);
                    for (int k = 0; k < rows; k++)
                    {
                        sb.Append("<tr><td>").Append(k).Append("</td></tr>");
                    }

                    sb.Append("</tbody>").Append(r.Next(2) == 0 ? "<tfoot><tr><td>F</td></tr></tfoot>" : "").Append("</table>");
                    break;
                }

                case 3:
                    sb.Append("<a href=\"#a\">link</a><p>").Append(r.Pick(Texts)).Append("</p>");
                    break;
                case 4:
                    sb.Append("<ol><li>one<li>two<li>three</ol>");
                    break;
                default:
                    if (depth >= 4)
                    {
                        break;
                    }

                    sb.Append("<div style=\"height:").Append(r.Pick(Heights)).Append(";break-before:").Append(r.Pick(Breaks)).Append(";break-after:").Append(r.Pick(Breaks))
                        .Append(";break-inside:").Append(r.Pick(Breaks)).Append(";page:").Append(r.Pick(Names));
                    if (r.Next(4) == 0)
                    {
                        sb.Append(";display:contents");
                    }

                    if (r.Next(4) == 0)
                    {
                        sb.Append(";border:3px solid;padding:4px;box-decoration-break:clone");
                    }

                    sb.Append("\">");
                    Content(sb, r, depth + 1, count + i + 1);
                    sb.Append("</div>");
                    break;
            }
        }
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
