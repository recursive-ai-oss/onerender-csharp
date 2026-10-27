using OneRender.Layout;
using OneRender.Tree;

namespace OneRender;

/// <summary>
/// The retained front half of the pipeline: brings boxes and fragments up to date with the pending changes of a styled tree.
/// <see cref="LivePage"/> adds painting on top; the HTML page uses it directly.
/// </summary>
internal sealed class PipelineUpdater
{
    private readonly BlockLayoutEngine _engine;
    private readonly IFontProvider _fonts;
    private bool _dirtyAll;
    private bool _boxesStale;

    public PipelineUpdater(StyledTree styled, BoxTree boxes, FragmentTree fragments, BlockLayoutEngine engine, IFontProvider fonts, double width, double height)
    {
        Styled = styled;
        Boxes = boxes;
        Fragments = fragments;
        _engine = engine;
        _fonts = fonts;
        Width = width;
        Height = height;
    }

    public StyledTree Styled { get; }

    public BoxTree Boxes { get; }

    public FragmentTree Fragments { get; }

    public double Width { get; private set; }

    public double Height { get; private set; }

    public int ReusedSubtrees => _engine.ReusedSubtrees;

    /// <summary>How the last layout differs from the one before it; valid right after <see cref="UpdateBoxesAndLayout"/> reported <see cref="FlushWork.Relaid"/>.</summary>
    internal LayoutDelta LayoutDelta => _engine.Delta;

    public bool NeedsFlush => _dirtyAll || _boxesStale || Styled.HasPending;

    public void Resize(double width, double height)
    {
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        _dirtyAll = true;
    }

    /// <summary>Makes the next <see cref="UpdateBoxesAndLayout"/> generate the boxes again (style rows were rewritten under their ids).</summary>
    public void InvalidateBoxes() => _boxesStale = true;

    /// <summary>Recomputes the styles the pending changes reach (everything after a resize).</summary>
    public void Restyle()
    {
        if (_dirtyAll)
        {
            // Style rows are keyed by the viewport; everything is computed again.
            Styled.Styles.SetViewport(Width, Height);
            Styled.RestyleAll(force: true);
            _boxesStale = true;
            _dirtyAll = false;
        }
        else
        {
            Styled.Restyle();
        }
    }

    /// <summary>Patches or regenerates the boxes for the restyled tree and lays out what changed. Clears the pending changes.</summary>
    public FlushWork UpdateBoxesAndLayout(CancellationToken cancellationToken)
    {
        FlushWork work = FlushWork.Restyled;
        try
        {
            if (_boxesStale)
            {
                Boxes.Build(cancellationToken);
                _boxesStale = false;
                work |= FlushWork.BoxesRebuilt | FlushWork.Relaid;
                LayoutAll(cancellationToken);
            }
            else
            {
                switch (Boxes.ApplyChanges(cancellationToken))
                {
                    case BoxUpdate.Rebuilt:
                        work |= FlushWork.BoxesRebuilt | FlushWork.Relaid;
                        LayoutAll(cancellationToken);
                        break;
                    case BoxUpdate.Patched:
                        work |= FlushWork.BoxesPatched | FlushWork.Relaid;
                        LayoutAll(cancellationToken);
                        break;
                    case BoxUpdate.PatchedPaintOnly:
                        work |= FlushWork.BoxesPatched;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The boxes or fragments may be half built; the next flush generates them again from the styled tree.
            _boxesStale = true;
            throw;
        }

        Styled.ClearPending();
        return work;
    }

    public void LayoutAll(CancellationToken cancellationToken)
    {
        var input = new LayoutInput(Width, Height, _fonts) { Reuse = true };
        _engine.Layout(Boxes, in input, Fragments, cancellationToken);
        Boxes.ClearDirty();
    }
}
