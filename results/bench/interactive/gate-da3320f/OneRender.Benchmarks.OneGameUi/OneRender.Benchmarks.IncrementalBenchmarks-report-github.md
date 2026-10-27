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
| IdleFrame       |       0.7586 ns |     0.0122 ns |     0.0383 ns |       0.7437 ns |         - |
| HoverRestyle    |   3,401.2411 ns |    20.4912 ns |    61.0569 ns |   3,380.1158 ns |         - |
| TextChange      |  38,973.3879 ns |   242.8405 ns |   734.7826 ns |  38,764.8650 ns |         - |
| SubtreeRelayout | 334,314.6118 ns | 2,246.2455 ns | 6,727.7686 ns | 333,303.9792 ns |         - |
