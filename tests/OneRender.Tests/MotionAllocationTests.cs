using System.Diagnostics;
using System.Text;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.TestSupport;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>Ticking running transform and opacity animations on a page of about a thousand nodes: no allocation, no relayout.</summary>
public class MotionAllocationTests
{
    private const string Css =
        "@keyframes spin{to{transform:rotate(360deg)}}@keyframes pulse{from{opacity:.2}to{opacity:1}}" +
        ".spin{animation:spin 2s linear infinite}.pulse{animation:pulse 1s ease-in-out infinite alternate}";

    private static InteractivePage UiPage(int animated)
    {
        var sb = new StringBuilder("<div id=app style='width:780px'>");
        for (int i = 0; i < 250; i++)
        {
            string cls = i < animated ? (i % 2 == 0 ? " spin" : " pulse") : string.Empty;
            sb.Append("<div class='row").Append(cls).Append("' id=r").Append(i).Append(" style='height:24px;overflow:hidden'><span class=lbl>Item ").Append(i)
                .Append("</span><button id=b").Append(i).Append(" style='width:60px'>Go</button><i style='display:inline-block;width:10px'></i></div>");
        }

        sb.Append("</div>");
        return Ix.Page(sb.ToString(), 800, 600, Css, new InteractionOptions { AnimationStartTime = 0 });
    }

    [Fact]
    public void TickingAHundredTransformAndOpacityAnimationsAllocatesNothingAndDoesNotRelayout()
    {
        InteractivePage p = UiPage(100);
        Assert.InRange(p.Page.Styled.Count, 1000, 1700);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(800, 600) { Scroll = p.Page.Scroll };
        painter.Paint(p.Page.Fragments, retained, options);
        p.Advance(0);
        int generation = p.Generation;
        double t = 0;
        InputOutcome last = InputOutcome.None;
        int patched = 0;

        void Frame()
        {
            t += 16;
            last = p.Advance(t);
            patched = p.PatchAnimations(painter, retained);
        }

        Frame();
        Assert.Equal(InputOutcome.Animated, last);
        Assert.True(patched >= 100, "every animated element is patched: " + patched);
        Allocation.AssertZero(Frame, "advance and patch of 100 running animations", warmup: 20, iterations: 100);
        Assert.Equal(InputOutcome.Animated, last);
        Assert.Equal(generation, p.Generation);
    }

    [Fact]
    public void PatchedListEqualsAFreshPaintAtTheSameTime()
    {
        InteractivePage p = UiPage(20);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(800, 600) { Scroll = p.Page.Scroll };
        painter.Paint(p.Page.Fragments, retained, options);
        for (int i = 1; i <= 7; i++)
        {
            p.Advance(i * 137);
            p.PatchAnimations(painter, retained);
        }

        var fresh = new DisplayList();
        new Painter().Paint(p.Page.Fragments, fresh, options);
        DisplayList patched = retained.Current;
        Assert.Equal(fresh.Count, patched.Count);
        for (int i = 0; i < fresh.Count; i++)
        {
            Assert.Equal(fresh[i].Id, patched[i].Id);
            Assert.Equal(fresh[i].ContentHash, patched[i].ContentHash);
        }
    }

    [Fact]
    public void TickTimeOfAHundredAnimations()
    {
        InteractivePage p = UiPage(100);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(p.Page.Fragments, retained, new PaintOptions(800, 600) { Scroll = p.Page.Scroll });
        double t = 0;
        for (int i = 0; i < 200; i++)
        {
            t += 16;
            p.Advance(t);
            p.PatchAnimations(painter, retained);
        }

        long start = Stopwatch.GetTimestamp();
        const int N = 1000;
        for (int i = 0; i < N; i++)
        {
            t += 16;
            p.Advance(t);
            p.PatchAnimations(painter, retained);
        }

        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds / N;
        Console.WriteLine("tick of 100 animations on " + p.Page.Styled.Count + " nodes: " + ms.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + " ms");
        Assert.True(ms < 5, "a tick takes " + ms + " ms");
    }
}
