using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace OneRender.Tests.Wpt;

/// <summary>
/// The css-transitions and css-animations testharness files that can be replayed without a script engine: the parsing and computed-value
/// helpers (through <see cref="WptStyleRunner"/>) and the interpolation helpers (through <see cref="WptMotionRunner"/>, which drives the
/// engine with the host clock). Every failure is listed with a reason in <c>testdata/wpt-motion/expected-failures.tsv</c> and every listed
/// failure must still fail, so the list only shrinks. <c>floors.tsv</c> holds each module's pass rate, which only rises. Files that are not replayed are
/// listed with a reason in <c>excluded.tsv</c>. Set <c>ONERENDER_WPT_MOTION_OUT</c> to a directory to write the report and a draft expectations file.
/// </summary>
public class WptMotionReplayTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "testdata", "wpt-motion");

    private static List<WptStyleResult> RunAll()
    {
        var all = new List<WptStyleResult>();
        foreach (string f in Directory.GetFiles(Root, "*.html", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            all.AddRange(f.Replace('\\', '/').Contains("/parsing/", StringComparison.Ordinal) ? WptStyleRunner.RunFile(Root, f) : WptMotionRunner.RunFile(Root, f));
        }

        return all;
    }

    private static string ModuleOf(string file) => file.StartsWith("css/css-animations/", StringComparison.Ordinal) ? "css-animations" : "css-transitions";

    private static string Report(List<WptStyleResult> all)
    {
        var sb = new StringBuilder("module\tgroup\tpass\tfail\tn/a\trate\n");
        void Row(string module, string group, IEnumerable<WptStyleResult> rs)
        {
            int p = 0, f = 0, n = 0;
            foreach (WptStyleResult r in rs)
            {
                switch (r.Outcome)
                {
                    case WptOutcome.Pass: p++; break;
                    case WptOutcome.Fail: f++; break;
                    default: n++; break;
                }
            }

            sb.Append(module).Append('\t').Append(group).Append('\t').Append(p).Append('\t').Append(f).Append('\t').Append(n).Append('\t')
                .Append(Rate(p, f).ToString("F4", CultureInfo.InvariantCulture)).Append('\n');
        }

        foreach (IGrouping<string, WptStyleResult> m in all.Where(r => r.Kind != "unreadable").GroupBy(r => ModuleOf(r.File)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Row(m.Key, "all", m);
            Row(m.Key, "parsing", m.Where(r => r.File.Contains("/parsing/", StringComparison.Ordinal)));
            Row(m.Key, "interpolation", m.Where(r => !r.File.Contains("/parsing/", StringComparison.Ordinal)));
        }

        foreach (IGrouping<string, WptStyleResult> g in all.Where(r => r.Outcome == WptOutcome.NotApplicable).GroupBy(r => r.Detail).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.Append("n/a: ").Append(g.Key).Append(" = ").Append(g.Count()).Append('\n');
        }

        foreach (IGrouping<string, WptStyleResult> g in all.Where(r => r.Outcome == WptOutcome.NotApplicable && r.Detail.StartsWith("property not", StringComparison.Ordinal) || r.Detail.StartsWith("shorthand", StringComparison.Ordinal)).GroupBy(r => r.Property).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.Append("n/a property: ").Append(g.Key).Append(" = ").Append(g.Count()).Append('\n');
        }

        return sb.ToString();
    }

    private static double Rate(int pass, int fail) => pass + fail == 0 ? 0 : (double)pass / (pass + fail);

    [Fact]
    public void ReplayMatchesTheExpectationsAndFloors()
    {
        List<WptStyleResult> all = RunAll();
        Assert.True(all.Count > 500, "the committed WPT files were not found: " + all.Count);

        string? outDir = Environment.GetEnvironmentVariable("ONERENDER_WPT_MOTION_OUT");
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "report.txt"), Report(all));
            var draft = new StringBuilder();
            foreach (WptStyleResult r in all.Where(r => r.Outcome == WptOutcome.Fail))
            {
                draft.Append(r.Key).Append('\t').Append(r.Detail.Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
            }

            File.WriteAllText(Path.Combine(outDir, "failures.tsv"), draft.ToString());
        }

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(Root, "expected-failures.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            Assert.True(f.Length >= 5 && f[4].Length > 0, "expectation without a reason: " + line);
            expected.Add(string.Join('\t', f[..4]));
        }

        var failed = new HashSet<string>(all.Where(r => r.Outcome == WptOutcome.Fail).Select(r => r.Key), StringComparer.Ordinal);
        string[] unexpected = failed.Where(k => !expected.Contains(k)).Take(10).ToArray();
        string[] fixedNow = expected.Where(k => !failed.Contains(k)).Take(10).ToArray();
        Assert.True(unexpected.Length == 0, "unexpected failures (first 10): " + string.Join(" || ", unexpected));
        Assert.True(fixedNow.Length == 0, "listed failures that now pass (first 10): " + string.Join(" || ", fixedNow));

        foreach (string line in File.ReadAllLines(Path.Combine(Root, "floors.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            double floor = double.Parse(f[1], CultureInfo.InvariantCulture);
            IEnumerable<WptStyleResult> rs = all.Where(r => r.Kind != "unreadable" && ModuleOf(r.File) == f[0]);
            int p = rs.Count(r => r.Outcome == WptOutcome.Pass);
            int fl = rs.Count(r => r.Outcome == WptOutcome.Fail);
            Assert.True(Rate(p, fl) + 1e-9 >= floor, f[0] + " is below its floor " + floor.ToString(CultureInfo.InvariantCulture) + ": " + Rate(p, fl).ToString("F4", CultureInfo.InvariantCulture));
        }
    }
}
