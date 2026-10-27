using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

internal sealed partial class StyleComputer
{
    private static readonly string[] FlexDirectionWords = ["row", "row-reverse", "column", "column-reverse"];
    private static readonly string[] FlexWrapWords = ["nowrap", "wrap", "wrap-reverse"];

    // The keyword order matches AlignKind from Normal (1) on.
    private static readonly string[] AlignWords =
    [
        "auto", "normal", "stretch", "flex-start", "flex-end", "start", "end", "self-start", "self-end", "center",
        "baseline", "last-baseline", "left", "right", "space-between", "space-around", "space-evenly",
    ];

    private bool ApplyFlex(PropertyId id, ComponentValueList v)
    {
        switch (id)
        {
            case PropertyId.FlexDirection:
            {
                if (!Word(v, FlexDirectionWords, out int k))
                {
                    return false;
                }

                _r.FlexDirection = (FlexDirectionKind)k;
                return true;
            }

            case PropertyId.FlexWrap:
            {
                if (!Word(v, FlexWrapWords, out int k))
                {
                    return false;
                }

                _r.FlexWrap = (FlexWrapKind)k;
                return true;
            }

            case PropertyId.FlexGrow:
            case PropertyId.FlexShrink:
            {
                if (!SinglePart(v, out ComponentValue p) || !PartNumber(p, out double n) || n < 0)
                {
                    return false;
                }

                if (id == PropertyId.FlexGrow)
                {
                    _r.FlexGrow = n;
                }
                else
                {
                    _r.FlexShrink = n;
                }

                return true;
            }

            case PropertyId.FlexBasis:
            {
                if (IsWord(v, "content"))
                {
                    _r.FlexBasis = Length.Auto;
                    _r.FlexBasisContent = true;
                    return true;
                }

                if (!Len(v, LengthAllow.Percent | LengthAllow.Auto | LengthAllow.Sizing | LengthAllow.NonNegative, out Length b))
                {
                    return false;
                }

                _r.FlexBasis = b;
                _r.FlexBasisContent = false;
                return true;
            }

            case PropertyId.Order:
            {
                if (!SinglePart(v, out ComponentValue p) || p.Kind != ComponentValueKind.Token || p.Token.Kind != CssTokenKind.Number || double.IsInfinity(p.Token.NumberValue) || p.Token.NumberValue != Math.Floor(p.Token.NumberValue))
                {
                    return false;
                }

                _r.Order = (int)Math.Clamp(p.Token.NumberValue, int.MinValue, int.MaxValue);
                return true;
            }

            case PropertyId.JustifyContent:
                return ParseAlign(v, content: true, self: false, out AlignKind jc, out AlignOverflowKind jo) && SetAlign(ref _r, id, jc, jo);
            case PropertyId.AlignContent:
                return ParseAlign(v, content: true, self: false, out AlignKind ac, out AlignOverflowKind ao) && SetAlign(ref _r, id, ac, ao);
            case PropertyId.AlignItems:
                return ParseAlign(v, content: false, self: false, out AlignKind ai, out AlignOverflowKind aio) && SetAlign(ref _r, id, ai, aio);
            case PropertyId.AlignSelf:
                return ParseAlign(v, content: false, self: true, out AlignKind asf, out AlignOverflowKind aso) && SetAlign(ref _r, id, asf, aso);

            case PropertyId.RowGap:
            case PropertyId.ColumnGap:
            {
                if (!Len(v, LengthAllow.Percent | LengthAllow.Normal | LengthAllow.NonNegative, out Length g))
                {
                    return false;
                }

                if (id == PropertyId.RowGap)
                {
                    _r.RowGap = g;
                }
                else
                {
                    _r.ColumnGap = g;
                }

                return true;
            }

            default:
                return false;
        }
    }

    private static bool SetAlign(ref ComputedValues r, PropertyId id, AlignKind k, AlignOverflowKind o)
    {
        switch (id)
        {
            case PropertyId.JustifyContent: r.JustifyContent = k; r.JustifyContentOverflow = o; break;
            case PropertyId.AlignContent: r.AlignContent = k; r.AlignContentOverflow = o; break;
            case PropertyId.AlignItems: r.AlignItems = k; r.AlignItemsOverflow = o; break;
            default: r.AlignSelf = k; r.AlignSelfOverflow = o; break;
        }

        return true;
    }

    /// <summary>
    /// CSS Box Alignment 3 §4: <c>normal | stretch | [first | last]? baseline | [safe | unsafe]? position | distribution</c>, limited to
    /// what the property accepts (distribution keywords for the content properties, <c>auto</c> for <c>align-self</c> only).
    /// </summary>
    private bool ParseAlign(ComponentValueList v, bool content, bool self, out AlignKind kind, out AlignOverflowKind overflow)
    {
        kind = AlignKind.Normal;
        overflow = AlignOverflowKind.Default;
        int n = SplitParts(v);
        if (n < 1 || n > 2 || !IsIdentPart(_parts[0], out ReadOnlySpan<char> first))
        {
            return false;
        }

        if (n == 2)
        {
            if (!IsIdentPart(_parts[1], out ReadOnlySpan<char> second))
            {
                return false;
            }

            if (first.Equals("first", StringComparison.OrdinalIgnoreCase) && second.Equals("baseline", StringComparison.OrdinalIgnoreCase))
            {
                kind = AlignKind.Baseline;
                return true;
            }

            if (first.Equals("last", StringComparison.OrdinalIgnoreCase) && second.Equals("baseline", StringComparison.OrdinalIgnoreCase))
            {
                kind = AlignKind.LastBaseline;
                return true;
            }

            if (first.Equals("safe", StringComparison.OrdinalIgnoreCase))
            {
                overflow = AlignOverflowKind.Safe;
            }
            else if (first.Equals("unsafe", StringComparison.OrdinalIgnoreCase))
            {
                overflow = AlignOverflowKind.Unsafe;
            }
            else
            {
                return false;
            }

            int w = WordIndex(second, AlignWords);
            if (w < 0 || !IsPosition((AlignKind)w))
            {
                return false;
            }

            kind = (AlignKind)w;
            return true;
        }

        int k = WordIndex(first, AlignWords);
        if (k < 0)
        {
            return false;
        }

        var a = (AlignKind)k;
        if (a == AlignKind.Auto)
        {
            return self;
        }

        if (a == AlignKind.LastBaseline)
        {
            // Spelled "last baseline" with a space only.
            return false;
        }

        if (a is AlignKind.SpaceBetween or AlignKind.SpaceAround or AlignKind.SpaceEvenly)
        {
            if (!content)
            {
                return false;
            }
        }

        if (!content && a == AlignKind.Stretch)
        {
            kind = a;
            return true;
        }

        kind = a;
        return true;
    }

    private static bool IsPosition(AlignKind k) =>
        k is AlignKind.FlexStart or AlignKind.FlexEnd or AlignKind.Start or AlignKind.End or AlignKind.SelfStart or AlignKind.SelfEnd or AlignKind.Center or AlignKind.Left or AlignKind.Right;

    private static void CopyFlexProperty(PropertyId id, ref ComputedValues d, in ComputedValues s)
    {
        switch (id)
        {
            case PropertyId.FlexDirection: d.FlexDirection = s.FlexDirection; break;
            case PropertyId.FlexWrap: d.FlexWrap = s.FlexWrap; break;
            case PropertyId.FlexGrow: d.FlexGrow = s.FlexGrow; break;
            case PropertyId.FlexShrink: d.FlexShrink = s.FlexShrink; break;
            case PropertyId.FlexBasis: d.FlexBasis = s.FlexBasis; d.FlexBasisContent = s.FlexBasisContent; break;
            case PropertyId.Order: d.Order = s.Order; break;
            case PropertyId.JustifyContent: d.JustifyContent = s.JustifyContent; d.JustifyContentOverflow = s.JustifyContentOverflow; break;
            case PropertyId.AlignContent: d.AlignContent = s.AlignContent; d.AlignContentOverflow = s.AlignContentOverflow; break;
            case PropertyId.AlignItems: d.AlignItems = s.AlignItems; d.AlignItemsOverflow = s.AlignItemsOverflow; break;
            case PropertyId.AlignSelf: d.AlignSelf = s.AlignSelf; d.AlignSelfOverflow = s.AlignSelfOverflow; break;
            case PropertyId.RowGap: d.RowGap = s.RowGap; break;
            case PropertyId.ColumnGap: d.ColumnGap = s.ColumnGap; break;
        }
    }
}
