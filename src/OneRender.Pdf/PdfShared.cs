using System.IO;
using OnePdf;
using OneRender.Image;
using OneRender.Painting;
using OneFont;

namespace OneRender.Pdf;

/// <summary>State shared by every page of one document: the writer, the font and image caches, the structure tree and the diagnostics.</summary>
internal sealed class PdfShared
{
    private readonly Dictionary<FontFace, PdfCreatedFont?> _fonts = new(ReferenceEqualityComparer.Instance as IEqualityComparer<FontFace>);
    private readonly Dictionary<int, PdfCreatedImage?> _images = [];
    private readonly Dictionary<(FontFace Face, int Glyph), PdfCreatedImage?> _glyphImages = [];

    public PdfShared(PdfDocumentWriter writer, PdfOutputOptions options, IImageResolver? images, DiagnosticBag diagnostics)
    {
        Writer = writer;
        Options = options;
        Images = images;
        Diagnostics = diagnostics;
        Tags = new TagWriter(writer, options.Tagged);
    }

    public PdfDocumentWriter Writer { get; }

    public PdfOutputOptions Options { get; }

    public IImageResolver? Images { get; }

    public DiagnosticBag Diagnostics { get; }

    public TagWriter Tags { get; }

    public int Pages { get; set; }

    /// <summary>Total number of embedded raster-island pixels, to bound a hostile document that is all filters.</summary>
    public long IslandPixels { get; set; }

    public const long MaxIslandPixels = 400_000_000;

    public PdfCreatedFont? FontFor(FontFace face)
    {
        if (_fonts.TryGetValue(face, out PdfCreatedFont? font))
        {
            return font;
        }

        byte[]? file = SfntBuilder.Build(face);
        if (file is null)
        {
            Diagnostics.Add(DiagnosticKind.Unsupported, "font '" + face.FamilyName + "' has no embeddable outlines; its text is not written");
            font = null;
        }
        else
        {
            try
            {
                font = Writer.AddFont(file, new PdfEmbedFontOptions { Subset = true });
            }
            catch (PdfException e)
            {
                Diagnostics.Add(DiagnosticKind.Unsupported, "font '" + face.FamilyName + "' cannot be embedded: " + e.Message);
                font = null;
            }
        }

        _fonts[face] = font;
        return font;
    }

    /// <summary>The embedded image of one glyph of a color bitmap font, or null when the font has none for it or it cannot be decoded.</summary>
    public PdfCreatedImage? GlyphImageFor(FontFace face, int glyphId, in OneRender.Text.ColorBitmapGlyph glyph)
    {
        if (_glyphImages.TryGetValue((face, glyphId), out PdfCreatedImage? image))
        {
            return image;
        }

        RgbaImage? pixels = ColorGlyphBitmaps.Decode(glyph.Png.Span);
        image = pixels is null ? null : Embed(pixels);
        _glyphImages[(face, glyphId)] = image;
        return image;
    }

    public PdfCreatedImage? ImageFor(ImageHandle handle)
    {
        if (_images.TryGetValue(handle.Value, out PdfCreatedImage? image))
        {
            return image;
        }

        RgbaImage? pixels = Images?.Resolve(handle);
        image = pixels is null ? null : Embed(pixels);
        if (image is null)
        {
            Diagnostics.Add(DiagnosticKind.MissingResource, "image " + handle.Value + " was not supplied; it is not drawn");
        }

        _images[handle.Value] = image;
        return image;
    }

    public PdfCreatedImage Embed(RgbaImage pixels)
    {
        int count = pixels.Width * pixels.Height;
        byte[] rgb = new byte[count * 3];
        byte[] alpha = new byte[count];
        bool translucent = false;
        byte[] src = pixels.Pixels;
        for (int i = 0; i < count; i++)
        {
            rgb[i * 3] = src[i * 4];
            rgb[(i * 3) + 1] = src[(i * 4) + 1];
            rgb[(i * 3) + 2] = src[(i * 4) + 2];
            byte a = src[(i * 4) + 3];
            alpha[i] = a;
            translucent |= a != 255;
        }

        return translucent
            ? Writer.AddRawImage(pixels.Width, pixels.Height, PdfImageColor.Rgb, rgb, alpha)
            : Writer.AddRawImage(pixels.Width, pixels.Height, PdfImageColor.Rgb, rgb);
    }
}
