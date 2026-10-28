using System.Collections.Frozen;

namespace OneRender.Style;

/// <summary>
/// The used values of the CSS system colors (CSS Color 4 §6.2) under each color scheme (CSS Color Adjust 1 §2.2). The specifications
/// leave the values to the user agent; these are what Chrome 156 reports for each keyword under <c>color-scheme: light</c> and
/// <c>color-scheme: dark</c> (measured from its computed styles; <c>docs/forms.md</c> lists them).
/// </summary>
internal static class SystemColors
{
    private readonly record struct Pair(ColorValue Light, ColorValue Dark);

    private static ColorValue Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    private static readonly FrozenDictionary<string, Pair> Table = Build();

    private static FrozenDictionary<string, Pair> Build()
    {
        var d = new Dictionary<string, Pair>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, ColorValue light, ColorValue dark) => d[name] = new Pair(light, dark);
        void Same(string name, ColorValue c) => Add(name, c, c);
        void Gray(string name, int light, int dark) => Add(name, Rgb(light, light, light), Rgb(dark, dark, dark));

        // The two palettes: the page (Canvas), its text, a control's face and text, a field's face and text.
        Gray("Canvas", 255, 18);
        Gray("CanvasText", 0, 255);
        Gray("Field", 255, 59);
        Gray("FieldText", 0, 255);
        Gray("ButtonFace", 239, 107);
        Gray("ButtonText", 0, 255);
        Gray("ButtonBorder", 0, 255);
        Same("GrayText", Rgb(128, 128, 128));
        Same("Highlight", Rgb(0, 65, 198, 0.8f));
        Same("HighlightText", Rgb(255, 255, 255));
        Same("Mark", Rgb(255, 255, 0));
        Same("MarkText", Rgb(0, 0, 0));
        Same("AccentColor", Rgb(0, 117, 255));
        Same("AccentColorText", Rgb(255, 255, 255));
        Same("ActiveText", Rgb(255, 0, 0));
        Add("LinkText", Rgb(0, 0, 238), Rgb(158, 158, 255));
        Add("VisitedText", Rgb(85, 26, 139), Rgb(208, 173, 240));
        Add("SelectedItem", Rgb(25, 103, 210), Rgb(153, 200, 255));
        Add("SelectedItemText", Rgb(255, 255, 255), Rgb(59, 59, 59));

        // The deprecated keywords (CSS Color 4 §6.3) follow the colors they alias.
        Gray("ActiveBorder", 0, 255);
        Gray("ActiveCaption", 255, 18);
        Gray("AppWorkspace", 255, 18);
        Gray("Background", 255, 18);
        Gray("ButtonHighlight", 239, 107);
        Gray("ButtonShadow", 239, 107);
        Gray("CaptionText", 0, 255);
        Gray("InactiveBorder", 0, 255);
        Gray("InactiveCaption", 255, 18);
        Same("InactiveCaptionText", Rgb(128, 128, 128));
        Gray("InfoBackground", 255, 18);
        Gray("InfoText", 0, 255);
        Gray("Menu", 255, 18);
        Gray("MenuText", 0, 255);
        Gray("Scrollbar", 255, 18);
        Gray("ThreeDDarkShadow", 0, 255);
        Gray("ThreeDFace", 239, 107);
        Gray("ThreeDHighlight", 0, 255);
        Gray("ThreeDLightShadow", 0, 255);
        Gray("ThreeDShadow", 0, 255);
        Gray("Window", 255, 18);
        Gray("WindowFrame", 0, 255);
        Gray("WindowText", 0, 255);
        return d.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The value of the system color <paramref name="name"/> under the light or the dark scheme; false when the name is not a system color.</summary>
    public static bool TryResolve(string name, bool dark, out ColorValue color)
    {
        if (Table.TryGetValue(name, out Pair p))
        {
            color = dark ? p.Dark : p.Light;
            return true;
        }

        color = default;
        return false;
    }

    /// <summary>The page's canvas color under a scheme (<c>Canvas</c>).</summary>
    public static ColorValue Canvas(bool dark) => dark ? Rgb(18, 18, 18) : Rgb(255, 255, 255);

    /// <summary>The initial text color under a scheme (<c>CanvasText</c>).</summary>
    public static ColorValue CanvasText(bool dark) => dark ? Rgb(255, 255, 255) : Rgb(0, 0, 0);
}
