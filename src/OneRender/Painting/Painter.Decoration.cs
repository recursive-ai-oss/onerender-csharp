using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Painting;

// Box decoration: shadows, background layers, borders, border-image, outlines (CSS Backgrounds 3, CSS UI 4).
public sealed partial class Painter
{
    private const int MaxLayers = 16;
    private const int MaxTiles = 4096;
    private const int MaxEdgeTiles = 512;

    private IImageSource? _images;
    private IFontProvider? _fonts;
    private double _dpr = 1;
    private GradientStop[] _stopBuf = new GradientStop[32];
    private double[] _stopPos = new double[32];
    private ColorValue[] _stopColor = new ColorValue[32];

    /// <summary>What the last paint could not draw as asked: images the host did not supply, features not drawn. Cleared at each paint.</summary>
    public DiagnosticBag Diagnostics { get; } = new();

    // The four boxes of one element in absolute CSS px, snapped to device pixels, with the corner radii of each edge.
    private struct BoxGeometry
    {
        public RectF Border;
        public RectF Padding;
        public RectF Content;
        public CornerRadii BorderRadii;
        public CornerRadii PaddingRadii;
        public CornerRadii ContentRadii;
    }

    // ------------------------------------------------------------------ pixel snapping

    // Backgrounds, borders and clips snap to device pixels the way browsers do: an edge at x lands on round(x) device pixels, and
    // a box's far edge is snapped on its own, so adjacent boxes share an edge exactly and leave no blended seam.
    private double Snap(double v) => Math.Floor((v * _dpr) + 0.5) / _dpr;

    private RectF SnapRect(double x, double y, double w, double h)
    {
        double l = Snap(x), t = Snap(y), r = Snap(x + w), b = Snap(y + h);
        return new RectF(C(l), C(t), C(r - l), C(b - t));
    }

    private BoxGeometry GeometryOf(int f, in ComputedValues v)
    {
        Rect bb = _tree.BorderBoxOf(f);
        Insets border = _tree.BorderOf(f);
        Insets pad = _tree.PaddingOf(f);
        FragmentTraits traits = (FragmentTraits)_tree.TraitsOf(f);
        bool sliceStart = (traits & FragmentTraits.ContinuesBefore) != 0;
        bool sliceEnd = (traits & FragmentTraits.ContinuesAfter) != 0;
        double bl = sliceStart ? 0 : border.Left, br = sliceEnd ? 0 : border.Right;
        double pl = sliceStart ? 0 : pad.Left, pr = sliceEnd ? 0 : pad.Right;
        double ax = _absX[f], ay = _absY[f];
        var g = new BoxGeometry
        {
            Border = SnapRect(ax, ay, bb.Width, bb.Height),
            Padding = SnapRect(ax + bl, ay + border.Top, Math.Max(0, bb.Width - bl - br), Math.Max(0, bb.Height - border.Top - border.Bottom)),
            Content = SnapRect(
                ax + bl + pl,
                ay + border.Top + pad.Top,
                Math.Max(0, bb.Width - bl - br - pl - pr),
                Math.Max(0, bb.Height - border.Top - border.Bottom - pad.Top - pad.Bottom)),
        };
        g.BorderRadii = ResolveRadii(in v, bb.Width, bb.Height, (sliceStart ? 1 : 0) | (sliceEnd ? 2 : 0));
        g.PaddingRadii = Inner(g.BorderRadii, bl, br, border.Top, border.Bottom);
        g.ContentRadii = Inner(g.PaddingRadii, pl, pr, pad.Top, pad.Bottom);
        return g;
    }

    // Backgrounds 3 §5.2: the radii of an inner edge are the outer radii reduced by the widths between the edges.
    private static CornerRadii Inner(in CornerRadii o, double l, double r, double t, double b) =>
        o.IsZero
            ? o
            : new CornerRadii(
                Shrink(o.TopLeftX, l), Shrink(o.TopLeftY, t),
                Shrink(o.TopRightX, r), Shrink(o.TopRightY, t),
                Shrink(o.BottomRightX, r), Shrink(o.BottomRightY, b),
                Shrink(o.BottomLeftX, l), Shrink(o.BottomLeftY, b));

    // ------------------------------------------------------------------ the box

    private void PaintBoxDecoration(int f)
    {
        Box box = _boxes.Make(_tree.BoxOf(f));
        ref readonly ComputedValues v = ref box.Values;
        if (v.Visibility != VisibilityKind.Visible)
        {
            return;
        }

        Rect bb = _tree.BorderBoxOf(f);
        if (!(bb.Width > 0) || !(bb.Height > 0))
        {
            return;
        }

        // CSS2 §17.6.1.1: with separate borders, empty-cells: hide paints neither the background nor the borders of a cell without content.
        if (v.EmptyCells == EmptyCellsKind.Hide && v.BorderCollapse == BorderCollapseKind.Separate && box.Context == FormattingContext.TableCell && _tree.FirstChildOf(f) < 0)
        {
            return;
        }

        BoxGeometry g = GeometryOf(f, in v);
        FragmentTraits traits = (FragmentTraits)_tree.TraitsOf(f);
        bool sliceStart = (traits & FragmentTraits.ContinuesBefore) != 0;
        bool sliceEnd = (traits & FragmentTraits.ContinuesAfter) != 0;
        PaintColor cur = new(v.Color.R, v.Color.G, v.Color.B, v.Color.A);

        PaintOuterShadows(f, in v, in g, cur);

        if (f != _skipBackground && f != 0)
        {
            PaintBackground(f, in v, in g, cur, false, default);
        }

        PaintInsetShadows(f, in v, in g, cur);

        Insets border = PaintTableBorders(f, g.Border) ? default : _tree.BorderOf(f);
        double top = SideWidth(border.Top, v.BorderTopStyle);
        double right = sliceEnd ? 0 : SideWidth(border.Right, v.BorderRightStyle);
        double bottom = SideWidth(border.Bottom, v.BorderBottomStyle);
        double left = sliceStart ? 0 : SideWidth(border.Left, v.BorderLeftStyle);
        bool anyBorder = top > 0 || right > 0 || bottom > 0 || left > 0;
        // Backgrounds 3 §6: a border image replaces the border's style drawing.
        if (anyBorder && v.BorderImage.Source.Kind != ImageSourceKind.None && PaintBorderImage(f, in v, in g, top, right, bottom, left, cur))
        {
        }
        else if (anyBorder)
        {
            // Border widths snap down to whole device pixels, at least one when non-zero, so a 1.4px border draws as 1px.
            _list.DrawBorder(
                Id(f, 1),
                g.Border,
                g.BorderRadii,
                new BorderSide(SnapWidth(top), MapStyle(v.BorderTopStyle), ToColor(v.BorderTopColor, cur)),
                new BorderSide(SnapWidth(right), MapStyle(v.BorderRightStyle), ToColor(v.BorderRightColor, cur)),
                new BorderSide(SnapWidth(bottom), MapStyle(v.BorderBottomStyle), ToColor(v.BorderBottomColor, cur)),
                new BorderSide(SnapWidth(left), MapStyle(v.BorderLeftStyle), ToColor(v.BorderLeftColor, cur)));
        }

        if (v.IsMulticol && v.ColumnRuleWidth > 0)
        {
            PaintColumnRules(f, in v);
        }

        if (box.Kind == BoxKind.Replaced)
        {
            PaintReplaced(f, box, in v, in g);
        }
        else if (box.Kind == BoxKind.Marker)
        {
            PaintMarker(f, box, in v, in g);
        }
    }

    // Multicol 1 §4: a rule is drawn in the middle of the gap between two adjacent columns of a row, as high as the columns, above the
    // container's background and border and below its content. Rules separate columns that exist, so a row with fewer filled
    // columns than the count has fewer rules, and a spanning box ends the run (§4.1).
    private void PaintColumnRules(int f, in ComputedValues v)
    {
        if (v.ColumnRuleStyle is BorderStyleKind.None or BorderStyleKind.Hidden)
        {
            return;
        }

        PaintColor cur = ToColor(v.Color, v.Color);
        PaintColor color = ToColor(v.ColumnRuleColor, cur);
        float w = C(v.ColumnRuleWidth);
        BorderStyle style = MapStyle(v.ColumnRuleStyle);
        int prev = -1;
        for (int c = _tree.FirstChildOf(f); c >= 0; c = _tree.NextSiblingOf(c))
        {
            if ((FragmentKind)_tree.KindOf(c) != FragmentKind.Column)
            {
                prev = -1;
                continue;
            }

            if (prev >= 0)
            {
                Rect a = _tree.BorderBoxOf(prev), b = _tree.BorderBoxOf(c);
                if (Math.Abs(a.Y - b.Y) < 0.01)
                {
                    double gapStart = b.X >= a.X ? a.Right : b.Right;
                    double gapEnd = b.X >= a.X ? b.X : a.X;
                    double x = ((gapStart + gapEnd) / 2) - (w / 2);
                    var rect = new RectF(C(_absX[prev] - a.X + x), C(_absY[prev]), w, C(a.Height));
                    ItemId id = ItemId.Of((uint)(_tree.BoxOf(f) + 1), (ushort)(10 | ((_ordinal[f] >> 16) << 4)), (ushort)_tree.FragmentainerOf(c));
                    var none = new BorderSide(0f, BorderStyle.None, color);
                    _list.DrawBorder(id, rect, default, none, none, none, new BorderSide(w, style, color));
                }
            }

            prev = c;
        }
    }

    private float SnapWidth(double w) => !(w > 0) ? 0f : C(Math.Max(1.0 / _dpr, Math.Floor(w * _dpr) / _dpr));

    // ------------------------------------------------------------------ ids

    // Ids for items beyond the one-per-box ones: a second id space (top owner bit) keyed by box, kind, fragment ordinal and an index.
    private ItemId Id2(int fragment, int kind, int index)
    {
        int ordinal = _ordinal[fragment];
        uint owner = 0x80000000u | (uint)((ordinal >> 10) & 0x3FF) << 21 | ((uint)(_tree.BoxOf(fragment) + 1) & 0x1FFFFF);
        var id = ItemId.Of(owner, (ushort)((kind << 10) | (ordinal & 0x3FF)), (ushort)index);
        // Hostile trees (more than 2M boxes) could alias two ids; the list rejects duplicates, so find a free one.
        while (_list.TryFind(id, out _))
        {
            id = new ItemId(id.Value + (1UL << 32));
        }

        return id;
    }

    // ------------------------------------------------------------------ shadows

    private void PaintOuterShadows(int f, in ComputedValues v, in BoxGeometry g, PaintColor cur)
    {
        ShadowList shadows = v.BoxShadow;
        if (shadows.Count == 0)
        {
            return;
        }

        // Backgrounds 3 §7.1: the first shadow is on top, so they are drawn last to first.
        for (int i = shadows.Count - 1; i >= 0; i--)
        {
            ShadowSpec s = shadows.Items[i];
            if (s.Inset)
            {
                continue;
            }

            PaintColor c = ToColor(s.Color, cur);
            if (c.IsTransparent)
            {
                continue;
            }

            _list.DrawBoxShadow(Id2(f, 2, i), g.Border, g.BorderRadii, c, C(s.OffsetX), C(s.OffsetY), C(Math.Max(0, s.Blur)), C(s.Spread), false);
        }
    }

    private void PaintInsetShadows(int f, in ComputedValues v, in BoxGeometry g, PaintColor cur)
    {
        ShadowList shadows = v.BoxShadow;
        for (int i = shadows.Count - 1; i >= 0; i--)
        {
            ShadowSpec s = shadows.Items[i];
            if (!s.Inset)
            {
                continue;
            }

            PaintColor c = ToColor(s.Color, cur);
            if (c.IsTransparent || g.Padding.IsEmpty)
            {
                continue;
            }

            _list.DrawBoxShadow(Id2(f, 3, i), g.Padding, g.PaddingRadii, c, C(s.OffsetX), C(s.OffsetY), C(Math.Max(0, s.Blur)), C(s.Spread), true);
        }
    }

    // ------------------------------------------------------------------ backgrounds

    // geometry: the fragment the boxes come from. For the canvas, the layers of `v` are positioned against the root element's boxes
    // and painted over `canvasRect` (Backgrounds 3 §2.11.2).
    private void PaintBackground(int f, in ComputedValues v, in BoxGeometry g, PaintColor cur, bool canvas, in RectF canvasRect)
    {
        BackgroundSpec bg = v.Background;
        int layers = Math.Min(bg.LayerCount, MaxLayers);
        // Compositing 1 §5: background-blend-mode blends each layer with the layers below it and the color, never with what is behind the
        // element, so the whole background is one isolated group when any layer blends.
        bool blends = bg.HasImages && AnyBlend(v.BackgroundBlendModes) && _scopes < MaxScopes - 2;
        if (blends)
        {
            _list.PushGroup(Id2(f, 28, 0), 1f);
            _scopes++;
        }

        if (!canvas && v.BackgroundColor.A > 0f)
        {
            // The color is clipped like the bottom (last) layer (Backgrounds 3 §3.10).
            BackgroundBoxKind colorClip = bg.Clips[(bg.LayerCount - 1) % bg.Clips.Length];
            ClipBoxOf(colorClip, in g, out RectF cr, out CornerRadii crad);
            PaintColor c = ToColor(v.BackgroundColor, cur);
            if (!cr.IsEmpty)
            {
                if (crad.IsZero)
                {
                    _list.FillRect(Id(f, 0), cr, c);
                }
                else
                {
                    _list.FillRoundedRect(Id(f, 0), cr, crad, c);
                }
            }
        }

        if (!bg.HasImages)
        {
            return;
        }

        for (int i = layers - 1; i >= 0; i--)
        {
            ImageSpec image = bg.Images[i];
            if (image.Kind != ImageSourceKind.None)
            {
                BlendMode mode = blends ? v.BackgroundBlendModes[i % v.BackgroundBlendModes.Length] : BlendMode.Normal;
                if (mode != BlendMode.Normal && _scopes < MaxScopes)
                {
                    _list.PushGroup(Id2(f, 29, i), 1f, mode);
                    _scopes++;
                    PaintLayer(f, i, in v, in g, image, cur, canvas, in canvasRect);
                    _list.PopGroup();
                    _scopes--;
                }
                else
                {
                    PaintLayer(f, i, in v, in g, image, cur, canvas, in canvasRect);
                }
            }
        }

        if (blends)
        {
            _list.PopGroup();
            _scopes--;
        }
    }

    private static bool AnyBlend(BlendMode[] modes)
    {
        foreach (BlendMode m in modes)
        {
            if (m != BlendMode.Normal)
            {
                return true;
            }
        }

        return false;
    }

    private static void ClipBoxOf(BackgroundBoxKind k, in BoxGeometry g, out RectF rect, out CornerRadii radii)
    {
        switch (k)
        {
            case BackgroundBoxKind.PaddingBox:
                rect = g.Padding;
                radii = g.PaddingRadii;
                break;
            case BackgroundBoxKind.ContentBox:
                rect = g.Content;
                radii = g.ContentRadii;
                break;
            default:
                rect = g.Border;
                radii = g.BorderRadii;
                break;
        }
    }

    private void PaintLayer(int f, int i, in ComputedValues v, in BoxGeometry g, ImageSpec image, PaintColor cur, bool canvas, in RectF canvasRect)
    {
        BackgroundSpec bg = v.Background;
        BackgroundBoxKind clipKind = bg.Clips[i % bg.Clips.Length];
        BackgroundBoxKind originKind = bg.Origins[i % bg.Origins.Length];
        BackgroundAttachmentKind attach = bg.Attachments[i % bg.Attachments.Length];
        (BackgroundRepeatKind rx, BackgroundRepeatKind ry) = bg.Repeats[i % bg.Repeats.Length];
        PositionSpec pos = new(bg.PositionsX[i % bg.PositionsX.Length], bg.PositionsY[i % bg.PositionsY.Length]);
        BackgroundSizeSpec size = bg.Sizes[i % bg.Sizes.Length];
        if (clipKind is BackgroundBoxKind.Text or BackgroundBoxKind.BorderArea)
        {
            Diagnostics.Add(DiagnosticKind.Unsupported, "background-clip: text and border-area are not painted");
            return;
        }

        RectF clipRect;
        CornerRadii clipRadii;
        if (canvas)
        {
            clipRect = canvasRect;
            clipRadii = CornerRadii.Zero;
        }
        else
        {
            ClipBoxOf(clipKind, in g, out clipRect, out clipRadii);
        }

        if (clipRect.IsEmpty)
        {
            return;
        }

        ClipBoxOf(originKind == BackgroundBoxKind.Text ? BackgroundBoxKind.PaddingBox : originKind, in g, out RectF origin, out _);
        bool fixedToViewport = attach == BackgroundAttachmentKind.Fixed && _vw > 0 && _vh > 0;
        if (fixedToViewport)
        {
            origin = new RectF(0, 0, C(_vw), C(_vh));
        }

        if (origin.IsEmpty)
        {
            return;
        }

        // The image and its natural size.
        ImageInfo info = default;
        bool natural = false;
        if (image.Kind == ImageSourceKind.Url)
        {
            if (!TryImage(image.Url!, out info))
            {
                return;
            }

            natural = info.Width > 0 && info.Height > 0;
        }

        ComputeTileSize(in size, natural, info.Width, info.Height, origin.Width, origin.Height, rx, ry, out double tw, out double th);
        if (!(tw > 0) || !(th > 0))
        {
            return;
        }

        // Positions: space spreads whole tiles over the area (Backgrounds 3 §3.4); other axes position by background-position.
        double gapX = 0, gapY = 0;
        double x0 = origin.X + pos.X.Resolve(origin.Width - tw);
        double y0 = origin.Y + pos.Y.Resolve(origin.Height - th);
        bool repX = rx != BackgroundRepeatKind.NoRepeat, repY = ry != BackgroundRepeatKind.NoRepeat;
        if (rx == BackgroundRepeatKind.Space)
        {
            int n = (int)Math.Floor((origin.Width / tw) + 1e-9);
            if (n >= 2)
            {
                gapX = (origin.Width - (n * tw)) / (n - 1);
                x0 = origin.X;
            }
            else
            {
                repX = false;
            }
        }

        if (ry == BackgroundRepeatKind.Space)
        {
            int n = (int)Math.Floor((origin.Height / th) + 1e-9);
            if (n >= 2)
            {
                gapY = (origin.Height - (n * th)) / (n - 1);
                y0 = origin.Y;
            }
            else
            {
                repY = false;
            }
        }

        // Which tiles reach the painting area. For a fixed layer the area is the viewport: the layer lives in a space that is
        // counter-scrolled, so only what the viewport shows is needed and scrolling just moves the whole layer.
        RectF area = fixedToViewport ? new RectF(0, 0, C(_vw), C(_vh)) : clipRect;
        AxisTiles(x0, tw, gapX, repX, area.X, area.Right, out double firstX, out int nx);
        AxisTiles(y0, th, gapY, repY, area.Y, area.Bottom, out double firstY, out int ny);
        if (nx <= 0 || ny <= 0)
        {
            return;
        }

        bool pushedClip = false;
        bool roundedClip = !clipRadii.IsZero && !canvas;
        if (_scopes + 3 >= MaxScopes)
        {
            return;
        }

        if (roundedClip)
        {
            _list.PushClipRoundedRect(Id2(f, 4, i), clipRect, clipRadii);
            _scopes++;
            pushedClip = true;
        }
        else if (fixedToViewport || (nx == 1 && ny == 1 && image.Kind == ImageSourceKind.Url))
        {
            _list.PushClipRect(Id2(f, 4, i), clipRect);
            _scopes++;
            pushedClip = true;
        }

        bool pushedFixed = fixedToViewport && PushFixedTransform(Id2(f, 5, i));

        ImageSampling sampling = SamplingOf(in v);
        if (image.Kind == ImageSourceKind.Url)
        {
            var tile = new RectF(C(firstX), C(firstY), C(tw), C(th));
            if (nx == 1 && ny == 1)
            {
                _list.DrawImage(Id2(f, 1, i << 12), info.Handle, default, tile, sampling);
            }
            else
            {
                // One item for the whole repetition (display list 1.1); the area is limited to the rows or columns that repeat.
                RectF cover = RectF.Intersect(area, new RectF(C(repX ? area.X : firstX), C(repY ? area.Y : firstY), C(repX ? area.Width : tw), C(repY ? area.Height : th)));
                _list.DrawImageTiled(Id2(f, 1, i << 12), info.Handle, default, tile, cover, C(gapX), C(gapY), sampling);
            }
        }
        else
        {
            GradientSpec gs = image.Gradient!;
            int count = 0;
            for (int ty = 0; ty < ny; ty++)
            {
                for (int tx = 0; tx < nx; tx++)
                {
                    if (count >= MaxTiles)
                    {
                        Diagnostics.Add(DiagnosticKind.LimitExceeded, "too many background tiles; the rest are not painted");
                        goto done;
                    }

                    double px = firstX + (tx * (tw + gapX)), py = firstY + (ty * (th + gapY));
                    RectF cell = RectF.Intersect(SnapRect(px, py, tw, th), area); // tiles share snapped edges, so no seam shows between them
                    if (cell.IsEmpty)
                    {
                        continue;
                    }

                    Paint paint = MakeGradient(gs, px, py, tw, th, cur);
                    _list.FillRect(Id2(f, 1, (i << 12) | count), cell, paint);
                    count++;
                }
            }
        }

    done:
        if (pushedFixed)
        {
            _list.PopTransform();
            _scopes--;
        }

        if (pushedClip)
        {
            _list.PopClip();
            _scopes--;
        }
    }

    private static ImageSampling SamplingOf(in ComputedValues v) =>
        v.ImageRendering is ImageRenderingKind.Pixelated or ImageRenderingKind.CrispEdges ? ImageSampling.Pixelated : ImageSampling.Smooth;

    private bool TryImage(string address, out ImageInfo info)
    {
        info = default;
        if (_images is not null && _images.TryGetImage(address, out info))
        {
            return true;
        }

        Diagnostics.Add(DiagnosticKind.MissingResource, "an image was not supplied by the host");
        return false;
    }

    // A fixed background moves with the viewport: inside the viewport scroll transform it is counter-translated by the scroll offset,
    // an item the scroll patch updates in place (Backgrounds 3 §3.8).
    private bool PushFixedTransform(ItemId id)
    {
        if (_scroll is null || !_vpRange.CanScroll || _scopes >= MaxScopes)
        {
            return false;
        }

        int k = AddEntry(PatchFixedKind, -1, id.Value);
        _patch[k].Range = _vpRange;
        ComputeEntry(k, out float tx, out float ty);
        _patch[k].LastX = tx;
        _patch[k].LastY = ty;
        _list.PushTransform(id, System.Numerics.Matrix3x2.CreateTranslation(tx, ty));
        _scopes++;
        return true;
    }

    // Images 3 §5.3 default sizing with a specified size (Backgrounds 3 §3.9), then `round` (§3.4).
    private static void ComputeTileSize(in BackgroundSizeSpec s, bool natural, double nw, double nh, double aw, double ah, BackgroundRepeatKind rx, BackgroundRepeatKind ry, out double w, out double h)
    {
        double ratio = natural ? nw / nh : 0;
        bool autoW = s.Kind == BackgroundSizeKind.Explicit && s.Width.IsAuto;
        bool autoH = s.Kind == BackgroundSizeKind.Explicit && s.Height.IsAuto;
        switch (s.Kind)
        {
            case BackgroundSizeKind.Cover:
            case BackgroundSizeKind.Contain:
                if (!natural)
                {
                    w = aw;
                    h = ah;
                }
                else
                {
                    double sx = aw / nw, sy = ah / nh;
                    double k = s.Kind == BackgroundSizeKind.Cover ? Math.Max(sx, sy) : Math.Min(sx, sy);
                    w = nw * k;
                    h = nh * k;
                }

                break;
            default:
                w = autoW ? double.NaN : s.Width.ResolveOr(aw, 0);
                h = autoH ? double.NaN : s.Height.ResolveOr(ah, 0);
                if (autoW && autoH)
                {
                    w = natural ? nw : aw;
                    h = natural ? nh : ah;
                }
                else if (autoW)
                {
                    w = natural ? h * ratio : aw;
                }
                else if (autoH)
                {
                    h = natural ? w / ratio : ah;
                }

                break;
        }

        bool roundX = rx == BackgroundRepeatKind.Round, roundY = ry == BackgroundRepeatKind.Round;
        if (roundX && w > 0)
        {
            double nwr = aw / Math.Max(1, Math.Round(aw / w));
            if (!roundY && autoH && s.Kind == BackgroundSizeKind.Explicit)
            {
                h = h * nwr / w;
            }

            w = nwr;
        }

        if (roundY && h > 0)
        {
            double nhr = ah / Math.Max(1, Math.Round(ah / h));
            if (!roundX && autoW && s.Kind == BackgroundSizeKind.Explicit)
            {
                w = w * nhr / h;
            }

            h = nhr;
        }

        if (double.IsNaN(w) || double.IsNaN(h))
        {
            w = h = 0;
        }
    }

    // The first tile index range along one axis that overlaps [lo, hi): tiles start at `start` and repeat every size + gap.
    private static void AxisTiles(double start, double size, double gap, bool repeat, double lo, double hi, out double first, out int count)
    {
        first = start;
        count = 1;
        if (!repeat)
        {
            return;
        }

        double period = size + gap;
        if (!(period > 0))
        {
            return;
        }

        double k0 = Math.Floor((lo - start) / period);
        first = start + (k0 * period);
        double n = Math.Ceiling((hi - first) / period);
        count = n < 1 ? 0 : (n > MaxTiles ? MaxTiles : (int)n);
    }

    // ------------------------------------------------------------------ gradients

    private Paint MakeGradient(GradientSpec g, double bx, double by, double bw, double bh, PaintColor cur)
    {
        switch (g.Kind)
        {
            case GradientKind.Linear:
            {
                double dx, dy;
                if (g.CornerKeyword)
                {
                    // Images 3 §3.1.1: the 50% line passes through the two corners that are not the target.
                    double vx = bw * g.CornerX, vy = -bh * g.CornerY;
                    double nx = vy, ny = -vx;
                    if ((nx * g.CornerX * bw) + (ny * g.CornerY * bh) < 0)
                    {
                        nx = -nx;
                        ny = -ny;
                    }

                    double len = Math.Sqrt((nx * nx) + (ny * ny));
                    dx = len > 0 ? nx / len : 0;
                    dy = len > 0 ? ny / len : 1;
                }
                else
                {
                    dx = Math.Sin(g.Angle);
                    dy = -Math.Cos(g.Angle);
                }

                double lineLen = Math.Abs(bw * dx) + Math.Abs(bh * dy);
                double cx = bx + (bw / 2), cy = by + (bh / 2);
                int n = BuildStops(g, lineLen, false, cur);
                return _list.LinearGradient(
                    C(cx - (dx * lineLen / 2)), C(cy - (dy * lineLen / 2)), C(cx + (dx * lineLen / 2)), C(cy + (dy * lineLen / 2)),
                    _stopBuf.AsSpan(0, n), g.Repeating);
            }

            case GradientKind.Radial:
            {
                double cx = bx + g.CenterX.Resolve(bw), cy = by + g.CenterY.Resolve(bh);
                RadialRadii(g, cx - bx, cy - by, bw, bh, out double rx, out double ry);
                int n = BuildStops(g, rx, false, cur);
                return _list.RadialGradient(C(cx), C(cy), C(Math.Max(0, rx)), C(Math.Max(0, ry)), _stopBuf.AsSpan(0, n), g.Repeating);
            }

            default:
            {
                double cx = bx + g.CenterX.Resolve(bw), cy = by + g.CenterY.Resolve(bh);
                int n = BuildStops(g, 1, true, cur);
                return _list.ConicGradient(C(cx), C(cy), C(g.Angle), _stopBuf.AsSpan(0, n), g.Repeating);
            }
        }
    }

    // Images 3 §3.2.1: the ending shape's radii. (cx, cy) is the center relative to the box; the box is bw by bh.
    private static void RadialRadii(GradientSpec g, double cx, double cy, double bw, double bh, out double rx, out double ry)
    {
        double left = Math.Abs(cx), right = Math.Abs(bw - cx), top = Math.Abs(cy), bottom = Math.Abs(bh - cy);
        double nearX = Math.Min(left, right), farX = Math.Max(left, right), nearY = Math.Min(top, bottom), farY = Math.Max(top, bottom);
        switch (g.Extent)
        {
            case RadialExtent.Explicit:
                rx = g.RadiusX.ResolveOr(bw, 0);
                ry = g.Circle ? rx : g.RadiusY.ResolveOr(bh, 0);
                return;
            case RadialExtent.ClosestSide:
                if (g.Circle)
                {
                    rx = ry = Math.Min(nearX, nearY);
                }
                else
                {
                    rx = nearX;
                    ry = nearY;
                }

                return;
            case RadialExtent.FarthestSide:
                if (g.Circle)
                {
                    rx = ry = Math.Max(farX, farY);
                }
                else
                {
                    rx = farX;
                    ry = farY;
                }

                return;
            case RadialExtent.ClosestCorner:
                if (g.Circle)
                {
                    rx = ry = Math.Sqrt((nearX * nearX) + (nearY * nearY));
                }
                else
                {
                    // The ellipse of the closest-side ratio, grown to pass through the closest corner.
                    double k = Math.Sqrt(2);
                    rx = nearX * k;
                    ry = nearY * k;
                }

                return;
            default:
                if (g.Circle)
                {
                    rx = ry = Math.Sqrt((farX * farX) + (farY * farY));
                }
                else
                {
                    double k = Math.Sqrt(2);
                    rx = farX * k;
                    ry = farY * k;
                }

                return;
        }
    }

    private void EnsureStops(int n)
    {
        if (_stopPos.Length >= n)
        {
            return;
        }

        int size = Math.Max(n, _stopPos.Length * 2);
        _stopPos = new double[size];
        _stopColor = new ColorValue[size];
    }

    // CSS Images 3 §3.4.3 color-stop fixup and §3.5.3 transition hints. Returns the number of stops written to _stopBuf, as fractions
    // of the gradient line (linear), of the ending shape's radius (radial) or of a turn (conic).
    private int BuildStops(GradientSpec g, double lineLen, bool conic, PaintColor cur)
    {
        GradientStopSpec[] src = g.Stops;
        int n = Math.Min(src.Length, DisplayListLimits.MaxStopsPerGradient / 2);
        EnsureStops(n + 1);
        int m = 0;
        // Pass 1: colors and positions as fractions; hints are kept as the position of the stop that follows them.
        double[] pos = _stopPos;
        for (int i = 0; i < n; i++)
        {
            GradientStopSpec s = src[i];
            double p = double.NaN;
            switch (s.Position.Kind)
            {
                case LengthKind.Px: p = lineLen > 0 ? s.Position.Value / lineLen : 0; break;
                case LengthKind.Percent: p = s.Position.Value / 100; break;
                case LengthKind.Number: p = s.Position.Value / (2 * Math.PI); break;
                case LengthKind.Calc: p = lineLen > 0 ? s.Position.Resolve(lineLen) / lineLen : 0; break;
            }

            if (s.IsHint)
            {
                continue;
            }

            pos[m] = p;
            _stopColor[m] = s.Color;
            m++;
        }

        // Hints: remember each one's position by its gap index.
        EnsureHints(m);
        for (int i = 0; i < m; i++)
        {
            _hintOf[i] = double.NaN;
        }

        int gap = -1;
        for (int i = 0; i < n; i++)
        {
            if (src[i].IsHint)
            {
                if (gap >= 0)
                {
                    double p = double.NaN;
                    Length l = src[i].Position;
                    switch (l.Kind)
                    {
                        case LengthKind.Px: p = lineLen > 0 ? l.Value / lineLen : 0; break;
                        case LengthKind.Percent: p = l.Value / 100; break;
                        case LengthKind.Number: p = l.Value / (2 * Math.PI); break;
                        case LengthKind.Calc: p = lineLen > 0 ? l.Resolve(lineLen) / lineLen : 0; break;
                    }

                    _hintOf[gap] = p;
                }
            }
            else
            {
                gap++;
            }
        }

        if (m == 0)
        {
            return 0;
        }

        if (double.IsNaN(pos[0]))
        {
            pos[0] = 0;
        }

        if (double.IsNaN(pos[m - 1]))
        {
            pos[m - 1] = 1;
        }

        double run = pos[0];
        for (int i = 1; i < m; i++)
        {
            if (!double.IsNaN(pos[i]))
            {
                if (pos[i] < run)
                {
                    pos[i] = run;
                }

                run = pos[i];
            }
        }

        for (int i = 1; i < m - 1; i++)
        {
            if (!double.IsNaN(pos[i]))
            {
                continue;
            }

            int j = i;
            while (double.IsNaN(pos[j + 1]))
            {
                j++;
            }

            double a = pos[i - 1], b = pos[j + 1];
            int span = j - i + 2;
            for (int k = i; k <= j; k++)
            {
                pos[k] = a + ((b - a) * (k - i + 1) / span);
            }

            i = j;
        }

        // Pass 2: write stops, expanding hints into intermediate stops (the midpoint color sits at the hint).
        int outN = 0;
        for (int i = 0; i < m; i++)
        {
            PaintColor c = ToColor(_stopColor[i], cur);
            Put(ref outN, (float)pos[i], c);
            if (i + 1 < m && !double.IsNaN(_hintOf[i]))
            {
                double a = pos[i], b = pos[i + 1], h = Math.Clamp(_hintOf[i], a, b);
                PaintColor c2 = ToColor(_stopColor[i + 1], cur);
                double t = b > a ? (h - a) / (b - a) : 0.5;
                if (b <= a)
                {
                    continue;
                }

                if (t <= 0.0001)
                {
                    Put(ref outN, (float)a, c2);
                }
                else if (t >= 0.9999)
                {
                    Put(ref outN, (float)b, c);
                }
                else
                {
                    double exponent = Math.Log(0.5) / Math.Log(t);
                    const int Steps = 12;
                    for (int k = 1; k <= Steps; k++)
                    {
                        double u = (double)k / (Steps + 1);
                        double w = Math.Pow(u, exponent);
                        Put(ref outN, (float)(a + ((b - a) * u)), Mix(c, c2, (float)w));
                    }
                }
            }
        }

        return outN;
    }

    private double[] _hintOf = new double[32];

    private void EnsureHints(int m)
    {
        if (_hintOf.Length < m)
        {
            _hintOf = new double[Math.Max(m, _hintOf.Length * 2)];
        }
    }

    private void Put(ref int n, float offset, PaintColor color)
    {
        if (n == _stopBuf.Length)
        {
            if (n >= DisplayListLimits.MaxStopsPerGradient)
            {
                return;
            }

            Array.Resize(ref _stopBuf, Math.Min(_stopBuf.Length * 2, DisplayListLimits.MaxStopsPerGradient));
        }

        // Stop positions can come out infinite or NaN (calc(infinity * 1px)); the list only takes finite ones.
        _stopBuf[n++] = new GradientStop(float.IsNaN(offset) ? 0f : Math.Clamp(offset, -1e8f, 1e8f), color);
    }

    // Premultiplied interpolation, as the gradient itself does (Images 3 §3.5.2).
    private static PaintColor Mix(PaintColor a, PaintColor b, float t)
    {
        float aa = a.A * (1 - t), ba = b.A * t;
        float al = aa + ba;
        if (al <= 0f)
        {
            return PaintColor.Transparent;
        }

        return new PaintColor(((a.R * aa) + (b.R * ba)) / al, ((a.G * aa) + (b.G * ba)) / al, ((a.B * aa) + (b.B * ba)) / al, al);
    }

    // ------------------------------------------------------------------ outlines

    private void PaintOutlines(int owner, bool own)
    {
        for (int e = _outlineHead[owner]; e >= 0; e = _nextOutline[e])
        {
            if ((e == owner) != own)
            {
                continue;
            }

            PaintOutline(e);
        }
    }

    private void PaintOutline(int f)
    {
        Box box = _boxes.Make(_tree.BoxOf(f));
        ref readonly ComputedValues v = ref box.Values;
        if (v.Visibility != VisibilityKind.Visible || !(v.OutlineWidth > 0) || (v.OutlineStyle == BorderStyleKind.None && !v.OutlineAuto))
        {
            return;
        }

        Rect bb = _tree.BorderBoxOf(f);
        if (!(bb.Width > 0) && !(bb.Height > 0))
        {
            return;
        }

        PaintColor cur = new(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
        PaintColor color = ToColor(v.OutlineColor, cur);
        double w = v.OutlineWidth;
        double grow = v.OutlineOffset;
        CornerRadii radii = ResolveRadii(in v, bb.Width, bb.Height, 0);
        if (v.OutlineAuto)
        {
            // A focus-ring look: a 2 px ring in the outline color around a 1 px white ring, with a small corner radius.
            // CSS UI 4 §4.4 leaves the rendering to the user agent.
            double ring = 2;
            DrawOutlineRing(f, 0, grow + 1, 1, PaintColor.White, BorderStyle.Solid, radii, 2);
            DrawOutlineRing(f, 1, grow + 1 + ring, ring, color, BorderStyle.Solid, radii, 2);
            return;
        }

        DrawOutlineRing(f, 0, grow + w, w, color, BorderStyle.Solid, radii, 0, MapStyle(v.OutlineStyle));
    }

    // outer: how far the ring's outer edge lies from the border box.
    private void DrawOutlineRing(int f, int index, double outer, double width, PaintColor color, BorderStyle fallback, in CornerRadii radii, double minRadius, BorderStyle? style = null)
    {
        if (color.IsTransparent || !(width > 0))
        {
            return;
        }

        RectF r = SnapRect(_absX[f] - outer, _absY[f] - outer, _tree.BorderBoxOf(f).Width + (2 * outer), _tree.BorderBoxOf(f).Height + (2 * outer));
        if (r.IsEmpty)
        {
            return;
        }

        // The outline follows the border radius, grown by the distance (CSS UI 4 §4.3).
        CornerRadii rad = radii.IsZero && minRadius <= 0
            ? radii
            : new CornerRadii(
                Grow(radii.TopLeftX, outer, minRadius), Grow(radii.TopLeftY, outer, minRadius),
                Grow(radii.TopRightX, outer, minRadius), Grow(radii.TopRightY, outer, minRadius),
                Grow(radii.BottomRightX, outer, minRadius), Grow(radii.BottomRightY, outer, minRadius),
                Grow(radii.BottomLeftX, outer, minRadius), Grow(radii.BottomLeftY, outer, minRadius));
        float sw = C(width);
        BorderStyle bs = style ?? fallback;
        _list.DrawBorder(Id2(f, 6, index), r, rad, new BorderSide(sw, bs, color), new BorderSide(sw, bs, color), new BorderSide(sw, bs, color), new BorderSide(sw, bs, color));
    }

    private static float Grow(float radius, double by, double min) => (float)Math.Max(min, radius > 0 ? radius + by : 0);
}
