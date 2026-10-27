using OneRender.Style;

namespace OneRender.Accessibility;

/// <summary>The text a browser lists as the laid-out text of a text node: the source text with <c>text-transform</c> applied.</summary>
internal static class TextTransforms
{
    /// <summary>
    /// CSS Text 3 §2.1 by simple case mapping with the invariant culture. When the mapping changes the length (the German sharp s
    /// becomes SS), the offsets of the text boxes would no longer index the text, so the source text is returned.
    /// </summary>
    public static string Apply(string text, TextTransformKind kind)
    {
        if (kind == TextTransformKind.None || text.Length == 0)
        {
            return text;
        }

        return string.Create(text.Length, (text, kind), static (span, state) =>
        {
            (string source, TextTransformKind k) = state;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                span[i] = k switch
                {
                    TextTransformKind.Uppercase => c == '\u00DF' ? c : char.ToUpperInvariant(c),
                    TextTransformKind.Lowercase => char.ToLowerInvariant(c),
                    TextTransformKind.Capitalize => char.IsLetter(c) && !InWord(span, i) ? char.ToUpperInvariant(c) : c,
                    _ => c,
                };
            }
        });
    }

    private static bool InWord(ReadOnlySpan<char> done, int at)
    {
        if (at == 0)
        {
            return false;
        }

        char last = done[at - 1];
        return char.IsLetterOrDigit(last) || (last is '\'' or '\u2019' && at > 1 && char.IsLetterOrDigit(done[at - 2]));
    }
}
