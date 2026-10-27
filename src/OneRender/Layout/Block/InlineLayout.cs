using System.Threading;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

/// <summary>What the block layout tells an inline layout about the block container whose inline-level children it must lay out.</summary>
public readonly struct InlineLayoutRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="container">The block container; its <see cref="Box.ChildLayout"/> is <see cref="ChildLayout.Inline"/>.</param>
    /// <param name="availableWidth">The content-box width in px lines may fill.</param>
    /// <param name="contentLeft">Offset of the content box's left edge from the container fragment's border-box left edge.</param>
    /// <param name="contentTop">Offset of the content box's top edge from the container fragment's border-box top edge.</param>
    /// <param name="bfcLeft">Inline offset of the content box's left edge in the content box of the block formatting context root, for float exclusions.</param>
    /// <param name="bfcTop">Block offset of the content box's top edge in the content box of the block formatting context root, for float exclusions.</param>
    /// <param name="fonts">The font provider of the layout.</param>
    public InlineLayoutRequest(Box container, double availableWidth, double contentLeft, double contentTop, double bfcLeft, double bfcTop, IFontProvider fonts)
    {
        Container = container;
        AvailableWidth = availableWidth;
        ContentLeft = contentLeft;
        ContentTop = contentTop;
        BfcLeft = bfcLeft;
        BfcTop = bfcTop;
        Fonts = fonts;
    }

    /// <summary>The block container; its <see cref="Box.ChildLayout"/> is <see cref="ChildLayout.Inline"/>.</summary>
    public Box Container { get; }

    /// <summary>The content-box width in px that lines may fill.</summary>
    public double AvailableWidth { get; }

    /// <summary>Offset of the content box's left edge from the container fragment's border-box left edge. Line fragments are added to the container fragment in its border-box coordinates, so they start here.</summary>
    public double ContentLeft { get; }

    /// <summary>Offset of the content box's top edge from the container fragment's border-box top edge; the first line box starts here.</summary>
    public double ContentTop { get; }

    /// <summary>Inline offset of the content box's left edge in the content box of the block formatting context root. Float exclusions, when they exist, are expressed in these coordinates.</summary>
    public double BfcLeft { get; }

    /// <summary>Block offset of the content box's top edge in the content box of the block formatting context root.</summary>
    public double BfcTop { get; }

    /// <summary>The font provider of the layout.</summary>
    public IFontProvider Fonts { get; }
}

/// <summary>What an inline layout reports back about the lines it produced.</summary>
public readonly struct InlineLayoutResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="height">Total height of the line boxes in px (0 when there are none).</param>
    /// <param name="lineCount">Number of line boxes produced.</param>
    /// <param name="firstBaseline">Baseline of the first line, measured from the top of the content box; NaN when there is no line.</param>
    /// <param name="lastBaseline">Baseline of the last line, measured from the top of the content box; NaN when there is no line.</param>
    public InlineLayoutResult(double height, int lineCount, double firstBaseline, double lastBaseline)
    {
        Height = height;
        LineCount = lineCount;
        FirstBaseline = firstBaseline;
        LastBaseline = lastBaseline;
    }

    /// <summary>Total height of the line boxes in px.</summary>
    public double Height { get; }

    /// <summary>Number of line boxes produced.</summary>
    public int LineCount { get; }

    /// <summary>Baseline of the first line from the top of the content box, or NaN.</summary>
    public double FirstBaseline { get; }

    /// <summary>Baseline of the last line from the top of the content box, or NaN.</summary>
    public double LastBaseline { get; }
}

/// <summary>
/// The seam between block layout and inline layout. A block container whose children are inline-level hands them to an
/// implementation of this interface, which owns line boxes, line breaking and the inline-level boxes inside them.
/// Block layout owns everything outside: the container's own size and position, margin collapsing, and the stack of block
/// siblings. Implementations must not allocate on the steady-state path and must be deterministic.
/// </summary>
public interface IInlineLayout
{
    /// <summary>
    /// Lays out the inline-level children of <see cref="InlineLayoutRequest.Container"/> into lines. Adds a
    /// <see cref="FragmentKind.Line"/> fragment (and its descendants) as a child of <paramref name="parent"/>, which is the
    /// container's fragment, with coordinates relative to the container's border box.
    /// </summary>
    /// <param name="request">The container and the space available.</param>
    /// <param name="output">Receives the fragments.</param>
    /// <param name="parent">The container's fragment.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The size of the lines.</returns>
    InlineLayoutResult Layout(in InlineLayoutRequest request, FragmentTree output, Fragment parent, CancellationToken cancellationToken);

    /// <summary>
    /// Measures the intrinsic inline sizes of the container's inline content (CSS Sizing 3 §5): the width it needs when it
    /// breaks at every opportunity (min-content) and when it never breaks (max-content), as content-box widths.
    /// </summary>
    /// <param name="container">The block container whose children are inline-level.</param>
    /// <param name="fonts">The font provider of the layout.</param>
    /// <param name="minContent">Receives the min-content width in px.</param>
    /// <param name="maxContent">Receives the max-content width in px.</param>
    void Measure(Box container, IFontProvider fonts, out double minContent, out double maxContent);
}

/// <summary>
/// A stand-in inline layout that exists until real inline formatting lands. It does no line breaking and shapes nothing: a
/// container that holds any non-blank text gets one empty line box as tall as its <c>line-height</c> (<c>normal</c> is 1.2 times
/// the font size), spanning the available width, and every intrinsic inline size is 0.
/// </summary>
public sealed class PlaceholderInlineLayout : IInlineLayout
{
    /// <inheritdoc />
    public InlineLayoutResult Layout(in InlineLayoutRequest request, FragmentTree output, Fragment parent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        Box container = request.Container;
        if (!HasText(container))
        {
            return new InlineLayoutResult(0, 0, double.NaN, double.NaN);
        }

        double height = LineHeight(in container.Values);
        double baseline = height * 0.8;
        output.AddLine(parent, new Rect(request.ContentLeft, request.ContentTop, request.AvailableWidth, height), baseline);
        return new InlineLayoutResult(height, 1, baseline, baseline);
    }

    /// <inheritdoc />
    public void Measure(Box container, IFontProvider fonts, out double minContent, out double maxContent)
    {
        minContent = 0;
        maxContent = 0;
    }

    /// <summary>The used <c>line-height</c> of a style in px; <c>normal</c> is 1.2 times the font size.</summary>
    /// <param name="v">The computed style.</param>
    /// <returns>The line height in px.</returns>
    internal static double LineHeight(in ComputedValues v)
    {
        Length lh = v.LineHeight;
        return lh.Kind switch
        {
            LengthKind.Number => lh.Value * v.FontSize,
            LengthKind.Px => lh.Value,
            LengthKind.Percent => lh.Value / 100 * v.FontSize,
            LengthKind.Calc => lh.Resolve(v.FontSize),
            _ => v.FontSize * 1.2,
        };
    }

    private static bool HasText(Box container)
    {
        // Walk the inline-level descendants with the sibling and parent links; no stack is needed.
        Box b = container.FirstChild;
        while (!b.IsNull)
        {
            if (b.Kind == BoxKind.Text)
            {
                foreach (char ch in b.Text)
                {
                    if (!(ch is ' ' or '\t' or '\n' or '\r' or '\f'))
                    {
                        return true;
                    }
                }
            }
            else if (b.Kind is BoxKind.Replaced or BoxKind.LineBreak || (b.Kind == BoxKind.Container && b.Level == BoxLevel.Inline))
            {
                return true;
            }

            Box next = b.Kind == BoxKind.Inline ? b.FirstChild : default;
            while (next.IsNull)
            {
                next = b.NextSibling;
                if (!next.IsNull)
                {
                    break;
                }

                b = b.Parent;
                if (b.IsNull || b == container)
                {
                    return false;
                }
            }

            b = next;
        }

        return false;
    }
}
