using OneRender.Layout;

namespace OneRender.Painting;

/// <summary>
/// Told by a <see cref="Painter"/> where it is in the paint order, so that a consumer can relate display list items to the
/// fragments that produced them and number the paint layers. The painter calls it from the painting thread, in paint order;
/// it never calls it when no observer is set.
/// </summary>
/// <remarks>
/// A <em>unit</em> is a fragment the painter paints as a whole (a stacking context, a positioned box, a float, an atomic inline,
/// a box that clips). Units nest. Between <see cref="EnterUnit"/> and <see cref="ExitUnit"/> of a unit, <see cref="Content"/>
/// reports the fragments painted in it, each before the items it produces are appended.
/// </remarks>
public interface IPaintObserver
{
    /// <summary>A unit starts painting.</summary>
    /// <param name="fragment">The fragment that owns the unit.</param>
    /// <param name="isLayer">True when the unit is positioned, begins a stacking context or clips its content (<c>overflow</c> other than <c>visible</c>): a box a compositor keeps a paint layer of.</param>
    /// <param name="isStackingContext">True when the unit begins a stacking context (CSS 2.2 §9.9.1, and opacity below 1).</param>
    /// <param name="firstItem">Index in the display list of the next item to be appended.</param>
    void EnterUnit(Fragment fragment, bool isLayer, bool isStackingContext, int firstItem);

    /// <summary>The unit entered last has finished painting, outlines included.</summary>
    /// <param name="fragment">The fragment that owns the unit.</param>
    /// <param name="endItem">Index in the display list one past the unit's last item.</param>
    void ExitUnit(Fragment fragment, int endItem);

    /// <summary>A box's own decoration (background, border, shadow) or a text fragment's glyphs are about to be painted.</summary>
    /// <param name="fragment">The box or text fragment.</param>
    /// <param name="firstItem">Index in the display list of the next item to be appended.</param>
    void Content(Fragment fragment, int firstItem);
}
