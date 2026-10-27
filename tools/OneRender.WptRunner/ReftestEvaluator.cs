using System.Collections.Concurrent;
using OneRender.Image;

namespace OneRender.WptRunner;

/// <summary>How one test ended.</summary>
public enum Outcome
{
    /// <summary>The comparison held and something was painted.</summary>
    Pass,

    /// <summary>The comparison did not hold, or rendering failed.</summary>
    Fail,

    /// <summary>The comparison held but the test rendered nothing (blank page), so it proves nothing about the renderer.</summary>
    Blank,

    /// <summary>The runner cannot run this test.</summary>
    Skipped,
}

/// <summary>An outcome with a short reason (empty for passes).</summary>
public readonly record struct Verdict(Outcome Outcome, string Reason);

/// <summary>Produces the image for a WPT-relative document path.</summary>
public interface IReftestRenderer
{
    /// <summary>Renders the document at <paramref name="wptPath"/> in an 800x600 viewport. May throw; the caller records it as a failure.</summary>
    RgbaImage Render(string wptPath);
}

/// <summary>A renderer that can also paginate: the pages of a print reftest, one image each, in order.</summary>
public interface IPagedReftestRenderer : IReftestRenderer
{
    /// <summary>
    /// Renders the document at <paramref name="wptPath"/> as pages of the print-reftest default size (12.7 cm by 7.62 cm, 12.7 mm margins)
    /// unless the document's own <c>@page</c> rules say otherwise. At least one page. May throw; the caller records it as a failure.
    /// </summary>
    IReadOnlyList<RgbaImage> RenderPages(string wptPath);
}

/// <summary>
/// Decides tests the way WPT's reftest rules say: a test with several references passes when at least one
/// <c>match</c> reference matches and every <c>mismatch</c> reference differs; a reference with references of its own
/// continues the chain, which must also hold.
/// </summary>
public sealed class ReftestEvaluator
{
    private const int MaxChainDepth = 16;
    private const int MaxCachedImages = 96;

    private readonly ReftestStore _store;
    private readonly IReftestRenderer _renderer;
    private readonly ConcurrentDictionary<string, RgbaImage> _images = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<RgbaImage>> _pages = new(StringComparer.Ordinal);

    /// <summary>Creates an evaluator over <paramref name="store"/> that renders with <paramref name="renderer"/>.</summary>
    public ReftestEvaluator(ReftestStore store, IReftestRenderer renderer)
    {
        _store = store;
        _renderer = renderer;
    }

    /// <summary>Why the test cannot be run, or null when it can.</summary>
    public string? SkipReason(string testPath)
    {
        ReftestFile? file = _store.Get(testPath);
        if (file is null)
        {
            return "file missing";
        }

        if (IsPrint(testPath) && _renderer is not IPagedReftestRenderer)
        {
            return "print reftest (paged output)";
        }

        if (file.References.Count == 0)
        {
            return file.TestHarness ? "testharness.js test" : "no reference";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(testPath);
        while (queue.Count > 0 && seen.Count < 64)
        {
            string p = queue.Dequeue();
            if (!seen.Add(p))
            {
                continue;
            }

            ReftestFile? f = _store.Get(p);
            if (f is null)
            {
                return "reference file missing: " + p;
            }

            if (f.ReftestWait)
            {
                return "reftest-wait (needs script)";
            }

            if (f.HasScript || f.TestHarness)
            {
                return "script-dependent";
            }

            foreach (ReferenceLink l in f.References)
            {
                queue.Enqueue(l.Path);
            }
        }

        return null;
    }

    /// <summary>
    /// True for a print reftest, by WPT's file-name rules: the last <c>-</c> segment of the name (before the extension and any
    /// <c>.tentative</c>-style flags) is <c>print</c>, or a directory is named <c>print</c>. <c>bar-print.html</c> and
    /// <c>bar-print.tentative.html</c> are print reftests; <c>bar-print-001.html</c> is not.
    /// </summary>
    public static bool IsPrint(string testPath)
    {
        string[] parts = testPath.Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] == "print")
            {
                return true;
            }
        }

        string name = parts[^1];
        int dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            name = name[..dot];
        }

        int dash = name.LastIndexOf('-');
        if (dash < 0)
        {
            return false;
        }

        string flag = name[(dash + 1)..];
        int flagEnd = flag.IndexOf('.', StringComparison.Ordinal);
        return (flagEnd < 0 ? flag : flag[..flagEnd]) == "print";
    }

    /// <summary>Runs one test.</summary>
    public Verdict Run(string testPath)
    {
        string? skip = SkipReason(testPath);
        if (skip is not null)
        {
            return new Verdict(Outcome.Skipped, skip);
        }

        try
        {
            ReftestFile file = _store.Get(testPath)!;
            string reason = string.Empty;
            bool print = IsPrint(testPath);
            bool ok = Holds(file, new HashSet<string>(StringComparer.Ordinal) { testPath }, 0, print, file, ref reason);
            if (!ok)
            {
                return new Verdict(Outcome.Fail, reason);
            }

            bool blank = print
                ? Pages(testPath, file).All(ImageComparer.IsBlank)
                : ImageComparer.IsBlank(Image(testPath));
            return blank
                ? new Verdict(Outcome.Blank, "test rendered nothing")
                : new Verdict(Outcome.Pass, string.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (Environment.GetEnvironmentVariable("ONERENDER_WPT_TRACE") is not null)
            {
                Console.Error.WriteLine(testPath + ": " + ex);
            }

            return new Verdict(Outcome.Fail, "error: " + ex.GetType().Name + ": " + FirstLine(ex.Message));
        }
    }

    private bool Holds(ReftestFile node, HashSet<string> path, int depth, bool print, ReftestFile root, ref string reason)
    {
        if (depth > MaxChainDepth)
        {
            reason = "reference chain too deep";
            return false;
        }

        bool anyMatch = false, matched = false;
        foreach (ReferenceLink l in node.References)
        {
            if (l.Mismatch)
            {
                continue;
            }

            anyMatch = true;
            if (RefHolds(node, l, path, depth, print, root, ref reason))
            {
                matched = true;
                break;
            }
        }

        if (anyMatch && !matched)
        {
            return false;
        }

        foreach (ReferenceLink l in node.References)
        {
            if (l.Mismatch && !RefHolds(node, l, path, depth, print, root, ref reason))
            {
                return false;
            }
        }

        return true;
    }

    private bool RefHolds(ReftestFile node, ReferenceLink link, HashSet<string> path, int depth, bool print, ReftestFile root, ref string reason)
    {
        ReftestFile refFile = _store.Get(link.Path)!;
        FuzzySpec? fuzzy = null;
        foreach (FuzzySpec f in node.Fuzzy)
        {
            if (string.Equals(f.Reference, link.Path, StringComparison.Ordinal))
            {
                fuzzy = f;
                break;
            }
        }

        fuzzy ??= node.Fuzzy.Find(static f => f.Reference is null);

        bool equal;
        string detail;
        if (print)
        {
            equal = PagesEqual(Pages(node.Path, root), Pages(link.Path, root), fuzzy, out detail);
        }
        else
        {
            equal = ImageComparer.AreEqual(Image(node.Path), Image(link.Path), fuzzy, out detail);
        }

        if (equal == link.Mismatch)
        {
            string name = link.Path[(link.Path.LastIndexOf('/') + 1)..];
            reason = link.Mismatch ? $"!= {name} identical" : $"== {name} differs ({detail})";
            return false;
        }

        // Chains continue only through matches (A == B and B == C). A mismatch reference is a different page by
        // definition, and its own links (often a mismatch back to the test) say nothing about the test.
        if (link.Mismatch || refFile.References.Count == 0)
        {
            return true;
        }

        if (!path.Add(link.Path))
        {
            // Already being checked further up this chain.
            return true;
        }

        bool ok = Holds(refFile, path, depth + 1, print, root, ref reason);
        path.Remove(link.Path);
        return ok;
    }

    // Print reftests compare page by page; the fuzzy allowance applies to each page separately (WPT print-reftests doc).
    // Different page counts never match. A "mismatch" reference therefore holds when any page differs or the counts differ.
    private static bool PagesEqual(IReadOnlyList<RgbaImage> a, IReadOnlyList<RgbaImage> b, FuzzySpec? fuzzy, out string detail)
    {
        if (a.Count != b.Count)
        {
            detail = $"{a.Count} pages vs {b.Count}";
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!ImageComparer.AreEqual(a[i], b[i], fuzzy, out string d))
            {
                detail = $"page {i + 1}: {d}";
                return false;
            }
        }

        detail = string.Empty;
        return true;
    }

    private IReadOnlyList<RgbaImage> Pages(string path, ReftestFile root)
    {
        if (!_pages.TryGetValue(path, out IReadOnlyList<RgbaImage>? all))
        {
            all = ((IPagedReftestRenderer)_renderer).RenderPages(path);
            if (_pages.Count >= 24)
            {
                _pages.Clear();
            }

            _pages[path] = all;
        }

        return PageRanges.Select(all, root.RangesFor(path));
    }

    private RgbaImage Image(string path)
    {
        if (_images.TryGetValue(path, out RgbaImage? hit))
        {
            return hit;
        }

        RgbaImage img = _renderer.Render(path);
        if (_images.Count >= MaxCachedImages)
        {
            _images.Clear();
        }

        _images[path] = img;
        return img;
    }

    private static string FirstLine(string s)
    {
        int nl = s.IndexOfAny(['\r', '\n']);
        string line = nl >= 0 ? s[..nl] : s;
        return line.Length > 120 ? line[..120] : line;
    }
}
