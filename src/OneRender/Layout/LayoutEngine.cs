using OneRender.Style;
using OneRender.Tree;
using OneRender.Text;

namespace OneRender.Layout;

/// <summary>
/// The text service layout calls: it turns computed font properties into a matched font list, shaped runs and metrics.
/// <see cref="TextFontProvider"/> is the engine's implementation, built on <see cref="FontCatalog"/> and <see cref="TextShaper"/>;
/// a host can wrap or replace it. Providers are single-thread objects and, for the same inputs while the font set does not
/// change, return the same objects and allocate nothing.
/// </summary>
public interface IFontProvider
{
    /// <summary>The faces and diagnostics behind the provider (add fonts, <c>@font-face</c> rules, read missing-glyph diagnostics).</summary>
    FontCatalog Catalog { get; }

    /// <summary>
    /// Matches a style (CSS Fonts 4 §5.2): the families of <c>font-family</c> against <c>font-weight</c>, <c>font-style</c> and
    /// <c>font-stretch</c>, then the default faces. The list also answers per-character fallback.
    /// </summary>
    /// <param name="style">The computed values of the box whose text is shaped.</param>
    /// <returns>A list with at least one face when any face is registered; never null.</returns>
    ResolvedFontList Resolve(in ComputedValues style);

    /// <summary>Shapes text for a style into a (cached) glyph run; see <see cref="TextShaper.Shape"/>.</summary>
    /// <param name="text">The text of one bidi level run, already white-space processed.</param>
    /// <param name="style">The computed values of the box.</param>
    /// <param name="rightToLeft">True for a right-to-left run.</param>
    /// <returns>A run owned by the provider's cache; do not modify it.</returns>
    ShapedText Shape(ReadOnlySpan<char> text, in ComputedValues style, bool rightToLeft = false);

    /// <summary>Metrics of the primary face of a style at its <c>font-size</c>, in px.</summary>
    /// <param name="style">The computed values.</param>
    /// <returns>The metrics, or the default value when no face exists.</returns>
    FontMetricsInfo GetMetrics(in ComputedValues style);
}

/// <summary>What a layout is given besides the box tree.</summary>
public readonly struct LayoutInput
{
    /// <summary>Creates layout input.</summary>
    /// <param name="availableWidth">The width of the initial containing block in px (the viewport width for screen layout).</param>
    /// <param name="availableHeight">The height of the initial containing block in px, or <see cref="double.NaN"/> when it is not limited.</param>
    /// <param name="fonts">The font provider.</param>
    public LayoutInput(double availableWidth, double availableHeight, IFontProvider fonts)
    {
        AvailableWidth = availableWidth;
        AvailableHeight = availableHeight;
        Fonts = fonts;
    }

    /// <summary>The width of the initial containing block in px.</summary>
    public double AvailableWidth { get; }

    /// <summary>The height of the initial containing block in px; NaN means the height is determined by the content (the page grows) and percentage heights resolve against nothing.</summary>
    public double AvailableHeight { get; }

    /// <summary>The font provider.</summary>
    public IFontProvider Fonts { get; }

    /// <summary>
    /// When true, an engine that supports it takes subtrees whose boxes did not change (see the box tree's dirty marks) from the
    /// layout it produced last time instead of laying them out again, and writes its result by replacing the contents of the output
    /// tree. The result is the same either way. Default false.
    /// </summary>
    public bool Reuse { get; init; }
}

/// <summary>
/// A layout algorithm for a whole box tree. Block and inline flow, floats, positioning, flex, grid, tables and
/// fragmentation each contribute to an implementation of this interface; the contract is the same for all of them.
/// </summary>
public interface ILayoutEngine
{
    /// <summary>
    /// Lays out <paramref name="boxes"/> and writes the result into <paramref name="output"/>. The implementation calls
    /// <see cref="FragmentTree.Clear"/> first, produces a fragment for the root box and for every box that generates one
    /// (nothing for boxes with <c>display: none</c>, which have no box), appends children in tree order, and reads
    /// nothing but <paramref name="boxes"/> (with its styled tree and style table) and <paramref name="input"/>.
    /// The result is a pure function of those inputs: same boxes, same fonts, same available size, same fragments.
    /// </summary>
    /// <param name="boxes">The built box tree.</param>
    /// <param name="input">Available size and fonts.</param>
    /// <param name="output">Receives the fragments; it must belong to <paramref name="boxes"/>.</param>
    /// <param name="cancellationToken">Checked at least once per few hundred boxes.</param>
    /// <exception cref="ArgumentException"><paramref name="output"/> was created for another box tree.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; <paramref name="output"/> is partly filled.</exception>
    void Layout(BoxTree boxes, in LayoutInput input, FragmentTree output, CancellationToken cancellationToken = default);
}
