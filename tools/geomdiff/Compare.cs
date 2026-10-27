using System.Globalization;
using System.Text;

namespace OneRender.GeomDiff;

internal enum Verdict
{
    Match,
    PositionOnly,
    SizeOnly,
    PositionAndSize,
    TextRun,
    MissingOurs,
    MissingChrome,
}

internal sealed class Tolerance(double px, double percent)
{
    public double Px { get; } = px;
    public double Percent { get; } = percent;

    /// <summary>Allowed difference along an axis whose reference extent is <paramref name="extent"/>: the larger of the pixel and percent allowances.</summary>
    public double Along(double extent) => Math.Max(Px, Percent / 100.0 * Math.Abs(extent));
}

internal static class Comparer
{
    /// <summary>
    /// Classifies one element. Both boxes are [x, y, w, h]; the reference (Chrome) extent sets the tolerance on each axis.
    /// A size difference on an element that holds text of its own or is inline is reported as text-run: its extent comes
    /// from line breaking and glyph advances, not from the box model.
    /// </summary>
    public static Verdict Classify(double[]? chrome, double[]? ours, bool textBearing, Tolerance tol)
    {
        if (chrome == null && ours == null)
        {
            return Verdict.Match;
        }
        if (ours == null)
        {
            return Verdict.MissingOurs;
        }
        if (chrome == null)
        {
            return Verdict.MissingChrome;
        }
        double tx = tol.Along(chrome[2]);
        double ty = tol.Along(chrome[3]);
        bool pos = Math.Abs(ours[0] - chrome[0]) > tx || Math.Abs(ours[1] - chrome[1]) > ty;
        bool size = Math.Abs(ours[2] - chrome[2]) > tx || Math.Abs(ours[3] - chrome[3]) > ty;
        if (!pos && !size)
        {
            return Verdict.Match;
        }
        if (size && textBearing)
        {
            return Verdict.TextRun;
        }
        return pos ? (size ? Verdict.PositionAndSize : Verdict.PositionOnly) : Verdict.SizeOnly;
    }

    public static bool TextBearing(ElementRecord? chrome) =>
        chrome != null && (chrome.Text || (chrome.Display?.StartsWith("inline", StringComparison.Ordinal) == true && chrome.Display != "inline-block" && chrome.Display != "inline-flex" && chrome.Display != "inline-grid" && chrome.Display != "inline-table"));

    private sealed class Tally
    {
        public readonly ViewportSummary Summary = new();
        public readonly List<double> PageRates = [];
        public readonly Dictionary<string, long> TagMismatch = new(StringComparer.Ordinal);
        public readonly Dictionary<string, long> TagTotal = new(StringComparer.Ordinal);
    }

    private static void Add(ClassCounts c, Verdict v)
    {
        switch (v)
        {
            case Verdict.PositionOnly: c.PositionOnly++; break;
            case Verdict.SizeOnly: c.SizeOnly++; break;
            case Verdict.PositionAndSize: c.PositionAndSize++; break;
            case Verdict.TextRun: c.TextRun++; break;
            case Verdict.MissingOurs: c.MissingOurs++; break;
            case Verdict.MissingChrome: c.MissingChrome++; break;
        }
    }

    public static string ClassName(Verdict v) => v switch
    {
        Verdict.PositionOnly => "position-only",
        Verdict.SizeOnly => "size-only",
        Verdict.PositionAndSize => "position-and-size",
        Verdict.TextRun => "text-run",
        Verdict.MissingOurs => "missing-ours",
        Verdict.MissingChrome => "missing-chrome",
        _ => "match",
    };

    /// <summary>Compares one page; returns its detail and adds to the running tally.</summary>
    private static PageDetail ComparePage(GeometryCapture chrome, GeometryCapture? ours, Tolerance tol, Tally t)
    {
        Dictionary<string, ElementRecord> theirs = new(StringComparer.Ordinal);
        if (ours != null)
        {
            foreach (ElementRecord e in ours.Elements)
            {
                theirs[e.Path] = e;
            }
        }
        Dictionary<string, Verdict> verdicts = new(StringComparer.Ordinal);
        PageDetail detail = new() { Page = chrome.Page, Viewport = chrome.Viewport.ToString() };
        List<(ElementRecord Chrome, ElementRecord? Ours, Verdict V)> rows = [];
        Dictionary<string, string> chromeTags = new(StringComparer.Ordinal);
        long elements = 0;
        long matched = 0;
        foreach (ElementRecord c in chrome.Elements)
        {
            chromeTags[c.Path] = c.Tag;
            theirs.TryGetValue(c.Path, out ElementRecord? o);
            if (o != null && o.Tag != c.Tag)
            {
                // The same path names different elements: the two parsers built different trees here.
                t.Summary.TagConflicts++;
                o = null;
            }
            t.TagTotal[c.Tag] = t.TagTotal.GetValueOrDefault(c.Tag) + 1;
            if (c.Box == null && o?.Box == null)
            {
                continue;
            }
            Verdict v = Classify(c.Box, o?.Box, TextBearing(c), tol);
            elements++;
            if (v == Verdict.Match)
            {
                matched++;
                continue;
            }
            verdicts[c.Path] = v;
            rows.Add((c, o, v));
            t.TagMismatch[c.Tag] = t.TagMismatch.GetValueOrDefault(c.Tag) + 1;
        }
        foreach (ElementRecord o in theirs.Values.OrderBy(static e => e.Path, StringComparer.Ordinal))
        {
            if (o.Box == null || (chromeTags.TryGetValue(o.Path, out string? tag) && tag == o.Tag))
            {
                continue;
            }
            elements++;
            verdicts[o.Path] = Verdict.MissingChrome;
            rows.Add((new ElementRecord { Path = o.Path, Tag = o.Tag }, o, Verdict.MissingChrome));
        }
        foreach ((ElementRecord c, ElementRecord? o, Verdict v) in rows)
        {
            bool root = true;
            if (v is not (Verdict.MissingOurs or Verdict.MissingChrome))
            {
                for (string p = c.Path; ;)
                {
                    int cut = p.LastIndexOf('/');
                    if (cut < 0)
                    {
                        break;
                    }
                    p = p[..cut];
                    if (verdicts.TryGetValue(p, out Verdict pv) && pv is not (Verdict.MissingOurs or Verdict.MissingChrome))
                    {
                        root = false;
                        break;
                    }
                }
            }
            Add(t.Summary.Classes, v);
            if (root)
            {
                Add(t.Summary.RootCauses, v);
            }
            detail.Mismatches.Add(new MismatchRecord { Path = c.Path, Tag = c.Tag, Class = ClassName(v), RootCause = root, Chrome = c.Box, Ours = o?.Box });
        }
        detail.Elements = elements;
        detail.Matched = matched;
        t.Summary.Elements += elements;
        t.Summary.Matched += matched;
        t.Summary.Pages++;
        t.PageRates.Add(elements == 0 ? 1.0 : (double)matched / elements);
        return detail;
    }

    private static void Finish(Tally t, double gate)
    {
        ViewportSummary s = t.Summary;
        s.Rate = s.Elements == 0 ? 1.0 : (double)s.Matched / s.Elements;
        t.PageRates.Sort();
        s.MedianPageRate = t.PageRates.Count == 0 ? 1.0 : t.PageRates[t.PageRates.Count / 2];
        s.MinPageRate = t.PageRates.Count == 0 ? 1.0 : t.PageRates[0];
        s.PagesAtGate = t.PageRates.Count(r => r >= gate);
        s.WorstTags = [.. t.TagMismatch.OrderByDescending(static p => p.Value).ThenBy(static p => p.Key, StringComparer.Ordinal).Take(10)
            .Select(p => new TagCount { Tag = p.Key, Mismatches = p.Value, Elements = t.TagTotal.GetValueOrDefault(p.Key) })];
    }

    public sealed class Options
    {
        public string ChromeDir = "";
        public string? OursDir;
        public string? SnapshotDir;
        public bool UseEngine;
        public string Set = "";
        public string Label = "";
        public string OutDir = "";
        public string? DetailDir;
        public Tolerance Tol = new(1, 0.5);
        public string? ExpectFile;
        public bool PageTable;
        public double Gate = 0.99;
    }

    public static int Run(Options o, CancellationToken ct)
    {
        List<string> pageDirs = [.. Directory.EnumerateDirectories(o.ChromeDir).Order(StringComparer.Ordinal)];
        SortedDictionary<string, Tally> byViewport = new(StringComparer.Ordinal);
        Tally total = new();
        StringBuilder pageRows = new();
        RunMeta? meta = File.Exists(Path.Combine(o.ChromeDir, "run.json")) ? Store.Read(Path.Combine(o.ChromeDir, "run.json"), Ctx.Default.RunMeta) : null;
        foreach (string dir in pageDirs)
        {
            ct.ThrowIfCancellationRequested();
            string page = Path.GetFileName(dir);
            foreach (string file in Directory.EnumerateFiles(dir, "*.json.gz").Order(StringComparer.Ordinal))
            {
                GeometryCapture chrome = Store.Read(file, Ctx.Default.GeometryCapture);
                GeometryCapture? ours;
                if (o.UseEngine)
                {
                    ours = EngineSide.LayOut(o.SnapshotDir!, chrome.Page, chrome.Viewport, ct);
                }
                else
                {
                    string of = Path.Combine(o.OursDir!, page, Path.GetFileName(file));
                    ours = File.Exists(of) ? Store.Read(of, Ctx.Default.GeometryCapture) : null;
                }
                string vp = chrome.Viewport.ToString();
                if (!byViewport.TryGetValue(vp, out Tally? t))
                {
                    byViewport[vp] = t = new Tally();
                    t.Summary.Viewport = vp;
                }
                PageDetail d = ComparePage(chrome, ours, o.Tol, t);
                ComparePage(chrome, ours, o.Tol, total);
                pageRows.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {page} | {vp} | {d.Elements} | {d.Matched} | {Pct(d.Elements == 0 ? 1.0 : (double)d.Matched / d.Elements)} | {d.Mismatches.Count} |"));
                if (o.DetailDir != null)
                {
                    Store.Write(Path.Combine(o.DetailDir, page + "-" + vp + ".json.gz"), d, Ctx.Default.PageDetail);
                }
            }
        }
        foreach (Tally t in byViewport.Values)
        {
            Finish(t, o.Gate);
        }
        Finish(total, o.Gate);
        total.Summary.Viewport = "all";
        SummaryFile sum = new()
        {
            Set = o.Set,
            Label = o.Label,
            Ours = o.UseEngine ? "engine" : "capture",
            TolerancePx = o.Tol.Px,
            TolerancePercent = o.Tol.Percent,
            GateRate = o.Gate,
            Browser = meta?.Browser ?? "",
            FontSet = meta?.FontSet ?? "",
            Viewports = [.. byViewport.Values.Select(static t => t.Summary)],
            Total = total.Summary,
        };
        sum.GateMet = byViewport.Values.All(t => t.Summary.Rate >= o.Gate) && byViewport.Count > 0;
        Store.Write(Path.Combine(o.OutDir, "summary.json"), sum, PrettyCtx.Default.SummaryFile);
        File.WriteAllText(Path.Combine(o.OutDir, "summary.md"), Markdown(sum), new UTF8Encoding(false));
        if (o.PageTable)
        {
            File.WriteAllText(Path.Combine(o.OutDir, "pages.md"), "| page | viewport | elements | within tolerance | rate | mismatches |\n|---|---|---|---|---|---|\n" + pageRows, new UTF8Encoding(false));
        }
        Console.WriteLine(Markdown(sum));
        if (o.ExpectFile != null)
        {
            Expectation exp = Store.Read(o.ExpectFile, Ctx.Default.Expectation);
            ClassCounts g = total.Summary.Classes;
            ClassCounts e = exp.Classes;
            bool ok = g.PositionOnly == e.PositionOnly && g.SizeOnly == e.SizeOnly && g.PositionAndSize == e.PositionAndSize
                && g.TextRun == e.TextRun && g.MissingOurs == e.MissingOurs && g.MissingChrome == e.MissingChrome;
            Console.WriteLine(ok ? "expectation: PASS (class counts equal the perturbation)" : "expectation: FAIL");
            return ok ? 0 : 1;
        }
        return 0;
    }

    private static string Pct(double r) => (r * 100).ToString("0.000", CultureInfo.InvariantCulture) + "%";

    public static string Markdown(SummaryFile s)
    {
        StringBuilder b = new();
        b.AppendLine(CultureInfo.InvariantCulture, $"# Geometry comparison: {s.Set} / {s.Label}");
        b.AppendLine();
        b.AppendLine(CultureInfo.InvariantCulture, $"- reference: {s.Browser}; ours: {s.Ours}; tolerance: max({s.TolerancePx} px, {s.TolerancePercent}% of the reference extent)");
        b.AppendLine(CultureInfo.InvariantCulture, $"- font set: {s.FontSet}");
        b.AppendLine(CultureInfo.InvariantCulture, $"- gate: {Pct(s.GateRate)} of elements within tolerance per viewport; met: {(s.GateMet ? "yes" : "no")}");
        b.AppendLine();
        b.AppendLine("| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |");
        b.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (ViewportSummary v in s.Viewports.Append(s.Total))
        {
            ClassCounts c = v.Classes;
            b.AppendLine(CultureInfo.InvariantCulture, $"| {v.Viewport} | {v.Pages} | {v.Elements} | {v.Matched} | {Pct(v.Rate)} | {c.PositionOnly} | {c.SizeOnly} | {c.PositionAndSize} | {c.TextRun} | {c.MissingOurs} | {c.MissingChrome} | {v.TagConflicts} | {v.PagesAtGate} | {Pct(v.MedianPageRate)} |");
        }
        b.AppendLine();
        b.AppendLine("Root causes (mismatches with no mismatching ancestor), all viewports: "
            + string.Create(CultureInfo.InvariantCulture, $"position-only {s.Total.RootCauses.PositionOnly}, size-only {s.Total.RootCauses.SizeOnly}, position+size {s.Total.RootCauses.PositionAndSize}, text-run {s.Total.RootCauses.TextRun}."));
        if (s.Total.WorstTags.Count > 0)
        {
            b.AppendLine();
            b.AppendLine("Tags with the most mismatches: " + string.Join(", ", s.Total.WorstTags.Select(static t => string.Create(CultureInfo.InvariantCulture, $"{t.Tag} {t.Mismatches}/{t.Elements}"))));
        }
        return b.ToString();
    }
}

/// <summary>Our side: the engine behind the provisional geometry seam.</summary>
internal static class EngineSide
{
    public static GeometryCapture LayOut(string snapshotRoot, string pageId, ViewportSize vp, CancellationToken ct)
    {
        (SnapshotPage page, string html, Dictionary<string, (byte[] Body, string Type)> res) = Snapshots.Load(snapshotRoot, pageId);
        Func<Uri, byte[]?> resolve = uri => res.TryGetValue(Snapshots.UrlKey(uri.AbsoluteUri), out (byte[] Body, string Type) r) ? r.Body : null;
        IReadOnlyList<ElementBox> boxes = GeometryProbe.LayOut(html, new Uri(page.Url), resolve, vp.Width, vp.Height, ct);
        GeometryCapture cap = new() { Source = "engine", Page = pageId, Viewport = vp };
        foreach (ElementBox b in boxes)
        {
            cap.Elements.Add(new ElementRecord { Path = b.Path, Tag = b.Tag, Box = [b.X, b.Y, b.Width, b.Height] });
        }
        return cap;
    }
}
