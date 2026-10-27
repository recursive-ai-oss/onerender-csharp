using System.Text;
using System.Text.Json;

namespace OneRender.GeomDiff;

internal static class Perturb
{
    private static uint Hash(int seed, string page, string path)
    {
        uint h = 2166136261u ^ (uint)seed;
        foreach (char c in page + "|" + path)
        {
            h = unchecked((h ^ c) * 16777619u);
        }
        return h;
    }

    /// <summary>
    /// Writes a deliberately wrong copy of a Chrome capture and the class counts that wrongness must be reported as:
    /// about 2% of boxes moved, 2% resized, 2% moved and resized, 2% of text-bearing boxes resized, 1% dropped,
    /// and one extra element per page that Chrome does not have.
    /// </summary>
    public static int Run(string inDir, string outDir, int seed, Tolerance tol, string expectFile)
    {
        ClassCounts expect = new();
        foreach (string file in Directory.EnumerateFiles(inDir, "*.json.gz", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            GeometryCapture src = Store.Read(file, Ctx.Default.GeometryCapture);
            GeometryCapture dst = new() { Source = "perturbed", Page = src.Page, Viewport = src.Viewport, DocumentWidth = src.DocumentWidth, DocumentHeight = src.DocumentHeight };
            foreach (ElementRecord e in src.Elements)
            {
                ElementRecord c = new() { Path = e.Path, Tag = e.Tag, Display = e.Display, Text = e.Text, Box = e.Box == null ? null : [.. e.Box] };
                if (c.Box != null)
                {
                    uint bucket = Hash(seed, src.Page, e.Path) % 100;
                    bool text = Comparer.TextBearing(e);
                    double step = 2 * tol.Along(c.Box[2]) + 2;
                    double stepY = 2 * tol.Along(c.Box[3]) + 2;
                    switch (bucket)
                    {
                        case < 2:
                            c.Box[0] += step;
                            expect.PositionOnly++;
                            break;
                        case < 4 when !text:
                            c.Box[2] += step;
                            expect.SizeOnly++;
                            break;
                        case < 6 when !text:
                            c.Box[1] += stepY;
                            c.Box[3] += stepY;
                            expect.PositionAndSize++;
                            break;
                        case < 8 when text:
                            c.Box[2] += step;
                            expect.TextRun++;
                            break;
                        case 8:
                            expect.MissingOurs++;
                            continue;
                    }
                }
                dst.Elements.Add(c);
            }
            dst.Elements.Add(new ElementRecord { Path = "99/99", Tag = "div", Box = [0, 0, 10, 10] });
            expect.MissingChrome++;
            string rel = Path.GetRelativePath(inDir, file);
            Store.Write(Path.Combine(outDir, rel), dst, Ctx.Default.GeometryCapture);
        }
        Store.Write(expectFile, new Expectation { Classes = expect }, PrettyCtx.Default.Expectation);
        Console.WriteLine($"perturbed: position-only {expect.PositionOnly}, size-only {expect.SizeOnly}, position+size {expect.PositionAndSize}, text-run {expect.TextRun}, missing-ours {expect.MissingOurs}, missing-chrome {expect.MissingChrome}");
        return 0;
    }
}

internal static class FontsCheck
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "Liberation Sans", "Liberation Serif", "Liberation Mono", "Carlito", "Caladea",
    };

    /// <summary>
    /// Loads a page whose elements each carry data-expect (the family that must render them), asks Chrome which platform
    /// fonts it used for each, and fails if any is outside the five shipped families or not the expected one.
    /// </summary>
    public static async Task<int> RunAsync(string pagePath, string output, string chromePath, string fontConfig, string workDir, bool? noSandbox, CancellationToken ct)
    {
        string html = File.ReadAllText(pagePath);
        SnapshotPage page = new() { Id = "fonts-check", Url = Snapshots.StaticHost + "fonts-check/index.html" };
        using ChromeInstance chrome = await ChromeInstance.StartAsync(new ChromeOptions { ChromePath = chromePath, FontConfigFile = fontConfig, WorkDir = workDir, NoSandbox = noSandbox }, ct).ConfigureAwait(false);
        using Tab tab = await Tab.OpenAsync(chrome, Snapshots.Replay(page, html, []), new ViewportSize { Width = 1280, Height = 800 }, ct).ConfigureAwait(false);
        await tab.NavigateAsync(page.Url, 30000, 200, ct).ConfigureAwait(false);
        await tab.SendAsync("DOM.enable", ct: ct).ConfigureAwait(false);
        await tab.SendAsync("CSS.enable", ct: ct).ConfigureAwait(false);
        JsonElement root = await tab.SendAsync("DOM.getDocument", "{\"depth\":0}", ct: ct).ConfigureAwait(false);
        int rootId = root.GetProperty("root").GetProperty("nodeId").GetInt32();
        JsonElement found = await tab.SendAsync("DOM.querySelectorAll", "{\"nodeId\":" + J.N(rootId) + ",\"selector\":\"[data-expect]\"}", ct: ct).ConfigureAwait(false);
        StringBuilder md = new();
        md.AppendLine("# Fonts check");
        md.AppendLine();
        md.AppendLine($"Browser: {chrome.Version}  ");
        md.AppendLine($"Font set: {FontSets.Describe(fontConfig)}");
        md.AppendLine();
        md.AppendLine("| font-family as written | expected | Chrome used (family: glyphs) | ok |");
        md.AppendLine("|---|---|---|---|");
        bool allOk = true;
        foreach (JsonElement idEl in found.GetProperty("nodeIds").EnumerateArray())
        {
            string nodeId = J.N(idEl.GetInt32());
            JsonElement attrs = (await tab.SendAsync("DOM.getAttributes", "{\"nodeId\":" + nodeId + "}", ct: ct).ConfigureAwait(false)).GetProperty("attributes");
            string expect = "";
            string written = "";
            for (int i = 0; i + 1 < attrs.GetArrayLength(); i += 2)
            {
                string k = attrs[i].GetString()!;
                if (k == "data-expect")
                {
                    expect = attrs[i + 1].GetString()!;
                }
                else if (k == "data-family")
                {
                    written = attrs[i + 1].GetString()!;
                }
            }
            JsonElement fonts = (await tab.SendAsync("CSS.getPlatformFontsForNode", "{\"nodeId\":" + nodeId + "}", ct: ct).ConfigureAwait(false)).GetProperty("fonts");
            List<string> used = [];
            bool ok = fonts.GetArrayLength() > 0;
            foreach (JsonElement f in fonts.EnumerateArray())
            {
                string fam = f.GetProperty("familyName").GetString() ?? "";
                used.Add(fam + ": " + f.GetProperty("glyphCount").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
                ok &= Allowed.Contains(fam) && (expect == "any-shipped" || fam == expect);
            }
            allOk &= ok;
            md.AppendLine($"| `{written}` | {expect} | {string.Join("; ", used)} | {(ok ? "yes" : "NO")} |");
        }
        md.AppendLine();
        md.AppendLine(allOk ? "Result: every node rendered only with shipped faces, as expected." : "Result: FAILED.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, md.ToString(), new UTF8Encoding(false));
        Console.Write(md.ToString());
        return allOk ? 0 : 1;
    }
}
