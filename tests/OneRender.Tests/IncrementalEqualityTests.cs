using System.IO;
using OneRender.Image;
using OneRender.TestSupport;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// Random edit sequences on random pages: after every edit the incrementally updated page must equal a page built from scratch
/// from the same content (styles, boxes, fragments, display list with ids).
/// </summary>
public class IncrementalEqualityTests
{
    private static string Run(int firstSeed, int sessions, int steps, int size, bool calm = false, bool churn = false)
    {
        long nothing = 0, paint = 0, patched = 0, rebuilt = 0, reused = 0, repainted = 0;
        var fonts = FontProviders.CreateDefault();
        for (int s = 0; s < sessions; s++)
        {
            int seed = firstSeed + s;
            var session = new EditSession(fonts, new Chooser(seed), size, calm, churn);
            session.CheckAgainstFresh();
            try
            {
                for (int i = 0; i < steps; i++)
                {
                    session.Step();
                }

                nothing += session.Stats.Nothing;
                paint += session.Stats.PaintOnly;
                patched += session.Stats.Patched;
                rebuilt += session.Stats.Rebuilt;
                reused += session.Stats.Reused;
                repainted += session.Page.PatchedPaints;
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidOperationException($"seed {seed}: {e.Message}", e);
            }
        }

        return $"nothing {nothing}, paint-only {paint}, patched {patched}, rebuilt {rebuilt}, reused subtrees {reused}, in-place paints {repainted}";
    }

    [Fact]
    public void TenThousandRandomEditsEqualAFreshBuild()
    {
        string stats = Run(1000, 50, 220, 36);
        Console.WriteLine("STATS " + stats);
        Assert.DoesNotContain("in-place paints 0", stats);
    }

    [Fact]
    public void LargerPagesWithFewerEdits()
    {
        string stats = Run(5000, 8, 120, 120);
        Console.WriteLine("STATS " + stats);
    }

    [Fact]
    public void CalmPagesReuseSubtreesAndStillEqualAFreshBuild()
    {
        string stats = Run(9000, 30, 200, 60, calm: true);
        Console.WriteLine("STATS calm " + stats);
        Assert.DoesNotContain("reused subtrees 0", stats);
    }

    // Slots of removed nodes are reused by the nodes inserted after them; none of that may show in the result.
    [Fact]
    public void HeavyInsertAndRemoveChurnEqualsAFreshBuild()
    {
        string stats = Run(20000, 40, 120, 30, churn: true);
        Console.WriteLine("STATS churn " + stats);
    }

    [Fact]
    public void ChurnOnCalmPagesEqualsAFreshBuild()
    {
        string stats = Run(21000, 20, 150, 80, calm: true, churn: true);
        Console.WriteLine("STATS calm churn " + stats);
        Assert.DoesNotContain("reused subtrees 0", stats);
    }

    // Inputs the fuzz target once failed on, replayed the way the target reads them.
    [Fact]
    public void SavedFuzzInputsEqualAFreshBuild()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "testdata", "fuzz");
        string[] files = Directory.GetFiles(dir, "incremental-*.bin");
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            byte[] bytes = File.ReadAllBytes(file);
            var session = new EditSession(FontProviders.CreateDefault(), new Chooser(bytes[2..]), 8 + (bytes[1] % 40), (bytes[0] & 1) != 0);
            for (int i = 0; i < 48 && !session.Exhausted; i++)
            {
                session.Step();
            }
        }
    }
}
