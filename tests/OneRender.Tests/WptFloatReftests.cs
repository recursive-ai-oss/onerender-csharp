using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OneFont;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Image;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// Reftests of the CSS2 float and clear directories, judged by geometry: the page and its reference are laid out and the painted
/// pixels of the engine's own rendering are compared. Tests that need text, positioning or tables
/// are not covered here; the committed pairs are the text-free ones this layout gets right.
/// </summary>
public partial class WptFloatReftests
{
    private const int CanvasW = 800, CanvasH = 600, White = -1;

    /// <summary>The page rendered through the engine's own painter and rasterizer, one packed ARGB int per pixel.</summary>
    internal static int[] Raster(string html)
    {
        RgbaImage img = HtmlRender.ToImage(html, new HtmlRenderOptions { Width = CanvasW, Height = CanvasH });
        var px = new int[CanvasW * CanvasH];
        byte[] b = img.Pixels;
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = (b[(4 * i) + 3] << 24) | (b[4 * i] << 16) | (b[(4 * i) + 1] << 8) | b[(4 * i) + 2];
        }

        return px;
    }

    private static string? MatchOf(string html)
    {
        Match m = MatchRel().Match(html);
        if (m.Success)
        {
            return m.Groups[1].Value;
        }

        m = MatchRelAfter().Match(html);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex("<link[^>]*rel=[\"']match[\"'][^>]*href=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MatchRel();

    [GeneratedRegex("<link[^>]*href=[\"']([^\"']+)[\"'][^>]*rel=[\"']match[\"']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MatchRelAfter();

    /// <summary>
    /// Reads a test file. <c>.xht</c> files are XML in the suite; the HTML parser is stood in for by removing CDATA markers and
    /// expanding self-closed elements.
    /// </summary>
    private static string Read(string path)
    {
        string text = File.ReadAllText(path);
        if (path.EndsWith(".xht", StringComparison.Ordinal))
        {
            text = text.Replace("<![CDATA[", string.Empty, StringComparison.Ordinal).Replace("]]>", string.Empty, StringComparison.Ordinal);
            text = SelfClosed().Replace(text, m => IsVoid(m.Groups[1].Value) ? m.Value : "<" + m.Groups[1].Value + m.Groups[2].Value + "></" + m.Groups[1].Value + ">");
        }

        return text;
    }

    private static bool IsVoid(string tag) =>
        tag is "br" or "img" or "input" or "meta" or "link" or "hr" or "col" or "area" or "base" or "param";

    [GeneratedRegex("""<([A-Za-z][A-Za-z0-9]*)((?:[^>"']|"[^"]*"|'[^']*')*?)\s*/>""", RegexOptions.CultureInvariant)]
    private static partial Regex SelfClosed();

    /// <summary>Compares a test with its reference; returns null on a match and the first difference otherwise.</summary>
    private static string? Compare(string testPath, string refPath)
    {
        int[] a = Raster(Read(testPath));
        int[] b = Raster(Read(refPath));
        int diff = 0, first = -1;
        bool blank = true;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != White || b[i] != White)
            {
                blank = false;
            }

            if (a[i] != b[i])
            {
                diff++;
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        if (diff > 0)
        {
            return diff.ToString(CultureInfo.InvariantCulture) + " pixels differ, first at " + (first % CanvasW).ToString(CultureInfo.InvariantCulture) + "," + (first / CanvasW).ToString(CultureInfo.InvariantCulture)
                + ": test " + a[first].ToString("x8", CultureInfo.InvariantCulture) + " reference " + b[first].ToString("x8", CultureInfo.InvariantCulture);
        }

        return blank ? "nothing painted" : null;
    }

    [Fact]
    public void CommittedFloatReftestsPaintTheSameBoxesAsTheirReferences()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "testdata", "wpt-floats", "css", "CSS2");
        string[] tests = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "testdata", "wpt-floats", "tests.txt"));
        int count = 0;
        var failures = new StringBuilder();
        foreach (string line in tests)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            count++;
            string testPath = Path.Combine(dir, line);
            string? rel = MatchOf(Read(testPath));
            Assert.NotNull(rel);
            string? diff = Compare(testPath, Path.Combine(Path.GetDirectoryName(testPath)!, rel));
            if (diff is not null)
            {
                failures.Append(line).Append(": ").AppendLine(diff);
            }
        }

        Assert.True(count >= 10, "only " + count + " reftests listed");
        Assert.True(failures.Length == 0, failures.ToString());
    }

    /// <summary>
    /// Sweeps css/CSS2/floats and css/CSS2/floats-clear of a web-platform-tests checkout and writes a result per test. Runs only when
    /// <c>ONERENDER_FLOAT_SWEEP</c> names the checkout's <c>css/CSS2</c> directory; <c>ONERENDER_FLOAT_SWEEP_OUT</c> names the result file.
    /// </summary>
    [Fact]
    public void SweepTheFloatDirectories()
    {
        string? root = Environment.GetEnvironmentVariable("ONERENDER_FLOAT_SWEEP");
        Assert.SkipWhen(string.IsNullOrEmpty(root), "set ONERENDER_FLOAT_SWEEP to a web-platform-tests css/CSS2 directory");
        string outPath = Environment.GetEnvironmentVariable("ONERENDER_FLOAT_SWEEP_OUT") ?? Path.Combine(Path.GetTempPath(), "float-sweep.tsv");
        var sb = new StringBuilder();
        foreach (string sub in new[] { "floats", "floats-clear" })
        {
            string[] files = Directory.GetFiles(Path.Combine(root!, sub));
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (!(name.EndsWith(".html", StringComparison.Ordinal) || name.EndsWith(".xht", StringComparison.Ordinal)) || name.Contains("-ref", StringComparison.Ordinal))
                {
                    continue;
                }

                string html = Read(file);
                string? rel = MatchOf(html);
                if (rel is null || !File.Exists(Path.Combine(Path.GetDirectoryName(file)!, rel)))
                {
                    continue;
                }

                string result;
                try
                {
                    result = Compare(file, Path.Combine(Path.GetDirectoryName(file)!, rel)) is { } d ? "FAIL\t" + d : "PASS\t";
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    result = "ERROR\t" + e.GetType().Name;
                }

                bool script = html.Contains("<script", StringComparison.OrdinalIgnoreCase);
                string kind = script ? "script" : (Regex.IsMatch(Regex.Replace(Regex.Replace(Regex.Replace(html, "<(style|script|head|title)[^>]*>.*?</\\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "<p[^>]*>[^<]*[Tt]est passes[^<]*(<strong>[^<]*</strong>[^<]*)?</p>", string.Empty, RegexOptions.CultureInvariant), "<[^>]*>", string.Empty, RegexOptions.CultureInvariant), "\\S", RegexOptions.CultureInvariant) ? "text" : "textfree");
                sb.Append(sub).Append('/').Append(name).Append('\t').Append(kind).Append('\t').AppendLine(result);
            }
        }

        File.WriteAllText(outPath, sb.ToString());
    }
}
