using System.Globalization;
using System.Numerics;
using System.Text;
using OneRender.Painting;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

// Parsing of the transform, filter, masking and compositing properties.
internal sealed partial class StyleComputer
{
    private static readonly string[] BlendWords =
    [
        "normal", "multiply", "screen", "overlay", "darken", "lighten", "color-dodge", "color-burn", "hard-light", "soft-light",
        "difference", "exclusion", "hue", "saturation", "color", "luminosity", "plus-lighter",
    ];

    private static readonly string[] TransformBoxWords = ["content-box", "border-box", "fill-box", "stroke-box", "view-box"];
    private static readonly string[] MaskModeWords = ["match-source", "alpha", "luminance"];
    private static readonly string[] MaskCompositeWords = ["add", "subtract", "intersect", "exclude"];
    private static readonly string[] GeometryBoxWords = ["border-box", "padding-box", "content-box", "margin-box", "fill-box", "stroke-box", "view-box"];

    private bool ApplyEffects(PropertyId id, ComponentValueList v)
    {
        switch (id)
        {
            case PropertyId.Transform:
                if (ParseTransformList(v, out TransformSpec t))
                {
                    _r.Transform = t;
                    return true;
                }

                return false;
            case PropertyId.Translate:
                if (ParseTranslateProperty(v, out TransformSpec tr))
                {
                    _r.Translate = tr;
                    return true;
                }

                return false;
            case PropertyId.Rotate:
                if (ParseRotateProperty(v, out TransformSpec ro))
                {
                    _r.Rotate = ro;
                    return true;
                }

                return false;
            case PropertyId.Scale:
                if (ParseScaleProperty(v, out TransformSpec sc))
                {
                    _r.Scale = sc;
                    return true;
                }

                return false;
            case PropertyId.TransformOrigin:
                {
                    int n = SplitParts(v);
                    if (n < 1 || n > 3)
                    {
                        return false;
                    }

                    double z = 0;
                    if (n == 3)
                    {
                        if (!TryLengthPart(_parts[2], LengthAllow.Length, out Length zl) || zl.Kind != LengthKind.Px)
                        {
                            return false;
                        }

                        z = zl.Value;
                        n = 2;
                    }

                    if (!ParsePosition(_parts.AsSpan(0, n), out PositionSpec pos) || HasEdgeOffset(pos))
                    {
                        return false;
                    }

                    _r.TransformOrigin = pos;
                    _r.TransformOriginZ = z;
                    return true;
                }

            case PropertyId.PerspectiveOrigin:
                {
                    int n = SplitParts(v);
                    if (n < 1 || n > 2 || !ParsePosition(_parts.AsSpan(0, n), out PositionSpec pos) || HasEdgeOffset(pos))
                    {
                        return false;
                    }

                    _r.PerspectiveOrigin = pos;
                    return true;
                }

            case PropertyId.TransformBox:
                if (Word(v, TransformBoxWords, out int tb))
                {
                    _r.TransformBox = (TransformBoxKind)tb;
                    return true;
                }

                return false;
            case PropertyId.Perspective:
                if (IsWord(v, "none"))
                {
                    _r.Perspective = 0;
                    return true;
                }

                if (Len(v, LengthAllow.Length | LengthAllow.NonNegative, out Length pl) && pl.Kind == LengthKind.Px)
                {
                    // Transforms 2 §10: a perspective of less than 1px is treated as 1px.
                    _r.Perspective = Math.Max(1, pl.Value);
                    return true;
                }

                return false;
            case PropertyId.TransformStyle:
                if (Word(v, ["flat", "preserve-3d"], out int ts))
                {
                    _r.TransformStyle = (TransformStyleKind)ts;
                    return true;
                }

                return false;
            case PropertyId.BackfaceVisibility:
                if (Word(v, ["visible", "hidden"], out int bf))
                {
                    _r.BackfaceHidden = bf == 1;
                    return true;
                }

                return false;
            case PropertyId.Filter:
                if (ParseFilterList(v, out FilterSpec f))
                {
                    _r.Filter = f;
                    return true;
                }

                return false;
            case PropertyId.BackdropFilter:
                if (ParseFilterList(v, out FilterSpec bdf))
                {
                    _r.BackdropFilter = bdf;
                    return true;
                }

                return false;
            case PropertyId.ClipPath:
                if (ParseClipPath(v, out ClipPathSpec cp))
                {
                    _r.ClipPath = cp;
                    return true;
                }

                return false;
            case PropertyId.MaskImage:
                if (Layers(v, p => TryImage(p, out ImageSpec i) ? i : null, out ImageSpec[] images) && images.Length <= 64)
                {
                    MaskSpec m = _r.Mask.Copy();
                    m.Images = images;
                    _r.Mask = m;
                    return true;
                }

                return false;
            case PropertyId.MaskMode:
                if (LayerSegments(v, ParseMaskMode, out MaskModeKind[] modes))
                {
                    MaskSpec m = _r.Mask.Copy();
                    m.Modes = modes;
                    _r.Mask = m;
                    return true;
                }

                return false;
            case PropertyId.MaskRepeat:
                if (LayerSegments(v, ParseRepeatPair, out (BackgroundRepeatKind, BackgroundRepeatKind)[] repeats))
                {
                    MaskSpec m = _r.Mask.Copy();
                    m.Repeats = repeats;
                    _r.Mask = m;
                    return true;
                }

                return false;
            case PropertyId.MaskPosition:
                if (LayerSegments(v, ParseMaskPosition, out PositionSpec[] positions))
                {
                    MaskSpec m = _r.Mask.Copy();
                    m.PositionsX = Array.ConvertAll(positions, p => p.X);
                    m.PositionsY = Array.ConvertAll(positions, p => p.Y);
                    _r.Mask = m;
                    return true;
                }

                return false;
            case PropertyId.MaskSize:
                if (LayerSegments(v, ParseSizePair, out BackgroundSizeSpec[] sizes))
                {
                    MaskSpec m = _r.Mask.Copy();
                    m.Sizes = sizes;
                    _r.Mask = m;
                    return true;
                }

                return false;
            case PropertyId.MaskOrigin:
                if (LayerSegments(v, ParseMaskBox, out BackgroundBoxKind[] origins))
                {
                    MaskSpec m = _r.Mask.Copy();
                    m.Origins = origins;
                    _r.Mask = m;
                    return true;
                }

                return false;
            case PropertyId.MaskClip:
                if (LayerSegments(v, ParseMaskBox, out BackgroundBoxKind[] clips))
                {
                    MaskSpec m = _r.Mask.Copy();
                    m.Clips = clips;
                    _r.Mask = m;
                    return true;
                }

                return false;
            case PropertyId.MaskComposite:
                if (LayerSegments(v, ParseMaskComposite, out MaskCompositeKind[] comps))
                {
                    MaskSpec m = _r.Mask.Copy();
                    m.Composites = comps;
                    _r.Mask = m;
                    return true;
                }

                return false;
            case PropertyId.MixBlendMode:
                if (Word(v, BlendWords, out int bm))
                {
                    _r.MixBlendMode = (BlendMode)bm;
                    return true;
                }

                return false;
            case PropertyId.Isolation:
                if (Word(v, ["auto", "isolate"], out int iso))
                {
                    _r.Isolate = iso == 1;
                    return true;
                }

                return false;
            case PropertyId.BackgroundBlendMode:
                if (LayerSegments(v, ParseBlendSegment, out BlendMode[] bbm))
                {
                    _r.BackgroundBlendModes = bbm;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static void CopyEffectsProperty(PropertyId id, ref ComputedValues d, in ComputedValues s)
    {
        switch (id)
        {
            case PropertyId.Transform: d.Transform = s.Transform; break;
            case PropertyId.Translate: d.Translate = s.Translate; break;
            case PropertyId.Rotate: d.Rotate = s.Rotate; break;
            case PropertyId.Scale: d.Scale = s.Scale; break;
            case PropertyId.TransformOrigin: d.TransformOrigin = s.TransformOrigin; d.TransformOriginZ = s.TransformOriginZ; break;
            case PropertyId.TransformBox: d.TransformBox = s.TransformBox; break;
            case PropertyId.Perspective: d.Perspective = s.Perspective; break;
            case PropertyId.PerspectiveOrigin: d.PerspectiveOrigin = s.PerspectiveOrigin; break;
            case PropertyId.TransformStyle: d.TransformStyle = s.TransformStyle; break;
            case PropertyId.BackfaceVisibility: d.BackfaceHidden = s.BackfaceHidden; break;
            case PropertyId.Filter: d.Filter = s.Filter; break;
            case PropertyId.BackdropFilter: d.BackdropFilter = s.BackdropFilter; break;
            case PropertyId.ClipPath: d.ClipPath = s.ClipPath; break;
            case PropertyId.MaskImage: { MaskSpec m = d.Mask.Copy(); m.Images = s.Mask.Images; d.Mask = m; break; }
            case PropertyId.MaskMode: { MaskSpec m = d.Mask.Copy(); m.Modes = s.Mask.Modes; d.Mask = m; break; }
            case PropertyId.MaskRepeat: { MaskSpec m = d.Mask.Copy(); m.Repeats = s.Mask.Repeats; d.Mask = m; break; }
            case PropertyId.MaskPosition: { MaskSpec m = d.Mask.Copy(); m.PositionsX = s.Mask.PositionsX; m.PositionsY = s.Mask.PositionsY; d.Mask = m; break; }
            case PropertyId.MaskSize: { MaskSpec m = d.Mask.Copy(); m.Sizes = s.Mask.Sizes; d.Mask = m; break; }
            case PropertyId.MaskOrigin: { MaskSpec m = d.Mask.Copy(); m.Origins = s.Mask.Origins; d.Mask = m; break; }
            case PropertyId.MaskClip: { MaskSpec m = d.Mask.Copy(); m.Clips = s.Mask.Clips; d.Mask = m; break; }
            case PropertyId.MaskComposite: { MaskSpec m = d.Mask.Copy(); m.Composites = s.Mask.Composites; d.Mask = m; break; }
            case PropertyId.MixBlendMode: d.MixBlendMode = s.MixBlendMode; break;
            case PropertyId.Isolation: d.Isolate = s.Isolate; break;
            case PropertyId.BackgroundBlendMode: d.BackgroundBlendModes = s.BackgroundBlendModes; break;
        }
    }

    // transform-origin and perspective-origin take no edge offsets (Transforms 1 §6: <length-percentage> or a keyword per axis).
    private static bool HasEdgeOffset(PositionSpec p) => p.X.FromEnd && !(p.X.Offset.Kind == LengthKind.Percent && p.X.Offset.Value == 0) || p.Y.FromEnd && !(p.Y.Offset.Kind == LengthKind.Percent && p.Y.Offset.Value == 0);

    // ---------------------------------------------------------------- argument helpers

    /// <summary>Splits a function's children at top-level commas into segments of non-whitespace values.</summary>
    private static bool FunctionArgs(ComponentValue fn, out List<ComponentValue[]> args)
    {
        args = [];
        ComponentValueList children = fn.Children;
        bool any = false;
        foreach (ComponentValue cv in children)
        {
            if (!cv.IsWhitespace)
            {
                any = true;
                break;
            }
        }

        return !any || SplitCommas(children, out args);
    }

    private static bool NumberOf(ComponentValue[] seg, out double n)
    {
        n = 0;
        if (seg.Length != 1 || seg[0].Kind != ComponentValueKind.Token || seg[0].Token.Kind != CssTokenKind.Number)
        {
            return false;
        }

        n = seg[0].Token.NumberValue;
        return double.IsFinite(n);
    }

    private bool LengthOf(ComponentValue[] seg, bool allowPercent, out Length l)
    {
        l = Length.Zero;
        return seg.Length == 1 && TryLengthPart(seg[0], allowPercent ? LengthAllow.Percent : LengthAllow.Length, out l);
    }

    private static bool AngleOf(ComponentValue[] seg, out double rad)
    {
        rad = 0;
        return seg.Length == 1 && TryAnglePart(seg[0], out rad);
    }

    private static string Num(double d) => CssText.Number(d);

    // ---------------------------------------------------------------- transform

    private bool ParseTransformList(ComponentValueList v, out TransformSpec spec)
    {
        spec = TransformSpec.None;
        if (IsWord(v, "none"))
        {
            return true;
        }

        int n = SplitParts(v);
        if (n < 1 || n > TransformSpec.MaxFunctions)
        {
            return false;
        }

        var fns = new TransformFunction[n];
        var text = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            ComponentValue p = _parts[i];
            if (p.Kind != ComponentValueKind.Function || !FunctionArgs(p, out List<ComponentValue[]> args) || !ParseTransformFunction(p.Token.Value, args, out TransformFunction? f))
            {
                return false;
            }

            fns[i] = f;
            if (i > 0)
            {
                text.Append(' ');
            }

            DescribeTransform(f, text);
        }

        spec = new TransformSpec(fns, text.ToString());
        return true;
    }

    private bool ParseTransformFunction(string name, List<ComponentValue[]> a, out TransformFunction f)
    {
        f = new TransformFunction();
        int c = a.Count;
        string lower = name.ToLowerInvariant();
        Length l;
        double d;
        switch (lower)
        {
            case "matrix":
                {
                    if (c != 6)
                    {
                        return false;
                    }

                    Span<double> m = stackalloc double[6];
                    for (int i = 0; i < 6; i++)
                    {
                        if (!NumberOf(a[i], out m[i]))
                        {
                            return false;
                        }
                    }

                    f.Kind = TransformKind.Matrix;
                    f.M = new Matrix4x4((float)m[0], (float)m[1], 0, 0, (float)m[2], (float)m[3], 0, 0, 0, 0, 1, 0, (float)m[4], (float)m[5], 0, 1);
                    return true;
                }

            case "matrix3d":
                {
                    if (c != 16)
                    {
                        return false;
                    }

                    Span<float> m = stackalloc float[16];
                    for (int i = 0; i < 16; i++)
                    {
                        if (!NumberOf(a[i], out double x))
                        {
                            return false;
                        }

                        m[i] = (float)x;
                    }

                    f.Kind = TransformKind.Matrix;
                    f.M = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
                    return true;
                }

            case "translate":
                if (c is < 1 or > 2 || !LengthOf(a[0], true, out l))
                {
                    return false;
                }

                f.Kind = TransformKind.Translate;
                f.X = l;
                if (c == 2)
                {
                    if (!LengthOf(a[1], true, out l))
                    {
                        return false;
                    }

                    f.Y = l;
                }

                return true;
            case "translatex":
                f.Kind = TransformKind.Translate;
                return c == 1 && LengthOf(a[0], true, out f.X);
            case "translatey":
                f.Kind = TransformKind.Translate;
                return c == 1 && LengthOf(a[0], true, out f.Y);
            case "translatez":
                f.Kind = TransformKind.Translate;
                if (c != 1 || !LengthOf(a[0], false, out l) || l.Kind != LengthKind.Px)
                {
                    return false;
                }

                f.Z = l.Value;
                return true;
            case "translate3d":
                f.Kind = TransformKind.Translate;
                if (c != 3 || !LengthOf(a[0], true, out f.X) || !LengthOf(a[1], true, out f.Y) || !LengthOf(a[2], false, out l) || l.Kind != LengthKind.Px)
                {
                    return false;
                }

                f.Z = l.Value;
                return true;
            case "scale":
                f.Kind = TransformKind.Scale;
                if (c is < 1 or > 2 || !NumberOf(a[0], out f.A))
                {
                    return false;
                }

                f.B = f.A;
                return c == 1 || NumberOf(a[1], out f.B);
            case "scalex":
                f.Kind = TransformKind.Scale;
                return c == 1 && NumberOf(a[0], out f.A);
            case "scaley":
                f.Kind = TransformKind.Scale;
                return c == 1 && NumberOf(a[0], out f.B);
            case "scalez":
                f.Kind = TransformKind.Scale;
                return c == 1 && NumberOf(a[0], out f.C);
            case "scale3d":
                f.Kind = TransformKind.Scale;
                return c == 3 && NumberOf(a[0], out f.A) && NumberOf(a[1], out f.B) && NumberOf(a[2], out f.C);
            case "rotate":
            case "rotatez":
                f.Kind = TransformKind.Rotate;
                f.A = 0;
                f.B = 0;
                f.C = 1;
                return c == 1 && AngleOf(a[0], out f.Angle);
            case "rotatex":
                f.Kind = TransformKind.Rotate;
                f.A = 1;
                f.B = 0;
                f.C = 0;
                return c == 1 && AngleOf(a[0], out f.Angle);
            case "rotatey":
                f.Kind = TransformKind.Rotate;
                f.A = 0;
                f.B = 1;
                f.C = 0;
                return c == 1 && AngleOf(a[0], out f.Angle);
            case "rotate3d":
                f.Kind = TransformKind.Rotate;
                return c == 4 && NumberOf(a[0], out f.A) && NumberOf(a[1], out f.B) && NumberOf(a[2], out f.C) && AngleOf(a[3], out f.Angle);
            case "skew":
                f.Kind = TransformKind.Skew;
                f.B = 0;
                if (c is < 1 or > 2 || !AngleOf(a[0], out f.A))
                {
                    return false;
                }

                return c == 1 || AngleOf(a[1], out f.B);
            case "skewx":
                f.Kind = TransformKind.Skew;
                f.B = 0;
                return c == 1 && AngleOf(a[0], out f.A);
            case "skewy":
                f.Kind = TransformKind.Skew;
                f.A = 0;
                return c == 1 && AngleOf(a[0], out f.B);
            case "perspective":
                f.Kind = TransformKind.Perspective;
                if (c != 1)
                {
                    return false;
                }

                if (a[0].Length == 1 && IsIdent(a[0][0]) && a[0][0].Raw.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    f.Z = 0;
                    return true;
                }

                if (!LengthOf(a[0], false, out l) || l.Kind != LengthKind.Px || l.Value < 0)
                {
                    return false;
                }

                d = l.Value;
                f.Z = Math.Max(1, d);
                return true;
            default:
                return false;
        }
    }

    private static void DescribeTransform(TransformFunction f, StringBuilder sb)
    {
        switch (f.Kind)
        {
            case TransformKind.Translate:
                sb.Append("translate3d(").Append(f.X.ToCss()).Append(", ").Append(f.Y.ToCss()).Append(", ").Append(CssText.Px(f.Z)).Append(')');
                break;
            case TransformKind.Scale:
                sb.Append("scale3d(").Append(Num(f.A)).Append(", ").Append(Num(f.B)).Append(", ").Append(Num(f.C)).Append(')');
                break;
            case TransformKind.Rotate:
                sb.Append("rotate3d(").Append(Num(f.A)).Append(", ").Append(Num(f.B)).Append(", ").Append(Num(f.C)).Append(", ").Append(Num(f.Angle)).Append("rad)");
                break;
            case TransformKind.Skew:
                sb.Append("skew(").Append(Num(f.A)).Append("rad, ").Append(Num(f.B)).Append("rad)");
                break;
            case TransformKind.Perspective:
                sb.Append("perspective(").Append(f.Z > 0 ? CssText.Px(f.Z) : "none").Append(')');
                break;
            default:
                {
                    Matrix4x4 m = f.M;
                    sb.Append("matrix3d(");
                    Span<float> x = [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
                    for (int i = 0; i < 16; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(", ");
                        }

                        sb.Append(Num(x[i]));
                    }

                    sb.Append(')');
                    break;
                }
        }
    }

    private bool ParseTranslateProperty(ComponentValueList v, out TransformSpec spec)
    {
        spec = TransformSpec.None;
        if (IsWord(v, "none"))
        {
            return true;
        }

        int n = SplitParts(v);
        if (n is < 1 or > 3)
        {
            return false;
        }

        var f = new TransformFunction { Kind = TransformKind.Translate };
        if (!TryLengthPart(_parts[0], LengthAllow.Percent, out f.X))
        {
            return false;
        }

        if (n >= 2 && !TryLengthPart(_parts[1], LengthAllow.Percent, out f.Y))
        {
            return false;
        }

        if (n == 3)
        {
            if (!TryLengthPart(_parts[2], LengthAllow.Length, out Length z) || z.Kind != LengthKind.Px)
            {
                return false;
            }

            f.Z = z.Value;
        }

        var sb = new StringBuilder();
        DescribeTransform(f, sb);
        spec = new TransformSpec([f], sb.ToString());
        return true;
    }

    private bool ParseScaleProperty(ComponentValueList v, out TransformSpec spec)
    {
        spec = TransformSpec.None;
        if (IsWord(v, "none"))
        {
            return true;
        }

        int n = SplitParts(v);
        if (n is < 1 or > 3)
        {
            return false;
        }

        Span<double> s = [1, 1, 1];
        for (int i = 0; i < n; i++)
        {
            if (!TryLengthPart(_parts[i], LengthAllow.Percent | LengthAllow.Number, out Length l) || l.Kind is not (LengthKind.Number or LengthKind.Percent))
            {
                return false;
            }

            s[i] = l.Kind == LengthKind.Percent ? l.Value / 100 : l.Value;
        }

        if (n == 1)
        {
            s[1] = s[0];
        }

        var f = new TransformFunction { Kind = TransformKind.Scale, A = s[0], B = s[1], C = s[2] };
        var sb = new StringBuilder();
        DescribeTransform(f, sb);
        spec = new TransformSpec([f], sb.ToString());
        return true;
    }

    private bool ParseRotateProperty(ComponentValueList v, out TransformSpec spec)
    {
        spec = TransformSpec.None;
        if (IsWord(v, "none"))
        {
            return true;
        }

        int n = SplitParts(v);
        var f = new TransformFunction { Kind = TransformKind.Rotate, A = 0, B = 0, C = 1 };
        if (n == 1)
        {
            if (!TryAnglePart(_parts[0], out f.Angle))
            {
                return false;
            }
        }
        else
        {
            // [ x | y | z | <number>{3} ] && <angle>, in either order.
            int axisAt = TryAnglePart(_parts[n - 1], out double rad) ? 0 : n > 0 && TryAnglePart(_parts[0], out rad) ? 1 : -1;
            if (axisAt < 0)
            {
                return false;
            }

            f.Angle = rad;
            int start = axisAt == 0 ? 0 : 1;
            int count = n - 1;
            if (count == 1 && IsIdentPart(_parts[start], out ReadOnlySpan<char> w))
            {
                if (w.Equals("x", StringComparison.OrdinalIgnoreCase))
                {
                    (f.A, f.B, f.C) = (1, 0, 0);
                }
                else if (w.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    (f.A, f.B, f.C) = (0, 1, 0);
                }
                else if (!w.Equals("z", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (count == 3)
            {
                Span<double> x = [0, 0, 0];
                for (int i = 0; i < 3; i++)
                {
                    ComponentValue p = _parts[start + i];
                    if (p.Kind != ComponentValueKind.Token || p.Token.Kind != CssTokenKind.Number)
                    {
                        return false;
                    }

                    x[i] = p.Token.NumberValue;
                }

                (f.A, f.B, f.C) = (x[0], x[1], x[2]);
            }
            else
            {
                return false;
            }
        }

        var sb = new StringBuilder();
        DescribeTransform(f, sb);
        spec = new TransformSpec([f], sb.ToString());
        return true;
    }

    // ---------------------------------------------------------------- filters

    private bool ParseFilterList(ComponentValueList v, out FilterSpec spec)
    {
        spec = FilterSpec.None;
        if (IsWord(v, "none"))
        {
            return true;
        }

        int n = SplitParts(v);
        if (n < 1 || n > FilterSpec.MaxFunctions)
        {
            return false;
        }

        var fns = new FilterFunction[n];
        var text = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            if (!ParseFilterFunction(_parts[i], out fns[i]))
            {
                return false;
            }

            if (i > 0)
            {
                text.Append(' ');
            }

            FilterFunction f = fns[i];
            text.Append(f.Kind switch
            {
                FilterKind.Blur => "blur(" + CssText.Px(f.Amount) + ")",
                FilterKind.HueRotate => "hue-rotate(" + Num(f.Amount) + "rad)",
                FilterKind.DropShadow => "drop-shadow(" + f.Color.ToCss() + " " + CssText.Px(f.OffsetX) + " " + CssText.Px(f.OffsetY) + " " + CssText.Px(f.Amount) + ")",
                FilterKind.Url => "url(\"" + f.Url + "\")",
                _ => FilterNames[(int)f.Kind] + "(" + Num(f.Amount) + ")",
            });
        }

        spec = new FilterSpec(fns, text.ToString());
        return true;
    }

    private static readonly string[] FilterNames = ["blur", "brightness", "contrast", "grayscale", "hue-rotate", "invert", "opacity", "saturate", "sepia", "drop-shadow", "url"];

    private bool ParseFilterFunction(ComponentValue p, out FilterFunction f)
    {
        f = default;
        if (p.Kind == ComponentValueKind.Token && p.Token.Kind == CssTokenKind.Url)
        {
            f = new FilterFunction(FilterKind.Url, 0, 0, 0, default, p.Token.Value);
            return true;
        }

        if (p.Kind != ComponentValueKind.Function)
        {
            return false;
        }

        string name = p.Token.Value.ToLowerInvariant();
        if (name == "url")
        {
            foreach (ComponentValue c in p.Children)
            {
                if (!c.IsWhitespace && c.Kind == ComponentValueKind.Token && c.Token.Kind == CssTokenKind.QuotedString)
                {
                    f = new FilterFunction(FilterKind.Url, 0, 0, 0, default, c.Token.Value);
                    return true;
                }
            }

            return false;
        }

        int kind = Array.IndexOf(FilterNames, name);
        if (kind < 0 || kind == (int)FilterKind.Url)
        {
            return false;
        }

        var parts = new List<ComponentValue>();
        foreach (ComponentValue c in p.Children)
        {
            if (!c.IsWhitespace)
            {
                parts.Add(c);
                if (parts.Count > 8)
                {
                    return false;
                }
            }
        }

        var fk = (FilterKind)kind;
        switch (fk)
        {
            case FilterKind.Blur:
                {
                    if (parts.Count == 0)
                    {
                        f = new FilterFunction(fk, 0, 0, 0, default, null);
                        return true;
                    }

                    if (parts.Count != 1 || !TryLengthPart(parts[0], LengthAllow.Length | LengthAllow.NonNegative, out Length l) || l.Kind != LengthKind.Px)
                    {
                        return false;
                    }

                    f = new FilterFunction(fk, l.Value, 0, 0, default, null);
                    return true;
                }

            case FilterKind.HueRotate:
                {
                    double rad = 0;
                    if (parts.Count > 1 || (parts.Count == 1 && !TryAnglePart(parts[0], out rad)))
                    {
                        return false;
                    }

                    f = new FilterFunction(fk, rad, 0, 0, default, null);
                    return true;
                }

            case FilterKind.DropShadow:
                {
                    ColorValue color = ColorValue.CurrentColor;
                    Span<double> len = [0, 0, 0];
                    int ln = 0;
                    bool haveColor = false;
                    foreach (ComponentValue part in parts)
                    {
                        if (TryColorPart(part, out ColorValue cc) && !haveColor)
                        {
                            color = cc;
                            haveColor = true;
                        }
                        else if (ln < 3 && TryLengthPart(part, LengthAllow.Length, out Length l) && l.Kind == LengthKind.Px)
                        {
                            len[ln++] = l.Value;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    if (ln < 2 || len[2] < 0)
                    {
                        return false;
                    }

                    f = new FilterFunction(fk, len[2], len[0], len[1], color, null);
                    return true;
                }

            default:
                {
                    double amount = 1;
                    if (parts.Count > 1)
                    {
                        return false;
                    }

                    if (parts.Count == 1)
                    {
                        if (!TryLengthPart(parts[0], LengthAllow.Percent | LengthAllow.Number | LengthAllow.NonNegative, out Length l) || l.Kind is not (LengthKind.Number or LengthKind.Percent))
                        {
                            return false;
                        }

                        amount = l.Kind == LengthKind.Percent ? l.Value / 100 : l.Value;
                    }

                    if (fk is FilterKind.Grayscale or FilterKind.Invert or FilterKind.Opacity or FilterKind.Sepia)
                    {
                        amount = Math.Min(1, amount); // Filter Effects 1 §5: values above 100% are clamped
                    }

                    f = new FilterFunction(fk, amount, 0, 0, default, null);
                    return true;
                }
        }
    }

    // ---------------------------------------------------------------- clip-path

    private bool ParseClipPath(ComponentValueList v, out ClipPathSpec spec)
    {
        spec = ClipPathSpec.None;
        if (IsWord(v, "none"))
        {
            return true;
        }

        int n = SplitParts(v);
        if (n is < 1 or > 2)
        {
            return false;
        }

        if (n == 1)
        {
            ComponentValue p = _parts[0];
            if (p.Kind == ComponentValueKind.Token && p.Token.Kind == CssTokenKind.Url)
            {
                spec = new ClipPathSpec(ClipPathKind.Reference, "url(\"" + p.Token.Value + "\")") { Url = p.Token.Value };
                return true;
            }

            if (p.IsFunctionNamed("url"))
            {
                foreach (ComponentValue c in p.Children)
                {
                    if (!c.IsWhitespace && c.Kind == ComponentValueKind.Token && c.Token.Kind == CssTokenKind.QuotedString)
                    {
                        spec = new ClipPathSpec(ClipPathKind.Reference, "url(\"" + c.Token.Value + "\")") { Url = c.Token.Value };
                        return true;
                    }
                }

                return false;
            }
        }

        GeometryBox box = GeometryBox.BorderBox;
        bool haveBox = false;
        ComponentValue shapePart = default;
        bool haveShape = false;
        for (int i = 0; i < n; i++)
        {
            ComponentValue p = _parts[i];
            if (IsIdentPart(p, out ReadOnlySpan<char> w))
            {
                int bi = WordIndex(w, GeometryBoxWords);
                if (bi < 0 || haveBox)
                {
                    return false;
                }

                box = (GeometryBox)bi;
                haveBox = true;
            }
            else if (p.Kind == ComponentValueKind.Function && !haveShape)
            {
                shapePart = p;
                haveShape = true;
            }
            else
            {
                return false;
            }
        }

        if (!haveShape)
        {
            spec = new ClipPathSpec(ClipPathKind.Box, GeometryBoxWords[(int)box]) { Box = box };
            return true;
        }

        if (!ParseBasicShape(shapePart, box, out ClipPathSpec? shape))
        {
            return false;
        }

        spec = shape;
        return true;
    }

    private bool ParseBasicShape(ComponentValue fn, GeometryBox box, out ClipPathSpec spec)
    {
        spec = ClipPathSpec.None;
        string name = fn.Token.Value.ToLowerInvariant();
        var items = new List<ComponentValue>();
        var args = new List<ComponentValue[]>();
        foreach (ComponentValue c in fn.Children)
        {
            if (!c.IsWhitespace)
            {
                items.Add(c);
                if (items.Count > (ClipPathSpec.MaxPoints * 4) + 64)
                {
                    return false;
                }
            }
        }

        string boxText = box == GeometryBox.BorderBox ? string.Empty : " " + GeometryBoxWords[(int)box];
        switch (name)
        {
            case "inset":
                {
                    var lens = new List<Length>();
                    int i = 0;
                    for (; i < items.Count && !(IsIdent(items[i]) && items[i].Raw.Equals("round", StringComparison.OrdinalIgnoreCase)); i++)
                    {
                        if (lens.Count == 4 || !TryLengthPart(items[i], LengthAllow.Percent, out Length l))
                        {
                            return false;
                        }

                        lens.Add(l);
                    }

                    if (lens.Count == 0)
                    {
                        return false;
                    }

                    Length t = lens[0], r = lens.Count > 1 ? lens[1] : t, b = lens.Count > 2 ? lens[2] : t, lf = lens.Count > 3 ? lens[3] : r;
                    Length[] radii = [];
                    if (i < items.Count)
                    {
                        if (!ParseCornerRadii(items, i + 1, out radii))
                        {
                            return false;
                        }
                    }

                    var sb = new StringBuilder("inset(").Append(t.ToCss()).Append(' ').Append(r.ToCss()).Append(' ').Append(b.ToCss()).Append(' ').Append(lf.ToCss());
                    foreach (Length rl in radii)
                    {
                        sb.Append(' ').Append(rl.ToCss());
                    }

                    spec = new ClipPathSpec(ClipPathKind.Shape, sb.Append(')').Append(boxText).ToString()) { Shape = ShapeKind.Inset, Box = box, Lengths = [t, r, b, lf], Radii = radii };
                    return true;
                }

            case "circle":
            case "ellipse":
                {
                    bool circle = name == "circle";
                    int radiusCount = circle ? 1 : 2;
                    var radiusLens = new Length[2];
                    byte[] keys = new byte[2];
                    int rc = 0, i = 0;
                    for (; i < items.Count; i++)
                    {
                        ComponentValue p = items[i];
                        if (IsIdent(p) && p.Raw.Equals("at", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        if (rc == radiusCount)
                        {
                            return false;
                        }

                        if (IsIdent(p) && p.Raw.Equals("closest-side", StringComparison.OrdinalIgnoreCase))
                        {
                            keys[rc++] = 1;
                        }
                        else if (IsIdent(p) && p.Raw.Equals("farthest-side", StringComparison.OrdinalIgnoreCase))
                        {
                            keys[rc++] = 2;
                        }
                        else if (TryLengthPart(p, LengthAllow.Percent | LengthAllow.NonNegative, out Length l))
                        {
                            radiusLens[rc++] = l;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    if (rc != 0 && rc != radiusCount)
                    {
                        return false;
                    }

                    if (rc == 0)
                    {
                        keys[0] = keys[1] = 1; // the default radius is closest-side
                    }

                    PositionSpec center = PositionSpec.Center;
                    if (i < items.Count)
                    {
                        int pc = items.Count - i - 1;
                        if (pc is < 1 or > 4)
                        {
                            return false;
                        }

                        var tmp = new ComponentValue[pc];
                        for (int k = 0; k < pc; k++)
                        {
                            tmp[k] = items[i + 1 + k];
                        }

                        if (!ParsePosition(tmp, out center))
                        {
                            return false;
                        }
                    }

                    var sb = new StringBuilder(name).Append('(');
                    for (int k = 0; k < radiusCount; k++)
                    {
                        sb.Append(k > 0 ? " " : string.Empty).Append(keys[k] == 1 ? "closest-side" : keys[k] == 2 ? "farthest-side" : radiusLens[k].ToCss());
                    }

                    sb.Append(" at ").Append(center.X.FromEnd ? "end " : string.Empty).Append(center.X.Offset.ToCss()).Append(' ').Append(center.Y.FromEnd ? "end " : string.Empty).Append(center.Y.Offset.ToCss()).Append(')').Append(boxText);
                    spec = new ClipPathSpec(ClipPathKind.Shape, sb.ToString())
                    {
                        Shape = circle ? ShapeKind.Circle : ShapeKind.Ellipse,
                        Box = box,
                        Lengths = circle ? [radiusLens[0]] : [radiusLens[0], radiusLens[1]],
                        RadiusKeyX = keys[0],
                        RadiusKeyY = keys[1],
                        Center = center,
                    };
                    return true;
                }

            case "polygon":
                {
                    if (!SplitCommas(fn.Children, out List<ComponentValue[]> segs))
                    {
                        return false;
                    }

                    bool evenOdd = false;
                    int first = 0;
                    if (segs[0].Length == 1 && IsIdent(segs[0][0]))
                    {
                        if (segs[0][0].Raw.Equals("evenodd", StringComparison.OrdinalIgnoreCase))
                        {
                            evenOdd = true;
                        }
                        else if (!segs[0][0].Raw.Equals("nonzero", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        first = 1;
                    }

                    int count = segs.Count - first;
                    if (count < 1 || count > ClipPathSpec.MaxPoints)
                    {
                        return false;
                    }

                    var pts = new Length[count * 2];
                    var sb = new StringBuilder("polygon(").Append(evenOdd ? "evenodd, " : string.Empty);
                    for (int k = 0; k < count; k++)
                    {
                        ComponentValue[] s = segs[first + k];
                        if (s.Length != 2 || !TryLengthPart(s[0], LengthAllow.Percent, out pts[k * 2]) || !TryLengthPart(s[1], LengthAllow.Percent, out pts[(k * 2) + 1]))
                        {
                            return false;
                        }

                        sb.Append(k > 0 ? ", " : string.Empty).Append(pts[k * 2].ToCss()).Append(' ').Append(pts[(k * 2) + 1].ToCss());
                    }

                    spec = new ClipPathSpec(ClipPathKind.Shape, sb.Append(')').Append(boxText).ToString()) { Shape = ShapeKind.Polygon, Box = box, Points = pts, EvenOdd = evenOdd };
                    return true;
                }

            case "path":
                {
                    if (!SplitCommas(fn.Children, out List<ComponentValue[]> segs))
                    {
                        return false;
                    }

                    bool evenOdd = false;
                    int k = 0;
                    if (segs.Count == 2 && segs[0].Length == 1 && IsIdent(segs[0][0]))
                    {
                        if (segs[0][0].Raw.Equals("evenodd", StringComparison.OrdinalIgnoreCase))
                        {
                            evenOdd = true;
                        }
                        else if (!segs[0][0].Raw.Equals("nonzero", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        k = 1;
                    }

                    if (segs.Count - k != 1 || segs[k].Length != 1 || segs[k][0].Kind != ComponentValueKind.Token || segs[k][0].Token.Kind != CssTokenKind.QuotedString)
                    {
                        return false;
                    }

                    string data = segs[k][0].Token.Value;
                    if (data.Length > 1 << 15 || !PathDataParser.IsValid(data))
                    {
                        return false;
                    }

                    spec = new ClipPathSpec(ClipPathKind.Shape, "path(" + (evenOdd ? "evenodd, " : string.Empty) + "\"" + data + "\")" + boxText) { Shape = ShapeKind.Path, Box = box, PathData = data, EvenOdd = evenOdd };
                    return true;
                }

            default:
                return false;
        }
    }

    // <'border-radius'> after `round`: one to four horizontal values, then optionally `/` and one to four vertical ones.
    private bool ParseCornerRadii(List<ComponentValue> items, int from, out Length[] radii)
    {
        radii = [];
        var h = new List<Length>();
        var vv = new List<Length>();
        bool vertical = false;
        for (int i = from; i < items.Count; i++)
        {
            if (IsSlash(items[i]))
            {
                if (vertical || h.Count == 0)
                {
                    return false;
                }

                vertical = true;
                continue;
            }

            List<Length> target = vertical ? vv : h;
            if (target.Count == 4 || !TryLengthPart(items[i], LengthAllow.Percent | LengthAllow.NonNegative, out Length l))
            {
                return false;
            }

            target.Add(l);
        }

        if (h.Count == 0 || (vertical && vv.Count == 0))
        {
            return false;
        }

        Length[] hc = ExpandSides4(h), vc = vertical ? ExpandSides4(vv) : hc;
        radii = [hc[0], vc[0], hc[1], vc[1], hc[2], vc[2], hc[3], vc[3]];
        return true;
    }

    private static Length[] ExpandSides4(List<Length> v)
    {
        Length a = v[0], b = v.Count > 1 ? v[1] : a, c = v.Count > 2 ? v[2] : a, d = v.Count > 3 ? v[3] : b;
        return [a, b, c, d];
    }

    // ---------------------------------------------------------------- mask segments

    private static bool ParseMaskMode(ComponentValue[] seg, out MaskModeKind k)
    {
        k = MaskModeKind.MatchSource;
        if (seg.Length != 1 || !IsIdentPart(seg[0], out ReadOnlySpan<char> w))
        {
            return false;
        }

        int i = WordIndex(w, MaskModeWords);
        k = (MaskModeKind)Math.Max(0, i);
        return i >= 0;
    }

    private static bool ParseMaskComposite(ComponentValue[] seg, out MaskCompositeKind k)
    {
        k = MaskCompositeKind.Add;
        if (seg.Length != 1 || !IsIdentPart(seg[0], out ReadOnlySpan<char> w))
        {
            return false;
        }

        int i = WordIndex(w, MaskCompositeWords);
        k = (MaskCompositeKind)Math.Max(0, i);
        return i >= 0;
    }

    private static bool ParseBlendSegment(ComponentValue[] seg, out BlendMode m)
    {
        m = BlendMode.Normal;
        if (seg.Length != 1 || !IsIdentPart(seg[0], out ReadOnlySpan<char> w))
        {
            return false;
        }

        int i = WordIndex(w, BlendWords);
        m = (BlendMode)Math.Max(0, i);
        return i >= 0;
    }

    // mask-origin and mask-clip: <geometry-box> | no-clip. The SVG boxes (fill-box and the like) act as the border box on HTML elements.
    private static bool ParseMaskBox(ComponentValue[] seg, out BackgroundBoxKind k)
    {
        k = BackgroundBoxKind.BorderBox;
        if (seg.Length != 1 || !IsIdentPart(seg[0], out ReadOnlySpan<char> w))
        {
            return false;
        }

        if (w.Equals("no-clip", StringComparison.OrdinalIgnoreCase) || w.Equals("fill-box", StringComparison.OrdinalIgnoreCase)
            || w.Equals("stroke-box", StringComparison.OrdinalIgnoreCase) || w.Equals("view-box", StringComparison.OrdinalIgnoreCase)
            || w.Equals("margin-box", StringComparison.OrdinalIgnoreCase) || w.Equals("border-box", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (w.Equals("padding-box", StringComparison.OrdinalIgnoreCase))
        {
            k = BackgroundBoxKind.PaddingBox;
            return true;
        }

        if (w.Equals("content-box", StringComparison.OrdinalIgnoreCase))
        {
            k = BackgroundBoxKind.ContentBox;
            return true;
        }

        return false;
    }

    private bool ParseMaskPosition(ComponentValue[] seg, out PositionSpec p) => ParsePosition(seg, out p);
}
