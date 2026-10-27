using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

internal sealed partial class StyleComputer
{
    private static readonly string[] PointerEventsWords =
    [
        "auto", "none", "visiblepainted", "visiblefill", "visiblestroke", "visible", "painted", "fill", "stroke", "all", "bounding-box",
    ];

    private static readonly string[] AppearanceWords =
    [
        "auto", "base", "base-select", "menulist-button", "textfield", "checkbox", "radio", "button", "menulist", "listbox", "meter", "progress-bar", "push-button", "slider-horizontal", "square-button", "searchfield", "textarea",
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

            case PropertyId.Appearance:
            {
                // CSS UI 4 §6.1: auto, none, base, base-select and the compatibility keywords; anything else is invalid.
                if (IsWord(v, "none"))
                {
                    _r.Appearance = AppearanceKind.None;
                    return true;
                }

                if (!Word(v, AppearanceWords, out _))
                {
                    return false;
                }

                _r.Appearance = AppearanceKind.Auto;
                return true;
            }

            case PropertyId.AccentColor:
            {
                if (IsWord(v, "auto"))
                {
                    _r.AccentColorAuto = true;
                    return true;
                }

                if (!TryColorList(v, out ColorValue accent))
                {
                    return false;
                }

                _r.AccentColorAuto = false;
                _r.AccentColor = accent;
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
            case PropertyId.Appearance: d.Appearance = s.Appearance; break;
            case PropertyId.AccentColor: d.AccentColor = s.AccentColor; d.AccentColorAuto = s.AccentColorAuto; break;
        }
    }
}
