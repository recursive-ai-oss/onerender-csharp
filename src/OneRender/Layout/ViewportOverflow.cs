using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

/// <summary>
/// CSS Overflow 3 §3.3: the root element's <c>overflow</c> (or the <c>body</c>'s, when the root is <c>html</c> with <c>visible</c>
/// and the body is not) belongs to the viewport, and the element it came from then behaves as if it were <c>visible</c>.
/// </summary>
internal static class ViewportOverflow
{
    /// <summary>Finds what the viewport scrolls and clips by, and which box gave it up.</summary>
    /// <param name="boxes">The box tree.</param>
    /// <param name="x">The viewport's horizontal overflow: <c>visible</c> becomes <c>auto</c> and <c>clip</c> becomes <c>hidden</c> as Overflow 3 says.</param>
    /// <param name="y">The viewport's vertical overflow.</param>
    /// <param name="source">The index of the box whose value was propagated, or -1 when none was.</param>
    public static void Resolve(BoxTree boxes, out OverflowKind x, out OverflowKind y, out int source)
    {
        x = y = OverflowKind.Auto;
        source = -1;
        Box root = boxes.Root;
        if (root.IsNull)
        {
            return;
        }

        ref readonly ComputedValues rv = ref root.Values;
        if (rv.OverflowX != OverflowKind.Visible || rv.OverflowY != OverflowKind.Visible)
        {
            source = root.Index;
            x = Map(rv.OverflowX);
            y = Map(rv.OverflowY);
            return;
        }

        if (!root.Node.IsNull && root.Node.TagName.Equals("html", StringComparison.OrdinalIgnoreCase))
        {
            for (Box c = root.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind != BoxKind.Container || c.Node.IsNull || !c.Node.TagName.Equals("body", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ref readonly ComputedValues bv = ref c.Values;
                if (bv.OverflowX != OverflowKind.Visible || bv.OverflowY != OverflowKind.Visible)
                {
                    source = c.Index;
                    x = Map(bv.OverflowX);
                    y = Map(bv.OverflowY);
                }

                return;
            }
        }
    }

    private static OverflowKind Map(OverflowKind k) => k switch
    {
        OverflowKind.Visible => OverflowKind.Auto,
        OverflowKind.Clip => OverflowKind.Hidden,
        _ => k,
    };
}
