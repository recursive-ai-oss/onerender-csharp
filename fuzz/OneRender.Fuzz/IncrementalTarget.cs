using System;
using OneRender.Image;
using OneRender.TestSupport;

namespace OneRender.Fuzz;

/// <summary>
/// Random edit sequences on a live page, read from the input bytes (bit 0 of the first byte picks the calm profile, bit 1 the insert/remove churn profile, bit 2 the profile whose edits make style values no row holds and sweep the style table after every flush): after every edit the incrementally updated page must equal a
/// page built from scratch (styles, boxes, fragments, display list). A difference raises <see cref="InvalidOperationException"/>,
/// which is a finding like any other escaping exception.
/// </summary>
internal static class IncrementalTarget
{
    private const int MaxSteps = 48;

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return;
        }

        byte[] bytes = data.ToArray();
        bool calm = (bytes[0] & 1) != 0;
        bool churn = (bytes[0] & 2) != 0;
        bool fresh = (bytes[0] & 4) != 0;
        int size = 8 + (bytes[1] % 40);
        var session = new EditSession(FontProviders.CreateDefault(), new Chooser(bytes[2..]), size, calm, churn, fresh);
        for (int i = 0; i < MaxSteps && !session.Exhausted; i++)
        {
            session.Step();
        }
    }

    public static int WriteSeeds(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var rng = new Random(29);
        for (int i = 0; i < 32; i++)
        {
            byte[] b = new byte[200 + rng.Next(1400)];
            rng.NextBytes(b);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "seed" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)), b);
        }

        return 0;
    }
}
