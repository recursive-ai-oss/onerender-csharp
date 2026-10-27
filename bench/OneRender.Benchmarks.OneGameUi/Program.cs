using BenchmarkDotNet.Running;

// --verify-scene: build the flex-only page through OneGame.UI and through the engine, print how they compare and exit (non-zero when they differ).
if (args.Length == 1 && args[0] == "--verify-scene")
{
    OneRender.Benchmarks.FlexHudBenchmarks.VerifyEquivalence(System.Console.Out);
    return 0;
}

BenchmarkSwitcher.FromAssembly(typeof(OneRender.Benchmarks.OneGameUiBenchmarks).Assembly).Run(args);
return 0;
