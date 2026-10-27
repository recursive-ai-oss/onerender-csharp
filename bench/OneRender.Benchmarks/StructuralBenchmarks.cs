using BenchmarkDotNet.Attributes;
using OneRender.Image;
using OneRender.TestSupport;

namespace OneRender.Benchmarks;

/// <summary>
/// Structural edits on the same page: a floating layer of about fifty nodes inserted, removed and moved, and one frame of the churn
/// soak (a layer in, the last one out, a chat line in, the oldest out). Each row is one flush with its setup outside the timing,
/// so the number is the cost of that edit alone. Expected: well under 1 ms and 0 B.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Engines.RunStrategy.Monitoring, launchCount: 1, warmupCount: 300, iterationCount: 2000)]
public class StructuralBenchmarks
{
    private ChurnSoak _soak = null!;

    [GlobalSetup]
    public void Setup()
    {
        _soak = new ChurnSoak(FontProviders.CreateDefault(), fresh: false);
        for (int i = 0; i < 500; i++)
        {
            _soak.Frame();
        }
    }

    [IterationSetup(Target = nameof(InsertSubtree))]
    public void BeforeInsert() => _soak.RemoveLayer();

    [IterationSetup(Targets = [nameof(RemoveSubtree), nameof(MoveSubtree)])]
    public void BeforeRemove() => _soak.InsertLayer();

    /// <summary>Inserts a subtree of about fifty nodes.</summary>
    [Benchmark]
    public void InsertSubtree() => _soak.InsertLayer();

    /// <summary>Removes it again.</summary>
    [Benchmark]
    public void RemoveSubtree() => _soak.RemoveLayer();

    /// <summary>Moves it under another parent.</summary>
    [Benchmark]
    public void MoveSubtree() => _soak.MoveLayer();

    /// <summary>One frame of the soak: fifty nodes in, fifty out, one chat line in and one out.</summary>
    [Benchmark]
    public void ChurnFrame() => _soak.Frame();
}
