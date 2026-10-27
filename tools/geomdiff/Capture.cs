using System.Globalization;
using System.Text.Json;

namespace OneRender.GeomDiff;

internal static class Capture
{
    public static ViewportSize ParseViewport(string s)
    {
        string[] p = s.Split('x');
        if (p.Length != 2 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out int w) || !int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int h) || w < 1 || h < 1)
        {
            throw new ArgumentException("viewport must be WxH, got " + s);
        }
        return new ViewportSize { Width = w, Height = h };
    }

    public static string FileFor(string root, string page, ViewportSize vp) => Path.Combine(root, page, vp.ToString() + ".json.gz");

    /// <summary>Captures one page at one viewport from the frozen snapshot.</summary>
    public static async Task<GeometryCapture> CaptureAsync(ChromeInstance chrome, string snapshotRoot, string pageId, ViewportSize vp, CancellationToken ct)
    {
        (SnapshotPage page, string html, Dictionary<string, (byte[] Body, string Type)> res) = Snapshots.Load(snapshotRoot, pageId);
        using Tab tab = await Tab.OpenAsync(chrome, Snapshots.Replay(page, html, res), vp, ct).ConfigureAwait(false);
        await tab.NavigateAsync(page.Url, 60000, 300, ct, measure: true).ConfigureAwait(false);
        JsonElement metrics = await tab.SendAsync("Page.getLayoutMetrics", ct: ct).ConfigureAwait(false);
        JsonElement size = metrics.TryGetProperty("cssContentSize", out JsonElement cs) ? cs : metrics.GetProperty("contentSize");
        // GEOMDIFF_STYLES (comma-separated property names) adds computed styles and writes the raw snapshot beside the capture, for probes of UA defaults.
        string? extra = Environment.GetEnvironmentVariable("GEOMDIFF_STYLES");
        string styleList = "\"display\"" + (string.IsNullOrEmpty(extra) ? "" : string.Concat(extra.Split(',').Select(static x => ",\"" + x + "\"")));
        JsonElement snap = await tab.SendAsync("DOMSnapshot.captureSnapshot", "{\"computedStyles\":[" + styleList + "]}", 120000, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(extra))
        {
            string dumpDir = Environment.GetEnvironmentVariable("GEOMDIFF_STYLES_OUT") ?? Path.GetTempPath();
            Directory.CreateDirectory(dumpDir);
            File.WriteAllText(Path.Combine(dumpDir, pageId + "-" + vp + ".styles.json"), snap.GetRawText());
        }
        GeometryCapture cap = new()
        {
            Source = "chrome",
            Page = pageId,
            Viewport = vp,
            DocumentWidth = size.GetProperty("width").GetDouble(),
            DocumentHeight = size.GetProperty("height").GetDouble(),
        };
        cap.Elements = ExtractElements(snap);
        return cap;
    }

    /// <summary>
    /// Turns a DOMSnapshot into element records keyed by element path. Pseudo-elements, shadow trees (the insides of
    /// form controls), template contents and other documents are not elements of the parsed tree and are skipped.
    /// </summary>
    public static List<ElementRecord> ExtractElements(JsonElement snapshot)
    {
        JsonElement strings = snapshot.GetProperty("strings");
        string[] str = new string[strings.GetArrayLength()];
        int si = 0;
        foreach (JsonElement s in strings.EnumerateArray())
        {
            str[si++] = s.GetString() ?? "";
        }
        JsonElement doc = snapshot.GetProperty("documents")[0];
        JsonElement nodes = doc.GetProperty("nodes");
        int[] parent = ReadInts(nodes.GetProperty("parentIndex"));
        int[] type = ReadInts(nodes.GetProperty("nodeType"));
        int[] name = ReadInts(nodes.GetProperty("nodeName"));
        int[] value = nodes.TryGetProperty("nodeValue", out JsonElement nv) ? ReadInts(nv) : new int[parent.Length];
        int n = parent.Length;
        bool[] skipChildren = new bool[n];
        bool[] pseudo = new bool[n];
        if (nodes.TryGetProperty("pseudoType", out JsonElement pt))
        {
            foreach (JsonElement i in pt.GetProperty("index").EnumerateArray())
            {
                pseudo[i.GetInt32()] = true;
            }
        }
        if (nodes.TryGetProperty("shadowRootType", out JsonElement sr))
        {
            foreach (JsonElement i in sr.GetProperty("index").EnumerateArray())
            {
                skipChildren[i.GetInt32()] = true;
            }
        }
        string?[] path = new string?[n];
        int[] childElements = new int[n];
        bool[] hasText = new bool[n];
        int[] elementOfNode = new int[n];
        Array.Fill(elementOfNode, -1);
        List<ElementRecord> records = [];
        for (int i = 0; i < n; i++)
        {
            int p = parent[i];
            if (type[i] == 3 && p >= 0 && !skipChildren[p] && path[p] != null && value[i] >= 0 && !IsBlank(str[value[i]]))
            {
                hasText[p] = true;
            }
            if (type[i] != 1 || pseudo[i] || p < 0 || skipChildren[p] || (type[p] != 1 && type[p] != 9) || (type[p] == 1 && path[p] == null))
            {
                continue;
            }
            string tag = str[name[i]].ToLowerInvariant();
            path[i] = p >= 0 && type[p] == 9 ? childElements[p]++.ToString(CultureInfo.InvariantCulture) : path[p] + "/" + (childElements[p]++).ToString(CultureInfo.InvariantCulture);
            if (type[p] == 1 && str[name[p]].Equals("TEMPLATE", StringComparison.OrdinalIgnoreCase))
            {
                path[i] = null;
                continue;
            }
            elementOfNode[i] = records.Count;
            records.Add(new ElementRecord { Path = path[i]!, Tag = tag });
        }
        for (int i = 0; i < n; i++)
        {
            if (elementOfNode[i] >= 0)
            {
                records[elementOfNode[i]].Text = hasText[i];
            }
        }
        JsonElement layout = doc.GetProperty("layout");
        JsonElement nodeIndex = layout.GetProperty("nodeIndex");
        JsonElement bounds = layout.GetProperty("bounds");
        JsonElement styles = layout.GetProperty("styles");
        for (int k = 0; k < nodeIndex.GetArrayLength(); k++)
        {
            int ni = nodeIndex[k].GetInt32();
            int rec = elementOfNode[ni];
            if (rec < 0 || records[rec].Box != null)
            {
                continue;
            }
            JsonElement b = bounds[k];
            records[rec].Box = [Round(b[0].GetDouble()), Round(b[1].GetDouble()), Round(b[2].GetDouble()), Round(b[3].GetDouble())];
            if (styles[k].GetArrayLength() > 0 && styles[k][0].GetInt32() >= 0)
            {
                records[rec].Display = str[styles[k][0].GetInt32()];
            }
        }
        return records;
    }

    private static double Round(double v) => Math.Round(v, 4);

    private static bool IsBlank(string s)
    {
        foreach (char c in s)
        {
            if (c is not (' ' or '\t' or '\n' or '\r' or '\f'))
            {
                return false;
            }
        }
        return true;
    }

    private static int[] ReadInts(JsonElement arr)
    {
        int[] r = new int[arr.GetArrayLength()];
        int i = 0;
        foreach (JsonElement e in arr.EnumerateArray())
        {
            r[i++] = e.GetInt32();
        }
        return r;
    }

    public static async Task<int> RunAsync(string snapshotRoot, string output, string chromePath, string fontConfig, List<ViewportSize> viewports, int jobs, int limit, string workDir, bool? noSandbox, CancellationToken ct)
    {
        List<string> ids = Snapshots.PageIds(snapshotRoot);
        if (limit > 0 && ids.Count > limit)
        {
            ids = ids.GetRange(0, limit);
        }
        List<string> todo = [.. ids.Where(id => viewports.Any(vp => !File.Exists(FileFor(output, id, vp))))];
        Console.WriteLine($"capturing {todo.Count} of {ids.Count} pages, {viewports.Count} viewport(s), {jobs} chrome(s)");
        string browser = "";
        string agent = "";
        object gate = new();
        ChromeOptions opt = new() { ChromePath = chromePath, FontConfigFile = fontConfig, WorkDir = workDir, NoSandbox = noSandbox };
        List<string> failures = await Pool.RunAsync(todo, static id => id, jobs, opt, 100, async (chrome, id) =>
        {
            lock (gate)
            {
                browser = chrome.Version;
                agent = chrome.UserAgent;
            }
            foreach (ViewportSize vp in viewports)
            {
                if (File.Exists(FileFor(output, id, vp)))
                {
                    continue;
                }
                GeometryCapture cap = await CaptureAsync(chrome, snapshotRoot, id, vp, ct).ConfigureAwait(false);
                Store.Write(FileFor(output, id, vp), cap, Ctx.Default.GeometryCapture);
            }
        }, ct, Console.WriteLine).ConfigureAwait(false);
        RunMeta meta = new()
        {
            Browser = browser,
            UserAgent = agent,
            Flags = [.. ChromeInstance.Flags, noSandbox == true ? "--no-sandbox" : "(sandbox if available)"],
            FontSet = FontSets.Describe(fontConfig),
            Viewports = [.. viewports.Select(static v => v.ToString())],
            Pages = ids.Count,
            Captured = ids.Count(id => viewports.All(vp => File.Exists(FileFor(output, id, vp)))),
            Failures = failures,
        };
        string metaPath = Path.Combine(output, "run.json");
        if (browser.Length > 0 || !File.Exists(metaPath))
        {
            Store.Write(metaPath, meta, PrettyCtx.Default.RunMeta);
        }
        Console.WriteLine($"captured {meta.Captured}/{meta.Pages}, failures {failures.Count}");
        foreach (string f in failures.Take(10))
        {
            Console.WriteLine("  failed: " + f);
        }
        return 0;
    }
}

internal static class FontSets
{
    /// <summary>The fontconfig file written by fontconfig.sh carries a comment line "fontset: &lt;hash&gt; &lt;dir&gt;".</summary>
    public static string Describe(string fontConfigFile)
    {
        foreach (string line in File.ReadLines(fontConfigFile))
        {
            int i = line.IndexOf("fontset:", StringComparison.Ordinal);
            if (i >= 0)
            {
                return line[(i + 8)..].Replace("-->", "", StringComparison.Ordinal).Trim();
            }
        }
        return "unknown";
    }
}
