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

            case PropertyId.Contain:
                return ApplyFragmentation(id, v);

            case PropertyId.HostScope:
            {
                // --host-scope: none | <integer>. Anything else is invalid and the element has no scope.
                if (IsWord(v, "none"))
                {
                    _r.HasHostScope = false;
                    _r.HostScope = 0;
                    return true;
                }

                if (!SinglePart(v, out ComponentValue hs) || hs.Kind != ComponentValueKind.Token || hs.Token.Kind != CssTokenKind.Number
                    || double.IsInfinity(hs.Token.NumberValue) || hs.Token.NumberValue != Math.Floor(hs.Token.NumberValue)
                    || hs.Token.NumberValue < int.MinValue || hs.Token.NumberValue > int.MaxValue)
                {
                    return false;
                }

                _r.HasHostScope = true;
                _r.HostScope = (int)hs.Token.NumberValue;
                return true;
            }

            case PropertyId.CaretColor:
            {
                if (IsWord(v, "auto"))
                {
                    _r.CaretColorAuto = true;
                    return true;
                }

                if (!TryColorList(v, out ColorValue caret))
                {
                    return false;
                }

                _r.CaretColorAuto = false;
                _r.CaretColor = caret;
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

            case PropertyId.ColorScheme:
                return ParseColorScheme(v);

            default: return false;
        }
    }

    // CSS Color Adjust 1 §2.1: normal | [ light | dark | <custom-ident> ]+ && only?. `only` stands first or last, once; `normal` stands alone;
    // a custom identifier is a scheme the engine does not know and is kept out of the computed value.
    private bool ParseColorScheme(ComponentValueList v)
    {
        int n = SplitParts(v);
        if (n < 1)
        {
            return false;
        }

        ColorSchemes found = ColorSchemes.Normal;
        for (int i = 0; i < n; i++)
        {
            ComponentValue p = _parts[i];
            if (!IsIdent(p))
            {
                return false;
            }

            ReadOnlySpan<char> w = p.Raw;
            if (w.Equals("normal", StringComparison.OrdinalIgnoreCase))
            {
                if (n != 1)
                {
                    return false;
                }
            }
            else if (w.Equals("light", StringComparison.OrdinalIgnoreCase))
            {
                found |= ColorSchemes.Light;
            }
            else if (w.Equals("dark", StringComparison.OrdinalIgnoreCase))
            {
                found |= ColorSchemes.Dark;
            }
            else if (w.Equals("only", StringComparison.OrdinalIgnoreCase))
            {
                if ((found & ColorSchemes.Only) != 0 || (i != 0 && i != n - 1) || n == 1)
                {
                    return false;
                }

                found |= ColorSchemes.Only;
            }
            else if (w.Equals("inherit", StringComparison.OrdinalIgnoreCase) || w.Equals("initial", StringComparison.OrdinalIgnoreCase)
                || w.Equals("unset", StringComparison.OrdinalIgnoreCase) || w.Equals("revert", StringComparison.OrdinalIgnoreCase) || w.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        _r.ColorScheme = found;
        return true;
    }

    private static void CopyInteractionProperty(PropertyId id, ref ComputedValues d, in ComputedValues s)
    {
        switch (id)
        {
            case PropertyId.PointerEvents: d.PointerEvents = s.PointerEvents; break;
            case PropertyId.Contain: d.Contain = s.Contain; break;
            case PropertyId.HostScope: d.HostScope = s.HostScope; d.HasHostScope = s.HasHostScope; break;
            case PropertyId.Appearance: d.Appearance = s.Appearance; break;
            case PropertyId.CaretColor: d.CaretColor = s.CaretColor; d.CaretColorAuto = s.CaretColorAuto; break;
            case PropertyId.AccentColor: d.AccentColor = s.AccentColor; d.AccentColorAuto = s.AccentColorAuto; break;
            case PropertyId.ColorScheme: d.ColorScheme = s.ColorScheme; d.DarkScheme = s.DarkScheme; break;
        }
    }
}
