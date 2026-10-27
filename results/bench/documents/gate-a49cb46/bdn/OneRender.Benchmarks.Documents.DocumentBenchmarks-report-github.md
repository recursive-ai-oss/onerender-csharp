```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-FPQYQY : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=15  LaunchCount=2  
UnrollFactor=1  WarmupCount=3  

```
| Method | Document | Mean        | Error      | StdDev     | Gen0       | Gen1      | Gen2      | Allocated |
|------- |--------- |------------:|-----------:|-----------:|-----------:|----------:|----------:|----------:|
| **ToPdf**  | **invoice**  |    **31.30 ms** |   **2.848 ms** |   **4.084 ms** |          **-** |         **-** |         **-** |   **8.07 MB** |
| **ToPdf**  | **report**   |   **146.86 ms** |  **29.204 ms** |  **42.807 ms** |  **2000.0000** | **2000.0000** | **2000.0000** |  **51.46 MB** |
| **ToPdf**  | **table10k** | **1,642.58 ms** | **202.374 ms** | **290.239 ms** | **10000.0000** | **5000.0000** | **4000.0000** | **838.25 MB** |
| **ToPdf**  | **tailwind** |   **101.42 ms** |   **5.536 ms** |   **7.940 ms** |          **-** |         **-** |         **-** |  **33.82 MB** |
