using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OneRender.WptRunner;

/// <summary>Counts for one module run.</summary>
public sealed record Counts(int Total, int Pass, int Fail, int Blank, int Skipped)
{
    /// <summary>Passes over runnable tests (total minus skipped); 0 when nothing was runnable.</summary>
    public double Rate => Total - Skipped == 0 ? 0 : (double)Pass / (Total - Skipped);
}

/// <summary>Everything recorded about one module in one run.</summary>
public sealed class ModuleResult
{
    /// <summary>The module id.</summary>
    public required string Module { get; init; }

    /// <summary>Every Nth discovered test was run (1 means all).</summary>
    public int SampleStride { get; init; } = 1;

    /// <summary>Tests the module has before sampling.</summary>
    public int Discovered { get; init; }

    /// <summary>Verdict per test id, ordinal order.</summary>
    public required SortedDictionary<string, Verdict> Tests { get; init; }

    /// <summary>Totals by outcome.</summary>
    public Counts Counts
    {
        get
        {
            int p = 0, f = 0, b = 0, s = 0;
            foreach (Verdict v in Tests.Values)
            {
                switch (v.Outcome)
                {
                    case Outcome.Pass: p++; break;
                    case Outcome.Fail: f++; break;
                    case Outcome.Blank: b++; break;
                    default: s++; break;
                }
            }

            return new Counts(Tests.Count, p, f, b, s);
        }
    }
}

/// <summary>Writes and summarizes results in the versioned layout described in docs/wpt-results.md.</summary>
public static class ResultsWriter
{
    /// <summary>The value of <c>formatVersion</c> in every file.</summary>
    public const int FormatVersion = 1;

    /// <summary>Writes <c>&lt;dir&gt;/&lt;module&gt;.json</c>.</summary>
    public static void WriteModule(string dir, string commit, string renderer, string rendererVersion, ModuleResult r)
    {
        Directory.CreateDirectory(dir);
        using var fs = File.Create(Path.Combine(dir, r.Module + ".json"));
        using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
        Counts c = r.Counts;
        w.WriteStartObject();
        w.WriteNumber("formatVersion", FormatVersion);
        w.WriteString("wptCommit", commit);
        w.WriteString("renderer", renderer);
        w.WriteString("rendererVersion", rendererVersion);
        w.WriteString("module", r.Module);
        w.WriteNumber("discovered", r.Discovered);
        w.WriteNumber("sampleStride", r.SampleStride);
        w.WriteStartObject("counts");
        w.WriteNumber("total", c.Total);
        w.WriteNumber("pass", c.Pass);
        w.WriteNumber("fail", c.Fail);
        w.WriteNumber("blank", c.Blank);
        w.WriteNumber("skipped", c.Skipped);
        w.WriteEndObject();
        w.WriteString("rate", Rate(c));
        WriteList(w, "pass", r, Outcome.Pass, withReason: false);
        WriteList(w, "blank", r, Outcome.Blank, withReason: false);
        WriteList(w, "fail", r, Outcome.Fail, withReason: true);
        WriteList(w, "skipped", r, Outcome.Skipped, withReason: true);
        w.WriteEndObject();
    }

    private static void WriteList(Utf8JsonWriter w, string name, ModuleResult r, Outcome o, bool withReason)
    {
        if (withReason)
        {
            w.WriteStartObject(name);
            foreach (var kv in r.Tests)
            {
                if (kv.Value.Outcome == o)
                {
                    w.WriteString(kv.Key, kv.Value.Reason);
                }
            }

            w.WriteEndObject();
        }
        else
        {
            w.WriteStartArray(name);
            foreach (var kv in r.Tests)
            {
                if (kv.Value.Outcome == o)
                {
                    w.WriteStringValue(kv.Key);
                }
            }

            w.WriteEndArray();
        }
    }

    /// <summary>Rate formatted with four decimals, culture-free, so files diff cleanly.</summary>
    public static string Rate(Counts c) => c.Rate.ToString("0.0000", CultureInfo.InvariantCulture);

    /// <summary>Rewrites <c>summary.json</c> and <c>summary.md</c> from every module file in <paramref name="dir"/>.</summary>
    public static void WriteSummary(string dir)
    {
        var rows = new List<(string Module, int Stride, int Discovered, Counts C, string Commit, string Renderer, string Version,
            List<KeyValuePair<string, int>> FailReasons, List<KeyValuePair<string, int>> SkipReasons)>();
        foreach (string file in Directory.GetFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == "summary.json")
            {
                continue;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = doc.RootElement;
            JsonElement c = root.GetProperty("counts");
            rows.Add((
                root.GetProperty("module").GetString()!,
                root.GetProperty("sampleStride").GetInt32(),
                root.GetProperty("discovered").GetInt32(),
                new Counts(c.GetProperty("total").GetInt32(), c.GetProperty("pass").GetInt32(), c.GetProperty("fail").GetInt32(),
                    c.GetProperty("blank").GetInt32(), c.GetProperty("skipped").GetInt32()),
                root.GetProperty("wptCommit").GetString()!,
                root.GetProperty("renderer").GetString()!,
                root.GetProperty("rendererVersion").GetString()!,
                Histogram(root.GetProperty("fail")),
                Histogram(root.GetProperty("skipped"))));
        }

        if (rows.Count == 0)
        {
            return;
        }

        var tot = new Counts(
            rows.Sum(r => r.C.Total), rows.Sum(r => r.C.Pass), rows.Sum(r => r.C.Fail), rows.Sum(r => r.C.Blank), rows.Sum(r => r.C.Skipped));

        using (var fs = File.Create(Path.Combine(dir, "summary.json")))
        using (var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("formatVersion", FormatVersion);
            w.WriteString("wptCommit", rows[0].Commit);
            w.WriteString("renderer", rows[0].Renderer);
            w.WriteString("rendererVersion", rows[0].Version);
            w.WriteStartArray("modules");
            foreach (var r in rows)
            {
                w.WriteStartObject();
                w.WriteString("module", r.Module);
                w.WriteNumber("sampleStride", r.Stride);
                w.WriteNumber("discovered", r.Discovered);
                w.WriteNumber("total", r.C.Total);
                w.WriteNumber("pass", r.C.Pass);
                w.WriteNumber("fail", r.C.Fail);
                w.WriteNumber("blank", r.C.Blank);
                w.WriteNumber("skipped", r.C.Skipped);
                w.WriteString("rate", Rate(r.C));
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartObject("totals");
            w.WriteNumber("total", tot.Total);
            w.WriteNumber("pass", tot.Pass);
            w.WriteNumber("fail", tot.Fail);
            w.WriteNumber("blank", tot.Blank);
            w.WriteNumber("skipped", tot.Skipped);
            w.WriteString("rate", Rate(tot));
            w.WriteEndObject();
            w.WriteEndObject();
        }

        var sb = new StringBuilder();
        sb.Append("# WPT reftest results: ").Append(rows[0].Renderer).Append('\n').Append('\n');
        sb.Append("- WPT commit: `").Append(rows[0].Commit).Append("`\n");
        sb.Append("- Renderer: ").Append(rows[0].Renderer).Append(" (").Append(rows[0].Version).Append(")\n");
        sb.Append("- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.\n");
        sb.Append("- Stride N means every Nth discovered test was run.\n\n");
        sb.Append("| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |\n|---|---:|---:|---:|---:|---:|---:|---:|\n");
        foreach (var r in rows)
        {
            sb.Append("| ").Append(r.Module).Append(" | ").Append(r.Stride).Append(" | ").Append(r.C.Total).Append(" | ").Append(r.C.Pass)
                .Append(" | ").Append(r.C.Fail).Append(" | ").Append(r.C.Blank).Append(" | ").Append(r.C.Skipped)
                .Append(" | ").Append(Rate(r.C)).Append(" |\n");
        }

        sb.Append("| **all** | | ").Append(tot.Total).Append(" | ").Append(tot.Pass).Append(" | ").Append(tot.Fail).Append(" | ")
            .Append(tot.Blank).Append(" | ").Append(tot.Skipped).Append(" | ").Append(Rate(tot)).Append(" |\n\n");

        sb.Append("## Why tests failed or were skipped\n\n");
        foreach (var r in rows)
        {
            sb.Append("### ").Append(r.Module).Append('\n').Append('\n');
            AppendReasons(sb, "Failures", r.FailReasons);
            AppendReasons(sb, "Skipped", r.SkipReasons);
        }

        File.WriteAllText(Path.Combine(dir, "summary.md"), sb.ToString());
    }

    private static void AppendReasons(StringBuilder sb, string title, List<KeyValuePair<string, int>> hist)
    {
        if (hist.Count == 0)
        {
            return;
        }

        sb.Append("- ").Append(title).Append(": ");
        sb.AppendJoin("; ", hist.Take(8).Select(kv => $"{kv.Value} × {kv.Key}"));
        if (hist.Count > 8)
        {
            sb.Append("; … ").Append(hist.Count - 8).Append(" more kinds");
        }

        sb.Append('\n');
    }

    // Groups reasons after dropping the numbers that differ per test ("max 12, 340 px"), so the histogram stays short.
    private static List<KeyValuePair<string, int>> Histogram(JsonElement obj)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonProperty p in obj.EnumerateObject())
        {
            string key = Generalize(p.Value.GetString() ?? string.Empty);
            d[key] = d.GetValueOrDefault(key) + 1;
        }

        return d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
    }

    private static string Generalize(string reason)
    {
        int paren = reason.IndexOf('(', StringComparison.Ordinal);
        if (reason.StartsWith("==", StringComparison.Ordinal) && paren > 0)
        {
            return reason.StartsWith("== ", StringComparison.Ordinal) ? "== reference differs" : reason;
        }

        return reason.StartsWith("!=", StringComparison.Ordinal) ? "!= reference identical" : reason.StartsWith("reference file missing", StringComparison.Ordinal) ? "reference file missing" : reason;
    }
}
