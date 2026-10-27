using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(OneRender.Benchmarks.OneGameUiBenchmarks).Assembly).Run(args);
