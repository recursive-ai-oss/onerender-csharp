using System.Numerics;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Painting;

// Transforms, filters, clip-path, masks, blending and inline SVG. The scopes of one element nest, outermost first:
//   transform > group (opacity, blend mode, isolation) > mask > clip-path > filter > the element's own painting
// which is the order Filter Effects 1 §3 and Masking 1 §5 give (filter, then clip, then mask, then opacity).
public sealed partial class Painter
{
    private const byte FxTransform = 1;
    private const byte FxMask = 2;
    private const byte FxClip = 4;
    private const byte FxFilter = 8;
    private const byte FxHidden = 16;

    private const int PartTransform = 20;
    private const int PartMask = 21;
    private const int PartMaskLayer = 22;
    private const int PartClipPath = 23;
    private const int PartFilter = 24;
    private const int PartBackdrop = 25;
    private const int PartHidden = 26;
    private const int PartSvg = 27;
    private const int PartHostScope = 32;

    private FilterOp[] _ops = new FilterOp[DisplayList.MaxFilterOps];
    private readonly PathBuffer _fxPath = new();
    private readonly PathBuffer _scratchPath = new();

    // ------------------------------------------------------------------ transform

    // Pushes the box's transform (and the parent's perspective) and, first, its backdrop filter. Returns the Fx bit.
    private byte EnterTransform(int f, in ComputedValues v)
    {
        byte fx = 0;
        if (v.CreatesTransformContext || ParentHasPerspective(f))
        {
            bool has = ComputeBoxMatrix(f, in v, out Matrix4x4 m);
            if (has && !(m.M14 != 0f || m.M24 != 0f) && !(m.M44 > 0f))
            {
                // Without perspective the plane maps with a constant w; a w at or below zero puts it behind the viewer, so nothing is drawn.
                _list.PushClipRect(Id2(f, PartHidden, 0), RectF.Empty);
                _scopes++;
                return FxHidden;
            }

            if (has && _scopes < MaxScopes - 1)
            {
                if (m.M14 != 0f || m.M24 != 0f)
                {
                    Diagnostics.Add(DiagnosticKind.Unsupported, "transform-style: preserve-3d is flattened; perspective projects each element on its own plane");
                }

                ItemId tid = Id2(f, PartTransform, 0);
                _list.PushTransform3D(tid, m);
                _scopes++;
                fx |= FxTransform;
                if (v.AnimatesTransform)
                {
                    // The host may patch this matrix in place while an animation runs (see PatchAnimated).
                    AddAnimEntry(PatchAnimTransformKind, f, tid);
                    _patch[_patchCount - 1].LastX = m.M14 != 0f || m.M24 != 0f ? 1f : 0f;
                }

                if (v.BackfaceHidden && TransformGeometry.FacesAway(in m, new RectF(C(_absX[f]), C(_absY[f]), C(_tree.BorderBoxOf(f).Width), C(_tree.BorderBoxOf(f).Height))) && _scopes < MaxScopes)
                {
                    // Transforms 2 §11: a box whose back faces the viewer is not painted; an empty clip hides it and its descendants.
                    _list.PushClipRect(Id2(f, PartHidden, 0), RectF.Empty);
                    _scopes++;
                    fx |= FxHidden;
                }
            }
        }

        return fx;
    }

    // The matrix of the box's transform properties, in root space, with the parent's perspective folded in. False when the box has neither.
    private bool ComputeBoxMatrix(int f, in ComputedValues v, out Matrix4x4 m)
    {
        Rect bb = _tree.BorderBoxOf(f);
        var border = new RectF(C(_absX[f]), C(_absY[f]), C(bb.Width), C(bb.Height));
        Insets bw = _tree.BorderOf(f);
        Insets pd = _tree.PaddingOf(f);
        var content = new RectF(
            C(_absX[f] + bw.Left + pd.Left),
            C(_absY[f] + bw.Top + pd.Top),
            C(Math.Max(0, bb.Width - bw.Left - bw.Right - pd.Left - pd.Right)),
            C(Math.Max(0, bb.Height - bw.Top - bw.Bottom - pd.Top - pd.Bottom)));
        bool has = TransformGeometry.Compute(in v, in border, in content, out m);
        if (TryParentPerspective(f, out Matrix4x4 pm))
        {
            m = m * pm;
            has = true;
        }

        return has;
    }

    private void AddAnimEntry(byte kind, int f, ItemId id)
    {
        int k = AddEntry(kind, _tree.BoxOf(f), id.Value);
        _patch[k].Fragment = f;
    }

    private bool ParentHasPerspective(int f)
    {
        int p = PerspectiveParent(f);
        return p >= 0 && _boxes.Make(_tree.BoxOf(p)).Values.Perspective > 0;
    }

    private bool TryParentPerspective(int f, out Matrix4x4 m)
    {
        m = Matrix4x4.Identity;
        int p = PerspectiveParent(f);
        if (p < 0)
        {
            return false;
        }

        ref readonly ComputedValues pv = ref _boxes.Make(_tree.BoxOf(p)).Values;
        Rect bb = _tree.BorderBoxOf(p);
        return TransformGeometry.Perspective(in pv, new RectF(C(_absX[p]), C(_absY[p]), C(bb.Width), C(bb.Height)), out m);
    }

    // The nearest ancestor fragment that is a box with a box of its own: the element whose children the perspective applies to.
    private int PerspectiveParent(int f)
    {
        for (int p = _tree.ParentOf(f); p >= 0; p = _tree.ParentOf(p))
        {
            if ((FragmentKind)_tree.KindOf(p) != FragmentKind.Box)
            {
                continue;
            }

            Box b = _boxes.Make(_tree.BoxOf(p));
            if (!b.IsAnonymous)
            {
                return p;
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------ mask, clip-path, filter

    private byte EnterFx(int f, in ComputedValues v, byte fx)
    {
        if (v.Mask.HasImages && _scopes < MaxScopes - 2 - MaskSpec.MaxLayers)
        {
            BoxGeometry g = GeometryOf(f, in v);
            if (PaintMask(f, in v, in g))
            {
                fx |= FxMask;
            }
        }

        if (v.ClipPath.Kind != ClipPathKind.None && _scopes < MaxScopes - 1)
        {
            BoxGeometry g = GeometryOf(f, in v);
            if (PushClipPathScope(f, in v, in g))
            {
                fx |= FxClip;
            }
        }

        if (!v.Filter.IsNone && _scopes < MaxScopes - 1)
        {
            int n = FillOps(in v, v.Filter, out PaintColor _);
            if (n > 0)
            {
                _list.PushFilter(Id2(f, PartFilter, 0), _ops.AsSpan(0, n));
                _scopes++;
                fx |= FxFilter;
            }
        }

        return fx;
    }

    private void ExitFx(byte fx)
    {
        if ((fx & FxFilter) != 0)
        {
            _list.PopFilter();
            _scopes--;
        }

        if ((fx & FxClip) != 0)
        {
            _list.PopClip();
            _scopes--;
        }

        if ((fx & FxMask) != 0)
        {
            _list.PopMask();
            _scopes--;
        }
    }

    private void ExitTransform(byte fx)
    {
        if ((fx & FxHidden) != 0)
        {
            _list.PopClip();
            _scopes--;
        }

        if ((fx & FxTransform) != 0)
        {
            _list.PopTransform();
            _scopes--;
        }
    }

    // Filter Effects 1 §5: resolves the computed functions into list operations. Returns the count; url() references are dropped.
    private int FillOps(in ComputedValues v, FilterSpec spec, out PaintColor cur)
    {
        cur = new PaintColor(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
        int n = 0;
        foreach (FilterFunction fn in spec.Functions)
        {
            FilterOpKind kind;
            switch (fn.Kind)
            {
                case FilterKind.Url:
                    Diagnostics.Add(DiagnosticKind.Unsupported, "filter: url() references to SVG filters are not rendered");
                    continue;
                case FilterKind.Blur:
                    _ops[n++] = new FilterOp(FilterOpKind.Blur, (float)Math.Min(fn.Amount, DisplayList.MaxBlur));
                    continue;
                case FilterKind.DropShadow:
                    _ops[n++] = new FilterOp(FilterOpKind.DropShadow, C(fn.OffsetX), C(fn.OffsetY), (float)Math.Min(fn.Amount, DisplayList.MaxBlur), ToColor(fn.Color, cur));
                    continue;
                default:
                    kind = (FilterOpKind)(int)fn.Kind;
                    _ops[n++] = new FilterOp(kind, (float)fn.Amount);
                    continue;
            }
        }

        return n;
    }

    // Filter Effects 2 §2: the filtered backdrop is painted before the element, under its border-box shape.
    private void PaintBackdropFilter(int f, in ComputedValues v)
    {
        int n = FillOps(in v, v.BackdropFilter, out _);
        if (n == 0)
        {
            return;
        }

        BoxGeometry g = GeometryOf(f, in v);
        if (!g.Border.IsEmpty)
        {
            _list.BackdropFilter(Id2(f, PartBackdrop, 0), g.Border, g.BorderRadii, _ops.AsSpan(0, n));
        }
    }

    // ------------------------------------------------------------------ clip-path

    private static RectF ReferenceBox(GeometryBox box, in BoxGeometry g, out CornerRadii radii)
    {
        switch (box)
        {
            case GeometryBox.PaddingBox:
                radii = g.PaddingRadii;
                return g.Padding;
            case GeometryBox.ContentBox:
                radii = g.ContentRadii;
                return g.Content;
            default:
                // The margin box and the SVG boxes (fill, stroke, view) act as the border box on an HTML box.
                radii = g.BorderRadii;
                return g.Border;
        }
    }

    private bool PushClipPathScope(int f, in ComputedValues v, in BoxGeometry g)
    {
        ClipPathSpec cp = v.ClipPath;
        ItemId id = Id2(f, PartClipPath, 0);
        if (cp.Kind == ClipPathKind.Reference)
        {
            return PushClipReference(f, cp, in g, id);
        }

        RectF r = ReferenceBox(cp.Box, in g, out CornerRadii radii);
        if (cp.Kind == ClipPathKind.Box)
        {
            if (radii.IsZero)
            {
                _list.PushClipRect(id, r);
            }
            else
            {
                _list.PushClipRoundedRect(id, r, radii);
            }

            _scopes++;
            return true;
        }

        double w = r.Width, h = r.Height;
        switch (cp.Shape)
        {
            case ShapeKind.Inset:
                {
                    double t = cp.Lengths[0].ResolveOr(h, 0), rt = cp.Lengths[1].ResolveOr(w, 0), b = cp.Lengths[2].ResolveOr(h, 0), l = cp.Lengths[3].ResolveOr(w, 0);
                    var box = new RectF(C(r.X + l), C(r.Y + t), C(Math.Max(0, w - l - rt)), C(Math.Max(0, h - t - b)));
                    if (cp.Radii.Length == 8)
                    {
                        Length[] q = cp.Radii;
                        var cr = new CornerRadii(
                            C(q[0].ResolveOr(w, 0)), C(q[1].ResolveOr(h, 0)), C(q[2].ResolveOr(w, 0)), C(q[3].ResolveOr(h, 0)),
                            C(q[4].ResolveOr(w, 0)), C(q[5].ResolveOr(h, 0)), C(q[6].ResolveOr(w, 0)), C(q[7].ResolveOr(h, 0)));
                        _list.PushClipRoundedRect(id, box, cr);
                    }
                    else
                    {
                        _list.PushClipRect(id, box);
                    }

                    break;
                }

            case ShapeKind.Circle:
            case ShapeKind.Ellipse:
                {
                    double cx = r.X + cp.Center.X.Resolve(w), cy = r.Y + cp.Center.Y.Resolve(h);
                    double dl = cx - r.X, dr = r.Right - cx, dt = cy - r.Y, db = r.Bottom - cy;
                    double rx, ry;
                    if (cp.Shape == ShapeKind.Circle)
                    {
                        double rad;
                        if (cp.RadiusKeyX == 0)
                        {
                            // CSS Shapes 1 §3.1: a percentage radius refers to sqrt(w² + h²) / sqrt(2).
                            rad = cp.Lengths[0].ResolveOr(Math.Sqrt((w * w) + (h * h)) / Math.Sqrt(2), 0);
                        }
                        else
                        {
                            double near = Math.Min(Math.Min(Math.Abs(dl), Math.Abs(dr)), Math.Min(Math.Abs(dt), Math.Abs(db)));
                            double far = Math.Max(Math.Max(Math.Abs(dl), Math.Abs(dr)), Math.Max(Math.Abs(dt), Math.Abs(db)));
                            rad = cp.RadiusKeyX == 1 ? near : far;
                        }

                        rx = ry = rad;
                    }
                    else
                    {
                        rx = cp.RadiusKeyX == 0 ? cp.Lengths[0].ResolveOr(w, 0) : cp.RadiusKeyX == 1 ? Math.Min(Math.Abs(dl), Math.Abs(dr)) : Math.Max(Math.Abs(dl), Math.Abs(dr));
                        ry = cp.RadiusKeyY == 0 ? cp.Lengths[1].ResolveOr(h, 0) : cp.RadiusKeyY == 1 ? Math.Min(Math.Abs(dt), Math.Abs(db)) : Math.Max(Math.Abs(dt), Math.Abs(db));
                    }

                    _fxPath.Clear();
                    AddEllipse(_fxPath, cx, cy, Math.Max(0, rx), Math.Max(0, ry));
                    _list.PushClipPath(id, _fxPath);
                    break;
                }

            case ShapeKind.Polygon:
                {
                    _fxPath.Clear();
                    int n = cp.Points.Length / 2;
                    for (int i = 0; i < n; i++)
                    {
                        float px = C(r.X + cp.Points[i * 2].ResolveOr(w, 0)), py = C(r.Y + cp.Points[(i * 2) + 1].ResolveOr(h, 0));
                        if (i == 0)
                        {
                            _fxPath.MoveTo(px, py);
                        }
                        else
                        {
                            _fxPath.LineTo(px, py);
                        }
                    }

                    _fxPath.Close();
                    _list.PushClipPath(id, _fxPath, cp.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
                    break;
                }

            default:
                {
                    _fxPath.Clear();
                    if (cp.PathData is null || !PathDataParser.Parse(cp.PathData, _fxPath, r.X, r.Y, out _) || _fxPath.VerbCount == 0)
                    {
                        _fxPath.Clear();
                        Diagnostics.Add(DiagnosticKind.InvalidValue, "clip-path: path() data could not be parsed");
                        return false;
                    }

                    _list.PushClipPath(id, _fxPath, cp.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
                    break;
                }
        }

        _scopes++;
        return true;
    }

    // Masking 1 §5.1: url(#id) to a <clipPath> of an inline svg. The shapes were flattened to one path when the document was read.
    private bool PushClipReference(int f, ClipPathSpec cp, in BoxGeometry g, ItemId id)
    {
        string? url = cp.Url;
        Tree.StyledTree? tree = _boxes.Make(_tree.BoxOf(f)).Node.Tree;
        string key = url is { Length: > 1 } && url[0] == '#' ? url.Substring(1) : string.Empty;
        if (tree is null || key.Length == 0 || !tree.ClipPaths.TryGetValue(key, out Tree.ClipPathDef? def))
        {
            Diagnostics.Add(DiagnosticKind.Unsupported, "clip-path: url() must name a <clipPath> in an inline svg of this document; it is ignored");
            return false;
        }

        RectF r = g.Border;
        _fxPath.Clear();
        bool ok;
        if (def.ObjectBoundingBox)
        {
            _scratchPath.Clear();
            ok = PathDataParser.Parse(def.PathData, _scratchPath, 0, 0, out _);
            ReadOnlySpan<byte> verbs = _scratchPath.Verbs;
            ReadOnlySpan<float> co = _scratchPath.Coords;
            int c = 0;
            for (int i = 0; i < verbs.Length; i++)
            {
                switch ((PathVerb)verbs[i])
                {
                    case PathVerb.Move: _fxPath.MoveTo(r.X + (co[c] * r.Width), r.Y + (co[c + 1] * r.Height)); c += 2; break;
                    case PathVerb.Line: _fxPath.LineTo(r.X + (co[c] * r.Width), r.Y + (co[c + 1] * r.Height)); c += 2; break;
                    case PathVerb.Quad:
                        _fxPath.QuadTo(r.X + (co[c] * r.Width), r.Y + (co[c + 1] * r.Height), r.X + (co[c + 2] * r.Width), r.Y + (co[c + 3] * r.Height));
                        c += 4;
                        break;
                    case PathVerb.Cubic:
                        _fxPath.CubicTo(r.X + (co[c] * r.Width), r.Y + (co[c + 1] * r.Height), r.X + (co[c + 2] * r.Width), r.Y + (co[c + 3] * r.Height),
                            r.X + (co[c + 4] * r.Width), r.Y + (co[c + 5] * r.Height));
                        c += 6;
                        break;
                    default: _fxPath.Close(); break;
                }
            }
        }
        else
        {
            // userSpaceOnUse on an HTML box: the origin is the box's top-left corner (Masking 1 §5.1).
            ok = PathDataParser.Parse(def.PathData, _fxPath, r.X, r.Y, out _);
        }

        if (!ok || _fxPath.VerbCount == 0)
        {
            _fxPath.Clear();
            Diagnostics.Add(DiagnosticKind.InvalidValue, "clip-path: the <clipPath> has no usable shapes");
            return false;
        }

        _list.PushClipPath(id, _fxPath, def.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
        _scopes++;
        return true;
    }

    private static void AddEllipse(PathBuffer p, double cx, double cy, double rx, double ry)
    {
        const double K = 0.5522847498307936;
        float x = C(cx), y = C(cy), a = C(rx), b = C(ry), ka = C(rx * K), kb = C(ry * K);
        p.MoveTo(x + a, y);
        p.CubicTo(x + a, y + kb, x + ka, y + b, x, y + b);
        p.CubicTo(x - ka, y + b, x - a, y + kb, x - a, y);
        p.CubicTo(x - a, y - kb, x - ka, y - b, x, y - b);
        p.CubicTo(x + ka, y - b, x + a, y - kb, x + a, y);
        p.Close();
    }

    // ------------------------------------------------------------------ mask

    private bool PaintMask(int f, in ComputedValues v, in BoxGeometry g)
    {
        MaskSpec mask = v.Mask;
        int layers = Math.Min(mask.Images.Length, MaskSpec.MaxLayers);
        if (mask.Images.Length > layers)
        {
            Diagnostics.Add(DiagnosticKind.LimitExceeded, "mask-image: more than 16 layers; the rest are ignored");
        }

        PaintColor cur = new(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
        ComputedValues layerValues = v;
        layerValues.Background = mask.AsBackground;
        _list.PushMask(Id2(f, PartMask, 0));
        _scopes++;
        for (int i = layers - 1; i >= 0; i--)
        {
            ImageSpec image = mask.Images[i];
            if (image.Kind == ImageSourceKind.None)
            {
                continue;
            }

            if (image.Kind == ImageSourceKind.Url && image.Url is { Length: > 0 } u && u[0] == '#')
            {
                Diagnostics.Add(DiagnosticKind.Unsupported, "mask-image: url() references to <mask> elements are not rendered");
            }

            MaskModeKind mode = mask.Modes[i % mask.Modes.Length];
            MaskCompositeKind comp = mask.Composites[i % mask.Composites.Length];
            _list.PushMaskLayer(Id2(f, PartMaskLayer, i), mode == MaskModeKind.Luminance ? MaskLayerMode.Luminance : MaskLayerMode.Alpha, (MaskCompositeOp)(int)comp);
            _scopes++;
            PaintLayer(f, i, in layerValues, in g, image, cur, false, default);
            _list.PopMaskLayer();
            _scopes--;
        }

        return true;
    }

    // ------------------------------------------------------------------ inline SVG

    // HTML Standard §15.4.4 / SVG 2: the svg element is a replaced element; its content box is the viewport of the document.
    private void PaintSvg(int f, Box box, in ComputedValues v, in BoxGeometry g)
    {
        string? markup = box.Node.ReplacedSource;
        RectF cr = g.Content;
        if (cr.IsEmpty)
        {
            return;
        }

        if (string.IsNullOrEmpty(markup))
        {
            Diagnostics.Add(DiagnosticKind.Unsupported, "inline svg: the element could not be serialized (too large or too complex), so it is not drawn");
            return;
        }

        var cur = new PaintColor(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
        bool clip = !g.ContentRadii.IsZero && _scopes < MaxScopes;
        if (clip)
        {
            _list.PushClipRoundedRect(Id2(f, 10, 0), cr, g.ContentRadii);
            _scopes++;
        }

        _list.SvgImage(Id2(f, PartSvg, 0), cr, markup, cur);
        if (clip)
        {
            _list.PopClip();
            _scopes--;
        }
    }
}
