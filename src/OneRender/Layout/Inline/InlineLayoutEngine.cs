using System.Threading;
using OneRender.Tree;

namespace OneRender.Layout;

/// <summary>
/// CSS inline layout: the engine behind <see cref="IInlineLayout"/> that the block layout uses for every block container whose
/// children are inline-level. It follows CSS Text 3 (white space, line breaking, alignment, spacing), CSS2 §10.8 and CSS Inline 3
/// (line box height, baselines, <c>vertical-align</c>), and CSS Writing Modes 3 with UAX #9 for bidirectional text.
/// See "Inline layout" in <c>docs/architecture.md</c> for the algorithm and its limits.
/// </summary>
/// <remarks>
/// One instance serves one thread at a time. Block layout re-enters it while an atomic inline (an inline-block, for example) is laid
/// out inside a line, so the paragraph state lives in workers kept per nesting level and reused; laying out an unchanged tree again
/// allocates nothing once the workers and the font service's caches are warm.
/// </remarks>
public sealed class InlineLayoutEngine : IInlineLayout, IAtomicInlineHostAware
{
    // The block layout nests atomic inlines to at most 32 levels; one more worker per level plus one for the measuring pass.
    private const int MaxDepth = 40;

    private readonly InlineWorker?[] _workers = new InlineWorker?[MaxDepth];
    private IAtomicInlineHost? _host;
    private int _depth;

    /// <summary>Creates an inline layout. Pass it to <see cref="BlockLayoutEngine"/>, which supplies the atomic-inline services.</summary>
    public InlineLayoutEngine()
    {
    }

    /// <inheritdoc />
    public void SetHost(IAtomicInlineHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <inheritdoc />
    public InlineLayoutResult Layout(in InlineLayoutRequest request, FragmentTree output, Fragment parent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_depth >= MaxDepth)
        {
            return new InlineLayoutResult(0, 0, double.NaN, double.NaN);
        }

        InlineWorker worker = _workers[_depth] ??= new InlineWorker();
        _depth++;
        try
        {
            return worker.Layout(in request, output, parent, _host, cancellationToken);
        }
        finally
        {
            _depth--;
        }
    }

    /// <inheritdoc />
    public void Measure(Box container, IFontProvider fonts, out double minContent, out double maxContent)
    {
        minContent = 0;
        maxContent = 0;
        if (container.IsNull || fonts is null || _depth >= MaxDepth)
        {
            return;
        }

        InlineWorker worker = _workers[_depth] ??= new InlineWorker();
        _depth++;
        try
        {
            worker.Measure(container, fonts, _host, out minContent, out maxContent);
        }
        finally
        {
            _depth--;
        }
    }
}
