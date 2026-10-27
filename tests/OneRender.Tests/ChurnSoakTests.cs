using System.Globalization;
using OneRender;
using OneRender.Image;
using OneRender.Style;
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

    // Rows freed by a sweep are reused, so the table's size may move between sweeps but must stay within one sweep gap of what it was.
    private const int RowSlack = 4096;

    private static void Soak(bool fresh, int frames, bool moving = false)
    {
        var soak = new ChurnSoak(FontProviders.CreateDefault(), fresh, moving);
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
        long flushBytesAtWarm = soak.FlushBytes;
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

            Console.WriteLine($"SOAK fresh={fresh} frame {soak.Frames}: {soak.Describe()}, heap {h / 1024} KiB");
            Assert.Equal(slots, tree.Count);
            Assert.Equal(capacity, tree.Capacity);
            Assert.InRange(tree.Styles.Count, 1, rows + RowSlack);
            Assert.InRange(tree.Styles.LiveCount, 1, rows + RowSlack);
            Assert.DoesNotContain(tree.Styles.Diagnostics.Items, d => d.Kind == DiagnosticKind.LimitExceeded);
        }

        if (moving && !fresh)
        {
            // The engine's share of a frame (the flush) allocates nothing; a sweep may compute again a style it had dropped, and the
            // runtime's own work can land in a frame, so the allowance is a few bytes a frame on average, not a per-frame zero.
            long perFrame = (soak.FlushBytes - flushBytesAtWarm) / (frames - warm);
            Console.WriteLine($"SOAK moving: {soak.FlushBytes - flushBytesAtWarm} B allocated by {frames - warm} flushes, rows {tree.Styles.Count} ({tree.Styles.LiveCount} live), {tree.Styles.ReclaimCount} sweeps");
            Assert.True(perFrame <= 8, $"flushes allocated {perFrame} B per frame");
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

    // One element's left and one label's width take a value never seen before on every frame, on top of the churn. Rows of the values it
    // has left behind are freed and reused, so the table stays small, the heap stays flat and nothing leaks into the diagnostics.
    [Fact]
    public void StylesThatTakeANewValueEveryFrameKeepTheTableFlat() => Soak(false, Frames("ONERENDER_SOAK_FRAMES_MOVING", 30000), moving: true);

    [Fact]
    public void FlushingNewStyleValuesAllocatesNothing()
    {
        var soak = new ChurnSoak(FontProviders.CreateDefault(), false, moving: true);
        for (int i = 0; i < 12000; i++)
        {
            soak.Frame();
        }

        // Setting a value parses CSS text, which is the host's work and allocates; the engine's flush must not. Like the other allocation
        // tests this takes the best of several rounds, because the runtime's own work (tiered compilation) can land in any one frame.
        StyleTable table = soak.Tree.Styles;
        long best = long.MaxValue;
        int sweeps = 0, dirtyFrames = 0, frames = 0;
        for (int round = 0; round < 40; round++)
        {
            long bytes = 0;
            for (int i = 0; i < 100; i++)
            {
                soak.Edit();
                int before = table.ReclaimCount;
                long start = GC.GetAllocatedBytesForCurrentThread();
                soak.FlushEdits();
                long used = GC.GetAllocatedBytesForCurrentThread() - start;
                bytes += used;
                frames++;
                dirtyFrames += used > 0 ? 1 : 0;
                sweeps += table.ReclaimCount != before ? 1 : 0;
            }

            best = Math.Min(best, bytes);
        }

        Console.WriteLine($"MOVING flush allocation: best round {best} B per 100 frames, {dirtyFrames} of {frames} frames allocated, {sweeps} frames swept, rows {table.Count} ({table.LiveCount} live)");
        Assert.True(sweeps > 0, "the run was too short to include a sweep");
        Assert.Equal(0, best);
    }

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
