using System.Diagnostics;
using System.Text;
using OneRender.Interaction;
using RecursiveAI.OneHtml;
using Xunit;
using Xunit.Sdk;

namespace OneRender.Tests;

/// <summary>Steady-state input handling on a UI-like page of about a thousand nodes allocates nothing.</summary>
public class InteractionAllocationTests
{
    private static InteractivePage UiPage()
    {
        var sb = new StringBuilder("<div id=app style='width:780px'>");
        for (int i = 0; i < 250; i++)
        {
            sb.Append("<div class=row id=r").Append(i).Append(" style='height:24px;overflow:hidden'><span class=lbl>Item ").Append(i)
                .Append("</span><button id=b").Append(i).Append(" style='width:60px'>Go</button><i style='display:inline-block;width:10px'></i></div>");
        }

        sb.Append("</div>");
        return Ix.Page(sb.ToString(), 800, 600, "button{margin-left:8px}");
    }

    // The fewest bytes over three timed passes. A cost paid on every call shows in every pass; a one-time cost that lands inside a
    // pass (a runtime compiling or tiering the method on a loaded machine) does not.
    private static long Measure(Action body, int warmup = 500, int iterations = 200)
    {
        for (int i = 0; i < warmup; i++)
        {
            body();
        }

        long best = long.MaxValue;
        for (int pass = 0; pass < 3 && best != 0; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++)
            {
                body();
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return best;
    }

    [Fact]
    public void ThePageHasAboutAThousandNodes()
    {
        InteractivePage p = UiPage();
        Assert.InRange(p.Page.Styled.Count, 1000, 1700);
    }

    [Fact]
    public void HitTestsAllocateNothing()
    {
        InteractivePage p = UiPage();
        int i = 0;
        long bytes = Measure(() =>
        {
            i++;
            p.Hits.HitTest(10 + (i % 700), 5 + ((i * 7) % 560), out _);
        });
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void ElementFromPointAllocatesNothing()
    {
        InteractivePage p = UiPage();
        int i = 0;
        Assert.Equal(0, Measure(() => p.ElementFromPoint(20, 5 + ((i++ * 13) % 500))));
    }

    [Fact]
    public void PointerMovesInsideOneElementAllocateNothing()
    {
        InteractivePage p = UiPage();
        p.HandlePointer(Ix.Move(5, 5, 0));
        int n = 0;
        p.Events.AddEventListener(EventTarget.Window, "mousemove", _ => n++);
        double x = 5;
        long bytes = Measure(() =>
        {
            x = x > 30 ? 5 : x + 0.25;
            p.HandlePointer(Ix.Move(x, 5, x));
        });
        Assert.Equal(0, bytes);
        Assert.True(n >= 250);
    }

    [Fact]
    public void DispatchToAListenerWithNoStateChangeAllocatesNothing()
    {
        InteractivePage p = UiPage();
        Node target = Ix.Id(p, "b100");
        int n = 0;
        p.Events.AddEventListener(EventTarget.Of(target), "ping", _ => n++);
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "app")), "ping", _ => n++, new EventListenerOptions { Capture = true });
        p.Events.AddEventListener(EventTarget.Window, "ping", _ => n++);
        var ev = new DomEvent("ping", bubbles: true, cancelable: true);
        EventTarget t = EventTarget.Of(target);
        Assert.Equal(0, Measure(() => p.Events.Dispatch(ev, t)));
        Assert.True(n > 0);
    }

    [Fact]
    public void WheelScrollingThatKeepsTheHoveredElementAllocatesNothing()
    {
        InteractivePage p = UiPage();
        p.HandlePointer(Ix.Move(5, 5, 0));
        int sign = 1;
        // Half-pixel steps back and forth stay inside the first row, so the element under the pointer does not change.
        long bytes = Measure(() =>
        {
            p.HandleWheel(new WheelInput { X = 5, Y = 5, DeltaY = sign * 0.5, Time = 1 });
            sign = -sign;
        });
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void ScrollChainWithAViewportScrollAllocatesNothing()
    {
        InteractivePage p = Ix.Page("<div id=a style='height:5000px'></div>", 400, 300);
        p.HandlePointer(Ix.Move(5, 5, 0));
        int dir = 1;
        long bytes = Measure(() =>
        {
            if (p.Scroll.ViewportY > 4000)
            {
                dir = -1;
            }
            else if (p.Scroll.ViewportY < 10)
            {
                dir = 1;
            }

            p.HandleWheel(new WheelInput { X = 5, Y = 5, DeltaY = dir * 30, Time = 1 });
        });
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void HitTestsOnAThousandNodePageFindTargets()
    {
        // The time of a hit test is a benchmark row (InteractionBenchmarks); here the answers are checked.
        InteractivePage p = UiPage();
        const int N = 2000;
        int hits = 0;
        for (int i = 0; i < N; i++)
        {
            if (p.Hits.HitTest(10 + (i % 700), 5 + ((i * 7) % 560), out _))
            {
                hits++;
            }
        }

        Assert.True(hits > 0);
    }
}
