using System.IO;
using OneRender.Painting;

namespace OneRender.Image;

/// <summary>
/// A tightly packed RGBA bitmap: four bytes per pixel in the order red, green, blue, alpha, rows top to bottom.
/// </summary>
public sealed class RgbaImage
{
    /// <summary>Creates an image over <paramref name="pixels"/>, which is not copied.</summary>
    /// <param name="width">Width in pixels, at least 1.</param>
    /// <param name="height">Height in pixels, at least 1.</param>
    /// <param name="pixels">Exactly <c>width * height * 4</c> bytes.</param>
    /// <exception cref="ArgumentException">The dimensions are not positive or the buffer has the wrong length.</exception>
    public RgbaImage(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width < 1 || height < 1 || (long)width * height * 4 != pixels.Length)
        {
            throw new ArgumentException("Pixel buffer does not match the image size.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixel bytes, RGBA, not premultiplied.</summary>
    public byte[] Pixels { get; }

    /// <summary>Encodes the image as a PNG file (8 bits per channel, with alpha).</summary>
    /// <returns>The bytes of the PNG file.</returns>
    public byte[] ToPng()
    {
        var px = new OneImage.Rgba32[Width * Height];
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = new OneImage.Rgba32(Pixels[i * 4], Pixels[(i * 4) + 1], Pixels[(i * 4) + 2], Pixels[(i * 4) + 3]);
        }

        using var ms = new MemoryStream();
        OneImage.Image.FromPixels<OneImage.Rgba32>(px, Width, Height).Save(ms, OneImage.PngFormat.Instance);
        return ms.ToArray();
    }
}

/// <summary>
/// The one narrow entry point test harnesses use to turn an HTML document into pixels:
/// "render this document at this viewport to an RGBA image". It is <see cref="HtmlRender.ToImage"/> with the argument shape
/// the reftest runner uses. Transitions and animations are shown at time 0, as a browser's screenshot at load would.
/// </summary>
public static class HtmlRenderSeam
{
    // Root-relative document addresses ("/css/...") get this origin so relative references resolve; it is removed again before
    // the host's resolver is asked, so the host sees root-relative addresses for them.
    private const string Origin = "https://document.invalid";

    /// <summary>
    /// Renders <paramref name="html"/> into a <paramref name="width"/> by <paramref name="height"/> viewport image.
    /// </summary>
    /// <param name="html">The document source.</param>
    /// <param name="documentUri">Absolute or root-relative URI of the document, used to resolve relative references. May be empty.</param>
    /// <param name="width">Viewport width in CSS pixels, 1 to 16384.</param>
    /// <param name="height">Viewport height in CSS pixels, 1 to 16384.</param>
    /// <param name="resources">
    /// Supplies the bytes of a stylesheet by URI, or null when it is unavailable. For a root-relative document URI the argument is
    /// root-relative too. The renderer never fetches anything itself. May be null for none.
    /// </param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>A new image, opaque white where nothing is painted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The viewport size is outside the allowed range.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static RgbaImage Render(
        string html,
        string documentUri,
        int width,
        int height,
        Func<string, byte[]?>? resources = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        bool rooted = documentUri is not null && documentUri.StartsWith('/');
        string? baseUrl = string.IsNullOrEmpty(documentUri) ? null : (rooted ? Origin + documentUri : documentUri);
        Func<string, byte[]?>? inner = resources is null ? null : url =>
            resources(url.StartsWith(Origin, StringComparison.Ordinal) ? url.Substring(Origin.Length) : url);
        return HtmlRender.ToImage(html, new HtmlRenderOptions { Width = width, Height = height, BaseUrl = baseUrl, Resources = inner, AnimationTime = 0 }, cancellationToken);
    }
}
