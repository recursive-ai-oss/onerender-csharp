namespace OneRender.Style;

/// <summary>The color schemes a <c>color-scheme</c> value names (CSS Color Adjust 1 §2.1).</summary>
[Flags]
public enum ColorSchemes : byte
{
    /// <summary><c>normal</c>, or a list naming only schemes the engine does not know: the page is light.</summary>
    Normal = 0,

    /// <summary>The list names <c>light</c>.</summary>
    Light = 1,

    /// <summary>The list names <c>dark</c>.</summary>
    Dark = 2,

    /// <summary>The list has the <c>only</c> keyword, which asks the user agent not to darken the page by itself; the engine never does.</summary>
    Only = 4,
}
