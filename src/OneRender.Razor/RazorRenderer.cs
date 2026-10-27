using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OneRender.Image;
using OneRender.Pdf;

namespace OneRender.Razor;

/// <summary>What <see cref="RazorRenderer.RenderPdfAsync{TComponent}(IReadOnlyDictionary{string, object?}?, HtmlPdfOptions?, CancellationToken)"/> produced.</summary>
/// <param name="Bytes">The PDF file; null when it was written to a caller's stream.</param>
/// <param name="PageCount">The number of pages.</param>
/// <param name="Diagnostics">What the layout, painter and writer approximated or left out, including resources the host did not have.</param>
/// <param name="MissingAssets">The web-root paths the document referred to that no file provider had, in the order they were asked for. Empty when all were found.</param>
public sealed record RazorPdfResult(byte[]? Bytes, int PageCount, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string> MissingAssets);

/// <summary>What <see cref="RazorRenderer.RenderImageAsync{TComponent}"/> produced.</summary>
/// <param name="Image">The pixels of the viewport.</param>
/// <param name="MissingAssets">The web-root paths the document referred to that no file provider had. Empty when all were found.</param>
public sealed record RazorImageResult(RgbaImage Image, IReadOnlyList<string> MissingAssets)
{
    /// <summary>Encodes <see cref="Image"/> as a PNG file.</summary>
    /// <returns>The PNG bytes, never empty.</returns>
    public byte[] ToPng() => RazorRenderer.EncodePng(Image);
}

/// <summary>
/// Renders a Razor component to a PDF or an image in one call. It runs the component on .NET's <see cref="HtmlRenderer"/>, serves the
/// document's style sheets, images and fonts from the web root and static web assets (<see cref="StaticAssetResolver"/>), and hands the
/// markup to <see cref="HtmlPdf"/> or <see cref="HtmlRender"/>. It is a thin layer: everything it does can be done with those public APIs.
/// </summary>
/// <remarks>
/// The component renders inside a fresh service scope per call, so scoped services behave as in a request. When the component renders a
/// fragment instead of a whole document, it is wrapped in one that links <see cref="RazorRenderOptions.Stylesheets"/>. The instance is
/// thread-safe and meant to be shared (<c>AddOneRender()</c> registers it as a singleton).
/// </remarks>
public sealed class RazorRenderer
{
    private readonly IServiceProvider _services;
    private readonly StaticAssetResolver _assets;
    private readonly RazorRenderOptions _options;

    /// <summary>Creates a renderer.</summary>
    /// <param name="services">The service provider components get their injected services from. Its <see cref="ILoggerFactory"/>, if any, is used.</param>
    /// <param name="webRoot">The provider rooted at the web root, normally <c>IWebHostEnvironment.WebRootFileProvider</c>.</param>
    /// <param name="options">Base address, extra asset providers and wrapper settings; null for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="webRoot"/> is null.</exception>
    public RazorRenderer(IServiceProvider services, IFileProvider webRoot, RazorRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(webRoot);
        _services = services;
        _options = options ?? new RazorRenderOptions();
        var providers = new List<IFileProvider>(1 + _options.AdditionalAssets.Count) { webRoot };
        providers.AddRange(_options.AdditionalAssets);
        _assets = new StaticAssetResolver(providers.ToArray());
    }

    /// <summary>Renders the component to the HTML the engine will lay out, without rendering it.</summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="parameters">Values for the component's parameters by name; null for none.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>A complete document: the component's own when it has an <c>&lt;html&gt;</c> element, otherwise a wrapper around its markup.</returns>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    /// <exception cref="InvalidOperationException">The component threw; the exception is the component's own.</exception>
    public async Task<string> RenderHtmlAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
        where TComponent : IComponent
    {
        cancellationToken.ThrowIfCancellationRequested();
        IServiceScope? scope = _services.GetService<IServiceScopeFactory>()?.CreateScope();
        try
        {
            IServiceProvider sp = scope?.ServiceProvider ?? _services;
            ILoggerFactory logs = sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
            await using var renderer = new HtmlRenderer(sp, logs);
            ParameterView view = parameters is null ? ParameterView.Empty : ParameterView.FromDictionary(ToDictionary(parameters));
            string markup = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = await renderer.RenderComponentAsync<TComponent>(view);
                return root.ToHtmlString();
            }).WaitAsync(cancellationToken).ConfigureAwait(false);
            return Wrap(markup);
        }
        finally
        {
            if (scope is IAsyncDisposable ad)
            {
                await ad.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                scope?.Dispose();
            }
        }
    }

    /// <summary>Renders the component to a tagged PDF held in memory.</summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="parameters">Values for the component's parameters by name; null for none.</param>
    /// <param name="options">
    /// Page size and margins, output options and fonts, as for <see cref="HtmlPdf.ToPdf(string, Stream, HtmlPdfOptions, CancellationToken)"/>. Its <c>BaseUrl</c> and <c>Resources</c> are
    /// replaced by this renderer's: the web root is asked first, then the options' own <c>Resources</c>, if any.
    /// </param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>The file bytes, page count and diagnostics.</returns>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    /// <exception cref="PdfRenderException">A limit is reached or a requested profile's rules are broken.</exception>
    public async Task<RazorPdfResult> RenderPdfAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        IReadOnlyDictionary<string, object?>? parameters = null, HtmlPdfOptions? options = null, CancellationToken cancellationToken = default)
        where TComponent : IComponent
    {
        using var ms = new MemoryStream();
        RazorPdfResult r = await RenderPdfAsync<TComponent>(ms, parameters, options, cancellationToken).ConfigureAwait(false);
        return r with { Bytes = ms.ToArray() };
    }

    /// <summary>Renders the component to a tagged PDF written to <paramref name="output"/>.</summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="output">Where the PDF goes; closed afterwards unless the output options leave it open.</param>
    /// <param name="parameters">Values for the component's parameters by name; null for none.</param>
    /// <param name="options">As for the in-memory overload.</param>
    /// <param name="cancellationToken">Cancels the render; the output is then incomplete.</param>
    /// <returns>The page count and diagnostics; <see cref="RazorPdfResult.Bytes"/> is null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public async Task<RazorPdfResult> RenderPdfAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        Stream output, IReadOnlyDictionary<string, object?>? parameters = null, HtmlPdfOptions? options = null, CancellationToken cancellationToken = default)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(output);
        string html = await RenderHtmlAsync<TComponent>(parameters, cancellationToken).ConfigureAwait(false);
        var misses = new List<string>();
        options ??= new HtmlPdfOptions();
        var effective = new HtmlPdfOptions
        {
            Page = options.Page,
            Output = options.Output,
            Fonts = options.Fonts,
            Layout = options.Layout,
            BaseUrl = _options.BaseUrl,
            Resources = Chain(options.Resources, misses),
        };
        HtmlPdfResult r = HtmlPdf.ToPdf(html, output, effective, cancellationToken);
        return new RazorPdfResult(null, r.PageCount, r.Diagnostics, misses);
    }

    /// <summary>Renders the component to pixels.</summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="parameters">Values for the component's parameters by name; null for none.</param>
    /// <param name="options">
    /// Viewport, device-pixel ratio, fonts and page color, as for <see cref="HtmlRender.ToImage"/>. Its <c>BaseUrl</c> and <c>Resources</c>
    /// are replaced by this renderer's: the web root is asked first, then the options' own <c>Resources</c>, if any.
    /// </param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>The image and any assets that were missing.</returns>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The viewport or device-pixel ratio is out of range.</exception>
    public async Task<RazorImageResult> RenderImageAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        IReadOnlyDictionary<string, object?>? parameters = null, HtmlRenderOptions? options = null, CancellationToken cancellationToken = default)
        where TComponent : IComponent
    {
        string html = await RenderHtmlAsync<TComponent>(parameters, cancellationToken).ConfigureAwait(false);
        var misses = new List<string>();
        options ??= new HtmlRenderOptions();
        var effective = new HtmlRenderOptions
        {
            Width = options.Width,
            Height = options.Height,
            DevicePixelRatio = options.DevicePixelRatio,
            Fonts = options.Fonts,
            Layout = options.Layout,
            PageColor = options.PageColor,
            AnimationTime = options.AnimationTime,
            BaseUrl = _options.BaseUrl,
            Resources = Chain(options.Resources, misses),
        };
        RgbaImage image = HtmlRender.ToImage(html, effective, cancellationToken);
        return new RazorImageResult(image, misses);
    }

    internal static byte[] EncodePng(RgbaImage img)
    {
        ArgumentNullException.ThrowIfNull(img);
        var px = new OneImage.Rgba32[img.Width * img.Height];
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = new OneImage.Rgba32(img.Pixels[i * 4], img.Pixels[(i * 4) + 1], img.Pixels[(i * 4) + 2], img.Pixels[(i * 4) + 3]);
        }

        using var ms = new MemoryStream();
        OneImage.Image.FromPixels<OneImage.Rgba32>(px, img.Width, img.Height).Save(ms, OneImage.PngFormat.Instance);
        return ms.ToArray();
    }

    private Func<string, byte[]?> Chain(Func<string, byte[]?>? next, List<string> misses) => url =>
    {
        byte[]? found = _assets.Resolve(url);
        if (found is not null)
        {
            return found;
        }

        found = next?.Invoke(url);
        if (found is null && StaticAssetResolver.ToPath(url) is { } path && !misses.Contains(path))
        {
            misses.Add(path);
        }

        return found;
    };

    private static Dictionary<string, object?> ToDictionary(IReadOnlyDictionary<string, object?> parameters)
    {
        var d = new Dictionary<string, object?>(parameters.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> kv in parameters)
        {
            d[kv.Key] = kv.Value;
        }

        return d;
    }

    private string Wrap(string markup)
    {
        bool hasHtml = markup.Contains("<html", StringComparison.OrdinalIgnoreCase);
        bool hasDoctype = markup.Contains("<!doctype", StringComparison.OrdinalIgnoreCase);
        if (hasHtml)
        {
            return hasDoctype ? markup : "<!DOCTYPE html>" + markup;
        }

        var sb = new StringBuilder(markup.Length + 256);
        sb.Append("<!DOCTYPE html><html lang=\"").Append(WebUtility.HtmlEncode(_options.Language)).Append("\"><head><meta charset=\"utf-8\">");
        if (_options.Title is { } title)
        {
            sb.Append("<title>").Append(WebUtility.HtmlEncode(title)).Append("</title>");
        }

        foreach (string css in _options.Stylesheets)
        {
            sb.Append("<link rel=\"stylesheet\" href=\"").Append(WebUtility.HtmlEncode(css)).Append("\">");
        }

        sb.Append("</head><body>").Append(markup).Append("</body></html>");
        return sb.ToString();
    }
}
