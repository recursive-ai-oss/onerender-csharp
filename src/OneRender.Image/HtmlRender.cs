using OneRender.Layout;
using OneRender.Painting;

namespace OneRender.Image;

/// <summary>Options for <see cref="HtmlRender"/>.</summary>
public sealed class HtmlRenderOptions
{
    /// <summary>Viewport width in CSS px, 1 to 16384. Default 800.</summary>
    public int Width { get; init; } = 800;

    /// <summary>Viewport height in CSS px, 1 to 16384. Default 600.</summary>
    public int Height { get; init; } = 600;

    /// <summary>Device pixels per CSS px, 0.01 to 64. The image is <c>Width * DevicePixelRatio</c> pixels wide. Default 1.</summary>
    public float DevicePixelRatio { get; init; } = 1f;

    /// <summary>The address the document is deemed to have, for resolving relative references. Default none.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Supplies the bytes of a style sheet by absolute address, or null when the host does not have it. The renderer never fetches
    /// anything. Default none.
    /// </summary>
    public Func<string, byte[]?>? Resources { get; init; }

    /// <summary>The fonts to use; null uses <see cref="FontProviders.ForCurrentThread"/>.</summary>
    public IFontProvider? Fonts { get; init; }

    /// <summary>The layout to run; null uses the engine's default.</summary>
    public ILayoutEngine? Layout { get; init; }

    /// <summary>The page color behind everything, painted where the document paints no background. Default opaque white.</summary>
    public PaintColor PageColor { get; init; } = PaintColor.White;
}

/// <summary>
/// The happy path: HTML in, pixels out, in one call. It is built only on the public low level (<see cref="HtmlPage"/>,
/// <see cref="Painter"/>, <see cref="DisplayListRasterizer"/>); a caller who needs control uses those directly.
/// </summary>
public static class HtmlRender
{
    

    /// <summary>Renders <paramref name="html"/> to an RGBA image of the viewport.</summary>
    /// <param name="html">The document source, decoded to text.</param>
    /// <param name="options">Viewport, device-pixel ratio and hooks; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>An image of <c>Width * DevicePixelRatio</c> by <c>Height * DevicePixelRatio</c> pixels (rounded up).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The viewport or the device-pixel ratio is out of range.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static RgbaImage ToImage(string html, HtmlRenderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        options ??= new HtmlRenderOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Width, 16384);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Height, 16384);

        var pageOptions = new HtmlPageOptions(options.Fonts ?? FontProviders.ForCurrentThread())
        {
            ViewportWidth = options.Width,
            ViewportHeight = options.Height,
            BaseUrl = options.BaseUrl,
            StylesheetBytes = options.Resources,
            Layout = options.Layout,
        };
        HtmlPage page = HtmlPage.Create(html, pageOptions, cancellationToken);
        var list = new DisplayList();
        page.Paint(list, null, cancellationToken);
        int w = (int)Math.Ceiling(options.Width * options.DevicePixelRatio);
        int h = (int)Math.Ceiling(options.Height * options.DevicePixelRatio);
        return DisplayListRasterizer.Render(list, w, h, new RasterOptions { DevicePixelRatio = options.DevicePixelRatio, Background = options.PageColor }, cancellationToken);
    }
}
