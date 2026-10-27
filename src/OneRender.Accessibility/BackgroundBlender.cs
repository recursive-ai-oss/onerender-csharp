using OneRender.Painting;

namespace OneRender.Accessibility;

/// <summary>
/// Works out, from a display list, what is behind a point at the moment a given item is painted: the fills painted earlier that
/// cover the point, composited over the white of the canvas, and the opacity of the groups the item is painted in.
/// </summary>
/// <remarks>
/// Alpha is quantized to 8 bits and each channel rounded to the nearest integer when two colors are mixed, which is how a
/// browser's compositor mixes 8-bit colors. A gradient counts as its first stop. An image that covers the point makes the
/// color unknown, since its pixels are not in the list as colors.
/// </remarks>
internal sealed class BackgroundBlender
{
    private readonly DisplayList _list;
    private readonly int _scanLimit;
    private readonly int[] _enclosing;   // innermost open group before item i, as the index of its PushGroup item; -1 for none
    private readonly float[] _opacity;   // for a PushGroup item: its opacity
    private readonly int[] _mark;
    private readonly byte[] _scope;      // for a scope push: 0 = plain group, 1 = changes pixels in ways a color can't express, 2 = mask content
    private int _stamp;
    private (byte R, byte G, byte B, int A)[] _candidates = new (byte, byte, byte, int)[32];

    public BackgroundBlender(DisplayList list, int scanLimit)
    {
        _list = list;
        _scanLimit = scanLimit;
        int n = list.Count;
        _enclosing = new int[n + 1];
        _opacity = new float[n];
        _mark = new int[n];
        _scope = new byte[n];
        // One pass keeps the stack of open groups; the display list's own depth limit bounds it.
        int[] open = new int[16];
        int sp = 0;
        for (int i = 0; i < n; i++)
        {
            _enclosing[i] = sp == 0 ? -1 : open[sp - 1];
            ItemKind kind = list[i].Kind;
            if (kind is ItemKind.PushGroup or ItemKind.PushFilter or ItemKind.PushMask or ItemKind.PushMaskLayer)
            {
                // Filters, masks and non-normal blend modes (Compositing 1 §3, Filter Effects 1 §5, CSS Masking 1 §7) change the
                // pixels of everything flattened into them in ways a single color can't express; mask-layer content is never shown.
                _opacity[i] = kind == ItemKind.PushGroup ? list[i].Opacity : 1f;
                _scope[i] = kind switch
                {
                    ItemKind.PushMaskLayer => 2,
                    ItemKind.PushGroup => list[i].Blend == BlendMode.Normal ? (byte)0 : (byte)1,
                    _ => 1,
                };
                if (sp == open.Length)
                {
                    Array.Resize(ref open, sp * 2);
                }

                open[sp++] = i;
            }
            else if (kind is ItemKind.PopGroup or ItemKind.PopFilter or ItemKind.PopMask or ItemKind.PopMaskLayer && sp > 0)
            {
                sp--;
            }
        }

        _enclosing[n] = sp == 0 ? -1 : open[sp - 1];
    }

    /// <summary>The product of the opacities of the groups open at an item index.</summary>
    public float OpacityAt(int item)
    {
        float o = 1f;
        for (int g = Enclosing(item); g >= 0; g = _enclosing[g])
        {
            o *= _opacity[g];
        }

        return o;
    }

    private int Enclosing(int item) => (uint)item <= (uint)_list.Count ? _enclosing[item] : -1;

    /// <summary>
    /// The blended color behind (<paramref name="x"/>, <paramref name="y"/>) when item <paramref name="item"/> is painted, as
    /// <c>rgb(r, g, b)</c>; null when an image is in the way.
    /// </summary>
    public string? BlendedAt(int item, float x, float y)
    {
        if ((uint)item > (uint)_list.Count)
        {
            return null;
        }

        // Groups the item is painted in: fills inside them are composited together with the item, so their opacity is the
        // text's, not the background's. Fills in groups that closed before the item are flattened with the group's opacity.
        _stamp++;
        for (int g = Enclosing(item); g >= 0; g = _enclosing[g])
        {
            _mark[g] = _stamp;
        }

        int count = 0;
        bool unknown = false;
        int stop = Math.Max(0, item - _scanLimit);
        for (int i = item - 1; i >= stop; i--)
        {
            DisplayItem it = _list[i];
            ItemKind kind = it.Kind;
            if (kind is not (ItemKind.FillRect or ItemKind.FillRoundedRect or ItemKind.FillPath or ItemKind.Image
                or ItemKind.SvgImage or ItemKind.BackdropFilter))
            {
                continue;
            }

            RectF r = it.RootBounds;
            if (!(x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom))
            {
                continue;
            }

            bool skip = false;
            for (int g = _enclosing[i]; g >= 0 && _mark[g] != _stamp; g = _enclosing[g])
            {
                if (_scope[g] == 2)
                {
                    skip = true;
                    break;
                }

                if (_scope[g] == 1)
                {
                    unknown = true;
                }
            }

            if (skip)
            {
                continue;
            }

            if (unknown || kind is ItemKind.Image or ItemKind.SvgImage or ItemKind.BackdropFilter)
            {
                unknown = true;
                break;
            }

            Paint paint = it.Paint;
            PaintColor c = paint.Kind == PaintKind.Solid ? paint.Color : FirstStop(it);
            float group = 1f;
            for (int g = _enclosing[i]; g >= 0 && _mark[g] != _stamp; g = _enclosing[g])
            {
                group *= _opacity[g];
            }

            int a = (int)Math.Clamp((c.A * group * 255f) + 0.5f, 0f, 255f);
            if (a == 0)
            {
                continue;
            }

            if (count == _candidates.Length)
            {
                Array.Resize(ref _candidates, count * 2);
            }

            _candidates[count++] = (PaintColor.ToByte(c.R), PaintColor.ToByte(c.G), PaintColor.ToByte(c.B), a);
            if (a == 255)
            {
                break;
            }
        }

        if (unknown)
        {
            return null;
        }

        int cr = 255, cg = 255, cb = 255;
        for (int k = count - 1; k >= 0; k--)
        {
            (byte r, byte g, byte b, int a) = _candidates[k];
            cr = Mix(r, cr, a);
            cg = Mix(g, cg, a);
            cb = Mix(b, cb, a);
        }

        return FormattableString.Invariant($"rgb({cr}, {cg}, {cb})");
    }

    private static PaintColor FirstStop(DisplayItem it)
    {
        GradientView gradient = it.Gradient;
        return gradient.StopCount > 0 ? gradient.GetStop(0).Color : default;
    }

    private static int Mix(int source, int backdrop, int alpha) => ((source * alpha) + (backdrop * (255 - alpha)) + 127) / 255;
}
