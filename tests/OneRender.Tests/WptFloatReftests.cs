using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OneFont;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// Reftests of the CSS2 float and clear directories, judged by geometry: the page and its reference are laid out and the painted
/// pixels (backgrounds and borders drawn in paint order, no text) are compared. Tests that need text, positioning or tables
/// are not covered here; the committed pairs are the text-free ones this layout gets right.
/// </summary>
public partial class WptFloatReftests
{
    private sealed class NoFonts : IFontProvider
    {
        public FontFallbackChain GetFallbackChain(in ComputedValues style) => throw new NotSupportedException();
    }

    private const int CanvasW = 800, CanvasH = 600;

    private static int Pack(ColorValue c) =>
        (255 << 24) | ((int)Math.Round(c.R * 255) << 16) | ((int)Math.Round(c.G * 255) << 8) | (int)Math.Round(c.B * 255);

    private static void Fill(int[] px, double x0, double y0, double x1, double y1, ColorValue c)
    {
        if (c.A < 1 || x1 <= x0 || y1 <= y0)
        {
            return;
        }

        int ix0 = Math.Max(0, (int)Math.Round(x0, MidpointRounding.AwayFromZero)), ix1 = Math.Min(CanvasW, (int)Math.Round(x1, MidpointRounding.AwayFromZero));
        int iy0 = Math.Max(0, (int)Math.Round(y0, MidpointRounding.AwayFromZero)), iy1 = Math.Min(CanvasH, (int)Math.Round(y1, MidpointRounding.AwayFromZero));
        int v = Pack(c);
        for (int y = iy0; y < iy1; y++)
        {
            for (int x = ix0; x < ix1; x++)
            {
                px[(y * CanvasW) + x] = v;
            }
        }
    }

    private static bool IsFloat(Fragment f) =>
        f.Kind == FragmentKind.Box && f.Box.Values.Floating != FloatKind.None && (f.Box.Flags & BoxTraits.OutOfFlow) != 0;

    private static Fragment NextInTree(Fragment cur, Fragment root, bool skipChildren)
    {
        if (!skipChildren && !cur.FirstChild.IsNull)
        {
            return cur.FirstChild;
        }

        while (!cur.Equals(root))
        {
            if (!cur.NextSibling.IsNull)
            {
                return cur.NextSibling;
            }

            cur = cur.Parent;
        }

        return default;
    }

    /// <summary>
    /// A stand-in for painting: block backgrounds and borders of the subtree in tree order, then its floats, each as a unit (CSS2
    /// Appendix E steps 4 and 5), into a canvas of opaque colours. Text, images and partial transparency are not drawn.
    /// </summary>
    private static void PaintContext(FragmentTree f, Fragment root, int[] px, int depth)
    {
        var floats = new List<Fragment>();
        Fragment cur = root;
        while (!cur.IsNull)
        {
            bool skip = false;
            if (cur.Kind == FragmentKind.Box)
            {
                if (!cur.Equals(root) && IsFloat(cur))
                {
                    floats.Add(cur);
                    skip = true;
                }
                else
                {
                    ref readonly ComputedValues v = ref cur.Box.Values;
                    Rect r = f.AbsoluteBorderBox(cur);
                    Fill(px, r.X, r.Y, r.X + r.Width, r.Y + r.Height, v.BackgroundColor);
                    ColorValue cc = v.Color;
                    if (v.BorderTopWidth > 0)
                    {
                        Fill(px, r.X, r.Y, r.X + r.Width, r.Y + v.BorderTopWidth, v.BorderTopColor.Resolve(cc));
                    }

                    if (v.BorderBottomWidth > 0)
                    {
                        Fill(px, r.X, r.Y + r.Height - v.BorderBottomWidth, r.X + r.Width, r.Y + r.Height, v.BorderBottomColor.Resolve(cc));
                    }

                    if (v.BorderLeftWidth > 0)
                    {
                        Fill(px, r.X, r.Y + v.BorderTopWidth, r.X + v.BorderLeftWidth, r.Y + r.Height - v.BorderBottomWidth, v.BorderLeftColor.Resolve(cc));
                    }

                    if (v.BorderRightWidth > 0)
                    {
                        Fill(px, r.X + r.Width - v.BorderRightWidth, r.Y + v.BorderTopWidth, r.X + r.Width, r.Y + r.Height - v.BorderBottomWidth, v.BorderRightColor.Resolve(cc));
                    }
                }
            }

            cur = NextInTree(cur, root, skip);
        }

        if (depth < 64)
        {
            foreach (Fragment fl in floats)
            {
                PaintContext(f, fl, px, depth + 1);
            }
        }
    }

    internal static int[] Raster(string html)
    {
        Pipeline p = Pipeline.FromHtml(html, CanvasW, CanvasH);
        var f = new FragmentTree(p.Boxes);
        new BlockLayoutEngine().Layout(p.Boxes, new LayoutInput(CanvasW, CanvasH, new NoFonts()), f);
        var px = new int[CanvasW * CanvasH];
        Array.Fill(px, -1);
        PaintContext(f, f.Root, px, 0);
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
            if (a[i] != -1 || b[i] != -1)
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
        Assert.Equal(string.Empty, failures.ToString());
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
