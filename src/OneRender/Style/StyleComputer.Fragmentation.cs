using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

internal sealed partial class StyleComputer
{
    // The keyword order matches BreakKind. "always" is the legacy page-break-* spelling of "page" (Fragmentation 3 §3.4).
    private static readonly string[] BreakWords = ["auto", "avoid", "avoid-page", "avoid-column", "avoid-region", "page", "left", "right", "recto", "verso", "column", "region"];
    private static readonly string[] BreakInsideWords = ["auto", "avoid", "avoid-page", "avoid-column", "avoid-region"];
    private static readonly string[] BoxDecorationWords = ["slice", "clone"];
    private static readonly string[] ColumnFillWords = ["balance", "auto"];
    private static readonly string[] ColumnSpanWords = ["none", "all"];

    private double _rawColumnRuleWidth = 3;

    private bool ApplyFragmentation(PropertyId id, ComponentValueList v)
    {
        switch (id)
        {
            case PropertyId.BreakBefore:
            case PropertyId.BreakAfter:
            {
                if (IsWord(v, "always"))
                {
                    SetBreak(id, BreakKind.Page);
                    return true;
                }

                if (!Word(v, BreakWords, out int k))
                {
                    return false;
                }

                SetBreak(id, (BreakKind)k);
                return true;
            }

            case PropertyId.BreakInside:
            {
                if (!Word(v, BreakInsideWords, out int k))
                {
                    return false;
                }

                _r.BreakInside = (BreakInsideKind)k;
                return true;
            }

            case PropertyId.BoxDecorationBreak:
            {
                if (!Word(v, BoxDecorationWords, out int k))
                {
                    return false;
                }

                _r.BoxDecorationBreak = (BoxDecorationBreakKind)k;
                return true;
            }

            case PropertyId.Page:
            {
                // Paged Media 3 §7.3: auto is the parent's used name (and none at the root), so it keeps the inherited value.
                if (!SinglePart(v, out ComponentValue pn) || pn.Kind != ComponentValueKind.Token || pn.Token.Kind != CssTokenKind.Ident)
                {
                    return false;
                }

                string word = pn.Token.Value;
                if (!word.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    _r.PageName = word;
                }

                return true;
            }

            case PropertyId.Orphans:
            case PropertyId.Widows:
            {
                // Fragmentation 3 §3.3: only positive integers.
                if (!SinglePart(v, out ComponentValue p) || !PartNumber(p, out double n) || n < 1 || n != Math.Floor(n))
                {
                    return false;
                }

                int count = (int)Math.Min(n, 1_000_000);
                if (id == PropertyId.Orphans)
                {
                    _r.Orphans = count;
                }
                else
                {
                    _r.Widows = count;
                }

                return true;
            }

            case PropertyId.ColumnCount:
            {
                if (Word(v, ["auto"], out _))
                {
                    _r.ColumnCount = 0;
                    return true;
                }

                // Multicol 1 §3.2: <integer [1,∞]>.
                if (!SinglePart(v, out ComponentValue p) || !PartNumber(p, out double n) || n < 1 || n != Math.Floor(n))
                {
                    return false;
                }

                _r.ColumnCount = (int)Math.Min(n, 1_000_000);
                return true;
            }

            case PropertyId.ColumnWidth:
            {
                if (!Len(v, LengthAllow.Auto | LengthAllow.NonNegative, out Length w) || (w.Kind != LengthKind.Auto && w.Kind != LengthKind.Px))
                {
                    return false;
                }

                _r.ColumnWidth = w;
                return true;
            }

            case PropertyId.ColumnFill:
            {
                if (!Word(v, ColumnFillWords, out int k))
                {
                    return false;
                }

                _r.ColumnFill = (ColumnFillKind)k;
                return true;
            }

            case PropertyId.ColumnSpan:
            {
                if (!Word(v, ColumnSpanWords, out int k))
                {
                    return false;
                }

                _r.ColumnSpan = (ColumnSpanKind)k;
                return true;
            }

            case PropertyId.ColumnRuleWidth:
            {
                double px;
                if (SinglePart(v, out ComponentValue bp) && IsIdent(bp))
                {
                    ReadOnlySpan<char> t = bp.Raw;
                    if (t.Equals("thin", StringComparison.OrdinalIgnoreCase))
                    {
                        px = 1;
                    }
                    else if (t.Equals("medium", StringComparison.OrdinalIgnoreCase))
                    {
                        px = 3;
                    }
                    else if (t.Equals("thick", StringComparison.OrdinalIgnoreCase))
                    {
                        px = 5;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    if (!Len(v, LengthAllow.NonNegative, out Length l))
                    {
                        return false;
                    }

                    px = l.Value;
                }

                // Multicol 1 §4.4 / Backgrounds 3 §4.3: a rule width between 0 and 1px computes to 1px, others round down to whole px.
                _rawColumnRuleWidth = px > 0 && px < 1 ? 1 : Math.Floor(px);
                return true;
            }

            case PropertyId.ColumnRuleStyle:
            {
                if (!Word(v, BorderStyleWords, out int k))
                {
                    return false;
                }

                _r.ColumnRuleStyle = (BorderStyleKind)k;
                return true;
            }

            case PropertyId.ColumnRuleColor:
            {
                if (!CssColor.TryParse(v, out CssColor c))
                {
                    return false;
                }

                if (c.IsCurrentColor)
                {
                    _r.ColumnRuleColor = ColorValue.CurrentColor;
                }
                else
                {
                    (double r, double g, double b, double a) = c.ToSrgb();
                    _r.ColumnRuleColor = new ColorValue((float)r, (float)g, (float)b, (float)a);
                }

                return true;
            }

            default:
                return false;
        }
    }

    private void SetBreak(PropertyId id, BreakKind kind)
    {
        if (id == PropertyId.BreakBefore)
        {
            _r.BreakBefore = kind;
        }
        else
        {
            _r.BreakAfter = kind;
        }
    }

    private void CopyFragmentationProperty(PropertyId id, ref ComputedValues d, in ComputedValues s, bool fromParent)
    {
        switch (id)
        {
            case PropertyId.BreakBefore: d.BreakBefore = s.BreakBefore; break;
            case PropertyId.BreakAfter: d.BreakAfter = s.BreakAfter; break;
            case PropertyId.BreakInside: d.BreakInside = s.BreakInside; break;
            case PropertyId.Orphans: d.Orphans = s.Orphans; break;
            case PropertyId.Widows: d.Widows = s.Widows; break;
            case PropertyId.Page: d.PageName = s.PageName; break;
            case PropertyId.BoxDecorationBreak: d.BoxDecorationBreak = s.BoxDecorationBreak; break;
            case PropertyId.ColumnCount: d.ColumnCount = s.ColumnCount; break;
            case PropertyId.ColumnWidth: d.ColumnWidth = s.ColumnWidth; break;
            case PropertyId.ColumnFill: d.ColumnFill = s.ColumnFill; break;
            case PropertyId.ColumnSpan: d.ColumnSpan = s.ColumnSpan; break;
            case PropertyId.ColumnRuleWidth: _rawColumnRuleWidth = fromParent ? s.ColumnRuleWidth : 3; break;
            case PropertyId.ColumnRuleStyle: d.ColumnRuleStyle = s.ColumnRuleStyle; break;
            case PropertyId.ColumnRuleColor: d.ColumnRuleColor = s.ColumnRuleColor; break;
        }
    }
}
