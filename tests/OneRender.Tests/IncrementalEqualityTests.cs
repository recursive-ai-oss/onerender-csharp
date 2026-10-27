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
    private static string Run(int firstSeed, int sessions, int steps, int size, bool calm = false)
    {
        long nothing = 0, paint = 0, patched = 0, rebuilt = 0, reused = 0;
        var fonts = FontProviders.CreateDefault();
        for (int s = 0; s < sessions; s++)
        {
            int seed = firstSeed + s;
            var session = new EditSession(fonts, new Chooser(seed), size, calm);
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
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidOperationException($"seed {seed}: {e.Message}", e);
            }
        }

        return $"nothing {nothing}, paint-only {paint}, patched {patched}, rebuilt {rebuilt}, reused subtrees {reused}";
    }

    [Fact]
    public void TenThousandRandomEditsEqualAFreshBuild()
    {
        string stats = Run(1000, 50, 220, 36);
        Console.WriteLine("STATS " + stats);
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
}
