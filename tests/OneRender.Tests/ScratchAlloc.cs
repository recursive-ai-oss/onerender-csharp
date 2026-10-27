using OneRender.Image;
using OneRender.Layout;
using OneRender.Tests.Support;
using OneRender.TestSupport;
using Xunit;

namespace OneRender.Tests;

public class ScratchAlloc
{
    static long Bytes(string body)
    {
        string html = "<!DOCTYPE html><html><head><style>body{margin:0;font:16px/20px 'Liberation Sans'}p{margin:0}</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, 800, 600);
        var f = new FragmentTree(p.Boxes);
        var e = new BlockLayoutEngine();
        var input = new LayoutInput(800, double.NaN, FontProviders.ForCurrentThread());
        return Allocation.Measure(() => e.Layout(p.Boxes, input, f), 5, 10);
    }

    [Fact]
    public void Probe()
    {
        string path = System.Environment.GetEnvironmentVariable("DUMP_OUT") ?? "/dev/null";
        var sb = new System.Text.StringBuilder();
        string[] cases =
        {
            "<p style='width:300px'>plain text that wraps over several lines in the box nicely ok</p>",
            "<p style='width:300px'>plain <b>bold</b> text that wraps over several lines in the box nicely ok</p>",
            "<p style='width:300px;text-align:justify'>plain text that wraps over several lines in the box nicely ok</p>",
            "<p style='width:300px'>plain <span style='display:inline-block;width:20px;height:8px'></span> text that wraps over several lines</p>",
            "<p style='width:300px'>plain <span style='padding:0 2px'>nested</span> text that wraps over several lines in the box nicely ok</p>",
            "<p style='width:300px'>one<br>two<br>three</p>",
        };
        string txt = "plain text that wraps over several lines in the box nicely ok";
        byte[] opp = new byte[100]; byte[] gs = new byte[100]; byte[] lv = new byte[100]; int[] ord = new int[100];
        sb.Append("opp ").AppendLine(Allocation.Measure(() => OneFont.TextSegmentation.FillLineBreakOpportunities(txt, opp), 5, 10).ToString());
        sb.Append("gs ").AppendLine(Allocation.Measure(() => OneFont.TextSegmentation.FillGraphemeStarts(txt, gs), 5, 10).ToString());
        sb.Append("bidi ").AppendLine(Allocation.Measure(() => OneFont.TextShaping.GetBidiLevels(txt, OneFont.TextDirection.LeftToRight, lv, out _), 5, 10).ToString());
        sb.Append("vo ").AppendLine(Allocation.Measure(() => OneFont.TextShaping.GetVisualOrder(lv.AsSpan(0, 10), ord), 5, 10).ToString());
        foreach (string c in cases)
        {
            sb.Append(Bytes(c)).Append(' ').AppendLine(c);
        }

        System.IO.File.WriteAllText(path, sb.ToString());
    }
}

sealed class AllocListener : System.Diagnostics.Tracing.EventListener
{
    public System.Collections.Generic.Dictionary<string, int> Counts = new();
    protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
        {
            EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)1);
        }
    }

    protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
    {
        if (e.EventName != null && e.EventName.StartsWith("GCAllocationTick"))
        {
            string t = e.Payload![e.PayloadNames!.IndexOf("TypeName")]!.ToString()!;
            lock (Counts) { Counts[t] = Counts.GetValueOrDefault(t) + 1; }
        }
    }
}

public class ScratchAlloc2
{
    [Fact]
    public void Types()
    {
        string path = System.Environment.GetEnvironmentVariable("DUMP_OUT") ?? "/dev/null";
        string body = System.Environment.GetEnvironmentVariable("DUMP_HTML") ?? "";
        string html = "<!DOCTYPE html><html><head><style>body{margin:0;font:16px/20px 'Liberation Sans'}p{margin:0}</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, 800, 600);
        var f = new FragmentTree(p.Boxes);
        var e = new BlockLayoutEngine();
        var input = new LayoutInput(800, double.NaN, FontProviders.ForCurrentThread());
        for (int i = 0; i < 20; i++) e.Layout(p.Boxes, input, f);
        using var l = new AllocListener();
        for (int i = 0; i < 20000; i++) e.Layout(p.Boxes, input, f);
        var sb = new System.Text.StringBuilder();
        foreach (var kv in l.Counts) sb.Append(kv.Value).Append(' ').AppendLine(kv.Key);
        System.IO.File.WriteAllText(path, sb.ToString());
    }
}
