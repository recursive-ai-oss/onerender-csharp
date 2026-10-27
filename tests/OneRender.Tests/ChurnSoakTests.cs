using System.Globalization;
using OneRender.Image;
using OneRender.TestSupport;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Runs alone: the heap figures are the whole process's, so other tests running beside it would blur them.</summary>
[CollectionDefinition("ChurnSoak", DisableParallelization = true)]
public sealed class ChurnSoakSerial;

/// <summary>A tree that adds and removes nodes for as long as the program runs stays the size of what is live.</summary>
[Collection("ChurnSoak")]
public class ChurnSoakTests
{
    // ONERENDER_SOAK_FRAMES and ONERENDER_SOAK_FRAMES_FRESH raise the frame counts for the long soak on a build machine.
    private static int Frames(string variable, int fallback)
    {
        string? e = Environment.GetEnvironmentVariable(variable);
        return e is not null && int.TryParse(e, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : fallback;
    }

    private static void Soak(bool fresh, int frames)
    {
        var soak = new ChurnSoak(FontProviders.CreateDefault(), fresh);
        int warm = Math.Min(2000, frames / 4);
        for (int i = 0; i < warm; i++)
        {
            soak.Frame();
        }

        StyledTree tree = soak.Tree;
        int slots = tree.Count, capacity = tree.Capacity, rows = tree.Styles.Count;
        long heap = GC.GetTotalMemory(true);
        Console.WriteLine($"SOAK fresh={fresh} warm {warm}: {soak.Describe()}, heap {heap / 1024} KiB");
        int checkpoints = 10;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long maxHeap = heap, midHeap = 0, lastHeap = heap;
        for (int c = 1; c <= checkpoints; c++)
        {
            for (int i = 0; i < (frames - warm) / checkpoints; i++)
            {
                soak.Frame();
            }

            long h = GC.GetTotalMemory(true);
            maxHeap = Math.Max(maxHeap, h);
            lastHeap = h;
            if (c == checkpoints / 2)
            {
                midHeap = h;
            }

            Console.WriteLine($"SOAK fresh={fresh} frame {soak.Frames}: {soak.Describe()}, heap {h / 1024} KiB, {clock.Elapsed.TotalSeconds:0} s since warm-up");
            Assert.Equal(slots, tree.Count);
            Assert.Equal(capacity, tree.Capacity);
            Assert.Equal(rows, tree.Styles.Count);
        }

        // Flat means flat. A host that makes a declaration per node fills the heap with short-lived garbage, and the measured
        // heap may settle on a higher plateau once (seen at about a tenth of the way into the long run, then constant for 150,000 frames);
        // what it may not do is keep growing: the second half of the run adds nothing, and the plateau stays within twice the warm-up heap.
        Assert.True(lastHeap <= midHeap + (midHeap / 20) + (256 * 1024), $"heap kept growing: {midHeap} at the middle, {lastHeap} at the end");
        Assert.True(maxHeap < 2 * heap + (1024 * 1024), $"heap grew from {heap} to {maxHeap}");
    }

    [Fact]
    public void SharedDeclarationsKeepTheTreeFlat() => Soak(false, Frames("ONERENDER_SOAK_FRAMES", 6000));

    [Fact]
    public void DeclarationsMadeForEachNodeKeepTheTreeFlat() => Soak(true, Frames("ONERENDER_SOAK_FRAMES_FRESH", 6000));

    [Fact]
    public void SteadyStateChurnFramesAllocateNothing()
    {
        var soak = new ChurnSoak(FontProviders.CreateDefault(), false);
        for (int i = 0; i < 3000; i++)
        {
            soak.Frame();
        }

        long best = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++)
            {
                soak.Frame();
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Console.WriteLine($"CHURN allocation: {best} B per 50 frames");
        Assert.Equal(0, best);
    }
}
