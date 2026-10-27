```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-QNOOHR : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=40  IterationTime=200ms  LaunchCount=3  
WarmupCount=15  

```
| Method                        | Mean           | Error       | StdDev        | Median         | Allocated |
|------------------------------ |---------------:|------------:|--------------:|---------------:|----------:|
| CompositorTick100             |  29,031.152 ns | 267.0674 ns |   828.2083 ns |  29,067.411 ns |         - |
| MixedTick100Compositor4Layout | 243,005.851 ns | 721.1713 ns | 2,182.1074 ns | 242,553.484 ns |         - |
| IdleAdvance                   |       7.442 ns |   0.1886 ns |     0.5960 ns |       7.204 ns |         - |
