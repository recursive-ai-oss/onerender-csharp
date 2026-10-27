```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-ZMWOCB : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-TGYSTY : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=40  IterationTime=200ms  LaunchCount=3  
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=15  

```
| Method        | Job        | InvocationCount | Mean     | Error    | StdDev    | Allocated |
|-------------- |----------- |---------------- |---------:|---------:|----------:|----------:|
| InsertSubtree | Job-ZMWOCB | 1               | 550.7 μs | 26.67 μs |  86.60 μs |         - |
| RemoveSubtree | Job-ZMWOCB | 1               | 400.7 μs | 16.44 μs |  53.36 μs |         - |
| MoveSubtree   | Job-ZMWOCB | 1               | 670.8 μs | 20.95 μs |  68.01 μs |         - |
| ChurnFrame    | Job-TGYSTY | Default         | 713.6 μs | 40.97 μs | 133.02 μs |         - |
