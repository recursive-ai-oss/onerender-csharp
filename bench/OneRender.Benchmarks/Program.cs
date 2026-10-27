using BenchmarkDotNet.Running;
if (args.Length > 1 && args[0] == "loop") { OneRender.Benchmarks.StageProbe.Loop(args[1]); return; }
if (args.Length > 0 && args[0] == "probe") { OneRender.Benchmarks.StageProbe.Run(); return; }
BenchmarkSwitcher.FromAssembly(typeof(OneRender.Benchmarks.SmokeBenchmarks).Assembly).Run(args);
