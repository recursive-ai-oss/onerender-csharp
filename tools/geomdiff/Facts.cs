using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OneRender.Accessibility;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Accessibility.Rendered;

namespace OneRender.GeomDiff;

/// <summary>
/// The facts differential: Chrome's <c>DOMSnapshot.captureSnapshot</c> facts for a frozen page against the facts the engine
/// writes for the same document, fact by fact. Capture needs Chrome; compare reads the capture files only.
/// </summary>
internal static class FactsCapture
{
    public const string Format = "onerender-facts-capture/1";

    public static string FileFor(string root, string page, ViewportSize vp) => Path.Combine(root, page, vp.ToString() + ".facts.json.gz");

    // The document as Chrome serialized it, doctype first, which is the input OneHtml.Accessibility expects.
    private const string SerializeExpression =
        "(function(){var d=document.doctype?new XMLSerializer().serializeToString(document.doctype):'';return d+document.documentElement.outerHTML;})()";

    public static async Task CaptureAsync(ChromeInstance chrome, string snapshotRoot, string pageId, ViewportSize vp, string output, CancellationToken ct)
    {
        (SnapshotPage page, string html, Dictionary<string, (byte[] Body, string Type)> res) = Snapshots.Load(snapshotRoot, pageId);
        using Tab tab = await Tab.OpenAsync(chrome, Snapshots.Replay(page, html, res), vp, ct).ConfigureAwait(false);
        await tab.NavigateAsync(page.Url, 60000, 300, ct, measure: true).ConfigureAwait(false);
        string dom = await tab.EvaluateStringAsync(SerializeExpression, ct).ConfigureAwait(false);
        string styles = "[" + string.Join(",", FactsOptions.StandardStyleList.Select(J.S)) + "]";
        JsonElement snap = await tab.SendAsync("DOMSnapshot.captureSnapshot",
            "{\"computedStyles\":" + styles + ",\"includePaintOrder\":true,\"includeDOMRects\":true,\"includeBlendedBackgroundColors\":true,\"includeTextColorOpacities\":true}", 300000, ct).ConfigureAwait(false);
        string path = FileFor(output, pageId, vp);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        using (FileStream fs = File.Create(tmp))
        using (GZipStream gz = new(fs, CompressionLevel.Fastest))
        using (Utf8JsonWriter w = new(gz))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteString("page", pageId);
            w.WriteString("viewport", vp.ToString());
            w.WriteString("product", chrome.Version);
            w.WriteStartArray("styles");
            foreach (string s in FactsOptions.StandardStyleList)
            {
                w.WriteStringValue(s);
            }
            w.WriteEndArray();
            w.WriteString("dom", dom);
            w.WritePropertyName("snapshot");
            snap.WriteTo(w);
            w.WriteEndObject();
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static async Task<int> RunAsync(string snapshotRoot, string output, string chromePath, string fontConfig, List<ViewportSize> viewports, int jobs, int limit, string workDir, bool? noSandbox, CancellationToken ct)
    {
        List<string> ids = Snapshots.PageIds(snapshotRoot);
        if (limit > 0 && ids.Count > limit)
        {
            ids = ids.GetRange(0, limit);
        }
        List<string> todo = [.. ids.Where(id => viewports.Any(vp => !File.Exists(FileFor(output, id, vp))))];
        Console.WriteLine($"capturing facts for {todo.Count} of {ids.Count} pages, {viewports.Count} viewport(s), {jobs} chrome(s)");
        ChromeOptions opt = new() { ChromePath = chromePath, FontConfigFile = fontConfig, WorkDir = workDir, NoSandbox = noSandbox };
        string browser = "";
        object gate = new();
        List<string> failures = await Pool.RunAsync(todo, static id => id, jobs, opt, 100, async (chrome, id) =>
        {
            lock (gate)
            {
                browser = chrome.Version;
            }
            foreach (ViewportSize vp in viewports)
            {
                if (!File.Exists(FileFor(output, id, vp)))
                {
                    await CaptureAsync(chrome, snapshotRoot, id, vp, output, ct).ConfigureAwait(false);
                }
            }
        }, ct, Console.WriteLine).ConfigureAwait(false);
        Directory.CreateDirectory(output);
        if (browser.Length > 0 || !File.Exists(Path.Combine(output, "run.txt")))
        {
            File.WriteAllText(Path.Combine(output, "run.txt"),
                $"browser: {browser}\nflags: {string.Join(' ', ChromeInstance.Flags)}\nfontset: {FontSets.Describe(fontConfig)}\nviewports: {string.Join(' ', viewports)}\ncomputedStyles: {FactsOptions.StandardStyleList.Count}\n");
        }
        Console.WriteLine($"failures {failures.Count}");
        foreach (string f in failures.Take(10))
        {
            Console.WriteLine("  failed: " + f);
        }
        return 0;
    }
}

/// <summary>One fact kind's tally over all pages of a set and viewport.</summary>
internal sealed class FactTally
{
    public long Total;
    public long Agree;
    public List<string> Examples { get; } = [];

    public double Rate => Total == 0 ? 1 : (double)Agree / Total;
}

internal sealed class FactsReport
{
    public SortedDictionary<string, FactTally> Kinds { get; } = new(StringComparer.Ordinal);
    public int Pages;
    public int Refused;
    public int Failed;
    public long Nodes;
    public List<string> RefusedPages { get; } = [];

    public void Rec(string kind, bool ok, Func<string>? example = null)
    {
        if (!Kinds.TryGetValue(kind, out FactTally? t))
        {
            Kinds[kind] = t = new FactTally();
        }
        t.Total++;
        if (ok)
        {
            t.Agree++;
        }
        else if (t.Examples.Count < 8 && example != null)
        {
            t.Examples.Add(example());
        }
    }

    public void Merge(FactsReport other)
    {
        foreach ((string kind, FactTally t) in other.Kinds)
        {
            if (!Kinds.TryGetValue(kind, out FactTally? mine))
            {
                Kinds[kind] = mine = new FactTally();
            }
            mine.Total += t.Total;
            mine.Agree += t.Agree;
            foreach (string e in t.Examples)
            {
                if (mine.Examples.Count < 8)
                {
                    mine.Examples.Add(e);
                }
            }
        }
        Pages += other.Pages;
        Refused += other.Refused;
        Failed += other.Failed;
        Nodes += other.Nodes;
        RefusedPages.AddRange(other.RefusedPages);
    }
}

internal static class FactsCompare
{
    private static OneRender.Text.TextFontProvider Fonts => OneRender.Image.FontProviders.ForCurrentThread();

    public static int Run(string capturesDir, string snapshotRoot, string set, string outDir, string? detailDir, int parallel, CancellationToken ct)
    {
        List<(string Page, string File)> files = [];
        foreach (string dir in Directory.EnumerateDirectories(capturesDir).Order(StringComparer.Ordinal))
        {
            foreach (string f in Directory.EnumerateFiles(dir, "*.facts.json.gz").Order(StringComparer.Ordinal))
            {
                files.Add((Path.GetFileName(dir), f));
            }
        }
        SortedDictionary<string, FactsReport> byViewport = new(StringComparer.Ordinal);
        object gate = new();
        string product = "";
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel), CancellationToken = ct }, item =>
        {
            FactsReport local = new();
            string vp = "";
            try
            {
                using FileStream fs = File.OpenRead(item.File);
                using GZipStream gz = new(fs, CompressionMode.Decompress);
                using JsonDocument doc = JsonDocument.Parse(gz);
                JsonElement root = doc.RootElement;
                vp = root.GetProperty("viewport").GetString()!;
                lock (gate)
                {
                    product = root.GetProperty("product").GetString()!;
                }
                ComparePage(root, snapshotRoot, item.Page, vp, local, detailDir, ct);
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException or KeyNotFoundException or DomSnapshotFormatException)
            {
                local.Failed++;
                local.RefusedPages.Add(item.Page + ": " + e.GetType().Name + ": " + e.Message);
            }
            lock (gate)
            {
                if (!byViewport.TryGetValue(vp, out FactsReport? r))
                {
                    byViewport[vp] = r = new FactsReport();
                }
                r.Merge(local);
            }
        });
        FactsReport all = new();
        foreach (FactsReport r in byViewport.Values)
        {
            all.Merge(r);
        }
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "summary.md"), Markdown(set, product, byViewport, all));
        File.WriteAllText(Path.Combine(outDir, "summary.json"), Json(set, product, byViewport, all));
        if (detailDir != null)
        {
            Directory.CreateDirectory(detailDir);
            StringBuilder ex = new();
            foreach ((string vp, FactsReport r) in byViewport)
            {
                foreach ((string kind, FactTally t) in r.Kinds.Where(static k => k.Value.Examples.Count > 0))
                {
                    ex.AppendLine(vp + " " + kind + " (" + t.Agree + "/" + t.Total + ")");
                    foreach (string e in t.Examples)
                    {
                        ex.AppendLine("    " + e);
                    }
                }
            }
            File.WriteAllText(Path.Combine(detailDir, set + "-examples.txt"), ex.ToString());
        }
        Console.WriteLine($"facts {set}: {all.Pages} page captures, {all.Refused} refused, {all.Failed} failed");
        return 0;
    }

    private static void ComparePage(JsonElement root, string snapshotRoot, string pageId, string vpText, FactsReport rep, string? detailDir, CancellationToken ct)
    {
        string dom = root.GetProperty("dom").GetString()!;
        List<string> styles = [.. root.GetProperty("styles").EnumerateArray().Select(static e => e.GetString()!)];
        ViewportSize vp = Capture.ParseViewport(vpText);
        byte[] snapBytes = Encoding.UTF8.GetBytes(root.GetProperty("snapshot").GetRawText());
        (SnapshotPage page, _, Dictionary<string, (byte[] Body, string Type)> res) = Snapshots.Load(snapshotRoot, pageId);
        Document document = Document.Parse(dom, null, ct);
        rep.Pages++;
        DomSnapshot snapshot = DomSnapshot.Parse(snapBytes, styles);
        DomSnapshotMapResult mapped = DomSnapshotAdapter.Map(snapshot, document, new DomSnapshotMapOptions { ViewportWidth = vp.Width, ViewportHeight = vp.Height }, ct);
        if (mapped.Facts is null)
        {
            rep.Refused++;
            rep.RefusedPages.Add(pageId + ": " + mapped.Refusal);
            return;
        }
        RenderedFacts chrome = mapped.Facts;
        HtmlPageOptions options = new(Fonts)
        {
            ViewportWidth = vp.Width,
            ViewportHeight = vp.Height,
            BaseUrl = page.Url,
            StylesheetBytes = url => res.TryGetValue(Snapshots.UrlKey(url), out (byte[] Body, string Type) r) ? r.Body : null,
        };
        RenderedPage ours = AccessibilityFacts.Render(document, options, new FactsOptions { ComputedStyles = styles }, ct);
        FactsReport local = new();
        CompareFacts(document, chrome, ours.Facts, styles, local, pageId, ct);
        rep.Merge(local);
        _ = detailDir;
    }

    private static bool Within(LayoutRect c, LayoutRect o)
    {
        double tx = Math.Max(1, 0.005 * Math.Abs(c.Width));
        double ty = Math.Max(1, 0.005 * Math.Abs(c.Height));
        return Math.Abs(c.X - o.X) <= tx && Math.Abs(c.Width - o.Width) <= tx && Math.Abs(c.Y - o.Y) <= ty && Math.Abs(c.Height - o.Height) <= ty;
    }

    private static string R(LayoutRect r) => string.Create(CultureInfo.InvariantCulture, $"[{r.X:0.##},{r.Y:0.##},{r.Width:0.##},{r.Height:0.##}]");

    private static string R(LayoutRect? r) => r is null ? "none" : R(r.Value);

    private static bool RectEqual(LayoutRect? a, LayoutRect? b, double slack) =>
        a is null || b is null ? a is null && b is null
            : Math.Abs(a.Value.X - b.Value.X) <= slack && Math.Abs(a.Value.Y - b.Value.Y) <= slack && Math.Abs(a.Value.Width - b.Value.Width) <= slack && Math.Abs(a.Value.Height - b.Value.Height) <= slack;

    private static (int R, int G, int B)? Rgb(string? s)
    {
        if (s is null || !Rgba.TryParse(s, out Rgba c))
        {
            return null;
        }
        return (c.R, c.G, c.B);
    }

    internal static void CompareFacts(Document doc, RenderedFacts chrome, RenderedFacts ours, IReadOnlyList<string> styles, FactsReport rep, string page, CancellationToken ct)
    {
        int prevC = -1, prevO = -1;
        string prevName = "";
        for (int i = 0; i < doc.NodeCount; i++)
        {
            if ((i & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            Node node = doc.GetNode(i);
            bool element = node.IsElement;
            bool text = node.Kind == NodeKind.Text;
            if (!element && !text)
            {
                continue;
            }
            rep.Nodes++;
            NodeFacts c = chrome[node];
            NodeFacts o = ours[node];
            string who = page + " " + (element ? "<" + node.LocalName + ">" : "#text") + "@" + i.ToString(CultureInfo.InvariantCulture);
            bool cl = c.HasLayout, ol = o.HasLayout;
            if (cl || ol)
            {
                rep.Rec(element ? "box.exists.element" : "box.exists.text", cl && ol, () => who + " chrome=" + cl + " ours=" + ol);
            }
            if (element)
            {
                CompareGenerated(c, o, rep, who);
                CompareForm(c, o, rep, who);
            }
            if (!(cl && ol))
            {
                continue;
            }
            BoxFacts cb = c.Layout, ob = o.Layout;
            if (text)
            {
                rep.Rec("text.layoutText", cb.LayoutText == ob.LayoutText, () => who + " chrome=" + Clip(cb.LayoutText) + " ours=" + Clip(ob.LayoutText));
                rep.Rec("text.bounds", Within(cb.BorderBox, ob.BorderBox), () => who + " chrome=" + R(cb.BorderBox) + " ours=" + R(ob.BorderBox));
                TextBox[] ct1 = cb.TextBoxes.ToArray(), ot = ob.TextBoxes.ToArray();
                bool sameCount = ct1.Length == ot.Length;
                rep.Rec("text.boxes.count", sameCount, () => who + " chrome=" + ct1.Length + " ours=" + ot.Length);
                if (sameCount)
                {
                    bool ranges = true, bounds = true;
                    for (int k = 0; k < ct1.Length; k++)
                    {
                        ranges &= ct1[k].Start == ot[k].Start && ct1[k].Length == ot[k].Length;
                        bounds &= Within(ct1[k].Bounds, ot[k].Bounds);
                    }
                    rep.Rec("text.boxes.ranges", ranges, () => who + " " + string.Join(",", ct1.Select(t => t.Start + "+" + t.Length)) + " vs " + string.Join(",", ot.Select(t => t.Start + "+" + t.Length)));
                    rep.Rec("text.boxes.bounds", bounds, () => who + " chrome " + string.Join(" ", ct1.Select(t => R(t.Bounds))) + " ours " + string.Join(" ", ot.Select(t => R(t.Bounds))));
                }
                continue;
            }
            rep.Rec("box.borderBox", Within(cb.BorderBox, ob.BorderBox), () => who + " chrome=" + R(cb.BorderBox) + " ours=" + R(ob.BorderBox));
            rep.Rec("stacking.context", cb.IsStackingContext == ob.IsStackingContext, () => who + " chrome=" + cb.IsStackingContext + " ours=" + ob.IsStackingContext);
            rep.Rec("stacking.root", c.StackingContextNodeIndex == o.StackingContextNodeIndex, () => who + " chrome=" + c.StackingContextNodeIndex + " ours=" + o.StackingContextNodeIndex);
            int cp = cb.PaintOrder ?? -1, op = ob.PaintOrder ?? -1;
            rep.Rec("paint.order.exact", cp == op, () => who + " chrome=" + cp + " ours=" + op);
            if (prevC >= 0)
            {
                bool same = Math.Sign(cp - prevC) == Math.Sign(op - prevO);
                string pn = prevName;
                rep.Rec("paint.order.relative", same, () => pn + " -> " + who + " chrome " + prevC + "->" + cp + " ours " + prevO + "->" + op);
            }
            prevC = cp;
            prevO = op;
            prevName = who;
            rep.Rec("bg.presence", (cb.BlendedBackgroundColor is null) == (ob.BlendedBackgroundColor is null), () => who + " chrome=" + cb.BlendedBackgroundColor + " ours=" + ob.BlendedBackgroundColor);
            if (cb.BlendedBackgroundColor is not null && ob.BlendedBackgroundColor is not null)
            {
                rep.Rec("bg.color.exact", cb.BlendedBackgroundColor == ob.BlendedBackgroundColor, () => who + " chrome=" + cb.BlendedBackgroundColor + " ours=" + ob.BlendedBackgroundColor);
                (int R, int G, int B)? x = Rgb(cb.BlendedBackgroundColor), y = Rgb(ob.BlendedBackgroundColor);
                rep.Rec("bg.color.within2", x is { } a && y is { } b && Math.Abs(a.R - b.R) <= 2 && Math.Abs(a.G - b.G) <= 2 && Math.Abs(a.B - b.B) <= 2, () => who + " chrome=" + cb.BlendedBackgroundColor + " ours=" + ob.BlendedBackgroundColor);
            }
            double co = cb.TextColorOpacity ?? 1, oo = ob.TextColorOpacity ?? 1;
            rep.Rec("text.opacity", Math.Abs(co - oo) < 0.01, () => who + " chrome=" + co + " ours=" + oo);
            rep.Rec("rect.offset", RectEqual(cb.OffsetRect, ob.OffsetRect, 0), () => who + " chrome=" + R(cb.OffsetRect) + " ours=" + R(ob.OffsetRect));
            rep.Rec("rect.client", RectEqual(cb.ClientRect, ob.ClientRect, 0), () => who + " chrome=" + R(cb.ClientRect) + " ours=" + R(ob.ClientRect));
            rep.Rec("rect.scroll", RectEqual(cb.ScrollRect, ob.ScrollRect, 0), () => who + " chrome=" + R(cb.ScrollRect) + " ours=" + R(ob.ScrollRect));
            rep.Rec("rect.offset.within1", RectEqual(cb.OffsetRect, ob.OffsetRect, 1), null);
            rep.Rec("rect.client.within1", RectEqual(cb.ClientRect, ob.ClientRect, 1), null);
            rep.Rec("rect.scroll.within1", RectEqual(cb.ScrollRect, ob.ScrollRect, 1), null);
            foreach (string name in styles)
            {
                string? cv = cb.GetStyle(name), ov = ob.GetStyle(name);
                if (ov is null)
                {
                    // A property the engine does not compute is counted on its own, not as a disagreement of the others.
                    rep.Rec("style.uncomputed", false, () => name);
                    continue;
                }
                bool eq = string.Equals(cv, ov, StringComparison.Ordinal);
                rep.Rec("style.all", eq, () => who + " " + name + " chrome=" + Clip(cv) + " ours=" + Clip(ov));
                rep.Rec("style:" + name, eq, () => who + " chrome=" + Clip(cv) + " ours=" + Clip(ov));
            }
        }
    }

    private static string Clip(string? s) => s is null ? "none" : s.Length <= 40 ? s : s[..40] + "...";

    private static void CompareGenerated(NodeFacts c, NodeFacts o, FactsReport rep, string who)
    {
        IReadOnlyList<GeneratedBox> cg = c.GeneratedBoxes, og = o.GeneratedBoxes;
        if (cg.Count == 0 && og.Count == 0)
        {
            return;
        }
        string ct1 = string.Join(",", cg.Select(static g => g.PseudoType)), ot = string.Join(",", og.Select(static g => g.PseudoType));
        // The engine lists a generated box only when it has layout; compare on those the browser laid out as well.
        string cl = string.Join(",", cg.Where(static g => g.HasLayout).Select(static g => g.PseudoType));
        string ol = string.Join(",", og.Where(static g => g.HasLayout).Select(static g => g.PseudoType));
        rep.Rec("generated.types", cl == ol, () => who + " chrome=" + ct1 + " ours=" + ot);
        if (cl != ol)
        {
            return;
        }
        List<GeneratedBox> cx = [.. cg.Where(static g => g.HasLayout)], ox = [.. og.Where(static g => g.HasLayout)];
        for (int k = 0; k < cx.Count && k < ox.Count; k++)
        {
            GeneratedBox x = cx[k], y = ox[k];
            rep.Rec("generated.bounds", Within(x.Layout.BorderBox, y.Layout.BorderBox), () => who + " " + x.PseudoType + " chrome=" + R(x.Layout.BorderBox) + " ours=" + R(y.Layout.BorderBox));
            rep.Rec("generated.text", x.Text == y.Text, () => who + " " + x.PseudoType + " chrome=" + Clip(x.Text) + " ours=" + Clip(y.Text));
        }
    }

    private static void CompareForm(NodeFacts c, NodeFacts o, FactsReport rep, string who)
    {
        if (c.InputValue is not null || o.InputValue is not null)
        {
            rep.Rec("form.inputValue", c.InputValue == o.InputValue, () => who + " chrome=" + Clip(c.InputValue) + " ours=" + Clip(o.InputValue));
        }
        if (c.TextValue is not null || o.TextValue is not null)
        {
            rep.Rec("form.textValue", c.TextValue == o.TextValue, () => who + " chrome=" + Clip(c.TextValue) + " ours=" + Clip(o.TextValue));
        }
        if (c.InputChecked || o.InputChecked)
        {
            rep.Rec("form.inputChecked", c.InputChecked == o.InputChecked, () => who);
        }
        if (c.OptionSelected || o.OptionSelected)
        {
            rep.Rec("form.optionSelected", c.OptionSelected == o.OptionSelected, () => who);
        }
    }

    private static string Pct(double v) => (v * 100).ToString("0.000", CultureInfo.InvariantCulture) + "%";

    private static string Markdown(string set, string product, SortedDictionary<string, FactsReport> byViewport, FactsReport all)
    {
        StringBuilder sb = new();
        sb.AppendLine("# Facts comparison: " + set);
        sb.AppendLine();
        sb.AppendLine("- reference: " + product + " `DOMSnapshot.captureSnapshot` facts; ours: the engine's facts for the same serialized document");
        sb.AppendLine("- tolerance for boxes: max(1 px, 0.5% of the reference extent) per axis; rectangles of CSSOM View, paint order, stacking, colors and styles are compared exactly unless the kind says otherwise");
        sb.AppendLine();
        sb.AppendLine("| viewport | page captures | refused by the mapping | failed | element and text nodes |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach ((string vp, FactsReport r) in byViewport)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {vp} | {r.Pages} | {r.Refused} | {r.Failed} | {r.Nodes} |"));
        }
        sb.AppendLine();
        sb.AppendLine("Agreement per fact kind (agree / compared, rate):");
        sb.AppendLine();
        sb.Append("| fact kind |");
        foreach (string vp in byViewport.Keys)
        {
            sb.Append(' ').Append(vp).Append(" |");
        }
        sb.AppendLine(" all |");
        sb.Append("|---|");
        foreach (string unused in byViewport.Keys)
        {
            _ = unused;
            sb.Append("---|");
        }
        sb.AppendLine("---|");
        foreach (string kind in all.Kinds.Keys.Where(static k => !k.StartsWith("style:", StringComparison.Ordinal)))
        {
            sb.Append("| ").Append(kind).Append(" |");
            foreach (FactsReport r in byViewport.Values)
            {
                sb.Append(r.Kinds.TryGetValue(kind, out FactTally? t) ? string.Create(CultureInfo.InvariantCulture, $" {t.Agree}/{t.Total} {Pct(t.Rate)} |") : " - |");
            }
            FactTally a = all.Kinds[kind];
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $" {a.Agree}/{a.Total} {Pct(a.Rate)} |"));
        }
        sb.AppendLine();
        sb.AppendLine("Computed-style agreement by property, all viewports (properties below 99.9%):");
        sb.AppendLine();
        sb.AppendLine("| property | agree / compared | rate |");
        sb.AppendLine("|---|---|---|");
        foreach ((string kind, FactTally t) in all.Kinds.Where(static k => k.Key.StartsWith("style:", StringComparison.Ordinal)).OrderBy(static k => k.Value.Rate).ThenBy(static k => k.Key, StringComparer.Ordinal))
        {
            if (t.Rate < 0.999)
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {kind[6..]} | {t.Agree}/{t.Total} | {Pct(t.Rate)} |"));
            }
        }
        if (all.Refused + all.Failed > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Captures the mapping refused or that failed to read (page and reason, first 20):");
            sb.AppendLine();
            foreach (string s in all.RefusedPages.Take(20))
            {
                sb.AppendLine("- " + s.Replace('\n', ' '));
            }
        }
        return sb.ToString();
    }

    private static string Json(string set, string product, SortedDictionary<string, FactsReport> byViewport, FactsReport all)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter w = new(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("set", set);
            w.WriteString("reference", product);
            w.WriteStartObject("viewports");
            foreach ((string vp, FactsReport r) in byViewport)
            {
                WriteReport(w, vp, r);
            }
            w.WriteEndObject();
            WriteReport(w, "all", all);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteReport(Utf8JsonWriter w, string name, FactsReport r)
    {
        w.WriteStartObject(name);
        w.WriteNumber("pages", r.Pages);
        w.WriteNumber("refused", r.Refused);
        w.WriteNumber("failed", r.Failed);
        w.WriteNumber("nodes", r.Nodes);
        w.WriteStartObject("kinds");
        foreach ((string kind, FactTally t) in r.Kinds)
        {
            w.WriteStartObject(kind);
            w.WriteNumber("compared", t.Total);
            w.WriteNumber("agree", t.Agree);
            w.WriteEndObject();
        }
        w.WriteEndObject();
        w.WriteEndObject();
    }
}
