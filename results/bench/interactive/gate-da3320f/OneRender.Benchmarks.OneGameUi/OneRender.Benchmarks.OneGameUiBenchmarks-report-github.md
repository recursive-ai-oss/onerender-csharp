```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-QNOOHR : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=40  IterationTime=200ms  LaunchCount=3  
WarmupCount=15  

```
| Method          | Mean      | Error     | StdDev    | Median    | Allocated |
|---------------- |----------:|----------:|----------:|----------:|----------:|
| IdleFrame       |  1.900 μs | 0.0030 μs | 0.0094 μs |  1.900 μs |         - |
| HoverRestyle    | 61.812 μs | 0.3489 μs | 1.0975 μs | 61.493 μs |    4424 B |
| TextChange      | 57.442 μs | 0.0477 μs | 0.1479 μs | 57.428 μs |    4264 B |
| SubtreeRelayout | 79.286 μs | 1.1251 μs | 3.4385 μs | 78.099 μs |    4304 B |
