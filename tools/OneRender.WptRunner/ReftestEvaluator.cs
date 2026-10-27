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

        // Print reftests: "-print" right before the extension, or a directory named "print" (WPT print-reftests doc).
        string stem = testPath[(testPath.LastIndexOf('/') + 1)..];
        int dot = stem.LastIndexOf('.');
        if ((dot >= 0 && stem[..dot].EndsWith("-print", StringComparison.Ordinal)) || testPath.Contains("/print/", StringComparison.Ordinal))
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
            bool ok = Holds(file, new HashSet<string>(StringComparer.Ordinal) { testPath }, 0, ref reason);
            if (!ok)
            {
                return new Verdict(Outcome.Fail, reason);
            }

            return ImageComparer.IsBlank(Image(testPath))
                ? new Verdict(Outcome.Blank, "test rendered nothing")
                : new Verdict(Outcome.Pass, string.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new Verdict(Outcome.Fail, "error: " + ex.GetType().Name + ": " + FirstLine(ex.Message));
        }
    }

    private bool Holds(ReftestFile node, HashSet<string> path, int depth, ref string reason)
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
            if (RefHolds(node, l, path, depth, ref reason))
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
            if (l.Mismatch && !RefHolds(node, l, path, depth, ref reason))
            {
                return false;
            }
        }

        return true;
    }

    private bool RefHolds(ReftestFile node, ReferenceLink link, HashSet<string> path, int depth, ref string reason)
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

        bool equal = ImageComparer.AreEqual(Image(node.Path), Image(link.Path), fuzzy, out string detail);
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

        bool ok = Holds(refFile, path, depth + 1, ref reason);
        path.Remove(link.Path);
        return ok;
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
