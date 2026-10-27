using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneRender.GeomDiff;

/// <summary>A frozen page: its HTML and every resource the page was allowed to load. Stored as page.json beside index.html.</summary>
internal sealed class SnapshotPage
{
    public string Format { get; set; } = "onerender-snapshot/1";
    public string Id { get; set; } = "";
    /// <summary>The address the page is loaded from, so relative references resolve as they did originally.</summary>
    public string Url { get; set; } = "";
    public List<SnapshotResource> Resources { get; set; } = [];
}

internal sealed class SnapshotResource
{
    public string Url { get; set; } = "";
    /// <summary>File name relative to the page directory.</summary>
    public string File { get; set; } = "";
    public string ContentType { get; set; } = "";
}

internal sealed class ViewportSize
{
    public int Width { get; set; }
    public int Height { get; set; }
    public override string ToString() => Width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x" + Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>One element of a geometry capture. Box is [x, y, width, height] or null when the element generates no box.</summary>
internal sealed class ElementRecord
{
    public string Path { get; set; } = "";
    public string Tag { get; set; } = "";
    public string? Display { get; set; }
    /// <summary>The element has a direct child text node that is not only white space.</summary>
    public bool Text { get; set; }
    public double[]? Box { get; set; }
}

/// <summary>Border boxes of every element of one page at one viewport. Format onerender-geometry/1.</summary>
internal sealed class GeometryCapture
{
    public string Format { get; set; } = "onerender-geometry/1";
    /// <summary>"chrome", "engine" or "perturbed".</summary>
    public string Source { get; set; } = "";
    public string Page { get; set; } = "";
    public ViewportSize Viewport { get; set; } = new();
    public double DocumentWidth { get; set; }
    public double DocumentHeight { get; set; }
    public List<ElementRecord> Elements { get; set; } = [];
}

/// <summary>Recorded beside a capture run: what produced the numbers.</summary>
internal sealed class RunMeta
{
    public string Format { get; set; } = "onerender-geometry-run/1";
    public string Source { get; set; } = "chrome";
    public string Browser { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public List<string> Flags { get; set; } = [];
    public string FontSet { get; set; } = "";
    public List<string> Viewports { get; set; } = [];
    public int Pages { get; set; }
    public int Captured { get; set; }
    public List<string> Failures { get; set; } = [];
}

internal sealed class ClassCounts
{
    public long PositionOnly { get; set; }
    public long SizeOnly { get; set; }
    public long PositionAndSize { get; set; }
    public long TextRun { get; set; }
    public long MissingOurs { get; set; }
    public long MissingChrome { get; set; }
}

internal sealed class TagCount
{
    public string Tag { get; set; } = "";
    public long Mismatches { get; set; }
    public long Elements { get; set; }
}

internal sealed class ViewportSummary
{
    public string Viewport { get; set; } = "";
    public int Pages { get; set; }
    /// <summary>Elements that have a box on at least one side.</summary>
    public long Elements { get; set; }
    public long Matched { get; set; }
    public double Rate { get; set; }
    public ClassCounts Classes { get; set; } = new();
    /// <summary>Mismatches whose nearest mismatching ancestor does not exist, by class.</summary>
    public ClassCounts RootCauses { get; set; } = new();
    /// <summary>Elements present on both sides under one path but with different tag names (parser divergence).</summary>
    public long TagConflicts { get; set; }
    public int PagesAtGate { get; set; }
    public double MedianPageRate { get; set; }
    public double MinPageRate { get; set; }
    public List<TagCount> WorstTags { get; set; } = [];
}

internal sealed class SummaryFile
{
    public string Format { get; set; } = "onerender-geometry-summary/1";
    public string Set { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>"capture" (another capture run) or "engine" (the engine under test).</summary>
    public string Ours { get; set; } = "";
    public double TolerancePx { get; set; }
    public double TolerancePercent { get; set; }
    public double GateRate { get; set; } = 0.99;
    public string Browser { get; set; } = "";
    public string FontSet { get; set; } = "";
    public List<ViewportSummary> Viewports { get; set; } = [];
    public ViewportSummary Total { get; set; } = new();
    public bool GateMet { get; set; }
}

/// <summary>Per-page mismatch list written for drill-down.</summary>
internal sealed class PageDetail
{
    public string Format { get; set; } = "onerender-geometry-detail/1";
    public string Page { get; set; } = "";
    public string Viewport { get; set; } = "";
    public long Elements { get; set; }
    public long Matched { get; set; }
    public List<MismatchRecord> Mismatches { get; set; } = [];
}

internal sealed class MismatchRecord
{
    public string Path { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Class { get; set; } = "";
    public bool RootCause { get; set; }
    public double[]? Chrome { get; set; }
    public double[]? Ours { get; set; }
}

/// <summary>Class counts the perturbation made, which the comparer must reproduce exactly.</summary>
internal sealed class Expectation
{
    public ClassCounts Classes { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SnapshotPage))]
[JsonSerializable(typeof(GeometryCapture))]
[JsonSerializable(typeof(RunMeta))]
[JsonSerializable(typeof(SummaryFile))]
[JsonSerializable(typeof(PageDetail))]
[JsonSerializable(typeof(Expectation))]
internal sealed partial class Ctx : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true)]
[JsonSerializable(typeof(SummaryFile))]
[JsonSerializable(typeof(RunMeta))]
[JsonSerializable(typeof(SnapshotPage))]
[JsonSerializable(typeof(Expectation))]
internal sealed partial class PrettyCtx : JsonSerializerContext
{
}

internal static class Store
{
    public static T Read<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        using FileStream fs = File.OpenRead(path);
        Stream s = path.EndsWith(".gz", StringComparison.Ordinal) ? new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress) : fs;
        using (s)
        {
            return JsonSerializer.Deserialize(s, info) ?? throw new InvalidDataException("empty JSON in " + path);
        }
    }

    public static void Write<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        using (FileStream fs = File.Create(tmp))
        {
            Stream s = path.EndsWith(".gz", StringComparison.Ordinal) ? new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest) : fs;
            using (s)
            {
                JsonSerializer.Serialize(s, value, info);
            }
        }
        File.Move(tmp, path, overwrite: true);
    }
}
