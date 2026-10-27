using System.Numerics;
using OneFont;
using OneRender.Painting;
using AlphaComposition = OneImage.AlphaComposition;
using Brush = OneImage.Drawing.Brush;
using Canvas = OneImage.Canvas;
using ImageRect = OneImage.ImageRect;
using IntersectionRule = OneImage.Drawing.IntersectionRule;
using OBlend = OneImage.BlendMode;
using Path = OneImage.Drawing.Path;
using PathBuilder = OneImage.Drawing.PathBuilder;
using PathBoolean = OneImage.Drawing.PathBoolean;
using PixelFormat = OneImage.PixelFormat;
using Rgba32 = OneImage.Rgba32;

namespace OneRender.Image;

/// <summary>Walks a display list once, front to back in paint order, keeping explicit stacks for transforms, clips and groups.</summary>
internal sealed partial class Rasterizer : IDisposable
{
    private const int MaxOpenGroups = 32;
    private const long MaxLayerPixels = 1L << 27;

    private readonly DisplayList _list;
    private readonly RasterOptions _options;
    private readonly CancellationToken _ct;
    private readonly int _surfaceW;
    private readonly int _surfaceH;
    private int _width;
    private int _height;
    private readonly DiagnosticBag? _diagnostics;
    private (int X, int Y) _off;
    private bool _in3D;
    private int _skip;
    private int _next = -1;
    private readonly float _dpr;
    private readonly Frame[] _stack = new Frame[DisplayListLimits.MaxNestingDepth + 1];
    private readonly Dictionary<int, RgbaImage?> _images = [];

    private Canvas _target;
    private Canvas _root;
    private Matrix3x2 _ctm;
    private ClipState _clip;
    private int _sp;
    private int _groups;
    private long _layerPixels;
    private RectF _visible;

    private struct Frame
    {
        public ItemKind Kind;
        public Matrix3x2 Ctm;
        public ClipState Clip;
        public Canvas Target;
        public float Opacity;
        public OBlend Blend;
        public bool Plus;
        public (int X, int Y) Off;
        public int W;
        public int H;
        public RectF Visible;
        public bool In3D;
        public RectI Region;
        public int Item;
        public float Scale;
        public float[]? Coverage;
        public Matrix4x4 Proj;
    }

    // A clip in device pixels. Rectangles are kept as rectangles so that nested rectangular clips need no path booleans.
    private readonly struct ClipState
    {
        public ClipState(Path? path, bool isRect, RectF rect, bool empty)
        {
            Path = path;
            IsRect = isRect;
            Rect = rect;
            IsEmpty = empty;
        }

        public Path? Path { get; }

        public bool IsRect { get; }

        public RectF Rect { get; }

        public bool IsEmpty { get; }
    }

    public Rasterizer(DisplayList list, int width, int height, RasterOptions options, CancellationToken ct)
    {
        _list = list;
        _options = options;
        _diagnostics = options.Diagnostics;
        _ct = ct;
        _width = _surfaceW = width;
        _height = _surfaceH = height;
        _dpr = options.DevicePixelRatio;
        _root = new Canvas(width, height, PixelFormat.Rgba8);
        _target = _root;
        _ctm = Matrix3x2.CreateScale(_dpr);
        _clip = default;
        _visible = new RectF(0, 0, width / _dpr, height / _dpr);
    }

    public void Dispose()
    {
        while (_sp > 0)
        {
            Frame f = _stack[--_sp];
            if (f.Kind is ItemKind.PushGroup or ItemKind.PushFilter or ItemKind.PushMask or ItemKind.PushMaskLayer or ItemKind.PushTransform3D)
            {
                _target.Dispose();
                _target = f.Target;
            }
        }

        _root.Dispose();
    }

    public void LoadPixels(RgbaImage surface)
    {
        Rgba32[] px = new Rgba32[_width * _height];
        byte[] b = surface.Pixels;
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = new Rgba32(b[i * 4], b[(i * 4) + 1], b[(i * 4) + 2], b[(i * 4) + 3]);
        }

        _root.CopyFrom<Rgba32>(px, new ImageRect(0, 0, _width, _height));
    }

    public void StorePixels(RgbaImage surface, RectI region)
    {
        Rgba32[] px = new Rgba32[region.Width * region.Height];
        _root.CopyTo<Rgba32>(px, new ImageRect(region.X, region.Y, region.Width, region.Height));
        byte[] b = surface.Pixels;
        for (int y = 0; y < region.Height; y++)
        {
            for (int x = 0; x < region.Width; x++)
            {
                Rgba32 p = px[(y * region.Width) + x];
                int o = (((region.Y + y) * _width) + region.X + x) * 4;
                b[o] = p.R;
                b[o + 1] = p.G;
                b[o + 2] = p.B;
                b[o + 3] = p.A;
            }
        }
    }

    public RgbaImage ToImage()
    {
        Rgba32[] px = new Rgba32[_width * _height];
        _root.CopyTo<Rgba32>(px, new ImageRect(0, 0, _width, _height));
        byte[] bytes = new byte[px.Length * 4];
        for (int i = 0; i < px.Length; i++)
        {
            bytes[i * 4] = px[i].R;
            bytes[(i * 4) + 1] = px[i].G;
            bytes[(i * 4) + 2] = px[i].B;
            bytes[(i * 4) + 3] = px[i].A;
        }

        return new RgbaImage(_width, _height, bytes);
    }

    /// <summary>Draws the list, limited to a device-pixel region (the whole surface for a full render).</summary>
    public void Run(RectI region)
    {
        bool full = region.X == 0 && region.Y == 0 && region.Width == _surfaceW && region.Height == _surfaceH;
        PaintColor bg = _options.Background;
        Path regionPath = Path.Rectangle(region.X, region.Y, region.Width, region.Height);
        if (full)
        {
            _root.Fill(ToRgba(bg), 1f, OBlend.Normal, AlphaComposition.Src);
        }
        else
        {
            // Clear only the region, replacing what was there.
            _root.Fill(OneImage.Drawing.Brush.Solid(ToRgba(bg)), regionPath, false, IntersectionRule.NonZero, 1f, OBlend.Normal, AlphaComposition.Src);
            _clip = new ClipState(regionPath, true, new RectF(region.X, region.Y, region.Width, region.Height), false);
            _visible = new RectF(region.X / _dpr, region.Y / _dpr, region.Width / _dpr, region.Height / _dpr);
        }

        int n = _list.Count;
        for (int i = 0; i < n; i++)
        {
            if ((i & 127) == 0)
            {
                _ct.ThrowIfCancellationRequested();
            }

            DisplayItem it = _list[i];
            _next = -1;
            switch (it.Kind)
            {
                case ItemKind.FillRect:
                    if (Visible(it))
                    {
                        RectF r = it.Rect;
                        Brush b = MakeBrush(it);
                        DrawPath(b, Path.Rectangle(r.X, r.Y, r.Width, r.Height), IntersectionRule.NonZero);
                    }

                    break;
                case ItemKind.FillRoundedRect:
                    if (Visible(it))
                    {
                        Brush b = MakeBrush(it);
                        DrawPath(b, Shapes.RoundedRect(it.Rect, it.Radii), IntersectionRule.NonZero);
                    }

                    break;
                case ItemKind.FillPath:
                    if (Visible(it))
                    {
                        Brush b = MakeBrush(it);
                        DrawPath(b, Shapes.FromItem(it), it.FillRule == FillRule.EvenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero);
                    }

                    break;
                case ItemKind.Border:
                    if (Visible(it))
                    {
                        DrawBorder(it);
                    }

                    break;
                case ItemKind.BoxShadow:
                    if (Visible(it))
                    {
                        DrawShadow(it);
                    }

                    break;
                case ItemKind.Image:
                    if (Visible(it))
                    {
                        DrawImage(it);
                    }

                    break;
                case ItemKind.GlyphRun:
                    if (Visible(it))
                    {
                        DrawGlyphs(it);
                    }

                    break;
                case ItemKind.PushClipRect:
                    PushClip(it, Shapes.Rect(it.Rect), true, it.Rect);
                    break;
                case ItemKind.PushClipRoundedRect:
                    PushClip(it, Shapes.RoundedRect(it.Rect, it.Radii), false, default);
                    break;
                case ItemKind.PushClipPath:
                    PushClip(it, Shapes.FromItem(it), false, default, it.FillRule == FillRule.EvenOdd);
                    break;
                case ItemKind.PopClip:
                    Restore();
                    break;
                case ItemKind.PopTransform:
                    if (_sp > 0 && _stack[_sp - 1].Kind == ItemKind.PushTransform3D)
                    {
                        PopProjection();
                    }
                    else
                    {
                        Restore();
                    }

                    break;
                case ItemKind.PushTransform:
                    {
                        Save(ItemKind.PushTransform);
                        _ctm = it.Matrix * _ctm;

                        // A transform that cannot be inverted flattens its content to nothing, so nothing inside it is drawn.
                        if (!Matrix3x2.Invert(_ctm, out _))
                        {
                            _clip = EmptyClip;
                        }

                        break;
                    }

                case ItemKind.PushGroup:
                    PushGroup(i, it);
                    break;
                case ItemKind.PopGroup:
                    PopGroup();
                    break;
                case ItemKind.PushFilter:
                    PushFilter(i, it);
                    break;
                case ItemKind.PopFilter:
                    PopFilter();
                    break;
                case ItemKind.PushMask:
                    PushMask(i, it);
                    break;
                case ItemKind.PopMask:
                    PopMask();
                    break;
                case ItemKind.PushMaskLayer:
                    PushMaskLayer(i, it);
                    break;
                case ItemKind.PopMaskLayer:
                    PopMaskLayer();
                    break;
                case ItemKind.PushTransform3D:
                    PushProjection(i, it);
                    break;
                case ItemKind.BackdropFilter:
                    if (Visible(it))
                    {
                        BackdropFilter(it);
                    }

                    break;
                case ItemKind.SvgImage:
                    if (Visible(it))
                    {
                        DrawSvg(it);
                    }

                    break;
                default:
                    // Tags carry structure for other backends; they have no pixels.
                    break;
            }

            if (_next >= 0)
            {
                i = _next;
            }
        }
    }

    private bool Visible(in DisplayItem it)
    {
        if (_clip.IsEmpty)
        {
            return false;
        }

        RectF b = _in3D ? it.LayerBounds : it.RootBounds;
        return !b.IsEmpty && !RectF.Intersect(b, _visible).IsEmpty;
    }

    private static Rgba32 ToRgba(in PaintColor c) =>
        new(PaintColor.ToByte(c.R), PaintColor.ToByte(c.G), PaintColor.ToByte(c.B), PaintColor.ToByte(c.A));

    private Brush MakeBrush(in DisplayItem it)
    {
        Paint p = it.Paint;
        if (p.Kind == PaintKind.Solid)
        {
            return Brush.Solid(ToRgba(p.Color));
        }

        return new GradientBrush(it.Gradient, _ctm);
    }

    private void DrawPath(Brush brush, Path local, IntersectionRule rule, float opacity = 1f, OBlend blend = OBlend.Normal, AlphaComposition comp = AlphaComposition.SrcOver)
    {
        if (_clip.IsEmpty)
        {
            return;
        }

        _target.Fill(brush, local, true, rule, opacity, blend, comp, false, _ctm, _clip.Path, IntersectionRule.NonZero);
    }

    // ---------------------------------------------------------------- state stack

    private void Save(ItemKind kind)
    {
        if (_sp >= _stack.Length)
        {
            throw new RasterizationException("Nesting is deeper than the display list limit.");
        }

        _stack[_sp++] = new Frame { Kind = kind, Ctm = _ctm, Clip = _clip, Target = _target, Off = _off, W = _width, H = _height, Visible = _visible, In3D = _in3D };
    }

    private void Restore()
    {
        Frame f = _stack[--_sp];
        _ctm = f.Ctm;
        _clip = f.Clip;
    }

    private void PushClip(in DisplayItem it, Path local, bool isRect, RectF rect, bool evenOdd = false)
    {
        _ = it;
        Save(ItemKind.PushClipRect);
        if (_clip.IsEmpty)
        {
            return;
        }

        bool axis = (_ctm.M12 == 0f && _ctm.M21 == 0f) || (_ctm.M11 == 0f && _ctm.M22 == 0f);
        if (isRect && axis)
        {
            RectF dev = rect.Transform(_ctm);
            if (_clip.Path is null)
            {
                _clip = dev.IsEmpty ? EmptyClip : new ClipState(Path.Rectangle(dev.X, dev.Y, dev.Width, dev.Height), true, dev, false);
                return;
            }

            if (_clip.IsRect)
            {
                RectF r = RectF.Intersect(_clip.Rect, dev);
                _clip = r.IsEmpty ? EmptyClip : new ClipState(Path.Rectangle(r.X, r.Y, r.Width, r.Height), true, r, false);
                return;
            }
        }

        Path devPath = local.Transform(_ctm);
        if (_clip.Path is null)
        {
            _clip = new ClipState(evenOdd ? PathBoolean.Resolve(devPath, IntersectionRule.EvenOdd) : devPath, false, default, false);
            return;
        }

        Path merged;
        try
        {
            merged = PathBoolean.Intersect(_clip.Path, devPath, IntersectionRule.NonZero, evenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero);
        }
        catch (OneImage.ImageLimitException)
        {
            // A clip shape with coordinates beyond the boolean engine's range (a hostile clip-path or transform) keeps the clip it had.
            return;
        }

        _clip = new ClipState(merged, false, default, merged.IsEmpty);
    }

    private static ClipState EmptyClip => new(Path.Empty, true, default, true);

    private static (OBlend Blend, bool Plus) MapBlend(BlendMode m) => m switch
    {
        BlendMode.Multiply => (OBlend.Multiply, false),
        BlendMode.Screen => (OBlend.Screen, false),
        BlendMode.Overlay => (OBlend.Overlay, false),
        BlendMode.Darken => (OBlend.Darken, false),
        BlendMode.Lighten => (OBlend.Lighten, false),
        BlendMode.ColorDodge => (OBlend.ColorDodge, false),
        BlendMode.ColorBurn => (OBlend.ColorBurn, false),
        BlendMode.HardLight => (OBlend.HardLight, false),
        BlendMode.SoftLight => (OBlend.SoftLight, false),
        BlendMode.Difference => (OBlend.Difference, false),
        BlendMode.Exclusion => (OBlend.Exclusion, false),
        BlendMode.Hue => (OBlend.Hue, false),
        BlendMode.Saturation => (OBlend.Saturation, false),
        BlendMode.Color => (OBlend.Color, false),
        BlendMode.Luminosity => (OBlend.Luminosity, false),
        BlendMode.PlusLighter => (OBlend.Normal, true),
        _ => (OBlend.Normal, false),
    };

    // ---------------------------------------------------------------- images and glyphs

    private void DrawImage(in DisplayItem it)
    {
        ImageHandle h = it.Image;
        if (!_images.TryGetValue(h.Value, out RgbaImage? img))
        {
            img = _options.Images?.Resolve(h);
            _images[h.Value] = img;
        }

        RectF dst = it.ImageDestination;
        if (img is null || dst.IsEmpty)
        {
            return;
        }

        RectF src = it.ImageSource;
        if (it.IsTiledImage)
        {
            RectF area = it.ImageTileArea;
            if (area.IsEmpty)
            {
                return;
            }

            Brush tiled = new ImageSamplerBrush(img, src, dst, it.ImageTileGapX, it.ImageTileGapY, _ctm, it.Sampling == ImageSampling.Smooth);
            DrawPath(tiled, Shapes.Rect(area), IntersectionRule.NonZero);
            return;
        }

        float sw = src.IsEmpty ? img.Width : src.Width;
        float sh = src.IsEmpty ? img.Height : src.Height;
        float sx = src.IsEmpty ? 0f : src.X;
        float sy = src.IsEmpty ? 0f : src.Y;
        float kx = dst.Width / sw, ky = dst.Height / sh;
        Matrix3x2 srcToLocal = new(kx, 0, 0, ky, dst.X - (kx * sx), dst.Y - (ky * sy));
        Brush brush = new ImageSamplerBrush(img, src, srcToLocal * _ctm, it.Sampling == ImageSampling.Smooth);
        DrawPath(brush, Shapes.Rect(dst), IntersectionRule.NonZero);
    }

    private void DrawGlyphs(in DisplayItem it)
    {
        GlyphRunView run = it.GlyphRun;
        ReadOnlySpan<int> ids = run.GlyphIds;
        if (ids.Length == 0 || run.Color.IsTransparent)
        {
            return;
        }

        FontFace face = run.Face;
        float scale = run.FontSize / Math.Max(1, face.UnitsPerEm);
        PathBuilder pb = new();
        GlyphSink sink = new(pb);
        ReadOnlySpan<float> xs = run.X, ys = run.Y;
        for (int i = 0; i < ids.Length; i++)
        {
            GlyphOutline outline = face.GetOutline(ids[i]);
            if (outline.IsEmpty)
            {
                continue;
            }

            // Font units have y up; the screen has y down.
            Matrix3x2 m = new(scale, 0, 0, -scale, run.OriginX + xs[i], run.OriginY + ys[i]);
            outline.Write(sink, m);
        }

        Path path = pb.Build();
        if (path.IsEmpty)
        {
            return;
        }

        if (run.Blur > 0f)
        {
            // A text shadow: the glyph shapes blurred into a coverage mask, as box shadows are (Text Decoration 4 / Backgrounds 3 §7.2).
            float sigma = Math.Min(run.Blur * 0.5f * _dpr, MaxSigma);
            int margin = (int)MathF.Ceiling(3f * sigma);
            RectI region = LayerRegion(it.LocalBounds.Transform(_ctm), margin);
            if (region.IsEmpty)
            {
                return;
            }

            Path clip = _clip.Path ?? Path.Rectangle(region.X, region.Y, region.Width, region.Height);
            byte[] mask = BlurredMask(region, sigma, white: false, path, null, _ctm);
            FillMask(run.Color, mask, region, clip);
            return;
        }

        Brush brush = Brush.Solid(ToRgba(run.Color));
        DrawPath(brush, path, IntersectionRule.NonZero);
    }

    private sealed class GlyphSink(PathBuilder b) : IOutlineSink
    {
        public void MoveTo(float x, float y) => b.MoveTo(x, y);

        public void LineTo(float x, float y) => b.LineTo(x, y);

        public void QuadTo(float cx, float cy, float x, float y) => b.QuadraticBezierTo(cx, cy, x, y);

        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y) => b.CubicBezierTo(c1x, c1y, c2x, c2y, x, y);

        public void Close() => b.Close();
    }
}
