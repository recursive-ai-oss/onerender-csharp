using System.Numerics;
using OnePdf;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Pdf;

internal sealed partial class PageEmitter
{
    internal static bool IsLeaf(ItemKind k) =>
        k is ItemKind.FillRect or ItemKind.FillRoundedRect or ItemKind.FillPath or ItemKind.Border or ItemKind.BoxShadow or ItemKind.Image or ItemKind.GlyphRun
            or ItemKind.SvgImage or ItemKind.BackdropFilter;

    private int CountLeaves(int from, int to, int limit)
    {
        int n = 0;
        int i = from;
        while (i < to && n < limit)
        {
            DisplayItem it = _list[i];
            if (!_mark[i])
            {
                i = IsPush(it.Kind) ? it.MatchIndex + 1 : i + 1;
                continue;
            }

            if (IsLeaf(it.Kind))
            {
                n++;
            }

            i++;
        }

        return n;
    }

    /// <summary>
    /// The area the items from <paramref name="from"/> to <paramref name="to"/> (inclusive) can touch, in the space they are painted
    /// in: leaf bounds carried through the transforms inside the range, widened by the outset of a filter that starts the range.
    /// Clips are not applied, so it is conservative.
    /// </summary>
    private RectF LocalBoundsOf(int from, int to)
    {
        var stack = new List<Matrix3x2>(4) { Matrix3x2.Identity };
        RectF bounds = RectF.Empty;
        for (int k = from; k <= to && k < _list.Count; k++)
        {
            DisplayItem it = _list[k];
            switch (it.Kind)
            {
                case ItemKind.PushTransform:
                    stack.Add(it.Matrix * stack[^1]);
                    break;
                case ItemKind.PushTransform3D:
                    {
                        Matrix4x4 m = it.Matrix4;
                        float w = m.M44 == 0f ? 1f : m.M44;
                        stack.Add(new Matrix3x2(m.M11 / w, m.M12 / w, m.M21 / w, m.M22 / w, m.M41 / w, m.M42 / w) * stack[^1]);
                        break;
                    }

                case ItemKind.PopTransform:
                    if (stack.Count > 1)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }

                    break;
                default:
                    if (IsLeaf(it.Kind))
                    {
                        RectF lb = it.LocalBounds;
                        if (!lb.IsEmpty)
                        {
                            bounds = RectF.Union(bounds, lb.Transform(stack[^1]));
                        }
                    }

                    break;
            }
        }

        if (bounds.IsEmpty)
        {
            return bounds;
        }

        DisplayItem head = _list[from];
        float outset = 0f;
        if (head.Kind == ItemKind.PushFilter)
        {
            for (int k = 0; k < head.FilterOpCount; k++)
            {
                FilterOp op = head.GetFilterOp(k);
                if (op.Kind == FilterOpKind.Blur)
                {
                    outset += 3f * Math.Max(0f, op.A);
                }
                else if (op.Kind == FilterOpKind.DropShadow)
                {
                    outset += (3f * Math.Max(0f, op.C)) + MathF.Abs(op.A) + MathF.Abs(op.B);
                }
            }
        }

        outset += 1f;
        return new RectF(bounds.X - outset, bounds.Y - outset, bounds.Width + (2 * outset), bounds.Height + (2 * outset));
    }

    // ------------------------------------------------------------------ rasterized islands

    /// <summary>
    /// Draws items PDF cannot express (a blur, other filters, a perspective projection, inline SVG, a blurred shadow) as an image with
    /// an alpha channel. The items from <paramref name="from"/> to <paramref name="to"/> are copied to a list of their own, drawn by the
    /// reference rasterizer at <see cref="PdfOutputOptions.RasterDpi"/>, and the picture is placed where the items were.
    /// </summary>
    private void Island(int from, int to, string what, bool report = true)
    {
        RectF b = LocalBoundsOf(from, to);
        if (b.IsEmpty)
        {
            return;
        }

        double scale = _s.Options.RasterDpi / 96.0;
        const double MaxPixelsPerImage = 16e6;
        double area = (double)b.Width * scale * ((double)b.Height * scale);
        bool reduced = false;
        if (area > MaxPixelsPerImage)
        {
            scale *= Math.Sqrt(MaxPixelsPerImage / area);
            reduced = true;
        }

        int pw = (int)Math.Clamp(Math.Ceiling(b.Width * scale), 1, DisplayListRasterizer.MaxDimension);
        int ph = (int)Math.Clamp(Math.Ceiling(b.Height * scale), 1, DisplayListRasterizer.MaxDimension);
        if (_s.IslandPixels + ((long)pw * ph) > PdfShared.MaxIslandPixels)
        {
            _s.Diagnostics.Add(DiagnosticKind.LimitExceeded, what + " (item " + _list[from].Id.Value + ") is not drawn: the document already holds " + PdfShared.MaxIslandPixels + " pixels of rasterized content");
            return;
        }

        _s.IslandPixels += (long)pw * ph;
        var sub = new DisplayList();
        sub.PushTransform(ItemId.Of(uint.MaxValue, ushort.MaxValue, ushort.MaxValue), Matrix3x2.CreateTranslation(-b.X, -b.Y));
        DisplayListCopier.Copy(_list, from, to, sub);
        sub.PopTransform();
        RgbaImage pixels = DisplayListRasterizer.Render(
            sub,
            pw,
            ph,
            new RasterOptions { DevicePixelRatio = (float)scale, Background = PaintColor.Transparent, Images = _s.Images, Diagnostics = _s.Diagnostics },
            _token);
        PdfCreatedImage image = _s.Embed(pixels);
        if (report)
        {
            _s.Diagnostics.Add(
                DiagnosticKind.Unsupported,
                what + " (item " + _list[from].Id.Value + ") is drawn as a " + pw + "×" + ph + " px image" + (reduced ? " at reduced resolution" : " at " + (int)Math.Round(scale * 96) + " dpi") + " because PDF cannot express it");
        }

        bool content = !(what is "blurred shadow");
        BeginLeaf(content, _list[from].RootBounds.IsEmpty ? b : _list[from].RootBounds);
        bool saved = BeginAlpha(1f);
        _b.DrawImage(image, b.X, b.Y + b.Height, b.Width, -b.Height);
        EndAlpha(saved);
    }

    // ------------------------------------------------------------------ groups, filters, masks

    private int PushGroup(in DisplayItem it, int index, float opacity, BlendMode blend)
    {
        int match = it.MatchIndex;
        if (opacity >= 0.9995f && blend == BlendMode.Normal)
        {
            Open(Closer.None);
            return index + 1;
        }

        int leaves = CountLeaves(index + 1, match, 2);
        if (leaves == 0)
        {
            return match + 1;
        }

        // One painted object: the group's opacity is the object's opacity, with no compositing layer (ISO 32000-2 11.4.7 note: for a
        // single object a group and constant alpha agree).
        if (leaves == 1 || _formDepth >= MaxFormDepth)
        {
            if (leaves > 1)
            {
                _s.Diagnostics.Add(DiagnosticKind.LimitExceeded, "opacity groups nest deeper than " + MaxFormDepth + "; the inner ones apply to each object instead of the group");
            }

            ref Scope s = ref Open(Closer.Alpha);
            s.PrevAlpha = _alpha;
            s.PrevBlend = _blend;
            _alpha *= opacity;
            if (blend != BlendMode.Normal)
            {
                _blend = blend;
            }

            return index + 1;
        }

        RectF bounds = LocalBoundsOf(index + 1, match - 1);
        if (bounds.IsEmpty)
        {
            return match + 1;
        }

        PdfFormContentBuilder fb = _s.Writer.BeginForm(new PdfRectangle(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
        fb.SetTransparencyGroup(true);
        var nested = new PageEmitter(_s, _list, _mark, fb, null, _formDepth + 1, _toPoints, _token);
        nested.EmitRange(index + 1, match);
        nested.Finish();
        PdfFormXObject form = fb.Commit();

        BeginLeaf(true, it.RootBounds);
        _b.Save();
        _b.SetOpacity(Math.Clamp(opacity * _alpha, 0, 1));
        if (blend != BlendMode.Normal)
        {
            PdfBlendMode m = Geo.Blend(blend, out bool exact);
            if (!exact && !_warnedBlend)
            {
                _warnedBlend = true;
                _s.Diagnostics.Add(DiagnosticKind.Unsupported, "blend mode " + blend + " has no operator in the PDF writer; drawn as normal");
            }

            _b.SetBlendMode(m);
        }

        _b.DrawForm(form);
        _b.Restore();
        return match + 1;
    }

    private int PushFilter(in DisplayItem it, int index)
    {
        int match = it.MatchIndex;
        if (it.PdfNative)
        {
            double opacity = 1;
            for (int k = 0; k < it.FilterOpCount; k++)
            {
                opacity *= Math.Clamp(it.GetFilterOp(k).A, 0f, 1f);
            }

            return PushGroup(in it, index, (float)opacity, BlendMode.Normal);
        }

        Island(index, match, "filter");
        return match + 1;
    }

    private int PushMask(in DisplayItem it, int index)
    {
        int match = it.MatchIndex;
        int j = index + 1;
        int layers = 0;
        bool native = true;
        MaskLayerMode mode = MaskLayerMode.Alpha;
        int firstLayer = j;
        while (j < match && _list[j].Kind == ItemKind.PushMaskLayer)
        {
            DisplayItem l = _list[j];
            if (layers == 0)
            {
                mode = l.MaskMode;
            }

            native &= l.MaskComposite == MaskCompositeOp.Add && l.MaskMode == mode;
            layers++;
            j = l.MatchIndex + 1;
        }

        if (layers == 0)
        {
            return match + 1; // Masking 1 §6: a mask with no layers hides its content
        }

        if (!native || _formDepth >= MaxFormDepth || !CanOpen())
        {
            Island(index, match, "mask");
            return match + 1;
        }

        RectF bounds = RectF.Empty;
        int k = firstLayer;
        for (int n = 0; n < layers; n++)
        {
            DisplayItem l = _list[k];
            bounds = RectF.Union(bounds, LocalBoundsOf(k + 1, l.MatchIndex - 1));
            k = l.MatchIndex + 1;
        }

        if (bounds.IsEmpty)
        {
            return match + 1;
        }

        PdfFormContentBuilder fb = _s.Writer.BeginForm(new PdfRectangle(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
        fb.SetTransparencyGroup(true);
        var nested = new PageEmitter(_s, _list, _mark, fb, null, _formDepth + 1, _toPoints, _token);
        k = firstLayer;
        for (int n = 0; n < layers; n++)
        {
            DisplayItem l = _list[k];
            for (int m = k; m <= l.MatchIndex; m++)
            {
                _mark[m] = true;
            }

            nested.EmitRange(k + 1, l.MatchIndex);
            k = l.MatchIndex + 1;
        }

        nested.Finish();
        PdfFormXObject maskForm = fb.Commit();

        CloseMc();
        _b.Save();
        _depth++;
        Open(Closer.MaskRestore);
        _b.SetSoftMask(maskForm, mode == MaskLayerMode.Alpha ? PdfSoftMaskKind.Alpha : PdfSoftMaskKind.Luminosity);
        return j;
    }
}
