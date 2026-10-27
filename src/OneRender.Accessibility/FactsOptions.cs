using RecursiveAI.OneHtml.Accessibility.Rendered;

namespace OneRender.Accessibility;

/// <summary>What to put in the facts and how far to go.</summary>
public sealed class FactsOptions
{
    /// <summary>
    /// The computed-style properties to serialize for every box, as longhand names in lower case. The facts read them back by name
    /// with <c>BoxFacts.GetStyle</c>. A property the engine does not compute is absent from the facts. Default: none.
    /// <see cref="StandardStyleList"/> is the list of properties accessibility checks usually ask for.
    /// </summary>
    public IReadOnlyList<string> ComputedStyles { get; init; } = [];

    /// <summary>Bounds on the number of boxes, text boxes and strings in the facts; null means <see cref="RenderedFactsLimits.Default"/>. A page over a bound throws <see cref="RenderedFactsLimitException"/>. There is no bound on the size of the page as bytes.</summary>
    public RenderedFactsLimits? Limits { get; init; }

    /// <summary>
    /// How many display list items before a piece of text are looked at to work out the color behind it, at least 1.
    /// Default 16384. Past it the color is that of the fills found so far over white, which bounds the work to
    /// the number of texts times this value.
    /// </summary>
    public int BackgroundScanLimit { get; init; } = 16384;

    /// <summary>
    /// The properties of the usual accessibility capture: display, visibility, opacity, position, z-index, overflow, the box
    /// metrics, colors, backgrounds, borders, font, text and direction properties.
    /// </summary>
    public static IReadOnlyList<string> StandardStyleList { get; } =
    [
        "display", "visibility", "content", "content-visibility", "opacity", "position", "top", "right", "bottom", "left", "z-index",
        "float", "clear", "overflow", "overflow-x", "overflow-y", "clip", "clip-path", "pointer-events", "transform", "filter",
        "mix-blend-mode", "isolation", "contain", "width", "height", "min-width", "min-height", "max-width", "max-height",
        "margin-top", "margin-right", "margin-bottom", "margin-left", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "color", "background-color", "background-image", "background-position", "background-size", "background-repeat",
        "background-clip", "background-attachment", "-webkit-text-fill-color", "-webkit-text-stroke-color", "-webkit-text-stroke-width",
        "text-shadow", "box-shadow", "border-top-color", "border-top-style", "border-top-width", "border-right-color",
        "border-right-style", "border-right-width", "border-bottom-color", "border-bottom-style", "border-bottom-width",
        "border-left-color", "border-left-style", "border-left-width", "outline-color", "outline-style", "outline-width",
        "outline-offset", "font-family", "font-size", "font-style", "font-weight", "font-variant", "line-height", "letter-spacing",
        "word-spacing", "text-align", "text-indent", "text-transform", "text-decoration-line", "text-decoration-color",
        "text-decoration-style", "text-overflow", "white-space", "word-break", "overflow-wrap", "vertical-align", "direction",
        "unicode-bidi", "writing-mode", "cursor", "user-select", "appearance", "forced-color-adjust", "color-scheme",
        "list-style-type", "table-layout",
    ];
}
