using System.Collections.Concurrent;
using System.Text.Json;

namespace OneRender.WptRunner;

/// <summary>One module of the module list: an id and the WPT directories that make it up.</summary>
public sealed record ModuleDef(string Id, IReadOnlyList<string> Dirs);

/// <summary>Reads and caches test files from a WPT checkout, and discovers the tests of each module.</summary>
public sealed class ReftestStore
{
    private static readonly string[] Extensions = [".html", ".htm", ".xht", ".xhtml", ".svg"];

    // Directories that hold shared support files, never tests of their own.
    private static readonly HashSet<string> SupportDirs = new(StringComparer.Ordinal)
    {
        "support", "resources", "reference", "references", "tools", "reftest-references",
    };

    private readonly ConcurrentDictionary<string, ReftestFile?> _cache = new(StringComparer.Ordinal);

    /// <summary>Opens a checkout whose root is <paramref name="root"/>.</summary>
    public ReftestStore(string root) => Root = Path.GetFullPath(root);

    /// <summary>The checkout directory.</summary>
    public string Root { get; }

    /// <summary>Loads the module list from <paramref name="file"/>.</summary>
    public static List<ModuleDef> LoadModules(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var list = new List<ModuleDef>();
        foreach (JsonElement m in doc.RootElement.GetProperty("modules").EnumerateArray())
        {
            var dirs = new List<string>();
            foreach (JsonElement d in m.GetProperty("dirs").EnumerateArray())
            {
                dirs.Add(d.GetString()!);
            }

            list.Add(new ModuleDef(m.GetProperty("id").GetString()!, dirs));
        }

        return list;
    }

    /// <summary>The parsed file at WPT-relative <paramref name="path"/>, or null when it does not exist.</summary>
    public ReftestFile? Get(string path) => _cache.GetOrAdd(path, p =>
    {
        string full = FullPath(p);
        return File.Exists(full) ? ReftestParser.Parse(p, File.ReadAllText(full)) : null;
    });

    /// <summary>The absolute path for a WPT-relative path.</summary>
    public string FullPath(string wptPath) => Path.Combine(Root, wptPath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// All tests of <paramref name="module"/>, sorted by ordinal path: every file that links a reference, plus every
    /// testharness.js file (reported as skipped). Reference files are never tests.
    /// </summary>
    public List<string> Discover(ModuleDef module)
    {
        var result = new List<string>();
        foreach (string dir in module.Dirs)
        {
            string abs = FullPath(dir);
            if (!Directory.Exists(abs))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(abs, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(Root, file).Replace('\\', '/');
                if (!IsCandidate(rel))
                {
                    continue;
                }

                ReftestFile? f = Get(rel);
                if (f is not null && (f.References.Count > 0 || f.TestHarness))
                {
                    result.Add(rel);
                }
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static bool IsCandidate(string rel)
    {
        string[] segs = rel.Split('/');
        for (int i = 0; i < segs.Length - 1; i++)
        {
            if (SupportDirs.Contains(segs[i]))
            {
                return false;
            }
        }

        string name = segs[^1];
        foreach (string e in Extensions)
        {
            if (name.EndsWith(e, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
