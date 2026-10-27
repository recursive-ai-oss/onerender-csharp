using System.Globalization;
using OneRender.Image;
using OneRender.TestSupport;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Runs alone: the heap figures are the whole process's, so other tests running beside it would blur them.</summary>
[CollectionDefinition("ChurnSoak", DisableParallelization = true)]
public sealed class ChurnSoakCollection;

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
        long maxHeap = heap;
        for (int c = 1; c <= checkpoints; c++)
        {
            for (int i = 0; i < (frames - warm) / checkpoints; i++)
            {
                soak.Frame();
            }

            long h = GC.GetTotalMemory(true);
            maxHeap = Math.Max(maxHeap, h);
            Console.WriteLine($"SOAK fresh={fresh} frame {soak.Frames}: {soak.Describe()}, heap {h / 1024} KiB, {clock.Elapsed.TotalSeconds:0} s since warm-up");
            Assert.Equal(slots, tree.Count);
            Assert.Equal(capacity, tree.Capacity);
            Assert.Equal(rows, tree.Styles.Count);
        }

        // Flat means flat: the heap after the whole run is within a few percent of the heap after warm-up.
        Assert.True(maxHeap < heap + (heap / 10) + (256 * 1024), $"heap grew from {heap} to {maxHeap}");
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
