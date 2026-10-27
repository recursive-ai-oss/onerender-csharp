using OneRender.Painting;

namespace OneRender.Image;

/// <summary>Supplies the pixels behind the <see cref="ImageHandle"/>s of a display list. The rasterizer never loads anything itself.</summary>
public interface IImageResolver
{
    /// <summary>Returns the image for a handle, or null when the host has none (the image item is then skipped).</summary>
    /// <param name="handle">The handle a list item carries.</param>
    /// <returns>The pixels, RGBA, straight alpha, or null.</returns>
    RgbaImage? Resolve(ImageHandle handle);
}

/// <summary>Settings for <see cref="DisplayListRasterizer"/>.</summary>
public sealed class RasterOptions
{
    /// <summary>
    /// Device pixels per CSS pixel; every list coordinate is multiplied by it. Default 1. Must be finite and between 0.01 and 64.
    /// </summary>
    public float DevicePixelRatio { get; set; } = 1f;

    /// <summary>What the surface is cleared to before drawing. Default opaque white; use <see cref="PaintColor.Transparent"/> for a transparent image.</summary>
    public PaintColor Background { get; set; } = PaintColor.White;

    /// <summary>Resolves image handles. Null means every image item is skipped.</summary>
    public IImageResolver? Images { get; set; }

    /// <summary>Receives a diagnostic for content the rasterizer could not draw (an inline SVG that failed to render or is too large). Null drops them.</summary>
    public DiagnosticBag? Diagnostics { get; set; }
}

/// <summary>Rasterizing a display list failed for a reason in the list or the surface size, not in the caller's arguments.</summary>
public sealed class RasterizationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public RasterizationException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public RasterizationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying failure.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public RasterizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The reference rasterizer: draws a <see cref="DisplayList"/> into pixels through oneimage. Every test of the engine's
/// output looks at pixels made here. See docs/display-list.md for what each item means; the notes below say how this
/// backend realizes them.
/// <list type="bullet">
/// <item>No pixel snapping: coordinates are scaled by the device-pixel ratio and anti-aliased as given. A painter that wants crisp
/// edges snaps its rectangles before putting them in the list.</item>
/// <item>Blending is done on encoded sRGB values, as CSS specifies.</item>
/// <item>A group (opacity, blend mode) gets a transparent layer the size of the surface; at most 16 groups may be open at
/// once and live layers may not exceed 128 Mpixel in total.</item>
/// <item>Blurs are clamped to a standard deviation of 256 device pixels.</item>
/// </list>
/// </summary>
public static class DisplayListRasterizer
{
    /// <summary>Largest surface edge in device pixels.</summary>
    public const int MaxDimension = 16384;

    /// <summary>Most device pixels a surface may have (64 Mpixel).</summary>
    public const long MaxPixels = 1L << 26;

    /// <summary>Draws the list into a new image.</summary>
    /// <param name="list">The list to draw; it must have no open pushes.</param>
    /// <param name="width">Image width in device pixels, 1 to <see cref="MaxDimension"/>.</param>
    /// <param name="height">Image height in device pixels, 1 to <see cref="MaxDimension"/>.</param>
    /// <param name="options">Device-pixel ratio, background and image resolver; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the drawing between items.</param>
    /// <returns>An RGBA image, straight alpha, rows top to bottom.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size or the device-pixel ratio is outside the allowed range.</exception>
    /// <exception cref="DisplayListException">The list has unclosed pushes.</exception>
    /// <exception cref="RasterizationException">The drawing failed, for example because a layer would be too large.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static RgbaImage Render(DisplayList list, int width, int height, RasterOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        RasterOptions o = options ?? new RasterOptions();
        CheckArgs(width, height, o);
        list.Validate();
        try
        {
            using Rasterizer r = new(list, width, height, o, cancellationToken);
            r.Run(new RectI(0, 0, width, height));
            return r.ToImage();
        }
        catch (Exception ex) when (ex is OneImage.ImageException or OverflowException)
        {
            throw new RasterizationException("The display list could not be rasterized: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Redraws only a region of an existing surface: the region is cleared to the background and every item that touches it is
    /// drawn again, clipped to the region. Pixels outside it are untouched. Use it with <see cref="DamageRegion.ToDevicePixels"/>.
    /// </summary>
    /// <param name="list">The list to draw.</param>
    /// <param name="surface">The image to update in place; its size is the surface size.</param>
    /// <param name="region">The device-pixel rectangle to redraw; it is clamped to the surface.</param>
    /// <param name="options">Device-pixel ratio, background and image resolver; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the drawing between items.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The surface size or the device-pixel ratio is outside the allowed range.</exception>
    /// <exception cref="DisplayListException">The list has unclosed pushes.</exception>
    /// <exception cref="RasterizationException">The drawing failed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static void RenderRegion(DisplayList list, RgbaImage surface, RectI region, RasterOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(surface);
        RasterOptions o = options ?? new RasterOptions();
        CheckArgs(surface.Width, surface.Height, o);
        list.Validate();
        int x0 = Math.Clamp(region.X, 0, surface.Width), y0 = Math.Clamp(region.Y, 0, surface.Height);
        int x1 = Math.Clamp(region.Right, 0, surface.Width), y1 = Math.Clamp(region.Bottom, 0, surface.Height);
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        try
        {
            using Rasterizer r = new(list, surface.Width, surface.Height, o, cancellationToken);
            r.LoadPixels(surface);
            r.Run(new RectI(x0, y0, x1 - x0, y1 - y0));
            r.StorePixels(surface, new RectI(x0, y0, x1 - x0, y1 - y0));
        }
        catch (Exception ex) when (ex is OneImage.ImageException or OverflowException)
        {
            throw new RasterizationException("The display list could not be rasterized: " + ex.Message, ex);
        }
    }

    private static void CheckArgs(int width, int height, RasterOptions o)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxDimension);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxDimension);
        if ((long)width * height > MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The surface has more than 64 Mpixel.");
        }

        if (!float.IsFinite(o.DevicePixelRatio) || o.DevicePixelRatio < 0.01f || o.DevicePixelRatio > 64f)
        {
            throw new ArgumentOutOfRangeException(nameof(o), "The device-pixel ratio must be between 0.01 and 64.");
        }
    }
}
