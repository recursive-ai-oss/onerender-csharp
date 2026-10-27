using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OneRender.GeomDiff;

// Usage: OneRender.ChromePdf --chrome <path> --fontconfig <file> --docs <dir> --out <dir> [--repeat N] [--cold N] [--doc name]... [--no-sandbox]
//
// For every document (<docs>/<name>.html) it writes one JSON line per measurement on stdout:
//   cold: a new browser, started, then one print; the time runs from the start of the process to the arrival of the PDF
//   warm: the same browser prints the document again (a new tab each time); the time is the print alone, from navigation to the PDF
// Memory is the sum over the browser's process tree, sampled every 10 ms: rss is the resident size counted per process (shared pages are
// counted in each process, so it overstates), pss the proportional set size (shared pages divided among their users).
string chrome = "", fontconfig = "", docs = "", outDir = "";
int repeat = 3, cold = 3;
bool noSandbox = false;
var names = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--chrome": chrome = args[++i]; break;
        case "--fontconfig": fontconfig = args[++i]; break;
        case "--docs": docs = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        case "--repeat": repeat = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--cold": cold = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--doc": names.Add(args[++i]); break;
        case "--no-sandbox": noSandbox = true; break;
        default: Console.Error.WriteLine("unknown argument " + args[i]); return 2;
    }
}

if (chrome.Length == 0 || fontconfig.Length == 0 || docs.Length == 0 || outDir.Length == 0)
{
    Console.Error.WriteLine("usage: --chrome <path> --fontconfig <file> --docs <dir> --out <dir> [--repeat N] [--cold N] [--doc name]... [--no-sandbox]");
    return 2;
}

Directory.CreateDirectory(outDir);
if (names.Count == 0)
{
    foreach (string f in Directory.GetFiles(docs, "*.html").Order(StringComparer.Ordinal))
    {
        names.Add(Path.GetFileNameWithoutExtension(f));
    }
}

var options = new ChromeOptions { ChromePath = chrome, FontConfigFile = fontconfig, WorkDir = outDir, Name = "bench", NoSandbox = noSandbox ? true : null };
foreach (string name in names)
{
    string url = new Uri(Path.GetFullPath(Path.Combine(docs, name + ".html"))).AbsoluteUri;
    string pdfPath = Path.Combine(outDir, name + ".chrome.pdf");
    for (int c = 0; c < cold; c++)
    {
        using var mem = new TreeMemory();
        var sw = Stopwatch.StartNew();
        using ChromeInstance chromeInstance = await ChromeInstance.StartAsync(options, CancellationToken.None);
        mem.Watch(chromeInstance.RootPid);
        byte[] pdf = await Print(chromeInstance, url);
        sw.Stop();
        mem.Stop();
        File.WriteAllBytes(pdfPath, pdf);
        Report(name, "cold", sw.Elapsed.TotalMilliseconds, mem, pdf, chromeInstance.Version);
        if (c < cold - 1)
        {
            continue;
        }

        // The last cold browser stays up for the warm prints, so the idle size after the first print is known too.
        for (int w = 0; w < repeat; w++)
        {
            using var warm = new TreeMemory();
            warm.Watch(chromeInstance.RootPid);
            var sw2 = Stopwatch.StartNew();
            byte[] again = await Print(chromeInstance, url);
            sw2.Stop();
            warm.Stop();
            Report(name, "warm", sw2.Elapsed.TotalMilliseconds, warm, again, chromeInstance.Version);
        }
    }
}

return 0;

static void Report(string doc, string mode, double ms, TreeMemory mem, byte[] pdf, string version)
{
    string text = Encoding.Latin1.GetString(pdf);
    int pages = -1;
    foreach (Match m in Regex.Matches(text, @"/Type\s*/Pages\b[^>]*?/Count\s+(\d+)|/Count\s+(\d+)[^>]*?/Type\s*/Pages\b"))
    {
        pages = Math.Max(pages, int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{{\"tool\":\"chrome\",\"version\":\"{version}\",\"doc\":\"{doc}\",\"mode\":\"{mode}\",\"ms\":{ms:F1},\"rssPeakKb\":{mem.PeakRssKb},\"pssPeakKb\":{mem.PeakPssKb},\"processesPeak\":{mem.PeakProcesses},\"pages\":{pages},\"bytes\":{pdf.Length}}}"));
}

static async Task<byte[]> Print(ChromeInstance chrome, string url)
{
    CdpConnection cdp = chrome.Cdp;
    JsonElement created = await cdp.SendAsync("Target.createTarget", "{\"url\":\"about:blank\"}", timeoutMs: 60000);
    string targetId = created.GetProperty("targetId").GetString()!;
    JsonElement attached = await cdp.SendAsync("Target.attachToTarget", "{\"targetId\":" + J.S(targetId) + ",\"flatten\":true}", timeoutMs: 60000);
    string session = attached.GetProperty("sessionId").GetString()!;
    var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    void On(string method, string? s, JsonElement p)
    {
        if (method == "Page.loadEventFired" && s == session)
        {
            loaded.TrySetResult();
        }
    }

    cdp.EventReceived += On;
    try
    {
        await cdp.SendAsync("Page.enable", null, session);
        await cdp.SendAsync("Page.navigate", "{\"url\":" + J.S(url) + "}", session, 60000);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(120));
        // A4 with half-inch margins, which is what the other tools are given; an @page rule in the document wins.
        const string Params = "{\"paperWidth\":8.267717,\"paperHeight\":11.692913,\"marginTop\":0.5,\"marginBottom\":0.5,\"marginLeft\":0.5,\"marginRight\":0.5,"
            + "\"printBackground\":true,\"preferCSSPageSize\":true,\"displayHeaderFooter\":false,\"generateTaggedPDF\":true}";
        JsonElement r = await cdp.SendAsync("Page.printToPDF", Params, session, 600000);
        return Convert.FromBase64String(r.GetProperty("data").GetString()!);
    }
    finally
    {
        cdp.EventReceived -= On;
        try
        {
            await cdp.SendAsync("Target.closeTarget", "{\"targetId\":" + J.S(targetId) + "}", timeoutMs: 10000);
        }
        catch (CdpException)
        {
        }
    }
}

/// <summary>Samples the memory of a process and all its descendants from /proc until stopped.</summary>
sealed class TreeMemory : IDisposable
{
    private Thread? _thread;
    private volatile bool _stop;
    private int _root;
    public long PeakRssKb { get; private set; }
    public long PeakPssKb { get; private set; }
    public int PeakProcesses { get; private set; }

    public void Watch(int rootPid)
    {
        _root = rootPid;
        _thread = new Thread(Loop) { IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join();
    }

    public void Dispose() => Stop();

    private void Loop()
    {
        long n = 0;
        while (!_stop)
        {
            Sample(n++ % 5 == 0);
            Thread.Sleep(10);
        }

        Sample(true);
    }

    private void Sample(bool pss)
    {
        var parent = new Dictionary<int, int>();
        foreach (string dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
            {
                continue;
            }

            try
            {
                string stat = File.ReadAllText(dir + "/stat");
                int close = stat.LastIndexOf(')');
                string[] f = stat.AsSpan(close + 2).ToString().Split(' ');
                parent[pid] = int.Parse(f[1], CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
            }
        }

        var tree = new HashSet<int> { _root };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (KeyValuePair<int, int> kv in parent)
            {
                if (!tree.Contains(kv.Key) && tree.Contains(kv.Value))
                {
                    tree.Add(kv.Key);
                    grew = true;
                }
            }
        }

        long rss = 0, pssSum = 0;
        int alive = 0;
        foreach (int pid in tree)
        {
            try
            {
                string[] sm = File.ReadAllText("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/statm").Split(' ');
                rss += long.Parse(sm[1], CultureInfo.InvariantCulture) * 4;
                alive++;
                if (pss)
                {
                    foreach (string line in File.ReadLines("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/smaps_rollup"))
                    {
                        if (line.StartsWith("Pss:", StringComparison.Ordinal))
                        {
                            pssSum += long.Parse(line.AsSpan(4).Trim(" kB"), CultureInfo.InvariantCulture);
                            break;
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
            }
        }

        PeakRssKb = Math.Max(PeakRssKb, rss);
        PeakPssKb = Math.Max(PeakPssKb, pssSum);
        PeakProcesses = Math.Max(PeakProcesses, alive);
    }
}
