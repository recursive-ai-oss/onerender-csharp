using BenchmarkDotNet.Attributes;

namespace OneRender.Benchmarks;

/// <summary>A placeholder that proves the benchmark project runs and reports allocations.</summary>
[MemoryDiagnoser]
public class SmokeBenchmarks
{
    [Benchmark]
    public int PackageName() => EngineInfo.Name.Length;
}
