namespace OneRender.Layout;

/// <summary>
/// The 1/64 px grid the reference browser keeps layout geometry on. Lengths that come out of a computation (a line height of
/// 1.6 × 16 px, say) are cut to this grid before they are added up, so that sums of many such lengths agree with it to the last
/// bit instead of drifting by a fraction of a pixel per line. The grid is a convention; see <c>docs/layout-conventions.md</c>.
/// </summary>
internal static class LayoutUnits
{
    private const double Scale = 64;

    /// <summary>Rounds toward negative infinity to a multiple of 1/64 px.</summary>
    /// <param name="v">A length in px.</param>
    /// <returns>The largest multiple of 1/64 that is not above <paramref name="v"/>.</returns>
    internal static double Floor(double v) => Math.Floor(v * Scale) / Scale;
}
