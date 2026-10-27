using System.Numerics;
using OneRender.Painting;
using OneRender.Style;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    /// <summary>
    /// CSS Transforms 1 §2: a transformed box adds the bounding box of its transformed border box and transformed overflow to the
    /// scrollable overflow of its parent, in place of the untransformed extent. Coordinates are the box's own (origin at its border
    /// box corner); a clipped axis has already been cut to the border box by the caller.
    /// </summary>
    private static void TransformOverflow(in ComputedValues v, double w, double h, ref double ovL, ref double ovT, ref double ovR, ref double ovB)
    {
        if (!v.HasTransform)
        {
            return;
        }

        var border = new RectF(0, 0, (float)w, (float)h);
        if (!TransformGeometry.Compute(in v, in border, in border, out Matrix4x4 m))
        {
            return;
        }

        double l = Math.Min(0, ovL), t = Math.Min(0, ovT), r = Math.Max(w, ovR), b = Math.Max(h, ovB);
        if (!double.IsFinite(l) || !double.IsFinite(t) || !double.IsFinite(r) || !double.IsFinite(b))
        {
            return;
        }

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            float cx = (float)((i & 1) == 0 ? l : r), cy = (float)((i & 2) == 0 ? t : b);
            if (!TransformGeometry.MapPoint(in m, cx, cy, out float ox, out float oy))
            {
                // A corner behind the viewer has no finite image; keep the untransformed extent.
                return;
            }

            minX = Math.Min(minX, ox);
            minY = Math.Min(minY, oy);
            maxX = Math.Max(maxX, ox);
            maxY = Math.Max(maxY, oy);
        }

        ovL = minX;
        ovT = minY;
        ovR = maxX;
        ovB = maxY;
    }
}
