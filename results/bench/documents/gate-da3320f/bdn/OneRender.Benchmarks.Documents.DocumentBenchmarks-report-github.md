```

BenchmarkDotNet v0.15.8, Linux Linux Mint 22.3 (Zena)
AMD Ryzen 9 7950X3D 2.99GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-FPQYQY : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=15  LaunchCount=2  
UnrollFactor=1  WarmupCount=3  

```
| Method | Document | Mean        | Error     | StdDev    | Gen0       | Gen1      | Gen2      | Allocated |
|------- |--------- |------------:|----------:|----------:|-----------:|----------:|----------:|----------:|
| **ToPdf**  | **invoice**  |    **20.76 ms** |  **3.006 ms** |  **4.312 ms** |          **-** |         **-** |         **-** |   **8.05 MB** |
| **ToPdf**  | **report**   |   **126.85 ms** | **19.087 ms** | **28.569 ms** |  **2000.0000** | **2000.0000** | **2000.0000** |  **51.37 MB** |
| **ToPdf**  | **table10k** | **1,250.42 ms** | **40.280 ms** | **56.467 ms** | **10000.0000** | **6000.0000** | **4000.0000** | **835.43 MB** |
| **ToPdf**  | **tailwind** |   **180.04 ms** | **44.178 ms** | **66.124 ms** |          **-** |         **-** |         **-** |  **84.69 MB** |
