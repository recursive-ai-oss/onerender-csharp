namespace OneRender.Text;

/// <summary>
/// Maps a character no installed face covers to the opt-in fonts package that ships a face for it. The default fonts cover
/// Latin, Greek and Cyrillic (the Liberation, Carlito and Caladea families); everything else comes from a separate package so
/// that applications pay for the scripts they use.
/// </summary>
public static class FontPackages
{
    /// <summary>The package id prefix shared by every fonts package.</summary>
    private const string Prefix = "RecursiveAI.OneRender.Fonts.";

    /// <summary>The package id of the color emoji fonts.</summary>
    public const string Emoji = Prefix + "Emoji";

    /// <summary>The package id of the CJK (Chinese, Japanese, Korean) fonts.</summary>
    public const string Cjk = Prefix + "Cjk";

    // Sorted by Start, no overlaps. The script blocks follow the Unicode block layout; a block inside a script's range that
    // another package covers is listed on its own line. Add a package by adding rows.
    private static readonly (int Start, int End, string Package)[] s_ranges =
    [
        (0x0530, 0x058F, Prefix + "Noto.Armenian"),
        (0x0590, 0x05FF, Prefix + "Noto.Hebrew"),
        (0x0600, 0x06FF, Prefix + "Noto.Arabic"),
        (0x0700, 0x074F, Prefix + "Noto.Syriac"),
        (0x0750, 0x077F, Prefix + "Noto.Arabic"),
        (0x0780, 0x07BF, Prefix + "Noto.Thaana"),
        (0x07C0, 0x07FF, Prefix + "Noto.Nko"),
        (0x0900, 0x097F, Prefix + "Noto.Devanagari"),
        (0x0980, 0x09FF, Prefix + "Noto.Bengali"),
        (0x0A00, 0x0A7F, Prefix + "Noto.Gurmukhi"),
        (0x0A80, 0x0AFF, Prefix + "Noto.Gujarati"),
        (0x0B00, 0x0B7F, Prefix + "Noto.Oriya"),
        (0x0B80, 0x0BFF, Prefix + "Noto.Tamil"),
        (0x0C00, 0x0C7F, Prefix + "Noto.Telugu"),
        (0x0C80, 0x0CFF, Prefix + "Noto.Kannada"),
        (0x0D00, 0x0D7F, Prefix + "Noto.Malayalam"),
        (0x0D80, 0x0DFF, Prefix + "Noto.Sinhala"),
        (0x0E00, 0x0E7F, Prefix + "Noto.Thai"),
        (0x0E80, 0x0EFF, Prefix + "Noto.Lao"),
        (0x0F00, 0x0FFF, Prefix + "Noto.Tibetan"),
        (0x1000, 0x109F, Prefix + "Noto.Myanmar"),
        (0x10A0, 0x10FF, Prefix + "Noto.Georgian"),
        (0x1100, 0x11FF, Cjk), // Hangul Jamo
        (0x1200, 0x139F, Prefix + "Noto.Ethiopic"),
        (0x13A0, 0x13FF, Prefix + "Noto.Cherokee"),
        (0x1780, 0x17FF, Prefix + "Noto.Khmer"),
        (0x1800, 0x18AF, Prefix + "Noto.Mongolian"),
        (0x1B00, 0x1B7F, Prefix + "Noto.Balinese"),
        (0x1C90, 0x1CBF, Prefix + "Noto.Georgian"),
        (0x2190, 0x21FF, Prefix + "Noto.Symbols"), // Arrows
        (0x2200, 0x22FF, Prefix + "Noto.Math"),
        (0x2300, 0x23FF, Prefix + "Noto.Symbols"), // Miscellaneous Technical
        (0x2400, 0x25FF, Prefix + "Noto.Symbols"), // Control Pictures .. Geometric Shapes
        (0x2600, 0x27BF, Emoji), // Miscellaneous Symbols and Dingbats: the emoji font covers the pictographs
        (0x27C0, 0x27EF, Prefix + "Noto.Math"),
        (0x2800, 0x28FF, Prefix + "Noto.Symbols"), // Braille
        (0x2900, 0x2AFF, Prefix + "Noto.Math"),
        (0x2B00, 0x2BFF, Prefix + "Noto.Symbols"),
        (0x2E80, 0x2FDF, Cjk),
        (0x3000, 0x9FFF, Cjk), // CJK punctuation, kana, bopomofo, ideographs
        (0xA980, 0xA9DF, Prefix + "Noto.Javanese"),
        (0xAC00, 0xD7AF, Cjk), // Hangul syllables
        (0xF900, 0xFAFF, Cjk),
        (0xFB1D, 0xFB4F, Prefix + "Noto.Hebrew"),
        (0xFB50, 0xFDFF, Prefix + "Noto.Arabic"),
        (0xFE30, 0xFE4F, Cjk),
        (0xFE70, 0xFEFF, Prefix + "Noto.Arabic"),
        (0xFF00, 0xFFEF, Cjk), // halfwidth and fullwidth forms
        (0x1F000, 0x1FAFF, Emoji),
        (0x20000, 0x3FFFF, Cjk),
    ];

    /// <summary>Finds the package that ships a face for a character.</summary>
    /// <param name="codePoint">The Unicode scalar value.</param>
    /// <returns>The NuGet package id (for example <c>RecursiveAI.OneRender.Fonts.Noto.Arabic</c>), or null when no optional package covers the character.</returns>
    public static string? ForCodePoint(int codePoint)
    {
        int lo = 0;
        int hi = s_ranges.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            (int start, int end, string package) = s_ranges[mid];
            if (codePoint < start)
            {
                hi = mid - 1;
            }
            else if (codePoint > end)
            {
                lo = mid + 1;
            }
            else
            {
                return package;
            }
        }

        return null;
    }
}
