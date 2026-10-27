using System.Globalization;
using System.Text;
using System.Text.Json;
using OneRender.Image;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.WptRunner.Chrome;

namespace OneRender.GeomDiff;

/// <summary>
/// Pixel similarity, for information: each frozen page is screenshotted in Chrome (the settled page, the geometry differential's
/// flags, fonts and viewports) and rendered by the engine through the image backend, then compared with SSIM.
/// </summary>
internal static class Pixels
{
    /// <summary>One page at one viewport.</summary>
    internal sealed record Row(string Page, ViewportSize Viewport, int Height, double Ssim, double DifferingPixels, bool Cropped);

    public static async Task<int> RunAsync(string snapshotRoot, string output, string setName, string chromePath, string fontConfig, List<ViewportSize> viewports,
        int jobs, int maxHeight, string? dump, string workDir, bool? noSandbox, CancellationToken ct)
    {
        List<string> ids = Snapshots.PageIds(snapshotRoot);
        List<Row> rows = [];
        ChromeOptions opt = new() { ChromePath = chromePath, FontConfigFile = fontConfig, WorkDir = workDir, NoSandbox = noSandbox };
        string browser = "";
        List<string> failures = await Pool.RunAsync(ids, static id => id, jobs, opt, 100, async (chrome, id) =>
        {
            browser = chrome.Version;
            foreach (ViewportSize vp in viewports)
            {
                Row row = await OnePageAsync(chrome, snapshotRoot, id, vp, maxHeight, dump, ct).ConfigureAwait(false);
                lock (rows)
                {
                    rows.Add(row);
                }
            }
        }, ct, Console.WriteLine).ConfigureAwait(false);
        rows.Sort(static (x, y) => { int c = string.CompareOrdinal(x.Page, y.Page); return c != 0 ? c : x.Viewport.Width.CompareTo(y.Viewport.Width); });
        Directory.CreateDirectory(output);
        StringBuilder tsv = new("page\tviewport\theight\tcropped\tssim\tdiffering-pixels\n");
        foreach (Row r in rows)
        {
            tsv.Append(r.Page).Append('\t').Append(r.Viewport).Append('\t').Append(r.Height.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(r.Cropped ? "yes" : "no").Append('\t')
                .Append(r.Ssim.ToString("F4", CultureInfo.InvariantCulture)).Append('\t').Append(r.DifferingPixels.ToString("F4", CultureInfo.InvariantCulture)).Append('\n');
        }
        File.WriteAllText(Path.Combine(output, "pages.tsv"), tsv.ToString());
        File.WriteAllText(Path.Combine(output, "summary.md"), Summary(setName, rows, failures, browser, FontSets.Describe(fontConfig).Split(' ')[0], maxHeight));
        Console.WriteLine($"{setName}: {rows.Count} page renders, {failures.Count} failures");
        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task<Row> OnePageAsync(ChromeInstance chrome, string root, string id, ViewportSize vp, int maxHeight, string? dump, CancellationToken ct)
    {
        (SnapshotPage page, string html, Dictionary<string, (byte[] Body, string Type)> res) = Snapshots.Load(root, id);
        int height;
        byte[] png;
        bool cropped;
        using (Tab tab = await Tab.OpenAsync(chrome, Snapshots.Replay(page, html, res), vp, ct).ConfigureAwait(false))
        {
            await tab.NavigateAsync(page.Url, 60000, 300, ct, measure: true).ConfigureAwait(false);
            JsonElement metrics = await tab.SendAsync("Page.getLayoutMetrics", ct: ct).ConfigureAwait(false);
            JsonElement size = metrics.TryGetProperty("cssContentSize", out JsonElement cs) ? cs : metrics.GetProperty("contentSize");
            int doc = (int)Math.Ceiling(size.GetProperty("height").GetDouble());
            height = Math.Max(vp.Height, doc);
            cropped = height > maxHeight;
            height = Math.Min(height, maxHeight);
            string shot = "{\"format\":\"png\",\"captureBeyondViewport\":true,\"clip\":{\"x\":0,\"y\":0,\"width\":" + vp.Width.ToString(CultureInfo.InvariantCulture)
                + ",\"height\":" + height.ToString(CultureInfo.InvariantCulture) + ",\"scale\":1}}";
            JsonElement r = await tab.SendAsync("Page.captureScreenshot", shot, 120000, ct).ConfigureAwait(false);
            png = Convert.FromBase64String(r.GetProperty("data").GetString()!);
        }
        Screenshot chromeImage = PngReader.Decode(png);
        RgbaImage ours = Render(html, page.Url, res, vp, height, ct);
        if (chromeImage.Width != ours.Width || chromeImage.Height != ours.Height)
        {
            throw new InvalidDataException($"{id} {vp}: Chrome image {chromeImage.Width}x{chromeImage.Height}, ours {ours.Width}x{ours.Height}");
        }
        (double ssim, double differing) = Ssim.Compare(chromeImage.Rgba, ours.Pixels, ours.Width, ours.Height);
        if (dump != null)
        {
            Directory.CreateDirectory(dump);
            File.WriteAllBytes(Path.Combine(dump, id + "-" + vp + "-chrome.png"), png);
            File.WriteAllBytes(Path.Combine(dump, id + "-" + vp + "-ours.png"), EncodePng(ours));
        }
        return new Row(id, vp, height, ssim, differing, cropped);
    }

    /// <summary>
    /// Our image: the same steps as <c>HtmlRender.ToImage</c>, but the layout viewport keeps the capture's height (viewport units and
    /// percentages resolve against it, as in Chrome) while the image is as tall as the screenshot.
    /// </summary>
    private static RgbaImage Render(string html, string url, Dictionary<string, (byte[] Body, string Type)> res, ViewportSize vp, int imageHeight, CancellationToken ct)
    {
        Func<string, byte[]?> resources = u => res.TryGetValue(Snapshots.UrlKey(u), out (byte[] Body, string Type) r) ? r.Body : null;
        ImageStore images = new(resources, url);
        HtmlPageOptions options = new(FontProviders.ForCurrentThread())
        {
            Images = images,
            Replaced = images,
            ViewportWidth = vp.Width,
            ViewportHeight = vp.Height,
            BaseUrl = url,
            StylesheetBytes = resources,
        };
        HtmlPage doc = HtmlPage.Create(html, options, ct);
        DisplayList list = new();
        doc.Paint(list, null, ct);
        return DisplayListRasterizer.Render(list, vp.Width, imageHeight, new RasterOptions { Background = PaintColor.White, Images = images }, ct);
    }

    /// <summary>An 8-bit RGBA PNG, for looking at a page by eye.</summary>
    private static byte[] EncodePng(RgbaImage image)
    {
        using MemoryStream raw = new();
        using (System.IO.Compression.ZLibStream z = new(raw, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int y = 0; y < image.Height; y++)
            {
                z.WriteByte(0);
                z.Write(image.Pixels, y * image.Width * 4, image.Width * 4);
            }
        }
        using MemoryStream png = new();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)image.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)image.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(png, "IHDR"u8, ihdr);
        Chunk(png, "IDAT"u8, raw.ToArray());
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void Chunk(MemoryStream s, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        s.Write(type);
        s.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type)
        {
            crc = Crc(crc, b);
        }
        foreach (byte b in data)
        {
            crc = Crc(crc, b);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, ~crc);
        s.Write(len);
    }

    private static uint Crc(uint crc, byte b)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc;
    }

    private static string Summary(string set, List<Row> rows, List<string> failures, string browser, string fonts, int maxHeight)
    {
        StringBuilder sb = new();
        sb.Append("# Pixel similarity: ").Append(set).Append("\n\n");
        sb.Append("- Chrome: ").Append(browser).Append("; fonts: ").Append(fonts).Append('\n');
        sb.Append("- Screenshots are cropped to ").Append(maxHeight.ToString(CultureInfo.InvariantCulture)).Append(" CSS px tall; SSIM is on luma, 8x8 windows, stride 4. Information only.\n\n");
        sb.Append("| viewport | pages | mean SSIM | min SSIM | min page | pixels identical (mean) |\n|---|---|---|---|---|---|\n");
        foreach (IGrouping<int, Row> g in rows.GroupBy(static r => r.Viewport.Width).OrderByDescending(static g => g.Key))
        {
            Row worst = g.OrderBy(static r => r.Ssim).ThenBy(static r => r.Page, StringComparer.Ordinal).First();
            sb.Append("| ").Append(worst.Viewport).Append(" | ").Append(g.Count().ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(g.Average(static r => r.Ssim).ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(worst.Ssim.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ").Append(worst.Page).Append(" | ")
                .Append((100 * (1 - g.Average(static r => r.DifferingPixels))).ToString("F2", CultureInfo.InvariantCulture)).Append("% |\n");
        }
        sb.Append("\n## Worst pages\n\n| page | viewport | SSIM | differing pixels |\n|---|---|---|---|\n");
        foreach (Row r in rows.OrderBy(static r => r.Ssim).ThenBy(static r => r.Page, StringComparer.Ordinal).Take(5))
        {
            sb.Append("| ").Append(r.Page).Append(" | ").Append(r.Viewport).Append(" | ").Append(r.Ssim.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append((100 * r.DifferingPixels).ToString("F2", CultureInfo.InvariantCulture)).Append("% |\n");
        }
        if (failures.Count > 0)
        {
            sb.Append("\n## Failures\n\n");
            foreach (string f in failures)
            {
                sb.Append("- ").Append(f).Append('\n');
            }
        }
        return sb.ToString();
    }
}
