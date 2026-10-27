using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

internal sealed partial class StyleComputer
{
    private static readonly string[] PointerEventsWords =
    [
        "auto", "none", "visiblepainted", "visiblefill", "visiblestroke", "visible", "painted", "fill", "stroke", "all", "bounding-box",
    ];

    // Cap on functions in one transform list, so a hostile value cannot make composition expensive.
    private const int MaxTransformFunctions = 64;

    private bool ApplyInteraction(PropertyId id, ComponentValueList v)
    {
        switch (id)
        {
            case PropertyId.PointerEvents:
            {
                if (!Word(v, PointerEventsWords, out int k))
                {
                    return false;
                }

                _r.PointerEvents = k == 1 ? PointerEventsKind.None : PointerEventsKind.Auto;
                return true;
            }

            case PropertyId.Transform: return ParseTransform(v);
            case PropertyId.TransformOrigin: return ParseTransformOrigin(v);
            default: return false;
        }
    }

    private static void CopyInteractionProperty(PropertyId id, ref ComputedValues d, in ComputedValues s)
    {
        switch (id)
        {
            case PropertyId.PointerEvents: d.PointerEvents = s.PointerEvents; break;
            case PropertyId.Transform: d.Transform = s.Transform; break;
            case PropertyId.TransformOrigin: d.TransformOriginX = s.TransformOriginX; d.TransformOriginY = s.TransformOriginY; break;
        }
    }

    // CSS Transforms 1 §6: none | <transform-list>.
    private bool ParseTransform(ComponentValueList v)
    {
        if (IsWord(v, "none"))
        {
            _r.Transform = Transform2D.Identity;
            return true;
        }

        Transform2D total = Transform2D.Identity;
        int count = 0;
        var args = new List<ComponentValue>(6);
        foreach (ComponentValue fn in v)
        {
            if (fn.IsWhitespace)
            {
                continue;
            }

            if (fn.Kind != ComponentValueKind.Function || ++count > MaxTransformFunctions)
            {
                return false;
            }

            args.Clear();
            if (!SplitCommaArgs(fn.Children, args, 6) || !ParseTransformFunction(fn, args, out Transform2D one))
            {
                return false;
            }

            total = total.Then(one);
        }

        if (count == 0)
        {
            return false;
        }

        _r.Transform = total;
        return true;
    }

    private bool ParseTransformFunction(ComponentValue fn, List<ComponentValue> args, out Transform2D t)
    {
        t = Transform2D.Identity;
        int n = args.Count;
        if (fn.IsFunctionNamed("matrix"))
        {
            if (n != 6)
            {
                return false;
            }

            var m = new double[6];
            for (int i = 0; i < 6; i++)
            {
                if (!TransformNumber(args[i], out m[i]))
                {
                    return false;
                }
            }

            t = Transform2D.FromMatrix(m[0], m[1], m[2], m[3], m[4], m[5]);
            return true;
        }

        if (fn.IsFunctionNamed("translate") || fn.IsFunctionNamed("translatex") || fn.IsFunctionNamed("translatey"))
        {
            bool onlyY = fn.IsFunctionNamed("translatey");
            bool single = onlyY || fn.IsFunctionNamed("translatex");
            if (n < 1 || n > (single ? 1 : 2))
            {
                return false;
            }

            double x = 0, xw = 0, y = 0, yh = 0;
            if (!TransformLength(args[0], out double c0, out double p0))
            {
                return false;
            }

            if (onlyY)
            {
                y = c0;
                yh = p0;
            }
            else
            {
                x = c0;
                xw = p0;
                if (n == 2 && !TransformLength(args[1], out y, out yh))
                {
                    return false;
                }
            }

            t = Transform2D.FromTranslation(x, xw, y, yh);
            return true;
        }

        if (fn.IsFunctionNamed("scale") || fn.IsFunctionNamed("scalex") || fn.IsFunctionNamed("scaley"))
        {
            bool onlyY = fn.IsFunctionNamed("scaley");
            bool single = onlyY || fn.IsFunctionNamed("scalex");
            if (n < 1 || n > (single ? 1 : 2) || !TransformNumber(args[0], out double s0))
            {
                return false;
            }

            double sx = onlyY ? 1 : s0;
            double sy = onlyY ? s0 : (single ? 1 : s0);
            if (n == 2 && !TransformNumber(args[1], out sy))
            {
                return false;
            }

            t = Transform2D.FromMatrix(sx, 0, 0, sy, 0, 0);
            return true;
        }

        if (fn.IsFunctionNamed("rotate") || fn.IsFunctionNamed("rotatez"))
        {
            if (n != 1 || !TransformAngle(args[0], out double deg))
            {
                return false;
            }

            double rad = deg * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            t = Transform2D.FromMatrix(cos, sin, -sin, cos, 0, 0);
            return true;
        }

        if (fn.IsFunctionNamed("skew") || fn.IsFunctionNamed("skewx") || fn.IsFunctionNamed("skewy"))
        {
            bool onlyY = fn.IsFunctionNamed("skewy");
            bool single = onlyY || fn.IsFunctionNamed("skewx");
            if (n < 1 || n > (single ? 1 : 2) || !TransformAngle(args[0], out double a0))
            {
                return false;
            }

            double ax = onlyY ? 0 : a0;
            double ay = onlyY ? a0 : 0;
            if (n == 2 && !TransformAngle(args[1], out ay))
            {
                return false;
            }

            t = Transform2D.FromMatrix(1, Math.Tan(ay * Math.PI / 180.0), Math.Tan(ax * Math.PI / 180.0), 1, 0, 0);
            return true;
        }

        return false;
    }

    private bool TransformNumber(ComponentValue part, out double value)
    {
        value = 0;
        if (!CssQuantity.TryParse(ListOf(part), QuantityKinds.Number, out CssQuantity q))
        {
            return false;
        }

        value = q.Calc is not null ? q.Calc.Resolve(in _ctx, 0) : q.Value;
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private bool TransformAngle(ComponentValue part, out double degrees)
    {
        degrees = 0;
        if (!CssQuantity.TryParse(ListOf(part), QuantityKinds.Angle | QuantityKinds.UnitlessZeroLength, out CssQuantity q))
        {
            return false;
        }

        degrees = Px(q, in _ctx);
        return !double.IsInfinity(degrees);
    }

    // A <length-percentage> as a constant in px plus a fraction of the reference box (a calc containing a percentage is linear in the basis).
    private bool TransformLength(ComponentValue part, out double px, out double fraction)
    {
        px = fraction = 0;
        if (!Len(ListOf(part), LengthAllow.Percent, out Length l))
        {
            return false;
        }

        switch (l.Kind)
        {
            case LengthKind.Px:
                px = l.Value;
                return true;
            case LengthKind.Percent:
                fraction = l.Value / 100.0;
                return true;
            case LengthKind.Calc:
                px = l.Resolve(0);
                fraction = (l.Resolve(100) - px) / 100.0;
                return !double.IsNaN(px) && !double.IsNaN(fraction);
            default:
                return false;
        }
    }

    // CSS Transforms 1 §6.2: [left | center | right | top | bottom | <length-percentage>]{1,2} [<length>]?.
    private bool ParseTransformOrigin(ComponentValueList v)
    {
        int n = SplitParts(v);
        if (n is < 1 or > 3)
        {
            return false;
        }

        if (!OriginPart(_parts[0], out Length first, out int firstAxis))
        {
            return false;
        }

        Length second = Length.FromPercent(50);
        int secondAxis = 0;
        if (n >= 2 && !OriginPart(_parts[1], out second, out secondAxis))
        {
            return false;
        }

        if (n == 3 && !Len(ListOf(_parts[2]), LengthAllow.Length, out _))
        {
            return false;
        }

        Length x = first, y = second;
        if (n == 1)
        {
            // One value: the other axis is center; a vertical keyword alone sets the vertical axis.
            if (firstAxis == 2)
            {
                x = Length.FromPercent(50);
                y = first;
            }
            else
            {
                y = Length.FromPercent(50);
            }
        }
        else if (firstAxis == 2 || secondAxis == 1)
        {
            // Keywords may come in either order; a keyword on the wrong axis for its slot swaps them.
            if (firstAxis == 1 || secondAxis == 2)
            {
                return false;
            }

            x = second;
            y = first;
        }

        _r.TransformOriginX = x;
        _r.TransformOriginY = y;
        return true;
    }

    private bool OriginPart(ComponentValue part, out Length l, out int axis)
    {
        axis = 0;
        l = Length.Zero;
        if (IsIdentPart(part, out ReadOnlySpan<char> word))
        {
            if (word.Equals("left", StringComparison.OrdinalIgnoreCase))
            {
                l = Length.FromPercent(0);
                axis = 1;
                return true;
            }

            if (word.Equals("right", StringComparison.OrdinalIgnoreCase))
            {
                l = Length.FromPercent(100);
                axis = 1;
                return true;
            }

            if (word.Equals("top", StringComparison.OrdinalIgnoreCase))
            {
                l = Length.FromPercent(0);
                axis = 2;
                return true;
            }

            if (word.Equals("bottom", StringComparison.OrdinalIgnoreCase))
            {
                l = Length.FromPercent(100);
                axis = 2;
                return true;
            }

            if (word.Equals("center", StringComparison.OrdinalIgnoreCase))
            {
                l = Length.FromPercent(50);
                return true;
            }

            return false;
        }

        return Len(ListOf(part), LengthAllow.Percent, out l);
    }
}
