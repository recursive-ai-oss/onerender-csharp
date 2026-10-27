using System.Text;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

internal static partial class ShorthandFallbacks
{
    private static readonly PropertyId[] BorderImageLonghands =
    [
        PropertyId.BorderImageSource, PropertyId.BorderImageSlice, PropertyId.BorderImageWidth, PropertyId.BorderImageOutset, PropertyId.BorderImageRepeat,
    ];

    // CSS Backgrounds 3 §6.6: <'border-image-source'> || <'border-image-slice'> [ / <'border-image-width'> | / <'border-image-width'>? / <'border-image-outset'> ]? || <'border-image-repeat'>
    // Longhands left out take their initial values.
    private static bool ExpandBorderImage(ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        string? source = null, repeat = null;
        var slice = new StringBuilder();
        var width = new StringBuilder();
        var outset = new StringBuilder();
        int slashes = 0;
        bool haveSlice = false;
        foreach (ComponentValue part in value)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            if (part.Kind == ComponentValueKind.Token && part.Token.Kind == CssTokenKind.Delim && part.Raw.Length == 1 && part.Raw[0] == '/')
            {
                if (!haveSlice || ++slashes > 2)
                {
                    return false;
                }

                continue;
            }

            bool isIdent = part.Kind == ComponentValueKind.Token && part.Token.Kind == CssTokenKind.Ident;
            if (isIdent && part.Raw.Equals("fill", StringComparison.OrdinalIgnoreCase) && slashes == 0)
            {
                slice.Append(' ').Append(part.Raw);
                haveSlice = true;
            }
            else if (isIdent && (part.Raw.Equals("stretch", StringComparison.OrdinalIgnoreCase) || part.Raw.Equals("repeat", StringComparison.OrdinalIgnoreCase)
                || part.Raw.Equals("round", StringComparison.OrdinalIgnoreCase) || part.Raw.Equals("space", StringComparison.OrdinalIgnoreCase)))
            {
                repeat = repeat is null ? part.Raw.ToString() : repeat + " " + part.Raw.ToString();
            }
            else if (slashes > 0 || (!isIdent && part.Kind == ComponentValueKind.Token && part.Token.Kind is CssTokenKind.Number or CssTokenKind.Percentage or CssTokenKind.Dimension)
                || (isIdent && part.Raw.Equals("auto", StringComparison.OrdinalIgnoreCase)) || part.IsFunctionNamed("calc"))
            {
                StringBuilder target = slashes == 0 ? slice : slashes == 1 ? width : outset;
                target.Append(' ').Append(part.Raw);
                haveSlice = true;
            }
            else
            {
                if (source is not null)
                {
                    return false;
                }

                source = part.Raw.ToString();
            }
        }

        if (source is null && !haveSlice && repeat is null)
        {
            return false;
        }

        output.Add((PropertyId.BorderImageSource, Text(source ?? "initial")));
        output.Add((PropertyId.BorderImageSlice, Text(slice.Length == 0 ? "initial" : slice.ToString())));
        output.Add((PropertyId.BorderImageWidth, Text(width.Length == 0 ? "initial" : width.ToString())));
        output.Add((PropertyId.BorderImageOutset, Text(outset.Length == 0 ? "initial" : outset.ToString())));
        output.Add((PropertyId.BorderImageRepeat, Text(repeat ?? "initial")));
        return true;
    }
}
