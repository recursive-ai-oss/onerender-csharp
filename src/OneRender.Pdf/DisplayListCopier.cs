using System.Numerics;
using OneRender.Painting;

namespace OneRender.Pdf;

/// <summary>Copies a run of items from one display list into another, by calling the public builder methods with what the reader properties give.</summary>
internal static class DisplayListCopier
{
    /// <summary>
    /// Copies items <paramref name="from"/> to <paramref name="to"/> (inclusive) of <paramref name="source"/> into <paramref name="target"/>.
    /// The run must be balanced: every push has its pop inside it. Tag scopes are not copied (they carry no pixels).
    /// </summary>
    public static void Copy(DisplayList source, int from, int to, DisplayList target)
    {
        for (int i = from; i <= to; i++)
        {
            DisplayItem it = source[i];
            switch (it.Kind)
            {
                case ItemKind.FillRect:
                    target.FillRect(it.Id, it.Rect, PaintOf(source, target, in it));
                    break;
                case ItemKind.FillRoundedRect:
                    target.FillRoundedRect(it.Id, it.Rect, it.Radii, PaintOf(source, target, in it));
                    break;
                case ItemKind.FillPath:
                    target.FillPath(it.Id, it.PathVerbs, it.PathCoords, PaintOf(source, target, in it), it.FillRule);
                    break;
                case ItemKind.Border:
                    target.DrawBorder(it.Id, it.Rect, it.Radii, it.BorderTop, it.BorderRight, it.BorderBottom, it.BorderLeft);
                    break;
                case ItemKind.BoxShadow:
                    target.DrawBoxShadow(it.Id, it.Rect, it.Radii, it.ShadowColor, it.ShadowOffsetX, it.ShadowOffsetY, it.ShadowBlur, it.ShadowSpread, it.ShadowInset);
                    break;
                case ItemKind.Image:
                    if (it.IsTiledImage)
                    {
                        target.DrawImageTiled(it.Id, it.Image, it.ImageSource, it.ImageDestination, it.ImageTileArea, it.ImageTileGapX, it.ImageTileGapY, it.Sampling);
                    }
                    else
                    {
                        target.DrawImage(it.Id, it.Image, it.ImageSource, it.ImageDestination, it.Sampling);
                    }

                    break;
                case ItemKind.GlyphRun:
                    {
                        GlyphRunView r = it.GlyphRun;
                        target.DrawGlyphRun(it.Id, r.Face, r.FontSize, r.OriginX, r.OriginY, r.Color, r.GlyphIds, r.X, r.Y, r.Clusters, r.Text, r.RightToLeft, r.Blur);
                        break;
                    }

                case ItemKind.SvgImage:
                    target.SvgImage(it.Id, it.Rect, it.SvgMarkup ?? string.Empty, it.SvgColor);
                    break;
                case ItemKind.BackdropFilter:
                    target.BackdropFilter(it.Id, it.Rect, it.Radii, Ops(in it));
                    break;
                case ItemKind.PushClipRect:
                    target.PushClipRect(it.Id, it.Rect);
                    break;
                case ItemKind.PushClipRoundedRect:
                    target.PushClipRoundedRect(it.Id, it.Rect, it.Radii);
                    break;
                case ItemKind.PushClipPath:
                    target.PushClipPath(it.Id, it.PathVerbs, it.PathCoords, it.FillRule);
                    break;
                case ItemKind.PopClip:
                    target.PopClip();
                    break;
                case ItemKind.PushTransform:
                    target.PushTransform(it.Id, it.Matrix);
                    break;
                case ItemKind.PushTransform3D:
                    target.PushTransform3D(it.Id, it.Matrix4);
                    break;
                case ItemKind.PopTransform:
                    target.PopTransform();
                    break;
                case ItemKind.PushGroup:
                    target.PushGroup(it.Id, it.Opacity, it.Blend);
                    break;
                case ItemKind.PopGroup:
                    target.PopGroup();
                    break;
                case ItemKind.PushFilter:
                    target.PushFilter(it.Id, Ops(in it));
                    break;
                case ItemKind.PopFilter:
                    target.PopFilter();
                    break;
                case ItemKind.PushMask:
                    target.PushMask(it.Id);
                    break;
                case ItemKind.PopMask:
                    target.PopMask();
                    break;
                case ItemKind.PushMaskLayer:
                    target.PushMaskLayer(it.Id, it.MaskMode, it.MaskComposite);
                    break;
                case ItemKind.PopMaskLayer:
                    target.PopMaskLayer();
                    break;
            }
        }
    }

    private static FilterOp[] Ops(in DisplayItem it)
    {
        var ops = new FilterOp[it.FilterOpCount];
        for (int i = 0; i < ops.Length; i++)
        {
            ops[i] = it.GetFilterOp(i);
        }

        return ops;
    }

    private static Paint PaintOf(DisplayList source, DisplayList target, in DisplayItem it)
    {
        Paint p = it.Paint;
        if (p.Kind == PaintKind.Solid)
        {
            return p;
        }

        GradientView g = it.Gradient;
        var stops = new GradientStop[g.StopCount];
        for (int i = 0; i < stops.Length; i++)
        {
            stops[i] = g.GetStop(i);
        }

        return p.Kind switch
        {
            PaintKind.Linear => target.LinearGradient(g.X0, g.Y0, g.X1, g.Y1, stops, g.Repeating),
            PaintKind.Radial => target.RadialGradient(g.X0, g.Y0, g.X1, g.Y1, stops, g.Repeating),
            _ => target.ConicGradient(g.X0, g.Y0, g.FromAngle, stops, g.Repeating),
        };
    }
}
