using System.Globalization;
using System.Text;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.Text;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Paged;

public sealed partial class PagedDocument
{
    private const int MaxLayerCache = 32;

    // CSS Paged Media 3 §4.2: the UA defaults of the sixteen margin boxes, in PageRuleSet.BoxNames order.
    private static readonly string[] DefaultTextAlign =
    [
        "right", "left", "center", "right", "left",
        "center", "center", "center",
        "left", "right", "center", "left", "right",
        "center", "center", "center",
    ];

    private static readonly string[] DefaultVerticalAlign =
    [
        "middle", "middle", "middle", "middle", "middle",
        "top", "middle", "bottom",
        "middle", "middle", "middle", "middle", "middle",
        "bottom", "middle", "top",
    ];

    /// <summary>
    /// The page layer of a page: its background and page-margin boxes, drawn in page coordinates (the page's top-left is the origin), or
    /// null when the page has none. Lists are cached and shared between pages that look the same, so treat the result as read-only.
    /// </summary>
    /// <param name="index">The 0-based page.</param>
    /// <param name="cancellationToken">Cancels the layout of the boxes.</param>
    /// <returns>A finished display list of the page's size, or null.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public DisplayList? GetPageLayer(int index, CancellationToken cancellationToken = default)
    {
        PageInfo info = GetPage(index);
        PageStyle style = _rules.Resolve(info.Name, index, info.IsRight, info.IsBlank);
        bool background = style.Css.Contains("background", StringComparison.Ordinal);
        if (!style.HasBoxes && !background)
        {
            return null;
        }

        // The boxes' text for this page.
        var texts = new string?[16];
        var key = new StringBuilder();
        if (!_styleIds.TryGetValue(style, out int sid))
        {
            sid = _styleIds.Count;
            _styleIds[style] = sid;
        }

        key.Append(sid.ToString(CultureInfo.InvariantCulture));
        for (int b = 0; b < 16; b++)
        {
            MarginBoxStyle? mb = style.Boxes[b];
            if (mb?.Content is { } content)
            {
                texts[b] = EvaluateMarginContent(content, index);
                key.Append('\u0001').Append(texts[b]);
            }
            else
            {
                key.Append('\u0002');
            }
        }

        string cacheKey = key.ToString();
        if (_layerCache.TryGetValue(cacheKey, out DisplayList? cached))
        {
            return cached;
        }

        DisplayList list = RenderLayer(style, texts, cancellationToken);
        if (_layerCache.Count >= MaxLayerCache)
        {
            _layerCache.Clear();
        }

        _layerCache[cacheKey] = list;
        return list;
    }

    private string EvaluateMarginContent(string raw, int pageIndex)
    {
        if (!_contentCache.TryGetValue(raw, out ContentList? cv))
        {
            cv = ContentList.Parse(raw);
            if (_contentCache.Count < 1024)
            {
                _contentCache[raw] = cv;
            }
        }

        var sb = new StringBuilder();
        foreach (GeneratedItem it in cv.Items)
        {
            if (sb.Length > (1 << 14))
            {
                break;
            }

            switch (it.Kind)
            {
                case GeneratedItemKind.Text:
                    sb.Append(it.A);
                    break;
                case GeneratedItemKind.Counter:
                    if (it.A.Equals("page", StringComparison.OrdinalIgnoreCase))
                    {
                        CounterStyles.Append(sb, pageIndex + 1, it.B);
                    }
                    else if (it.A.Equals("pages", StringComparison.OrdinalIgnoreCase))
                    {
                        CounterStyles.Append(sb, _pages.Length, it.B);
                    }

                    break;
                case GeneratedItemKind.StringRef:
                    sb.Append(LookupString(it.A, it.B, pageIndex));
                    break;
                case GeneratedItemKind.OpenQuote:
                    sb.Append('\u201C');
                    break;
                case GeneratedItemKind.CloseQuote:
                    sb.Append('\u201D');
                    break;
                case GeneratedItemKind.Leader:
                    sb.Append(' ');
                    break;
            }
        }

        return sb.ToString();
    }

    // GCPM 3 §2.2: the value string(name, keyword) takes on a page.
    private string LookupString(string name, string keyword, int page)
    {
        if (!_strings.TryGetValue(name, out List<StringEntry>? list))
        {
            return string.Empty;
        }

        // Entries are in document order, so pages never decrease: the last entry before this page is the carried-in value.
        string carried = string.Empty;
        int firstOn = -1, lastOn = -1;
        for (int i = 0; i < list.Count; i++)
        {
            StringEntry e = list[i];
            if (e.Page < page)
            {
                carried = e.Value;
            }
            else if (e.Page == page)
            {
                if (firstOn < 0)
                {
                    firstOn = i;
                }

                lastOn = i;
            }
            else
            {
                break;
            }
        }

        switch (keyword)
        {
            case "start":
                return firstOn >= 0 && list[firstOn].AtTop ? list[firstOn].Value : carried;
            case "last":
                return lastOn >= 0 ? list[lastOn].Value : carried;
            case "first-except":
                return firstOn >= 0 ? string.Empty : carried;
            default:
                return firstOn >= 0 ? list[firstOn].Value : carried;
        }
    }

    private DisplayList RenderLayer(PageStyle style, string?[] texts, CancellationToken ct)
    {
        PageGeometry g = style.Geometry;
        RectF[] rects = ComputeBoxRects(style, texts, ct);
        string prelude = Prelude(style);
        var sb = new StringBuilder(prelude);
        sb.Append("<div class=\"pg\">");
        for (int b = 0; b < 16; b++)
        {
            if (texts[b] is null || rects[b].IsEmpty)
            {
                continue;
            }

            MarginBoxStyle mb = style.Boxes[b]!;
            string vertical = (mb.VerticalAlign ?? DefaultVerticalAlign[b]) switch { "top" => "flex-start", "bottom" => "flex-end", _ => "center" };
            sb.Append("<div class=\"mb\" style=\"left:").Append(Px(rects[b].X)).Append(";top:").Append(Px(rects[b].Y))
                .Append(";width:").Append(Px(rects[b].Width)).Append(";height:").Append(Px(rects[b].Height))
                .Append(";justify-content:").Append(vertical).Append(";text-align:").Append(DefaultTextAlign[b]).Append(';')
                .Append(Attr(mb.Css)).Append("\"><div>").Append(Escape(texts[b]!)).Append("</div></div>");
        }

        sb.Append("</div></body></html>");
        HtmlPage page = HtmlPage.Create(sb.ToString(), LayerOptions(g.Width, g.Height), ct);
        var list = new DisplayList();
        new Painter().Paint(page.Fragments, list, new PaintOptions(g.Width, g.Height) { Scroll = page.Scroll, Images = _options.Images, Fonts = _options.Fonts, DevicePixelRatio = 1, BaseUrl = _options.BaseUrl }, ct);
        return list;
    }

    private HtmlPageOptions LayerOptions(double w, double h) => new(_options.Fonts)
    {
        ViewportWidth = Math.Max(1, w),
        ViewportHeight = Math.Max(1, h),
        BaseUrl = _options.BaseUrl,
        StylesheetBytes = _options.Resources,
        Replaced = _options.Replaced,
        Images = _options.Images,
        DevicePixelRatio = 1,
        Layout = _options.Layout,
        PrintMedia = true,
    };

    // The shared head of a layer document: the web fonts of the content, the root's text properties, and the page context's own.
    private string Prelude(PageStyle style)
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>");
        sb.Append(_fontFaceCss);
        sb.Append("html,body{margin:0;padding:0;background:transparent}");
        sb.Append(".pg{position:relative;overflow:hidden;box-sizing:border-box;width:").Append(Px(style.Geometry.Width)).Append(";height:").Append(Px(style.Geometry.Height)).Append(';');
        Node root = Content.Document.DocumentElement;
        if (!root.IsNull)
        {
            ComputedStyle rs = _styles.GetStyle(root);
            foreach (string p in InheritedRootProperties)
            {
                string v = rs.GetPropertyValue(p);
                if (v.Length > 0 && v.Length < 512)
                {
                    sb.Append(p).Append(':').Append(Attr(v)).Append(';');
                }
            }
        }

        sb.Append(Attr(style.Css));
        sb.Append("}.mb{position:absolute;box-sizing:border-box;display:flex;flex-direction:column;overflow:hidden}</style></head><body>");
        return sb.ToString();
    }

    private static readonly string[] InheritedRootProperties = ["font-family", "font-size", "font-weight", "font-style", "line-height", "color", "direction"];

    // Style attribute and <style> text must not be able to close their container.
    private static string Attr(string css) => css.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);

    private static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    private static string Px(double v) => v.ToString("0.####", CultureInfo.InvariantCulture) + "px";

    // ---- CSS Paged Media 3 §4.2 and §5.3: where the margin boxes go and how big they are ----

    private RectF[] ComputeBoxRects(PageStyle style, string?[] texts, CancellationToken ct)
    {
        PageGeometry g = style.Geometry;
        var rects = new RectF[16];
        double w = g.Width, h = g.Height;
        // Corners are fixed by the margins.
        Corner(rects, texts, 0, 0, 0, g.MarginLeft, g.MarginTop);
        Corner(rects, texts, 4, w - g.MarginRight, 0, g.MarginRight, g.MarginTop);
        Corner(rects, texts, 8, w - g.MarginRight, h - g.MarginBottom, g.MarginRight, g.MarginBottom);
        Corner(rects, texts, 12, 0, h - g.MarginBottom, g.MarginLeft, g.MarginBottom);

        // Sides: (first box index, start x, start y, cross size, available extent, horizontal). Top and bottom run left to right, the
        // sides top to bottom.
        bool anyAuto = false;
        var sides = new (int First, bool Horizontal, double Fixed, double Avail, double Origin, double Cross)[]
        {
            (1, true, g.MarginTop, g.ContentWidth, g.MarginLeft, 0),
            (9, true, g.MarginBottom, g.ContentWidth, g.MarginLeft, h - g.MarginBottom),
            (5, false, g.MarginRight, g.ContentHeight, g.MarginTop, w - g.MarginRight),
            (13, false, g.MarginLeft, g.ContentHeight, g.MarginTop, 0),
        };

        // Bottom boxes are listed right to left in BoxNames; map a side's three slots (start, middle, end) to box indices.
        int[][] slots = [[1, 2, 3], [11, 10, 9], [5, 6, 7], [15, 14, 13]];
        double[][] measured = new double[4][];
        for (int s = 0; s < 4; s++)
        {
            measured[s] = new double[3];
            for (int k = 0; k < 3; k++)
            {
                if (texts[slots[s][k]] is not null && IsAuto(style, slots[s][k], sides[s].Avail))
                {
                    anyAuto = true;
                }
            }
        }

        if (anyAuto)
        {
            var cross = new double[4];
            for (int s = 0; s < 4; s++)
            {
                cross[s] = sides[s].Fixed;
            }

            Measure(style, texts, slots, cross, measured, ct);
        }

        for (int s = 0; s < 4; s++)
        {
            var gen = new bool[3];
            var fixedSize = new double?[3];
            for (int k = 0; k < 3; k++)
            {
                int bi = slots[s][k];
                gen[k] = texts[bi] is not null;
                if (gen[k] && style.Boxes[bi]!.Size is { } sz && PageRuleSet.TryParseLength(sz, 16, sides[s].Avail, out double px) && px >= 0)
                {
                    fixedSize[k] = Math.Min(px, sides[s].Avail);
                }
            }

            var extent = new double[3];
            Distribute(sides[s].Avail, gen, measured[s], fixedSize, extent);
            double[] pos = [0, (sides[s].Avail - extent[1]) / 2, sides[s].Avail - extent[2]];
            for (int k = 0; k < 3; k++)
            {
                if (!gen[k] || !(extent[k] > 0))
                {
                    continue;
                }

                int bi = slots[s][k];
                rects[bi] = sides[s].Horizontal
                    ? new RectF((float)(sides[s].Origin + pos[k]), (float)sides[s].Cross, (float)extent[k], (float)sides[s].Fixed)
                    : new RectF((float)sides[s].Cross, (float)(sides[s].Origin + pos[k]), (float)sides[s].Fixed, (float)extent[k]);
            }
        }

        return rects;
    }

    private static void Corner(RectF[] rects, string?[] texts, int index, double x, double y, double w, double h)
    {
        if (texts[index] is not null && w > 0 && h > 0)
        {
            rects[index] = new RectF((float)x, (float)y, (float)w, (float)h);
        }
    }

    private static bool IsAuto(PageStyle style, int box, double avail) =>
        style.Boxes[box]!.Size is not { } sz || !PageRuleSet.TryParseLength(sz, 16, avail, out _);

    // Max-content extent of each auto-sized box in a probe document: widths of top and bottom boxes at max-content, heights of side
    // boxes when wrapped to the margin's width.
    private void Measure(PageStyle style, string?[] texts, int[][] slots, double[] crossSizes, double[][] measured, CancellationToken ct)
    {
        var sb = new StringBuilder(Prelude(style));
        sb.Append("<div class=\"pg\">");
        for (int s = 0; s < 4; s++)
        {
            for (int k = 0; k < 3; k++)
            {
                int bi = slots[s][k];
                if (texts[bi] is null)
                {
                    continue;
                }

                MarginBoxStyle mb = style.Boxes[bi]!;
                string size = s < 2 ? "width:max-content" : "width:" + Px(crossSizes[s]) + ";height:auto";
                sb.Append("<div id=\"m").Append(bi.ToString(CultureInfo.InvariantCulture)).Append("\" style=\"position:absolute;left:0;top:0;box-sizing:border-box;").Append(size)
                    .Append(";text-align:").Append(DefaultTextAlign[bi]).Append(';').Append(Attr(mb.Css)).Append("\">").Append(Escape(texts[bi]!)).Append("</div>");
            }
        }

        sb.Append("</div></body></html>");
        PageGeometry g = style.Geometry;
        HtmlPage probe = HtmlPage.Create(sb.ToString(), LayerOptions(g.Width, g.Height), ct);
        for (int s = 0; s < 4; s++)
        {
            for (int k = 0; k < 3; k++)
            {
                int bi = slots[s][k];
                if (texts[bi] is null)
                {
                    continue;
                }

                Node n = probe.Document.GetElementById("m" + bi.ToString(CultureInfo.InvariantCulture));
                if (!n.IsNull && probe.Fragments.TryGetBorderBox(n, out Rect r))
                {
                    measured[s][k] = s < 2 ? r.Width : r.Height;
                }
            }
        }
    }

    /// <summary>
    /// Paged Media 3 §5.3.2 and §5.3.3: shares <paramref name="avail"/> among the three boxes of a side (start, middle, end). The middle box
    /// stays centred, so the outer two get equal extents; free space goes to the auto boxes in proportion to their max-content extents,
    /// and when the boxes do not fit they shrink in that proportion. A box with an authored extent keeps it.
    /// </summary>
    internal static void Distribute(double avail, bool[] gen, double[] maxContent, double?[] fixedSize, double[] extent)
    {
        extent[0] = extent[1] = extent[2] = 0;
        if (gen[1])
        {
            double wb;
            if (fixedSize[1] is { } fb)
            {
                wb = fb;
            }
            else
            {
                double a = gen[0] ? fixedSize[0] ?? maxContent[0] : 0;
                double c = gen[2] ? fixedSize[2] ?? maxContent[2] : 0;
                double b = maxContent[1];
                double ac = Math.Max(a, c);
                double total = b + (2 * ac);
                wb = total <= 0 ? avail : total <= avail ? b + ((avail - total) * b / total) : avail * b / total;
            }

            wb = Math.Clamp(wb, 0, avail);
            double side = (avail - wb) / 2;
            extent[1] = wb;
            extent[0] = gen[0] ? Math.Min(fixedSize[0] ?? side, avail) : 0;
            extent[2] = gen[2] ? Math.Min(fixedSize[2] ?? side, avail) : 0;
            return;
        }

        if (gen[0] && gen[2])
        {
            if (fixedSize[0] is { } fa && fixedSize[2] is { } fc)
            {
                extent[0] = fa;
                extent[2] = fc;
            }
            else if (fixedSize[0] is { } fa2)
            {
                extent[0] = fa2;
                extent[2] = Math.Max(0, avail - fa2);
            }
            else if (fixedSize[2] is { } fc2)
            {
                extent[2] = fc2;
                extent[0] = Math.Max(0, avail - fc2);
            }
            else
            {
                double a = maxContent[0], c = maxContent[2];
                double total = a + c;
                if (total <= 0)
                {
                    extent[0] = extent[2] = avail / 2;
                }
                else if (total <= avail)
                {
                    extent[0] = a + ((avail - total) * a / total);
                    extent[2] = c + ((avail - total) * c / total);
                }
                else
                {
                    extent[0] = avail * a / total;
                    extent[2] = avail * c / total;
                }
            }

            return;
        }

        if (gen[0])
        {
            extent[0] = Math.Min(fixedSize[0] ?? avail, avail);
        }
        else if (gen[2])
        {
            extent[2] = Math.Min(fixedSize[2] ?? avail, avail);
        }
    }
}
