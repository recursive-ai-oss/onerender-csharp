using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using OneRender.Image;
using OneRender.WptRunner.Chrome;

namespace OneRender.WptRunner;

/// <summary>Renders through the engine, by way of the image seam in <c>OneRender.Image</c>.</summary>
internal sealed class EngineRenderer : IReftestRenderer
{
    private readonly ReftestStore _store;

    public EngineRenderer(ReftestStore store) => _store = store;

    public RgbaImage Render(string wptPath)
    {
        string source = File.ReadAllText(_store.FullPath(wptPath));
        if (XhtmlSource.IsXhtml(wptPath))
        {
            source = XhtmlSource.ToHtml(source);
        }

        return HtmlRenderSeam.Render(source, "/" + wptPath, 800, 600, uri => Load(uri));
    }

    private byte[]? Load(string uri)
    {
        string p = ReftestParser.Resolve("/", uri);
        string full = _store.FullPath(p);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }
}

/// <summary>Renders through headless Chrome for Testing, for calibrating the runner.</summary>
internal sealed class ChromeAdapter : IReftestRenderer
{
    private readonly ChromeRenderer _chrome;
    private readonly string _baseUri;

    public ChromeAdapter(ChromeRenderer chrome, string baseUri)
    {
        _chrome = chrome;
        _baseUri = baseUri;
    }

    public RgbaImage Render(string wptPath)
    {
        Screenshot s = _chrome.Render(_baseUri + "/" + string.Join('/', wptPath.Split('/').Select(Uri.EscapeDataString)));
        return new RgbaImage(s.Width, s.Height, s.Rgba);
    }
}

internal static partial class Program
{
    private const string Usage = """
        usage: OneRender.WptRunner run --wpt <checkout> --renderer onerender|chrome [options]

          --wpt <dir>         WPT checkout (fetch-wpt.sh prints it); its folder name is the commit, or pass --commit
          --commit <sha>      commit to record when the folder is not named after it
          --renderer <name>   onerender (the engine, through the image seam) or chrome (calibration)
          --chrome <path>     chrome executable (default: $ONERENDER_CACHE/chrome/<pinned>/chrome-linux64/chrome)
          --fonts <dir>       restrict Chrome to the fonts in this directory
          --modules <file>    module list (default tools/wpt/modules.json)
          --module <ids>      comma-separated module ids (default all)
          --filter <text>     only tests whose path contains the text
          --test <path>       run one WPT-relative test path (repeatable); prints the verdict
          --sample <n>        run at most about n tests per module, taking every Nth in path order
          --parallel <n>      concurrent tests (default 8 for onerender, 6 for chrome)
          --dump <dir>        save every rendered image as a PNG (for --test runs)
          --out <dir>         write results to <dir>/<commit>/<renderer>/
        """;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] != "run")
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            return Run(ParseOptions(args));
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }

    private static Dictionary<string, List<string>> ParseOptions(string[] args)
    {
        var o = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException("bad argument: " + args[i]);
            }

            string key = args[i][2..];
            if (!o.TryGetValue(key, out List<string>? list))
            {
                o[key] = list = [];
            }

            list.Add(args[++i]);
        }

        return o;
    }

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShaRegex();

    private static string? Opt(Dictionary<string, List<string>> o, string key) => o.TryGetValue(key, out var l) ? l[^1] : null;

    private static int Run(Dictionary<string, List<string>> o)
    {
        string wpt = Opt(o, "wpt") ?? throw new ArgumentException("--wpt is required");
        string renderer = Opt(o, "renderer") ?? throw new ArgumentException("--renderer is required");
        if (renderer is not ("onerender" or "chrome"))
        {
            throw new ArgumentException("--renderer must be onerender or chrome");
        }

        string commit = Opt(o, "commit") ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(wpt)));
        if (!ShaRegex().IsMatch(commit))
        {
            throw new ArgumentException("cannot tell the WPT commit; pass --commit");
        }

        var store = new ReftestStore(wpt);
        List<ModuleDef> all = ReftestStore.LoadModules(Opt(o, "modules") ?? "tools/wpt/modules.json");
        var wanted = o.TryGetValue("module", out var ml)
            ? ml.SelectMany(m => m.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal)
            : null;
        List<ModuleDef> modules = wanted is null ? all : all.Where(m => wanted.Contains(m.Id)).ToList();
        if (wanted is not null && modules.Count != wanted.Count)
        {
            throw new ArgumentException("unknown module in --module");
        }

        int sample = int.Parse(Opt(o, "sample") ?? "0", CultureInfo.InvariantCulture);
        int parallel = int.Parse(Opt(o, "parallel") ?? (renderer == "chrome" ? "6" : "8"), CultureInfo.InvariantCulture);
        string? filter = Opt(o, "filter");
        string? outRoot = Opt(o, "out");

        StaticFileServer? server = null;
        ChromeRenderer? chrome = null;
        string rendererVersion = "OneRender " + typeof(OneRender.EngineInfo).Assembly.GetName().Version;
        IReftestRenderer impl;
        try
        {
            if (renderer == "chrome")
            {
                string cache = Environment.GetEnvironmentVariable("ONERENDER_CACHE")
                               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "onerender", "cache");
                string exe = Opt(o, "chrome") ?? Path.Combine(cache, "chrome", "156.0.8078.47", "chrome-linux64", "chrome");
                server = new StaticFileServer(store.Root);
                string profile = Path.Combine(Path.GetTempPath(), "wptrunner-chrome-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                chrome = ChromeRenderer.StartAsync(exe, profile, parallel, 800, 600, Opt(o, "fonts")).GetAwaiter().GetResult();
                rendererVersion = chrome.Version;
                impl = new ChromeAdapter(chrome, server.BaseUri);
            }
            else
            {
                impl = new EngineRenderer(store);
            }

            string? dump = Opt(o, "dump");
            if (dump is not null)
            {
                impl = new DumpingRenderer(impl, dump);
            }

            var evaluator = new ReftestEvaluator(store, impl);
            string? outDir = outRoot is null ? null : Path.Combine(outRoot, commit, renderer);

            if (o.TryGetValue("test", out List<string>? single))
            {
                foreach (string t in single)
                {
                    Verdict v = evaluator.Run(t);
                    Console.WriteLine($"{v.Outcome.ToString().ToUpperInvariant()} {t} {v.Reason}");
                }

                return 0;
            }

            foreach (ModuleDef m in modules)
            {
                var sw = Stopwatch.StartNew();
                List<string> tests = store.Discover(m);
                int discovered = tests.Count;
                if (filter is not null)
                {
                    tests = tests.Where(t => t.Contains(filter, StringComparison.Ordinal)).ToList();
                }

                int stride = 1;
                if (sample > 0 && tests.Count > sample)
                {
                    stride = (tests.Count + sample - 1) / sample;
                    tests = tests.Where((_, i) => i % stride == 0).ToList();
                }

                var verdicts = new ConcurrentDictionary<string, Verdict>(StringComparer.Ordinal);
                Parallel.ForEach(
                    tests,
                    new ParallelOptions { MaxDegreeOfParallelism = parallel },
                    t => verdicts[t] = evaluator.Run(t));

                var result = new ModuleResult
                {
                    Module = m.Id,
                    SampleStride = stride,
                    Discovered = discovered,
                    Tests = new SortedDictionary<string, Verdict>(verdicts, StringComparer.Ordinal),
                };
                Counts c = result.Counts;
                Console.WriteLine(
                    $"{m.Id,-18} total {c.Total,6} pass {c.Pass,6} fail {c.Fail,6} blank {c.Blank,6} skipped {c.Skipped,6} rate {ResultsWriter.Rate(c)}  ({sw.Elapsed.TotalSeconds:0.0}s)");
                if (outDir is not null)
                {
                    ResultsWriter.WriteModule(outDir, commit, renderer, rendererVersion, result);
                }
            }

            if (outDir is not null)
            {
                ResultsWriter.WriteSummary(outDir);
            }

            return 0;
        }
        finally
        {
            chrome?.Dispose();
            server?.Dispose();
        }
    }
}
