using System;

namespace OneRender.TestSupport;

/// <summary>Measures managed allocation on the calling thread, for tests that hold a code path to zero bytes.</summary>
public static class Allocation
{
    /// <summary>
    /// Runs <paramref name="action"/> <paramref name="warmup"/> times so one-time costs (JIT, caches, pool growth) are paid,
    /// then returns the bytes allocated by the calling thread across <paramref name="iterations"/> further runs.
    /// </summary>
    /// <param name="action">The work to measure. Create the delegate before calling so its own allocation is not counted.</param>
    /// <param name="warmup">Unmeasured runs first; at least 0.</param>
    /// <param name="iterations">Measured runs; at least 1.</param>
    /// <returns>Total bytes allocated by this thread during the measured runs.</returns>
    public static long Measure(Action action, int warmup = 3, int iterations = 1)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfNegative(warmup);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        for (int i = 0; i < warmup; i++)
        {
            action();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            action();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> naming the byte count unless <paramref name="action"/> allocates nothing once warmed.</summary>
    /// <param name="action">The work to measure.</param>
    /// <param name="what">A label for the failure message.</param>
    /// <param name="warmup">Unmeasured runs first.</param>
    /// <param name="iterations">Measured runs.</param>
    public static void AssertZero(Action action, string what, int warmup = 3, int iterations = 10)
    {
        long bytes = Measure(action, warmup, iterations);
        if (bytes != 0)
        {
            throw new InvalidOperationException($"{what} allocated {bytes} bytes over {iterations} runs; expected 0.");
        }
    }
}
