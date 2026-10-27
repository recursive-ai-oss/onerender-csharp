```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-QNOOHR : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=40  IterationTime=200ms  LaunchCount=3  
WarmupCount=15  

```
| Method           | Mean           | Error         | StdDev         | Allocated |
|----------------- |---------------:|--------------:|---------------:|----------:|
| IdleFrame        |       1.002 ns |     0.0369 ns |      0.1192 ns |         - |
| HoverRestyle     |   3,874.493 ns |    99.1404 ns |    321.8658 ns |         - |
| HoverWithHitTest |  12,589.791 ns |   173.0442 ns |    559.3332 ns |         - |
| TextChange       |  46,059.128 ns | 1,104.6607 ns |  3,586.3527 ns |         - |
| SubtreeRelayout  | 341,692.383 ns | 7,002.7024 ns | 22,734.7290 ns |         - |
