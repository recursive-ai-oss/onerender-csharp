using OneFont;
using OneRender.Fonts;
using OneRender.Layout;
using OneRender.Style;

namespace OneRender.Image;

/// <summary>
/// Picks faces from the fonts that ship with <c>RecursiveAI.OneRender.Fonts</c>: the first family of <c>font-family</c> that
/// the package covers (generic names and common aliases included, see <see cref="DefaultFonts.TryResolveFamily(string?, out string?)"/>),
/// in the regular, bold, italic or bold italic face that <c>font-weight</c> and <c>font-style</c> ask for, followed by Liberation Sans
/// as the fallback. Loaded faces are cached for the life of the provider.
/// </summary>
public sealed class DefaultFontProvider : IFontProvider
{
    private readonly Dictionary<(string Family, int Style), FontFallbackChain> _chains = [];
    private readonly Dictionary<(string Family, int Style), FontFace> _faces = [];
    private const string SansFamily = "Liberation Sans";
    private readonly object _gate = new();

    /// <inheritdoc />
    public FontFallbackChain GetFallbackChain(in ComputedValues style)
    {
        string family = "Liberation Serif";
        FontFamilyList list = style.FontFamily;
        for (int i = 0; i < list.Count; i++)
        {
            if (DefaultFonts.TryResolveFamily(list[i], out string? resolved))
            {
                family = resolved;
                break;
            }
        }

        int bits = (style.FontWeight >= 600 ? 1 : 0) | (style.FontStyle != FontStyleKind.Normal ? 2 : 0);
        lock (_gate)
        {
            if (_chains.TryGetValue((family, bits), out FontFallbackChain? chain))
            {
                return chain;
            }

            FontFace primary = Face(family, bits);
            chain = string.Equals(family, SansFamily, StringComparison.Ordinal)
                ? new FontFallbackChain([primary])
                : new FontFallbackChain([primary, Face(SansFamily, bits)]);
            _chains[(family, bits)] = chain;
            return chain;
        }
    }

    private FontFace Face(string family, int bits)
    {
        if (_faces.TryGetValue((family, bits), out FontFace? face))
        {
            return face;
        }

        // The four faces of a family are listed regular, bold, italic, bold italic, which is the order of `bits`.
        ReadOnlySpan<FontFaceInfo> infos = DefaultFonts.GetFaces(family);
        face = FontFace.Load(infos[Math.Min(bits, infos.Length - 1)].ReadAllBytes());
        _faces[(family, bits)] = face;
        return face;
    }
}
