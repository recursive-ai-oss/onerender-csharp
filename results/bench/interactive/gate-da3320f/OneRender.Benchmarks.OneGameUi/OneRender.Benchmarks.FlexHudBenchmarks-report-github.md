```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-QNOOHR : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=40  IterationTime=200ms  LaunchCount=3  
WarmupCount=15  

```
| Method           | Mean            | Error         | StdDev         | Median          | Allocated |
|----------------- |----------------:|--------------:|---------------:|----------------:|----------:|
| IdleFrame        |       0.8252 ns |     0.0316 ns |      0.0980 ns |       0.7685 ns |         - |
| HoverRestyle     |   3,562.3198 ns |    16.2318 ns |     50.3367 ns |   3,546.9409 ns |         - |
| HoverWithHitTest |  10,662.1087 ns |    49.5957 ns |    160.3089 ns |  10,741.2278 ns |         - |
| TextChange       |  42,929.1287 ns | 1,535.6870 ns |  4,739.4374 ns |  39,567.9153 ns |         - |
| SubtreeRelayout  | 307,788.8372 ns | 8,244.3078 ns | 26,411.6641 ns | 296,238.7119 ns |         - |
