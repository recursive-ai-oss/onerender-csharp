namespace OneRender.Image;

/// <summary>
/// A tightly packed RGBA bitmap: four bytes per pixel in the order red, green, blue, alpha, rows top to bottom.
/// </summary>
/// <remarks>Provisional: the engine's public rendering API replaces this seam.</remarks>
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
}

/// <summary>
/// The one narrow entry point test harnesses use to turn an HTML document into pixels:
/// "render this document at this viewport to an RGBA image".
/// </summary>
/// <remarks>
/// Provisional. Today it paints an opaque white page and nothing else; the engine is wired in behind it later
/// without changing callers.
/// </remarks>
public static class HtmlRenderSeam
{
    /// <summary>
    /// Renders <paramref name="html"/> into a <paramref name="width"/> by <paramref name="height"/> viewport image.
    /// </summary>
    /// <param name="html">The document source.</param>
    /// <param name="documentUri">Absolute or root-relative URI of the document, used to resolve relative references. May be empty.</param>
    /// <param name="width">Viewport width in CSS pixels, 1 to 16384.</param>
    /// <param name="height">Viewport height in CSS pixels, 1 to 16384.</param>
    /// <param name="resources">
    /// Supplies the bytes of a stylesheet, image or font by absolute URI, or null when it is unavailable.
    /// The renderer never fetches anything itself. May be null for none.
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
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 16384);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 16384);
        cancellationToken.ThrowIfCancellationRequested();
        _ = documentUri;
        _ = resources;

        byte[] pixels = new byte[checked(width * height * 4)];
        pixels.AsSpan().Fill(0xFF);
        return new RgbaImage(width, height, pixels);
    }
}
