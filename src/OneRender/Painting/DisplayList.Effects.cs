using System.Numerics;

namespace OneRender.Painting;

/// <summary>The filter functions a <see cref="DisplayList"/> can carry (Filter Effects 1 §5), after the painter resolved their arguments.</summary>
public enum FilterOpKind : byte
{
    /// <summary>Gaussian blur; <see cref="FilterOp.A"/> is the standard deviation in CSS px.</summary>
    Blur = 0,

    /// <summary>Linear slope on each channel; <see cref="FilterOp.A"/> is the factor (1 leaves it unchanged).</summary>
    Brightness = 1,

    /// <summary>Contrast around 0.5; <see cref="FilterOp.A"/> is the factor.</summary>
    Contrast = 2,

    /// <summary>Partial grayscale; <see cref="FilterOp.A"/> is 0 to 1.</summary>
    Grayscale = 3,

    /// <summary>Hue rotation; <see cref="FilterOp.A"/> is the angle in radians.</summary>
    HueRotate = 4,

    /// <summary>Partial inversion; <see cref="FilterOp.A"/> is 0 to 1.</summary>
    Invert = 5,

    /// <summary>Multiplies alpha; <see cref="FilterOp.A"/> is 0 to 1.</summary>
    Opacity = 6,

    /// <summary>Saturation; <see cref="FilterOp.A"/> is the factor (1 unchanged, 0 gray).</summary>
    Saturate = 7,

    /// <summary>Partial sepia; <see cref="FilterOp.A"/> is 0 to 1.</summary>
    Sepia = 8,

    /// <summary>A blurred, offset, colored copy of the alpha under the content: A is dx, B is dy, C is the blur standard deviation, in CSS px.</summary>
    DropShadow = 9,
}

/// <summary>One step of a filter chain.</summary>
/// <param name="Kind">The function.</param>
/// <param name="A">First argument; see <see cref="FilterOpKind"/>.</param>
/// <param name="B">Second argument (drop shadow: dy).</param>
/// <param name="C">Third argument (drop shadow: blur standard deviation).</param>
/// <param name="Color">The shadow color of a drop shadow.</param>
public readonly record struct FilterOp(FilterOpKind Kind, float A, float B, float C, PaintColor Color)
{
    /// <summary>Creates an op with one argument.</summary>
    public FilterOp(FilterOpKind kind, float a)
        : this(kind, a, 0f, 0f, default)
    {
    }
}

/// <summary>How a mask layer turns its pixels into coverage (Masking 1 §6.2).</summary>
public enum MaskLayerMode : byte
{
    /// <summary>The layer's alpha is the coverage.</summary>
    Alpha = 0,

    /// <summary>The layer's luminance times its alpha is the coverage.</summary>
    Luminance = 1,
}

/// <summary>How a mask layer combines with the layers painted before it (Masking 1 §6.7, Porter-Duff on coverage).</summary>
public enum MaskCompositeOp : byte
{
    /// <summary>Source over: <c>s + d(1 − s)</c>.</summary>
    Add = 0,

    /// <summary>Source out: <c>s(1 − d)</c>.</summary>
    Subtract = 1,

    /// <summary>Source in: <c>s·d</c>.</summary>
    Intersect = 2,

    /// <summary>Xor: <c>s(1 − d) + d(1 − s)</c>.</summary>
    Exclude = 3,
}

public sealed partial class DisplayList
{
    /// <summary>The most operations in one filter chain.</summary>
    public const int MaxFilterOps = 32;

    /// <summary>The largest blur standard deviation a list accepts, in CSS px.</summary>
    public const float MaxBlur = 4096f;

    /// <summary>The most layers of one mask.</summary>
    public const int MaxMaskLayers = 16;

    /// <summary>The largest SVG markup a list accepts, in UTF-16 units.</summary>
    public const int MaxSvgChars = 1 << 20;

    private const int FilterStride = 8;

    // ---------------------------------------------------------------- bounds helpers used by the core

    private static RectF Inflate(in RectF r, float by) =>
        by <= 0f ? r : new RectF(r.X - by, r.Y - by, r.Width + (2f * by), r.Height + (2f * by));

    // Bounds of a rectangle in the current 3D plane after the perspective projection. A corner at or behind the viewer
    // (w ≤ 0) makes the bounds unbounded, which is conservative (CSS Transforms 2 §13).
    private static RectF Project(in RectF rel, in PushState st)
    {
        if (!st.Has3D || rel.IsEmpty)
        {
            return rel;
        }

        Matrix4x4 m = st.Outer;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float x = (i & 1) == 0 ? rel.X : rel.Right;
            float y = (i & 2) == 0 ? rel.Y : rel.Bottom;
            float w = (x * m.M14) + (y * m.M24) + m.M44;
            if (!(w > 1e-4f))
            {
                return RectF.Unbounded;
            }

            float px = ((x * m.M11) + (y * m.M21) + m.M41) / w;
            float py = ((x * m.M12) + (y * m.M22) + m.M42) / w;
            if (!float.IsFinite(px) || !float.IsFinite(py))
            {
                return RectF.Unbounded;
            }

            minX = Math.Min(minX, px);
            maxX = Math.Max(maxX, px);
            minY = Math.Min(minY, py);
            maxY = Math.Max(maxY, py);
        }

        return new RectF(minX, minY, maxX - minX, maxY - minY);
    }

    private static void SetBounds(ref ItemRecord rec, in PushState st)
    {
        RectF rel = Inflate(rec.Local.Transform(st.Ctm), st.Expand);
        rec.Rel = rel;
        rec.Device = RectF.Intersect(Project(rel, in st), st.Clip);
    }

    // ---------------------------------------------------------------- filters

    /// <summary>
    /// Starts a filter scope (Filter Effects 1 §3): everything until <see cref="PopFilter"/> is painted into a layer, the chain is applied
    /// to the layer in order, and the result is composited like any content. Clips outside the scope apply to the filtered result.
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="ops">One to <see cref="MaxFilterOps"/> operations.</param>
    /// <exception cref="DisplayListLimitException">The chain is too long or a blur is larger than <see cref="MaxBlur"/>.</exception>
    public void PushFilter(ItemId id, ReadOnlySpan<FilterOp> ops)
    {
        CheckOps(ops);
        float outset = OutsetOf(ops);
        Begin(ItemKind.PushFilter, id, RectF.Empty, 0, 2 + (ops.Length * FilterStride));
        PutF(outset);
        PutF(ops.Length);
        PutOps(ops);
        _requiredMinor = Math.Max(_requiredMinor, 2);
        FinishPush(_count - 1);
    }

    /// <summary>Ends the innermost filter scope and applies the chain.</summary>
    /// <exception cref="DisplayListException">The innermost open push is not a filter.</exception>
    public void PopFilter() => Pop(ItemKind.PopFilter, ItemKind.PushFilter);

    /// <summary>
    /// Filters what is already painted under a shape (Filter Effects 2 §2, <c>backdrop-filter</c>): the pixels of the current
    /// layer inside the rounded rectangle are replaced by the filtered pixels. Painted before the element's own background.
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="rect">The area, usually the border box.</param>
    /// <param name="radii">Corner radii of the area.</param>
    /// <param name="ops">One to <see cref="MaxFilterOps"/> operations.</param>
    public void BackdropFilter(ItemId id, in RectF rect, in CornerRadii radii, ReadOnlySpan<FilterOp> ops)
    {
        CheckRect(rect);
        CheckOps(ops);
        CornerRadii r = ConstrainRadii(rect, radii);
        Begin(ItemKind.BackdropFilter, id, rect, 0, 14 + (ops.Length * FilterStride));
        PutRect(rect);
        PutRadii(r);
        PutF(ops.Length);
        PutOps(ops);
        _requiredMinor = Math.Max(_requiredMinor, 2);
        Finish(_count - 1);
    }

    private static void CheckOps(ReadOnlySpan<FilterOp> ops)
    {
        if (ops.Length == 0 || ops.Length > MaxFilterOps)
        {
            throw new DisplayListLimitException("A filter chain has 1 to " + MaxFilterOps + " operations.");
        }

        foreach (FilterOp op in ops)
        {
            if ((int)op.Kind > (int)FilterOpKind.DropShadow)
            {
                throw new ArgumentOutOfRangeException(nameof(ops), "Unknown filter operation.");
            }

            Coord(op.A);
            Coord(op.B);
            Coord(op.C);
            if (op.Kind == FilterOpKind.Blur && op.A > MaxBlur || op.Kind == FilterOpKind.DropShadow && op.C > MaxBlur)
            {
                throw new DisplayListLimitException("A blur standard deviation is limited to " + MaxBlur + " px.");
            }
        }
    }

    private static float OutsetOf(ReadOnlySpan<FilterOp> ops)
    {
        float total = 0f;
        foreach (FilterOp op in ops)
        {
            if (op.Kind == FilterOpKind.Blur)
            {
                total += 3f * Math.Max(0f, op.A);
            }
            else if (op.Kind == FilterOpKind.DropShadow)
            {
                total += (3f * Math.Max(0f, op.C)) + MathF.Abs(op.A) + MathF.Abs(op.B);
            }
        }

        return total > 0f ? total + 1f : 0f;
    }

    private void PutOps(ReadOnlySpan<FilterOp> ops)
    {
        foreach (FilterOp op in ops)
        {
            PutF((float)op.Kind);
            PutF(op.A);
            PutF(op.B);
            PutF(op.C);
            PutColor(ClampColor(op.Color));
        }
    }

    // ---------------------------------------------------------------- masks

    /// <summary>
    /// Starts a masked scope (Masking 1 §6). Directly inside, zero or more <see cref="PushMaskLayer"/> scopes describe the mask,
    /// bottom layer first; everything after them is the masked content. At <see cref="PopMask"/> the content is multiplied by the
    /// combined coverage. A mask with no layers hides everything.
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    public void PushMask(ItemId id)
    {
        Begin(ItemKind.PushMask, id, RectF.Empty, 0, 0);
        _requiredMinor = Math.Max(_requiredMinor, 2);
        FinishPush(_count - 1);
    }

    /// <summary>Ends the masked scope and applies the mask.</summary>
    /// <exception cref="DisplayListException">The innermost open push is not a mask.</exception>
    public void PopMask() => Pop(ItemKind.PopMask, ItemKind.PushMask);

    /// <summary>
    /// Starts one mask layer: the items up to <see cref="PopMaskLayer"/> are painted into a transparent layer whose alpha or luminance
    /// is the layer's coverage, combined with the layers before it by <paramref name="op"/>. Only valid directly inside <see cref="PushMask"/>.
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="mode">How pixels become coverage.</param>
    /// <param name="op">How the coverage combines with the layers below.</param>
    /// <exception cref="DisplayListException">The enclosing scope is not a mask.</exception>
    /// <exception cref="DisplayListLimitException">The mask has <see cref="MaxMaskLayers"/> layers already.</exception>
    public void PushMaskLayer(ItemId id, MaskLayerMode mode, MaskCompositeOp op)
    {
        if (_top < 0 || _items[_top].Kind != ItemKind.PushMask)
        {
            throw new DisplayListException("A mask layer must be directly inside a mask.");
        }

        if ((int)mode > 1 || (int)op > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ref ItemRecord mask = ref _items[_top];
        if (++mask.A > MaxMaskLayers)
        {
            throw new DisplayListLimitException("A mask has at most " + MaxMaskLayers + " layers.");
        }

        Begin(ItemKind.PushMaskLayer, id, RectF.Empty, 0, 0);
        _items[_count - 1].A = (int)mode;
        _items[_count - 1].B = (int)op;
        H((ulong)mode);
        H((ulong)op);
        FinishPush(_count - 1);
    }

    /// <summary>Ends a mask layer.</summary>
    /// <exception cref="DisplayListException">The innermost open push is not a mask layer.</exception>
    public void PopMaskLayer() => Pop(ItemKind.PopMaskLayer, ItemKind.PushMaskLayer);

    // ---------------------------------------------------------------- SVG

    /// <summary>
    /// Draws an inline SVG document. The document is rendered as an image of exactly <paramref name="rect"/>'s size: the backend
    /// replaces the root's <c>width</c> and <c>height</c> with it and keeps <c>viewBox</c> and <c>preserveAspectRatio</c>, so
    /// the SVG rules fit the drawing into the rectangle. <c>currentColor</c> is <paramref name="color"/>.
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="rect">Where the SVG viewport is, in CSS px.</param>
    /// <param name="markup">The serialized <c>&lt;svg&gt;</c> element, at most <see cref="MaxSvgChars"/> characters.</param>
    /// <param name="color">The value of <c>currentColor</c>.</param>
    public void SvgImage(ItemId id, in RectF rect, string markup, in PaintColor color)
    {
        ArgumentNullException.ThrowIfNull(markup);
        CheckRect(rect);
        if (markup.Length > MaxSvgChars)
        {
            throw new DisplayListLimitException("SVG markup is limited to " + MaxSvgChars + " characters.");
        }

        if (_stringCount + 1 > DisplayListLimits.MaxStrings)
        {
            throw new DisplayListLimitException("Too many strings in one list.");
        }

        Begin(ItemKind.SvgImage, id, rect, 0, 8);
        PutRect(rect);
        PutColor(ClampColor(color));
        _items[_count - 1].A = StoreString(markup);
        _requiredMinor = Math.Max(_requiredMinor, 2);
        Finish(_count - 1);
    }

    // ---------------------------------------------------------------- 3D

    /// <summary>Replaces the matrix of a <see cref="ItemKind.PushTransform3D"/> in place. Allocates nothing.</summary>
    /// <param name="id">The id of the 3D transform item.</param>
    /// <param name="matrix">The new matrix; every entry must be finite and within the coordinate range.</param>
    /// <param name="damage">Receives the damaged areas; may be null.</param>
    public void PatchTransform3D(ItemId id, in Matrix4x4 matrix, DamageRegion? damage = null)
    {
        Check4(matrix);
        int i = FindClosed(id, ItemKind.PushTransform3D);
        AddRange(i, damage);
        int n = _items[i].Num;
        PutAt(n, matrix);
        ulong h = Mix(HashSeed, (ulong)ItemKind.PushTransform3D);
        for (int k = 0; k < 16; k++)
        {
            h = Mix(h, Bits(_num[n + k]));
        }

        _items[i].ContentHash = h;
        Rewalk(i);
        AddRange(i, damage);
    }

    private void PutAt(int n, in Matrix4x4 m)
    {
        _num[n] = m.M11;
        _num[n + 1] = m.M12;
        _num[n + 2] = m.M13;
        _num[n + 3] = m.M14;
        _num[n + 4] = m.M21;
        _num[n + 5] = m.M22;
        _num[n + 6] = m.M23;
        _num[n + 7] = m.M24;
        _num[n + 8] = m.M31;
        _num[n + 9] = m.M32;
        _num[n + 10] = m.M33;
        _num[n + 11] = m.M34;
        _num[n + 12] = m.M41;
        _num[n + 13] = m.M42;
        _num[n + 14] = m.M43;
        _num[n + 15] = m.M44;
    }

    private static void Check4(in Matrix4x4 m)
    {
        Span<float> v = [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
        foreach (float f in v)
        {
            Coord(f);
        }

        // M44 is the w of the page origin, which can be negative while the box itself is in front of the viewer; w is checked per point.
    }

    private void PushTransform3DCore(ItemId id, in Matrix4x4 matrix)
    {
        Check4(matrix);
        Begin(ItemKind.PushTransform3D, id, RectF.Empty, 0, 16);
        int n = _numCount;
        for (int k = 0; k < 16; k++)
        {
            _num[_numCount++] = 0f;
        }

        PutAt(n, matrix);
        for (int k = 0; k < 16; k++)
        {
            H(Bits(_num[n + k]));
        }

        _requiredMinor = Math.Max(_requiredMinor, 2);
        FinishPush(_count - 1);
    }

    private bool InsideProjection() => _top >= 0 && StateOf(_top).Has3D;
}

public readonly partial struct DisplayItem
{
    /// <summary>
    /// Bounds in the space of the nearest enclosing 3D transform (root space when none), widened by enclosing filters and not clipped.
    /// A backend sizes layers with it.
    /// </summary>
    public RectF LayerBounds => Rec.Rel;

    /// <summary>The 4×4 matrix of a <see cref="ItemKind.PushTransform3D"/> (row-vector layout of <see cref="Matrix4x4"/>); for <see cref="ItemKind.PushTransform"/> the 2D matrix lifted; identity otherwise.</summary>
    public Matrix4x4 Matrix4
    {
        get
        {
            ReadOnlySpan<float> n = Num;
            return Rec.Kind switch
            {
                ItemKind.PushTransform3D => new Matrix4x4(n[0], n[1], n[2], n[3], n[4], n[5], n[6], n[7], n[8], n[9], n[10], n[11], n[12], n[13], n[14], n[15]),
                ItemKind.PushTransform => new Matrix4x4(n[0], n[1], 0, 0, n[2], n[3], 0, 0, 0, 0, 1, 0, n[4], n[5], 0, 1),
                _ => Matrix4x4.Identity,
            };
        }
    }

    /// <summary>Number of operations in the chain of a <see cref="ItemKind.PushFilter"/> or <see cref="ItemKind.BackdropFilter"/>; 0 otherwise.</summary>
    public int FilterOpCount => Rec.Kind == ItemKind.PushFilter ? (int)Num[1] : Rec.Kind == ItemKind.BackdropFilter ? (int)Num[12] : 0;

    /// <summary>The operation at <paramref name="index"/> of a filter chain.</summary>
    /// <param name="index">0 to <see cref="FilterOpCount"/> - 1.</param>
    /// <returns>The operation.</returns>
    public FilterOp GetFilterOp(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)FilterOpCount);
        int at = (Rec.Kind == ItemKind.PushFilter ? 2 : 13) + (index * 8);
        ReadOnlySpan<float> n = Num;
        return new FilterOp((FilterOpKind)(int)n[at], n[at + 1], n[at + 2], n[at + 3], new PaintColor(n[at + 4], n[at + 5], n[at + 6], n[at + 7]));
    }

    /// <summary>How a <see cref="ItemKind.PushMaskLayer"/> turns pixels into coverage.</summary>
    public MaskLayerMode MaskMode => Rec.Kind == ItemKind.PushMaskLayer ? (MaskLayerMode)Rec.A : MaskLayerMode.Alpha;

    /// <summary>How a <see cref="ItemKind.PushMaskLayer"/> combines with the layers below it.</summary>
    public MaskCompositeOp MaskComposite => Rec.Kind == ItemKind.PushMaskLayer ? (MaskCompositeOp)Rec.B : MaskCompositeOp.Add;

    /// <summary>The markup of an <see cref="ItemKind.SvgImage"/>, or null.</summary>
    public string? SvgMarkup => Rec.Kind == ItemKind.SvgImage ? _list.StringAt(Rec.A) : null;

    /// <summary>The <c>currentColor</c> of an <see cref="ItemKind.SvgImage"/>.</summary>
    public PaintColor SvgColor => Rec.Kind == ItemKind.SvgImage ? ReadColor(4) : default;

    /// <summary>
    /// Whether a PDF writer can express this item natively. True for opacity and blend groups, mask scopes (soft masks) and a filter
    /// chain made only of <see cref="FilterOpKind.Opacity"/>; false for the other filters, backdrop filters, perspective projections and
    /// SVG images, which a PDF backend draws as a rasterized island with a diagnostic.
    /// </summary>
    public bool PdfNative
    {
        get
        {
            switch (Rec.Kind)
            {
                case ItemKind.PushFilter:
                    for (int i = 0; i < FilterOpCount; i++)
                    {
                        if (GetFilterOp(i).Kind != FilterOpKind.Opacity)
                        {
                            return false;
                        }
                    }

                    return true;
                case ItemKind.BackdropFilter:
                case ItemKind.PushTransform3D:
                case ItemKind.SvgImage:
                    return false;
                default:
                    return true;
            }
        }
    }
}
