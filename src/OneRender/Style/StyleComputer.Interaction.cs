using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

internal sealed partial class StyleComputer
{
    private static readonly string[] PointerEventsWords =
    [
        "auto", "none", "visiblepainted", "visiblefill", "visiblestroke", "visible", "painted", "fill", "stroke", "all", "bounding-box",
    ];

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

            default: return false;
        }
    }

    private static void CopyInteractionProperty(PropertyId id, ref ComputedValues d, in ComputedValues s)
    {
        switch (id)
        {
            case PropertyId.PointerEvents: d.PointerEvents = s.PointerEvents; break;
        }
    }
}
