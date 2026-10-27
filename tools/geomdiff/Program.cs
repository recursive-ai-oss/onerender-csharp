using System.Globalization;

namespace OneRender.GeomDiff;

internal static class Program
{
    private const string Usage = """
        onerender-geomdiff: Chrome geometry differential

          snapshot-corpus  --corpus <dir> --out <dir> --chrome <exe> --fontconfig <file> [--count N] [--jobs N] [--work <dir>]
          snapshot-static  --src <dir> --out <dir> [--prefix <p>]
          capture          --snapshots <dir> --out <dir> --chrome <exe> --fontconfig <file> [--viewport WxH]... [--jobs N] [--limit N] [--work <dir>]
          compare          --chrome-dir <dir> (--ours-dir <dir> | --engine --snapshots <dir>) --set <name> --label <name> --out <dir>
                           [--detail <dir>] [--tol-px 1] [--tol-pct 0.5] [--expect <file>] [--page-table]
          facts-capture    --snapshots <dir> --out <dir> --chrome <exe> --fontconfig <file> [--viewport WxH]... [--jobs N] [--limit N] [--work <dir>]
          facts-compare    --captures <dir> --snapshots <dir> --set <name> --out <dir> [--detail <dir>] [--parallel N]
          pixels           --snapshots <dir> --out <dir> --set <name> --chrome <exe> --fontconfig <file> [--viewport WxH]... [--jobs N] [--max-height 4000] [--dump <dir>] [--work <dir>]
          engine-table     --snapshots <dir> [--viewport WxH]
          perturb          --in <dir> --out <dir> --expect-out <file> [--seed 1]
          fonts-check      --page <html> --out <md> --chrome <exe> --fontconfig <file> [--work <dir>]

        Add --no-sandbox to run Chrome without its sandbox (only frozen, offline content is ever loaded).
        """;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        string cmd = args[0];
        Dictionary<string, List<string>> o = new(StringComparer.Ordinal);
        bool noSandbox = false;
        bool engine = false;
        bool pageTable = false;
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--no-sandbox") { noSandbox = true; continue; }
            if (a == "--engine") { engine = true; continue; }
            if (a == "--page-table") { pageTable = true; continue; }
            if (!a.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                Console.Error.WriteLine("bad argument: " + a);
                return 2;
            }
            if (!o.TryGetValue(a, out List<string>? l))
            {
                o[a] = l = [];
            }
            l.Add(args[++i]);
        }
        string Req(string k) => o.TryGetValue(k, out List<string>? v) ? v[0] : throw new ArgumentException("missing " + k);
        string? Opt(string k) => o.TryGetValue(k, out List<string>? v) ? v[0] : null;
        int OptInt(string k, int d) => o.TryGetValue(k, out List<string>? v) ? int.Parse(v[0], CultureInfo.InvariantCulture) : d;
        double OptDouble(string k, double d) => o.TryGetValue(k, out List<string>? v) ? double.Parse(v[0], CultureInfo.InvariantCulture) : d;
        string work = Opt("--work") ?? Path.Combine(Path.GetTempPath(), "onerender-geomdiff");
        bool? sandbox = noSandbox ? true : null;
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try
        {
            switch (cmd)
            {
                case "snapshot-static":
                    Console.WriteLine("snapshots: " + Snapshots.BuildStatic(Req("--src"), Req("--out"), Opt("--prefix") ?? ""));
                    return 0;
                case "snapshot-corpus":
                    {
                        CorpusStore corpus = new(Req("--corpus"));
                        List<CorpusStore.Record> picked = SnapshotBuilder.Sample(corpus, OptInt("--count", 400), out string desc);
                        string output = Req("--out");
                        Directory.CreateDirectory(output);
                        Console.WriteLine($"corpus pages: {corpus.Pages.Count}; sample: {picked.Count}; strata: {desc}");
                        File.WriteAllText(Path.Combine(output, "sample.txt"), $"corpus pages: {corpus.Pages.Count}\nsample: {picked.Count}\nstrata: {desc}\n");
                        List<CorpusStore.Record> todo = [.. picked.Where(p => !File.Exists(Path.Combine(Snapshots.PageDir(output, p.Sha), "page.json")))];
                        ChromeOptions opt = new() { ChromePath = Req("--chrome"), FontConfigFile = Req("--fontconfig"), WorkDir = work, NoSandbox = sandbox };
                        List<string> failures = await Pool.RunAsync(todo, static p => p.Sha, OptInt("--jobs", 4), opt, 100,
                            (chrome, rec) => SnapshotBuilder.BuildOneAsync(chrome, corpus, rec, output, cts.Token), cts.Token, Console.WriteLine).ConfigureAwait(false);
                        Console.WriteLine($"built {todo.Count - failures.Count} of {todo.Count}; failures {failures.Count}");
                        foreach (string f in failures.Take(10))
                        {
                            Console.WriteLine("  failed: " + f[..Math.Min(f.Length, 14)] + f[(f.IndexOf(':', StringComparison.Ordinal))..]);
                        }
                        File.AppendAllText(Path.Combine(output, "sample.txt"), $"built this run: {todo.Count - failures.Count}; failed: {failures.Count}\n");
                        return 0;
                    }
                case "capture":
                    {
                        List<ViewportSize> vps = o.TryGetValue("--viewport", out List<string>? v) ? [.. v.Select(Capture.ParseViewport)] : [new ViewportSize { Width = 1280, Height = 800 }];
                        return await Capture.RunAsync(Req("--snapshots"), Req("--out"), Req("--chrome"), Req("--fontconfig"), vps, OptInt("--jobs", 4), OptInt("--limit", 0), work, sandbox, cts.Token).ConfigureAwait(false);
                    }
                case "engine-table":
                    {
                        // Prints the engine's boxes in the table format of tools/forms/probe.sh, for reading beside Chrome's.
                        ViewportSize evp = Capture.ParseViewport(Opt("--viewport") ?? "1280x800");
                        foreach (string pageId in Snapshots.PageIds(Req("--snapshots")))
                        {
                            GeometryCapture ecap = EngineSide.LayOut(Req("--snapshots"), pageId, evp, cts.Token);
                            Console.WriteLine("# " + pageId);
                            foreach (ElementRecord er in ecap.Elements)
                            {
                                double[] b = er.Box ?? [];
                                Console.WriteLine(er.Path + " " + er.Tag + " - " + (b.Length == 4 ? string.Join(' ', b.Select(static x => x.ToString("0.####", CultureInfo.InvariantCulture))) : "-"));
                            }
                        }

                        return 0;
                    }
                case "compare":
                    return Comparer.Run(new Comparer.Options
                    {
                        ChromeDir = Req("--chrome-dir"),
                        OursDir = Opt("--ours-dir"),
                        SnapshotDir = Opt("--snapshots"),
                        UseEngine = engine,
                        Set = Req("--set"),
                        Label = Req("--label"),
                        OutDir = Req("--out"),
                        DetailDir = Opt("--detail"),
                        Tol = new Tolerance(OptDouble("--tol-px", 1), OptDouble("--tol-pct", 0.5)),
                        ExpectFile = Opt("--expect"),
                        PageTable = pageTable,
                    }, cts.Token);
                case "facts-capture":
                    {
                        List<ViewportSize> vps = o.TryGetValue("--viewport", out List<string>? v) ? [.. v.Select(Capture.ParseViewport)] : [new ViewportSize { Width = 1280, Height = 800 }];
                        return await FactsCapture.RunAsync(Req("--snapshots"), Req("--out"), Req("--chrome"), Req("--fontconfig"), vps, OptInt("--jobs", 4), OptInt("--limit", 0), work, sandbox, cts.Token).ConfigureAwait(false);
                    }
                case "facts-compare":
                    return FactsCompare.Run(Req("--captures"), Req("--snapshots"), Req("--set"), Req("--out"), Opt("--detail"), OptInt("--parallel", 4), cts.Token);
                case "pixels":
                    {
                        List<ViewportSize> vps = o.TryGetValue("--viewport", out List<string>? v) ? [.. v.Select(Capture.ParseViewport)] : [new ViewportSize { Width = 1280, Height = 800 }, new ViewportSize { Width = 390, Height = 844 }];
                        return await Pixels.RunAsync(Req("--snapshots"), Req("--out"), Req("--set"), Req("--chrome"), Req("--fontconfig"), vps, OptInt("--jobs", 4), OptInt("--max-height", 4000), Opt("--dump"), work, sandbox, cts.Token).ConfigureAwait(false);
                    }
                case "perturb":
                    return Perturb.Run(Req("--in"), Req("--out"), OptInt("--seed", 1), new Tolerance(OptDouble("--tol-px", 1), OptDouble("--tol-pct", 0.5)), Req("--expect-out"));
                case "fonts-check":
                    return await FontsCheck.RunAsync(Req("--page"), Req("--out"), Req("--chrome"), Req("--fontconfig"), work, sandbox, cts.Token).ConfigureAwait(false);
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
    }
}
