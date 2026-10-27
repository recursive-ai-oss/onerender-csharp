```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-QNOOHR : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=40  IterationTime=200ms  LaunchCount=3  
WarmupCount=15  

```
| Method          | Mean            | Error         | StdDev        | Median          | Allocated |
|---------------- |----------------:|--------------:|--------------:|----------------:|----------:|
| IdleFrame       |       0.8908 ns |     0.0463 ns |     0.1457 ns |       0.8678 ns |         - |
| HoverRestyle    |   3,792.6423 ns |   103.2689 ns |   327.8451 ns |   3,690.4957 ns |         - |
| TextChange      |  42,781.5237 ns | 1,306.7217 ns | 4,052.3019 ns |  41,098.2489 ns |         - |
| SubtreeRelayout | 352,026.8469 ns | 2,759.7536 ns | 8,680.6928 ns | 353,914.2604 ns |         - |
