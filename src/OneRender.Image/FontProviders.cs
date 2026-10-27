using System.Diagnostics.CodeAnalysis;
using OneRender.Fonts;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Text;

namespace OneRender.Image;

/// <summary>
/// The engine's text service over the fonts that ship with <c>RecursiveAI.OneRender.Fonts</c>: all twenty faces registered with
/// the package's family aliases (Arial, Helvetica, Times New Roman, Courier New, Calibri, Cambria and the generic names), so
/// matching, per-character fallback, <c>@font-face</c> and shaping work as described in <c>docs/text.md</c>. The faces of every opt-in fonts
/// package the application references (Noto scripts, CJK, emoji; see <see cref="OptionalFonts"/>) are registered after them as fallback faces.
/// </summary>
public static class FontProviders
{
    [ThreadStatic]
    private static TextFontProvider? t_current;

    [ThreadStatic]
    private static int t_optionalVersion;

    /// <summary>
    /// Creates a provider with the packaged faces registered. Add <c>@font-face</c> rules or other fonts to its
    /// <see cref="TextFontProvider.Catalog"/>. Each call loads the faces again; keep the provider for the life of the document or host.
    /// </summary>
    /// <param name="resolver">Supplies the bytes of <c>@font-face</c> <c>url()</c> sources; null for none (they become diagnostics).</param>
    /// <param name="systemFonts">Opt-in system fonts for <c>local()</c> and fallback; null (the default) uses none.</param>
    /// <returns>A new provider. It is not thread-safe.</returns>
    public static TextFontProvider CreateDefault(IFontResourceResolver? resolver = null, ISystemFontProvider? systemFonts = null)
    {
        var catalog = new FontCatalog(resolver, new PackagedAliases(), systemFonts);
        foreach (FontFaceInfo info in DefaultFonts.Faces)
        {
            catalog.AddFont(info.ReadAllBytes(), info.Family, info.Weight, info.Slant == FontSlant.Italic ? FontStyleKind.Italic : FontStyleKind.Normal, info.Stretch);
        }

        AddOptionalPackages(catalog);
        return new TextFontProvider(catalog);
    }

    /// <summary>
    /// A default provider shared by the calling thread: created on first use and reused, so one-call helpers do not reload the
    /// faces on every call. Providers are single-thread objects, hence one per thread.
    /// </summary>
    /// <returns>The calling thread's provider.</returns>
    public static TextFontProvider ForCurrentThread()
    {
        // A fonts package registered after the provider was made (a late Register call) starts a new provider so its faces take part.
        int version = OptionalFonts.Version;
        if (t_current is null || t_optionalVersion != version)
        {
            t_current = CreateDefault();
            t_optionalVersion = version;
        }

        return t_current;
    }

    // The faces of every registered opt-in package (Noto scripts, CJK, emoji) follow the default fonts, so they only supply characters
    // the default fonts and the listed families lack. A file shared by several faces (a TrueType Collection) is read once.
    private static void AddOptionalPackages(FontCatalog catalog)
    {
        foreach (OptionalFontPackage package in OptionalFonts.Packages)
        {
            string? lastFile = null;
            byte[]? bytes = null;
            foreach (FontFaceInfo info in package.Faces)
            {
                if (!string.Equals(lastFile, info.FileName, StringComparison.Ordinal))
                {
                    bytes = info.ReadAllBytes();
                    lastFile = info.FileName;
                }

                FontStyleKind style = info.Slant == FontSlant.Italic ? FontStyleKind.Italic : FontStyleKind.Normal;
                if (info.FaceIndex == 0)
                {
                    catalog.AddFont(bytes!, info.Family, info.Weight, style, info.Stretch);
                }
                else
                {
                    catalog.AddFont(OneFont.FontFace.Load(bytes!, info.FaceIndex), info.Family, info.Weight, style, info.Stretch);
                }
            }
        }
    }

    private sealed class PackagedAliases : IFontFamilyAliases
    {
        public bool TryResolve(ReadOnlySpan<char> name, [NotNullWhen(true)] out string? family) => DefaultFonts.TryResolveFamily(name, out family);
    }
}
