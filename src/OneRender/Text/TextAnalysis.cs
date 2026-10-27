using OneFont;

namespace OneRender.Text;

/// <summary>A stretch of a paragraph at one bidi embedding level (UAX #9), in logical order.</summary>
/// <param name="Start">The first UTF-16 offset.</param>
/// <param name="Length">The number of UTF-16 code units.</param>
/// <param name="Level">The embedding level; odd levels are right to left.</param>
public readonly record struct BidiRun(int Start, int Length, int Level)
{
    /// <summary>Whether the run is right to left (an odd level).</summary>
    public bool RightToLeft => (Level & 1) != 0;
}

/// <summary>
/// Text analysis the inline layout needs around shaping, taken from OneFont: bidi levels (UAX #9), line break opportunities
/// (UAX #14) and grapheme cluster boundaries (UAX #29). All methods work in the caller's buffers.
/// </summary>
public static class TextAnalysis
{
    /// <summary>
    /// Resolves the bidi levels of a paragraph and appends its runs of equal level, in logical order. Shape each run with
    /// <see cref="TextShaper"/> using <see cref="BidiRun.RightToLeft"/>, then order the runs of a line with
    /// <see cref="GetVisualOrder"/>.
    /// </summary>
    /// <param name="text">The paragraph.</param>
    /// <param name="direction">The base direction from <c>direction</c>; <see cref="TextDirection.Auto"/> takes it from the first strong character (<c>unicode-bidi: plaintext</c>).</param>
    /// <param name="runs">Receives the runs.</param>
    /// <param name="levels">A scratch buffer for one level per code unit, grown by this method when too short; pass null to allocate one.</param>
    /// <param name="paragraphLevel">The base level, 0 or 1.</param>
    /// <exception cref="ArgumentException">The text is longer than <see cref="TextShaping.MaxRunLength"/>.</exception>
    public static void GetBidiRuns(ReadOnlySpan<char> text, TextDirection direction, List<BidiRun> runs, ref byte[]? levels, out int paragraphLevel)
    {
        ArgumentNullException.ThrowIfNull(runs);
        paragraphLevel = direction == TextDirection.RightToLeft ? 1 : 0;
        if (text.Length == 0)
        {
            return;
        }

        if (levels is null || levels.Length < text.Length)
        {
            levels = new byte[text.Length];
        }

        TextShaping.GetBidiLevels(text, direction, levels, out paragraphLevel);
        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || levels[i] != levels[start])
            {
                runs.Add(new BidiRun(start, i - start, levels[start]));
                start = i;
            }
        }
    }

    /// <summary>
    /// Gives the left-to-right display order of the runs of one line (rule L2).
    /// </summary>
    /// <param name="runs">The line's runs in logical order.</param>
    /// <param name="order">Receives the indexes of <paramref name="runs"/> in display order; at least as long as <paramref name="runs"/>.</param>
    /// <param name="levelScratch">A scratch buffer at least as long as <paramref name="runs"/>.</param>
    public static void GetVisualOrder(ReadOnlySpan<BidiRun> runs, Span<int> order, Span<byte> levelScratch)
    {
        if (order.Length < runs.Length || levelScratch.Length < runs.Length)
        {
            throw new ArgumentException("A buffer is shorter than the runs.");
        }

        for (int i = 0; i < runs.Length; i++)
        {
            levelScratch[i] = (byte)Math.Min(runs[i].Level, 255);
        }

        TextShaping.GetVisualOrder(levelScratch[..runs.Length], order);
    }

    /// <summary>
    /// Fills the UAX #14 line break opportunities of a text: entry <c>i</c> says whether a line may (or must) end before the
    /// code unit at offset <c>i</c>; the last entry is the end of the text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="opportunities">Receives <c>text.Length + 1</c> entries.</param>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static void GetLineBreakOpportunities(ReadOnlySpan<char> text, Span<LineBreakOpportunity> opportunities) =>
        TextSegmentation.GetLineBreakOpportunities(text, opportunities);

    /// <summary>
    /// Marks the starts of grapheme clusters (UAX #29): entry <c>i</c> is 1 when a cluster starts at offset <c>i</c>, and the
    /// last entry (the end of the text) is always 1.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="starts">Receives <c>text.Length + 1</c> entries.</param>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    public static void GetGraphemeStarts(ReadOnlySpan<char> text, Span<byte> starts) =>
        TextSegmentation.FillGraphemeStarts(text, starts);
}
