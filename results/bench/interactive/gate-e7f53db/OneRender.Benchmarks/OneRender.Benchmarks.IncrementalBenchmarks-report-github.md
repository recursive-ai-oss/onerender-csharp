```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-QNOOHR : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=40  IterationTime=200ms  LaunchCount=3  
WarmupCount=15  

```
| Method          | Mean            | Error          | StdDev         | Median          | Allocated |
|---------------- |----------------:|---------------:|---------------:|----------------:|----------:|
| IdleFrame       |       0.7399 ns |      0.0062 ns |      0.0197 ns |       0.7366 ns |         - |
| HoverRestyle    |   3,146.2985 ns |      8.6815 ns |     27.1797 ns |   3,158.7729 ns |         - |
| TextChange      |  46,122.5843 ns |  1,819.7847 ns |  5,908.0494 ns |  45,372.2213 ns |         - |
| SubtreeRelayout | 405,141.1661 ns | 16,658.3988 ns | 53,845.1879 ns | 401,391.3250 ns |         - |
