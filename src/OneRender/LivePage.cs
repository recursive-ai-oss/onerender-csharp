using OneRender.Layout;
using OneRender.Painting;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tree;

namespace OneRender;

/// <summary>What <see cref="LivePage"/> needs besides the styled tree.</summary>
public sealed class LivePageOptions
{
    /// <summary>Creates options with the required font provider.</summary>
    /// <param name="fonts">Chooses the faces text is shaped with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fonts"/> is null.</exception>
    public LivePageOptions(IFontProvider fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Fonts = fonts;
    }

    /// <summary>Chooses the faces text is shaped with.</summary>
    public IFontProvider Fonts { get; }

    /// <summary>Layout viewport width in CSS px, at least 1. Default 800.</summary>
    public double ViewportWidth { get; init; } = 800;

    /// <summary>Layout viewport height in CSS px, at least 1. Default 600.</summary>
    public double ViewportHeight { get; init; } = 600;

    /// <summary>Supplies the images that <c>url()</c> values and image elements refer to; null means every image is missing.</summary>
    public IImageSource? Images { get; init; }

    /// <summary>Device pixels per CSS px, used to snap box edges when painting. Default 1.</summary>
    public double DevicePixelRatio { get; init; } = 1;
}

/// <summary>The work one <see cref="LivePage.Flush"/> did.</summary>
[Flags]
public enum FlushWork : byte
{
    /// <summary>Nothing was pending.</summary>
    None = 0,

    /// <summary>Computed styles were recomputed.</summary>
    Restyled = 1,

    /// <summary>Boxes were patched in place.</summary>
    BoxesPatched = 2,

    /// <summary>The box tree was generated again (the change reached which boxes exist).</summary>
    BoxesRebuilt = 4,

    /// <summary>Layout ran. Subtrees that did not change were taken from the layout before.</summary>
    Relaid = 8,

    /// <summary>The display list was painted again; <see cref="LivePage.Display"/> holds the damage.</summary>
    Repainted = 16,
}

/// <summary>What <see cref="LivePage.Flush"/> did and how much of the old layout it kept.</summary>
public readonly struct FlushResult
{
    internal FlushResult(FlushWork work, int reused)
    {
        Work = work;
        ReusedSubtrees = reused;
    }

    /// <summary>The stages that ran.</summary>
    public FlushWork Work { get; }

    /// <summary>How many subtrees layout copied from its previous result instead of laying them out.</summary>
    public int ReusedSubtrees { get; }

    /// <summary>True when the display list changed.</summary>
    public bool Repainted => (Work & FlushWork.Repainted) != 0;
}

/// <summary>
/// A retained pipeline from a styled-node tree to a display list that is brought up to date from change sets: edits made through
/// <see cref="StyledTree"/> (set a declaration, a state or a text, insert, remove or move nodes) are recorded, and
/// <see cref="Flush"/> restyles only what they reach, patches the box tree, lays out only the subtrees that changed, and paints the
/// display list again with damage. After every flush the result equals what a build from scratch of the same tree gives.
/// </summary>
/// <remarks>
/// <para>
/// Steady-state flushes of text, state, declaration and paint-only changes allocate nothing once the pipeline is warm. Changes that
/// alter which boxes exist (insert, remove, move, display changes, text that becomes empty or white space) generate the boxes and lay
/// out everything again; more than <see cref="StyledTree.MaxPendingChanges"/> changes in one flush do the same.
/// </para>
/// <para>
/// A script runtime batches its mutations per task and calls <see cref="Flush"/> once, and again before it answers a layout query
/// in the middle of a task. Not thread-safe.
/// </para>
/// </remarks>
public sealed class LivePage
{
    private readonly LivePageOptions _options;
    private readonly PipelineUpdater _updater;
    private readonly Painter _painter = new();
    private int _generation;

    /// <summary>Builds the boxes of <paramref name="tree"/>, lays them out and paints them. The tree starts recording edits from here.</summary>
    /// <param name="tree">The styled tree; it must not be shared with another page.</param>
    /// <param name="options">Viewport, fonts and images.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public LivePage(StyledTree tree, LivePageOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ViewportWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ViewportHeight, 1);
        _options = options;
        double vw = options.ViewportWidth, vh = options.ViewportHeight;
        Styled = tree;
        // Computed lengths embed the font metrics and the viewport, so a tree built under others is computed again.
        bool fontsChanged = !ReferenceEquals(tree.Styles.Fonts, options.Fonts);
        bool viewportChanged = tree.Styles.ViewportWidth != vw || tree.Styles.ViewportHeight != vh;
        tree.Styles.Fonts = options.Fonts;
        if (fontsChanged)
        {
            tree.Styles.Reset();
        }

        tree.Styles.SetViewport(vw, vh);
        tree.Tracking = true;
        if (fontsChanged || viewportChanged)
        {
            tree.RestyleAll(force: true);
        }
        else
        {
            tree.Restyle();
        }

        tree.ClearPending();
        Boxes = new BoxTree(tree);
        Fragments = new FragmentTree(Boxes);
        _updater = new PipelineUpdater(tree, Boxes, Fragments, new BlockLayoutEngine(), options.Fonts, vw, vh);
        Boxes.Build(cancellationToken);
        _updater.LayoutAll(cancellationToken);
        PaintAll(cancellationToken);
    }

    /// <summary>The styled-node tree; edit it, then call <see cref="Flush"/>.</summary>
    public StyledTree Styled { get; }

    /// <summary>The box tree, kept up to date by <see cref="Flush"/>.</summary>
    public BoxTree Boxes { get; }

    /// <summary>The fragment tree of the last flush. The object stays the same; its contents are replaced by each layout.</summary>
    public FragmentTree Fragments { get; }

    /// <summary>The retained display list. After a flush its <see cref="RetainedDisplayList.Damage"/> holds what changed since the host last called <see cref="RetainedDisplayList.ClearDamage"/>.</summary>
    public RetainedDisplayList Display { get; } = new();

    /// <summary>The scroll positions that painting applies; change them and call <see cref="Painter.PatchScroll(RetainedDisplayList)"/> to patch the list without a flush.</summary>
    public ScrollState Scroll { get; } = new();

    /// <summary>Counts the flushes that changed something; starts at 0.</summary>
    public int Generation => _generation;

    /// <summary>The viewport width in CSS px.</summary>
    public double ViewportWidth => _updater.Width;

    /// <summary>The viewport height in CSS px.</summary>
    public double ViewportHeight => _updater.Height;

    /// <summary>True when edits are waiting for <see cref="Flush"/>.</summary>
    public bool NeedsFlush => _updater.NeedsFlush;

    /// <summary>Changes the viewport. Styles with viewport units resolve again, so the next flush recomputes everything.</summary>
    /// <param name="width">At least 1.</param>
    /// <param name="height">At least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is below 1.</exception>
    public void Resize(double width, double height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        _updater.Resize(width, height);
    }

    /// <summary>
    /// Brings everything up to date with the edits made since the last flush: restyle, boxes, layout, display list.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work. A cancelled flush leaves the edits pending, and the next flush starts over from the styled tree.</param>
    /// <returns>What ran. <see cref="FlushWork.None"/> when nothing was pending.</returns>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public FlushResult Flush(CancellationToken cancellationToken = default)
    {
        if (!NeedsFlush)
        {
            return new FlushResult(FlushWork.None, 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _updater.Restyle();
        FlushWork work = _updater.UpdateBoxesAndLayout(cancellationToken);
        int reused = _updater.ReusedSubtrees;
        PaintFlush(work, cancellationToken);
        work |= FlushWork.Repainted;
        _generation++;
        return new FlushResult(work, reused);
    }

    /// <summary>
    /// Tells the page that the host replaced the pixels behind an image handle and kept their size: the damage of <see cref="Display"/> grows by
    /// the bounds of every item that draws it. No restyle, layout or paint runs, and nothing is allocated.
    /// </summary>
    /// <param name="image">The handle whose pixels changed.</param>
    /// <returns>The number of display items that draw the handle.</returns>
    public int InvalidateImage(ImageHandle image) => Display.InvalidateImage(image);

    /// <summary>What the painter draws over the text control being edited (see <see cref="Interaction.LiveTextEditor"/>); null when none. Set by the editor, read at every paint. While it is set every flush paints the whole list.</summary>
    public Forms.TextEditView? Editing { get; set; }

    /// <summary>Paints the display list again from the current layout, for a change that only touches painting (a caret blink, a selection, a scroll position).</summary>
    /// <param name="cancellationToken">Cancels the paint.</param>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public void Repaint(CancellationToken cancellationToken = default)
    {
        PaintAll(cancellationToken);
        _generation++;
    }

    private PaintOptions PaintOptionsNow() => new(_updater.Width, _updater.Height)
    {
        Scroll = Scroll,
        Images = _options.Images,
        Fonts = _options.Fonts,
        DevicePixelRatio = _options.DevicePixelRatio,
        Editing = Editing,
    };

    private void PaintAll(CancellationToken cancellationToken)
    {
        PaintOptions paint = PaintOptionsNow();
        _painter.Paint(Fragments, Display, in paint, cancellationToken);
        Boxes.ClearTouched();
    }

    // Rewrites the items of the fragments that changed when the layout kept every fragment in place; otherwise paints the whole list.
    private void PaintFlush(FlushWork work, CancellationToken cancellationToken)
    {
        if ((work & FlushWork.BoxesRebuilt) == 0)
        {
            PaintOptions paint = PaintOptionsNow();
            bool layoutRan = (work & FlushWork.Relaid) != 0;
            if (_painter.TryPatch(Fragments, Display, in paint, layoutRan ? _updater.LayoutDelta : null, layoutRan))
            {
                Boxes.ClearTouched();
                return;
            }
        }

        PaintAll(cancellationToken);
    }

    /// <summary>How many flushes rewrote only the changed items of the display list instead of painting all of it.</summary>
    internal int PatchedPaints => _painter.PatchedPaints;
}
