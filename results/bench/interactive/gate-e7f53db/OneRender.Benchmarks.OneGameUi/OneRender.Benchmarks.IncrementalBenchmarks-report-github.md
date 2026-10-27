```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-QNOOHR : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=40  IterationTime=200ms  LaunchCount=3  
WarmupCount=15  

```
| Method          | Mean            | Error         | StdDev         | Allocated |
|---------------- |----------------:|--------------:|---------------:|----------:|
| IdleFrame       |       0.9687 ns |     0.0465 ns |      0.1509 ns |         - |
| HoverRestyle    |   3,780.0548 ns |    92.2034 ns |    299.3444 ns |         - |
| TextChange      |  44,589.1095 ns | 1,095.5197 ns |  3,541.0644 ns |         - |
| SubtreeRelayout | 383,677.8409 ns | 8,968.2567 ns | 28,730.9243 ns |         - |
