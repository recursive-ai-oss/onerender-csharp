namespace OneRender;

/// <summary>
/// One element's border box as laid out for a viewport, in CSS pixels, with the origin at the top-left of the
/// document (scroll offset zero).
/// </summary>
/// <param name="Path">
/// The element's identity: the indexes of the element, and of each ancestor element, among the element children
/// of its parent, joined by '/'. The document element is "0", its second element child "0/1", and so on.
/// Text, comments and doctype nodes are not counted. The path is computed over the tree the HTML parser builds,
/// so the same HTML gives the same paths on every side that parses it per the HTML standard.
/// </param>
/// <param name="Tag">The lower-case local name of the element, used to catch parser divergence.</param>
/// <param name="X">Left edge of the border box.</param>
/// <param name="Y">Top edge of the border box.</param>
/// <param name="Width">Width of the border box; for an inline element that wraps, the width of the union of its line boxes.</param>
/// <param name="Height">Height of the border box, with the same union rule for inline elements.</param>
public readonly record struct ElementBox(string Path, string Tag, double X, double Y, double Width, double Height);

/// <summary>
/// Provisional entry point for geometry comparison: lay out an HTML document at a viewport and report the border
/// box of every element that generates one. The comparison tooling calls this and nothing else on the engine
/// side, so the real layout entry point can replace the body without changing the tooling. This type is
/// replaced when the engine has a public layout API.
/// </summary>
public static class GeometryProbe
{
    /// <summary>
    /// Lays out <paramref name="html"/> at the given viewport and returns the border boxes keyed by element path.
    /// Elements that generate no box (<c>display: none</c>, <c>display: contents</c>) are omitted.
    /// </summary>
    /// <param name="html">The document source, already decoded to text.</param>
    /// <param name="documentUrl">The address the document is deemed to have, used to resolve relative references; may be null.</param>
    /// <param name="resolveResource">
    /// Returns the bytes of a stylesheet, image or font for an absolute address, or null when it is not available.
    /// The engine never fetches anything itself. May be null, meaning nothing is available.
    /// </param>
    /// <param name="viewportWidth">Layout viewport width in CSS pixels; at least 1.</param>
    /// <param name="viewportHeight">Layout viewport height in CSS pixels; at least 1.</param>
    /// <param name="cancellationToken">Cancels the layout; an <see cref="OperationCanceledException"/> results.</param>
    /// <returns>
    /// The boxes in document order. Currently always empty: the engine has no layout yet, which makes every
    /// reference element count as missing on our side.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A viewport dimension is below 1.</exception>
    public static IReadOnlyList<ElementBox> LayOut(
        string html,
        Uri? documentUrl,
        Func<Uri, byte[]?>? resolveResource,
        int viewportWidth,
        int viewportHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentOutOfRangeException.ThrowIfLessThan(viewportWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(viewportHeight, 1);
        cancellationToken.ThrowIfCancellationRequested();
        return [];
    }
}
