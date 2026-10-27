using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using Xunit;

namespace OneRender.Tests.Wpt;

/// <summary>
/// The parsing and computed-value assertions of web-platform-tests, replayed against the style pipeline (see <see cref="WptStyleRunner"/>).
/// Every failure must be listed with a reason in <c>testdata/wpt-style/expected-failures.tsv</c>, and every listed failure must still fail,
/// so the list only shrinks. Set <c>ONERENDER_WPT_STYLE_OUT</c> to a directory to write the per-group report and a draft expectations file there.
/// </summary>
public class WptStyleReplayTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "testdata", "wpt-style");

    private static List<WptStyleResult> RunAll()
    {
        var all = new List<WptStyleResult>();
        foreach (string f in Directory.GetFiles(Root, "*.html", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            all.AddRange(WptStyleRunner.RunFile(Root, f));
        }

        return all;
    }

    [Fact]
    public void ReplayMatchesTheExpectations()
    {
        List<WptStyleResult> all = RunAll();
        Assert.True(all.Count > 1000, "the committed WPT files were not found: " + all.Count);

        string? outDir = Environment.GetEnvironmentVariable("ONERENDER_WPT_STYLE_OUT");
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "report.txt"), Report(all));
            var draft = new StringBuilder();
            foreach (WptStyleResult r in all.Where(r => r.Outcome == WptOutcome.Fail))
            {
                draft.Append(r.Key).Append('\t').Append(r.Detail).Append('\n');
            }

            File.WriteAllText(Path.Combine(outDir, "failures.tsv"), draft.ToString());
        }

        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(Root, "expected-failures.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            Assert.True(f.Length >= 5 && f[4].Length > 0, "expectation without a reason: " + line);
            expected[string.Join('\t', f[..4])] = f[4];
        }

        var failed = new HashSet<string>(all.Where(r => r.Outcome == WptOutcome.Fail).Select(r => r.Key), StringComparer.Ordinal);
        string[] unexpected = failed.Where(k => !expected.ContainsKey(k)).Take(10).ToArray();
        string[] fixedNow = expected.Keys.Where(k => !failed.Contains(k)).Take(10).ToArray();
        Assert.True(unexpected.Length == 0, "unexpected failures (first 10): " + string.Join(" || ", unexpected));
        Assert.True(fixedNow.Length == 0, "listed failures that now pass (first 10): " + string.Join(" || ", fixedNow));
    }

    private static string Report(List<WptStyleResult> all)
    {
        var sb = new StringBuilder();
        sb.Append("group\tkind\tpass\tfail\tn/a\trate\n");
        void Row(string group, string kind, IEnumerable<WptStyleResult> rs)
        {
            int p = 0, f = 0, n = 0;
            foreach (WptStyleResult r in rs)
            {
                if (r.Outcome == WptOutcome.Pass) { p++; }
                else if (r.Outcome == WptOutcome.Fail) { f++; }
                else { n++; }
            }

            double rate = p + f == 0 ? 0 : 100.0 * p / (p + f);
            sb.Append(group).Append('\t').Append(kind).Append('\t').Append(p).Append('\t').Append(f).Append('\t').Append(n).Append('\t')
                .Append(rate.ToString("F1", CultureInfo.InvariantCulture)).Append('\n');
        }

        foreach (IGrouping<string, WptStyleResult> g in all.Where(r => r.Kind != "unreadable").GroupBy(r => WptStyleRunner.GroupOf(r.Property)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Row(g.Key, "all", g);
            foreach (IGrouping<string, WptStyleResult> k in g.GroupBy(r => r.Kind).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                Row(g.Key, k.Key, k);
            }
        }

        Row("TOTAL", "all", all.Where(r => r.Kind != "unreadable"));
        foreach (IGrouping<string, WptStyleResult> k in all.Where(r => r.Kind != "unreadable").GroupBy(r => r.Kind).OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Row("TOTAL", k.Key, k);
        }

        sb.Append("unreadable calls (not string literals): ").Append(all.Where(r => r.Kind == "unreadable").Sum(r => int.Parse(r.Value, CultureInfo.InvariantCulture))).Append('\n');
        foreach (IGrouping<string, WptStyleResult> g in all.Where(r => r.Outcome == WptOutcome.NotApplicable && r.Kind != "unreadable").GroupBy(r => r.Detail).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.Append("n/a: ").Append(g.Key).Append(" = ").Append(g.Count()).Append('\n');
        }

        return sb.ToString();
    }
}
