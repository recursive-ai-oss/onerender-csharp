```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-QNOOHR : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=40  IterationTime=200ms  LaunchCount=3  
WarmupCount=15  

```
| Method          | Mean      | Error     | StdDev    | Allocated |
|---------------- |----------:|----------:|----------:|----------:|
| IdleFrame       |  2.136 μs | 0.0385 μs | 0.1244 μs |         - |
| HoverRestyle    | 65.683 μs | 1.4103 μs | 4.5585 μs |    4424 B |
| TextChange      | 64.016 μs | 1.3777 μs | 4.4729 μs |    4264 B |
| SubtreeRelayout | 86.003 μs | 1.9388 μs | 6.2946 μs |    4304 B |
