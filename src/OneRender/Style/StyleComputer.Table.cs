using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

internal sealed partial class StyleComputer
{
    private static readonly string[] BorderCollapseWords = ["separate", "collapse"];
    private static readonly string[] EmptyCellsWords = ["show", "hide"];
    private static readonly string[] CaptionSideWords = ["top", "bottom"];
    private static readonly string[] TableLayoutWords = ["auto", "fixed"];

    private bool ApplyTable(PropertyId id, ComponentValueList v)
    {
        switch (id)
        {
            case PropertyId.BorderCollapse:
            {
                if (!Word(v, BorderCollapseWords, out int k))
                {
                    return false;
                }

                _r.BorderCollapse = (BorderCollapseKind)k;
                return true;
            }

            case PropertyId.EmptyCells:
            {
                if (!Word(v, EmptyCellsWords, out int k))
                {
                    return false;
                }

                _r.EmptyCells = (EmptyCellsKind)k;
                return true;
            }

            case PropertyId.CaptionSide:
            {
                if (!Word(v, CaptionSideWords, out int k))
                {
                    return false;
                }

                _r.CaptionSide = (CaptionSideKind)k;
                return true;
            }

            case PropertyId.TableLayout:
            {
                if (!Word(v, TableLayoutWords, out int k))
                {
                    return false;
                }

                _r.TableLayout = (TableLayoutKind)k;
                return true;
            }

            case PropertyId.BorderSpacing:
            {
                // CSS2 §17.6.1: <length> <length>?; a single value sets both axes. Negative lengths are invalid.
                int n = SplitParts(v);
                if (n is < 1 or > 2)
                {
                    return false;
                }

                if (!Len(ListOf(_parts[0]), LengthAllow.NonNegative, out Length h) || h.Kind != LengthKind.Px)
                {
                    return false;
                }

                Length vv = h;
                if (n == 2 && (!Len(ListOf(_parts[1]), LengthAllow.NonNegative, out vv) || vv.Kind != LengthKind.Px))
                {
                    return false;
                }

                _r.BorderSpacingH = h.Value;
                _r.BorderSpacingV = vv.Value;
                return true;
            }

            default:
                return false;
        }
    }

    private static void CopyTableProperty(PropertyId id, ref ComputedValues d, in ComputedValues s)
    {
        switch (id)
        {
            case PropertyId.BorderCollapse: d.BorderCollapse = s.BorderCollapse; break;
            case PropertyId.BorderSpacing: d.BorderSpacingH = s.BorderSpacingH; d.BorderSpacingV = s.BorderSpacingV; break;
            case PropertyId.EmptyCells: d.EmptyCells = s.EmptyCells; break;
            case PropertyId.CaptionSide: d.CaptionSide = s.CaptionSide; break;
            case PropertyId.TableLayout: d.TableLayout = s.TableLayout; break;
        }
    }
}
