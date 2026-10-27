using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OneRender.Fuzz;

/// <summary>
/// Fuzz harness for the style value parsers, the styled-node tree and the box tree. Usage:
/// <c>OneRender.Fuzz.Values &lt;target&gt;</c> runs under SharpFuzz reading inputs from stdin (libFuzzer driver mode);
/// <c>&lt;target&gt; &lt;file&gt;</c> runs one input (to replay a crash);
/// <c>&lt;target&gt; --mutate &lt;seconds&gt; &lt;seed-dir&gt;</c> is a coverage-blind mutation loop for machines without a driver.
/// A target must never throw.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Under a coverage-guided driver nothing may touch the library before the first input.
        if (args.Length == 1 && args[0] != "--list")
        {
            string name = args[0];
            SharpFuzz.Fuzzer.LibFuzzer.Run(span => Targets.All[name](span));
            return 0;
        }

        if (args.Length < 2 || !Targets.All.TryGetValue(args[0], out Action<ReadOnlySpan<byte>>? target))
        {
            Console.Error.WriteLine("usage: OneRender.Fuzz.Values <target> <file> | <target> --mutate <seconds> <seed-dir> | <target>");
            Console.Error.WriteLine("targets: " + string.Join(", ", Targets.All.Keys));
            return 2;
        }

        if (args.Length == 4 && args[1] == "--mutate")
        {
            return Mutate(target, int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3]);
        }

        target(File.ReadAllBytes(args[1]));
        return 0;
    }

    private static int Mutate(Action<ReadOnlySpan<byte>> target, int seconds, string seedDir)
    {
        var seeds = new List<byte[]>();
        foreach (string f in Directory.EnumerateFiles(seedDir, "*", SearchOption.AllDirectories))
        {
            byte[] b = File.ReadAllBytes(f);
            if (b.Length <= 100_000)
            {
                seeds.Add(b);
            }
        }

        if (seeds.Count == 0)
        {
            Console.Error.WriteLine("no seeds");
            return 2;
        }

        var rng = new Random(12345);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long runs = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            byte[] input = (byte[])seeds[rng.Next(seeds.Count)].Clone();
            int edits = 1 + rng.Next(8);
            for (int e = 0; e < edits; e++)
            {
                int at = input.Length == 0 ? 0 : rng.Next(input.Length);
                switch (rng.Next(3))
                {
                    case 0 when input.Length > 0:
                        input[at] = (byte)rng.Next(256);
                        break;
                    case 1:
                        input = Splice(input, at, [(byte)rng.Next(256)], 0);
                        break;
                    default:
                        input = Splice(input, at, [], Math.Min(rng.Next(4), input.Length - at));
                        break;
                }
            }

            try
            {
                target(input);
            }
            catch (Exception ex)
            {
                string path = "crash-" + runs.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bin";
                File.WriteAllBytes(path, input);
                Console.Error.WriteLine(ex);
                return 1;
            }

            runs++;
        }

        Console.WriteLine("runs: " + runs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return 0;
    }

    private static byte[] Splice(byte[] input, int at, byte[] insert, int remove)
    {
        var r = new byte[input.Length - remove + insert.Length];
        Array.Copy(input, 0, r, 0, at);
        Array.Copy(insert, 0, r, at, insert.Length);
        Array.Copy(input, at + remove, r, at + insert.Length, input.Length - at - remove);
        return r;
    }
}
