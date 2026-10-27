using OneRender.Image;
using OneRender.Layout;
using OneRender.Tests.Support;
using Xunit;

namespace OneRender.Tests;

public class ScratchDump
{
    [Fact]
    public void Dump()
    {
        string? file = System.Environment.GetEnvironmentVariable("DUMP_HTML");
        if (file is null) return;
        string html = System.IO.File.ReadAllText(file);
        double w = double.Parse(System.Environment.GetEnvironmentVariable("DUMP_W") ?? "1280");
        Pipeline p = Pipeline.FromHtml(html, w, 800);
        var f = new FragmentTree(p.Boxes);
        var eng = new BlockLayoutEngine(); var inp = new LayoutInput(w, double.NaN, FontProviders.ForCurrentThread()); eng.Layout(p.Boxes, inp, f); string d1 = f.Dump(); eng.Layout(p.Boxes, inp, f); string d2 = f.Dump(); System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable("DUMP_OUT")! + ".1", d1); System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable("DUMP_OUT")! + ".2", d2);
        System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable("DUMP_OUT")!, f.Dump() + p.Boxes.Dump() + string.Join("\n", System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, p.Boxes.Count), i => i + ":" + p.Boxes.Make(i).Values.LineHeight.Kind + " " + p.Boxes.Make(i).Values.LineHeight.Value + " fs " + p.Boxes.Make(i).Values.FontSize)));
    }
}
