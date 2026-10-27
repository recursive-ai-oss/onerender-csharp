using OneRender.Style;
using OneRender.Tree;
using OneFont;

namespace OneRender.Layout;

/// <summary>Chooses fonts for computed styles. The host (or the fonts packages) implements it; the engine loads no font files itself.</summary>
public interface IFontProvider
{
    /// <summary>
    /// Returns the faces to use for a style, in preference order, as a fallback chain for shaping: the families of
    /// <c>font-family</c> matched against <c>font-weight</c>, <c>font-style</c> and <c>font-stretch</c> (CSS Fonts 4 §5.2), then
    /// the host's fallback faces. The result must be the same object for the same inputs while the font set does not change.
    /// </summary>
    /// <param name="style">The computed values of the box whose text is shaped.</param>
    /// <returns>A chain with at least one face; never null.</returns>
    FontFallbackChain GetFallbackChain(in ComputedValues style);
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
