```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-QNOOHR : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=40  IterationTime=200ms  LaunchCount=3  
WarmupCount=15  

```
| Method                        | Mean           | Error          | StdDev         | Allocated |
|------------------------------ |---------------:|---------------:|---------------:|----------:|
| CompositorTick100             |  29,655.180 ns |    540.4605 ns |  1,731.4323 ns |         - |
| MixedTick100Compositor4Layout | 311,012.942 ns | 11,055.8656 ns | 35,893.5870 ns |         - |
| IdleAdvance                   |       7.903 ns |      0.2702 ns |      0.8695 ns |         - |
| WidthTransitionTick           |  64,592.926 ns |  1,399.6603 ns |  4,504.1088 ns |         - |
