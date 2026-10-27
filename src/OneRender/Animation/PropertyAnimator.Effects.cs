using System.Numerics;
using System.Text;
using OneRender.Painting;
using OneRender.Style;

namespace OneRender.Animation;

internal static partial class PropertyAnimator
{
    // ---------------------------------------------------------------------------------------------------------------- transforms

    private static TransformSpec TransformOf(in ComputedValues v, PropertyId id) => id switch
    {
        PropertyId.Transform => v.Transform,
        PropertyId.Translate => v.Translate,
        PropertyId.Rotate => v.Rotate,
        _ => v.Scale,
    };

    private static void SetTransform(ref ComputedValues v, PropertyId id, TransformSpec s)
    {
        switch (id)
        {
            case PropertyId.Transform: v.Transform = s; break;
            case PropertyId.Translate: v.Translate = s; break;
            case PropertyId.Rotate: v.Rotate = s; break;
            default: v.Scale = s; break;
        }
    }

    private static int SlotOf(PropertyId id) => id switch { PropertyId.Transform => 0, PropertyId.Translate => 1, PropertyId.Rotate => 2, _ => 3 };

    /// <summary>
    /// Interpolates two transform lists (CSS Transforms 1 §9, Transforms 2 §"Interpolation of transform functions"): function by function
    /// when the lists match (a shorter list is padded with identity functions), otherwise through matrix decomposition.
    /// </summary>
    internal static TransformSpec InterpolateTransform(PropertyId id, TransformSpec a, TransformSpec b, double t, in InterpolationContext ctx, InterpolationScratch? scratch)
    {
        if (a.IsNone && b.IsNone)
        {
            return a;
        }

        TransformFunction[] fa = a.Functions, fb = b.Functions;
        int n = Math.Max(fa.Length, fb.Length);
        bool match = true;
        for (int i = 0; i < n && match; i++)
        {
            if (i < fa.Length && i < fb.Length)
            {
                match = Compatible(fa[i], fb[i]);
            }
        }

        int slot = SlotOf(id);
        TransformSpec? work = scratch?.Working[slot];
        if (!match)
        {
            TransformFunction? only = work is { Functions.Length: 1 } && work.Functions[0].Kind == TransformKind.Matrix ? work.Functions[0] : null;
            Span<double> ma = stackalloc double[16];
            Span<double> mb = stackalloc double[16];
            Span<double> mr = stackalloc double[16];
            TransformMath.Load(a.ToMatrix(ctx.ReferenceWidth, ctx.ReferenceHeight), ma);
            TransformMath.Load(b.ToMatrix(ctx.ReferenceWidth, ctx.ReferenceHeight), mb);
            if (!TransformMath.InterpolateMatrices(ma, mb, t, mr))
            {
                return t < 0.5 ? a : b;
            }

            if (only is null)
            {
                work = new TransformSpec([new TransformFunction { Kind = TransformKind.Matrix }], string.Empty);
                if (scratch is not null)
                {
                    scratch.Working[slot] = work;
                }

                only = work.Functions[0];
            }

            only.M = TransformMath.Store(mr);
            work!.Touch();
            return work;
        }

        bool reusable = work is not null && work.Functions.Length == n;
        if (reusable)
        {
            for (int i = 0; i < n && reusable; i++)
            {
                reusable = work!.Functions[i].Kind == KindOfPair(i < fa.Length ? fa[i] : null, i < fb.Length ? fb[i] : null);
            }
        }

        if (!reusable)
        {
            var fresh = new TransformFunction[n];
            for (int i = 0; i < n; i++)
            {
                fresh[i] = new TransformFunction { Kind = KindOfPair(i < fa.Length ? fa[i] : null, i < fb.Length ? fb[i] : null) };
            }

            work = new TransformSpec(fresh, string.Empty);
            if (scratch is not null)
            {
                scratch.Working[slot] = work;
            }
        }

        for (int i = 0; i < n; i++)
        {
            TransformFunction? x = i < fa.Length ? fa[i] : null;
            TransformFunction? y = i < fb.Length ? fb[i] : null;
            LerpFunction(work!.Functions[i], x, y, t, in ctx);
        }

        work!.Touch();
        return work;
    }

    private static TransformKind KindOfPair(TransformFunction? a, TransformFunction? b) => (a ?? b)!.Kind;

    // CSS Transforms 1 §9: functions of the same primitive type interpolate directly; others make the lists fall back to matrices.
    private static bool Compatible(TransformFunction a, TransformFunction b)
    {
        if (a.Kind != b.Kind)
        {
            return false;
        }

        switch (a.Kind)
        {
            case TransformKind.Rotate:
                if (a.Angle == 0 || b.Angle == 0)
                {
                    return true;
                }

                return SameAxis(a, b);
            case TransformKind.Perspective:
                return true;
            default:
                return true;
        }
    }

    private static bool SameAxis(TransformFunction a, TransformFunction b)
    {
        double la = Math.Sqrt((a.A * a.A) + (a.B * a.B) + (a.C * a.C));
        double lb = Math.Sqrt((b.A * b.A) + (b.B * b.B) + (b.C * b.C));
        if (la == 0 || lb == 0)
        {
            return true;
        }

        return Math.Abs((a.A / la) - (b.A / lb)) < 1e-9 && Math.Abs((a.B / la) - (b.B / lb)) < 1e-9 && Math.Abs((a.C / la) - (b.C / lb)) < 1e-9;
    }

    private static void LerpFunction(TransformFunction r, TransformFunction? a, TransformFunction? b, double t, in InterpolationContext ctx)
    {
        switch (r.Kind)
        {
            case TransformKind.Translate:
            {
                Length ax = a?.X ?? Length.Zero, ay = a?.Y ?? Length.Zero, bx = b?.X ?? Length.Zero, by = b?.Y ?? Length.Zero;
                r.X = LerpLength(ax, bx, t, false);
                r.Y = LerpLength(ay, by, t, false);
                r.Z = Lerp(a?.Z ?? 0, b?.Z ?? 0, t);
                break;
            }

            case TransformKind.Scale:
                r.A = Lerp(a?.A ?? 1, b?.A ?? 1, t);
                r.B = Lerp(a?.B ?? 1, b?.B ?? 1, t);
                r.C = Lerp(a?.C ?? 1, b?.C ?? 1, t);
                break;
            case TransformKind.Rotate:
            {
                // The axis comes from whichever side has a non-zero angle (an identity rotation takes the other's axis).
                TransformFunction axis = a is not null && a.Angle != 0 ? a : b ?? a!;
                r.A = axis.A;
                r.B = axis.B;
                r.C = axis.C;
                r.Angle = Lerp(a?.Angle ?? 0, b?.Angle ?? 0, t);
                break;
            }

            case TransformKind.Skew:
                r.A = Lerp(a?.A ?? 0, b?.A ?? 0, t);
                r.B = Lerp(a?.B ?? 0, b?.B ?? 0, t);
                break;
            case TransformKind.Perspective:
            {
                // Transforms 2 §9: perspective interpolates as the inverse of its length, so `none` (infinite) is 0.
                double ia = a is { Z: > 0 } ? 1 / a.Z : 0;
                double ib = b is { Z: > 0 } ? 1 / b.Z : 0;
                double inv = Lerp(ia, ib, t);
                r.Z = inv > 0 ? 1 / inv : 0;
                break;
            }

            default:
            {
                Span<double> ma = stackalloc double[16];
                Span<double> mb = stackalloc double[16];
                Span<double> mr = stackalloc double[16];
                TransformMath.Load(a?.M ?? Matrix4x4.Identity, ma);
                TransformMath.Load(b?.M ?? Matrix4x4.Identity, mb);
                r.M = TransformMath.InterpolateMatrices(ma, mb, t, mr) ? TransformMath.Store(mr) : t < 0.5 ? a?.M ?? Matrix4x4.Identity : b?.M ?? Matrix4x4.Identity;
                break;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- shadows

    private static bool ShadowsInterpolable(ShadowList a, ShadowList b)
    {
        int n = Math.Min(a.Items.Length, b.Items.Length);
        for (int i = 0; i < n; i++)
        {
            if (a.Items[i].Inset != b.Items[i].Inset)
            {
                return false;
            }
        }

        return true;
    }

    // CSS Backgrounds 3 §7.2 (box-shadow) and Text Decoration 3 (text-shadow): lists of different length are padded with
    // transparent shadows of zero offset and blur; each shadow's color is interpolated premultiplied and its lengths linearly.
    private static ShadowList LerpShadows(ShadowList a, ShadowList b, double t, ColorValue current)
    {
        int n = Math.Max(a.Items.Length, b.Items.Length);
        if (n == 0)
        {
            return a;
        }

        var items = new ShadowSpec[n];
        for (int i = 0; i < n; i++)
        {
            ShadowSpec x = i < a.Items.Length ? a.Items[i] : new ShadowSpec(new ColorValue(0, 0, 0, 0), 0, 0, 0, 0, b.Items[i].Inset);
            ShadowSpec y = i < b.Items.Length ? b.Items[i] : new ShadowSpec(new ColorValue(0, 0, 0, 0), 0, 0, 0, 0, a.Items[i].Inset);
            ColorValue cx = x.Color.Resolve(current);
            ColorValue cy = y.Color.Resolve(current);
            items[i] = new ShadowSpec(
                x.Color.IsCurrentColor && y.Color.IsCurrentColor ? x.Color : LerpColor(cx, cy, t),
                Lerp(x.OffsetX, y.OffsetX, t),
                Lerp(x.OffsetY, y.OffsetY, t),
                Math.Max(0, Lerp(x.Blur, y.Blur, t)),
                Lerp(x.Spread, y.Spread, t),
                x.Inset);
        }

        return new ShadowList(items);
    }

    // ---------------------------------------------------------------------------------------------------------------- filters

    private static double NeutralAmount(FilterKind k) => k switch
    {
        FilterKind.Brightness or FilterKind.Contrast or FilterKind.Opacity or FilterKind.Saturate => 1,
        _ => 0,
    };

    private static bool FiltersInterpolable(FilterSpec a, FilterSpec b)
    {
        int n = Math.Min(a.Functions.Length, b.Functions.Length);
        for (int i = 0; i < n; i++)
        {
            if (a.Functions[i].Kind != b.Functions[i].Kind || a.Functions[i].Kind == FilterKind.Url)
            {
                return false;
            }
        }

        // The tail of the longer list is paired with identity functions, which url() has none of.
        FilterFunction[] longer = a.Functions.Length >= b.Functions.Length ? a.Functions : b.Functions;
        for (int i = n; i < longer.Length; i++)
        {
            if (longer[i].Kind == FilterKind.Url)
            {
                return false;
            }
        }

        return true;
    }

    // Filter Effects 1 §"Interpolation": function by function with identity padding; amounts keep their ranges.
    private static FilterSpec LerpFilters(FilterSpec a, FilterSpec b, double t, ColorValue current)
    {
        int n = Math.Max(a.Functions.Length, b.Functions.Length);
        if (n == 0)
        {
            return a;
        }

        var fns = new FilterFunction[n];
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            FilterKind kind = i < a.Functions.Length ? a.Functions[i].Kind : b.Functions[i].Kind;
            FilterFunction x = i < a.Functions.Length ? a.Functions[i] : Identity(kind);
            FilterFunction y = i < b.Functions.Length ? b.Functions[i] : Identity(kind);
            double amount = Lerp(x.Amount, y.Amount, t);
            amount = kind switch
            {
                FilterKind.Grayscale or FilterKind.Invert or FilterKind.Opacity or FilterKind.Sepia => Math.Clamp(amount, 0, 1),
                FilterKind.HueRotate => amount,
                _ => Math.Max(0, amount),
            };
            ColorValue color = kind == FilterKind.DropShadow ? LerpColor(x.Color.Resolve(current), y.Color.Resolve(current), t) : x.Color;
            fns[i] = new FilterFunction(kind, amount, Lerp(x.OffsetX, y.OffsetX, t), Lerp(x.OffsetY, y.OffsetY, t), color, null);
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(FilterText(fns[i]));
        }

        return new FilterSpec(fns, sb.ToString());
    }

    private static FilterFunction Identity(FilterKind k) => new(k, NeutralAmount(k), 0, 0, new ColorValue(0, 0, 0, 0), null);

    private static readonly string[] FilterNames = ["blur", "brightness", "contrast", "grayscale", "hue-rotate", "invert", "opacity", "saturate", "sepia", "drop-shadow", "url"];

    // The same canonical text the filter parser writes, so an interpolated value and a parsed one compare equal.
    private static string FilterText(FilterFunction f) => f.Kind switch
    {
        FilterKind.Blur => "blur(" + CssText.Px(f.Amount) + ")",
        FilterKind.HueRotate => "hue-rotate(" + CssText.Number(f.Amount) + "rad)",
        FilterKind.DropShadow => "drop-shadow(" + f.Color.ToCss() + " " + CssText.Px(f.OffsetX) + " " + CssText.Px(f.OffsetY) + " " + CssText.Px(f.Amount) + ")",
        FilterKind.Url => "url(\"" + f.Url + "\")",
        _ => FilterNames[(int)f.Kind] + "(" + CssText.Number(f.Amount) + ")",
    };

    // ---------------------------------------------------------------------------------------------------------------- clip-path

    // CSS Shapes 1 §"Interpolation of basic shapes": the same shape function with the same number of values and the same reference box.
    private static bool ClipPathsInterpolable(ClipPathSpec a, ClipPathSpec b)
    {
        if (a.Kind != ClipPathKind.Shape || b.Kind != ClipPathKind.Shape || a.Shape != b.Shape || a.Box != b.Box)
        {
            return false;
        }

        switch (a.Shape)
        {
            case ShapeKind.Inset:
                return a.Radii.Length == b.Radii.Length && AllLengths(a.Lengths) && AllLengths(b.Lengths) && AllLengths(a.Radii) && AllLengths(b.Radii);
            case ShapeKind.Circle:
            case ShapeKind.Ellipse:
                return a.RadiusKeyX == 0 && a.RadiusKeyY == 0 && b.RadiusKeyX == 0 && b.RadiusKeyY == 0 && AllLengths(a.Lengths) && AllLengths(b.Lengths)
                    && a.Center.X.FromEnd == b.Center.X.FromEnd && a.Center.Y.FromEnd == b.Center.Y.FromEnd;
            case ShapeKind.Polygon:
                return a.Points.Length == b.Points.Length && a.EvenOdd == b.EvenOdd && AllLengths(a.Points) && AllLengths(b.Points);
            default:
                return false;
        }
    }

    private static bool AllLengths(Length[] items)
    {
        foreach (Length l in items)
        {
            if (!LengthLike(l, false))
            {
                return false;
            }
        }

        return true;
    }

    private static Length[] LerpAll(Length[] a, Length[] b, double t, bool nonNegative)
    {
        var r = new Length[a.Length];
        for (int i = 0; i < r.Length; i++)
        {
            r[i] = LerpLength(a[i], b[i], t, nonNegative);
        }

        return r;
    }

    private static ClipPathSpec LerpClipPaths(ClipPathSpec a, ClipPathSpec b, double t)
    {
        var sb = new StringBuilder();
        switch (a.Shape)
        {
            case ShapeKind.Inset:
            {
                Length[] l = LerpAll(a.Lengths, b.Lengths, t, false);
                Length[] r = LerpAll(a.Radii, b.Radii, t, true);
                sb.Append("inset(").Append(l[0].ToCss()).Append(' ').Append(l[1].ToCss()).Append(' ').Append(l[2].ToCss()).Append(' ').Append(l[3].ToCss());
                foreach (Length rl in r)
                {
                    sb.Append(' ').Append(rl.ToCss());
                }

                sb.Append(')');
                return new ClipPathSpec(ClipPathKind.Shape, sb.ToString()) { Shape = ShapeKind.Inset, Box = a.Box, Lengths = l, Radii = r };
            }

            case ShapeKind.Circle:
            case ShapeKind.Ellipse:
            {
                Length[] radii = LerpAll(a.Lengths, b.Lengths, t, true);
                var center = new PositionSpec(
                    new PositionAxis(LerpLength(a.Center.X.Offset, b.Center.X.Offset, t, false), a.Center.X.FromEnd),
                    new PositionAxis(LerpLength(a.Center.Y.Offset, b.Center.Y.Offset, t, false), a.Center.Y.FromEnd));
                sb.Append(a.Shape == ShapeKind.Circle ? "circle(" : "ellipse(");
                for (int k = 0; k < radii.Length; k++)
                {
                    sb.Append(k > 0 ? " " : string.Empty).Append(radii[k].ToCss());
                }

                sb.Append(" at ").Append(center.X.Offset.ToCss()).Append(' ').Append(center.Y.Offset.ToCss()).Append(')');
                return new ClipPathSpec(ClipPathKind.Shape, sb.ToString()) { Shape = a.Shape, Box = a.Box, Lengths = radii, Center = center };
            }

            default:
            {
                Length[] pts = LerpAll(a.Points, b.Points, t, false);
                sb.Append("polygon(").Append(a.EvenOdd ? "evenodd, " : string.Empty);
                for (int k = 0; k < pts.Length / 2; k++)
                {
                    sb.Append(k > 0 ? ", " : string.Empty).Append(pts[k * 2].ToCss()).Append(' ').Append(pts[(k * 2) + 1].ToCss());
                }

                sb.Append(')');
                return new ClipPathSpec(ClipPathKind.Shape, sb.ToString()) { Shape = ShapeKind.Polygon, Box = a.Box, Points = pts, EvenOdd = a.EvenOdd };
            }
        }
    }
}
