using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(OneRender.Benchmarks.SmokeBenchmarks).Assembly).Run(args);
