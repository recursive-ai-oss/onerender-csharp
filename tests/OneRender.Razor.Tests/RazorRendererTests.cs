using InvoiceApp.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OnePdf;
using OneRender.Image;
using OneRender.Pdf;

namespace OneRender.Razor.Tests;

public class RazorRendererTests
{
    private static readonly byte[] RedBodyCss = System.Text.Encoding.UTF8.GetBytes("html,body{margin:0;background:#ff0000}");

    private static RazorRenderer Create(IFileProvider files, Action<RazorRenderOptions>? configure = null, Action<IServiceCollection>? services = null)
    {
        var sc = new ServiceCollection();
        services?.Invoke(sc);
        var options = new RazorRenderOptions();
        configure?.Invoke(options);
        return new RazorRenderer(sc.BuildServiceProvider(), files, options);
    }

    private static string SampleRoot => Path.Combine(AppContext.BaseDirectory, "sample-wwwroot");

    private static Dictionary<string, object?> Parameters(string name) => new() { ["Name"] = name };

    private static string AllText(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(pdf);
        var sb = new System.Text.StringBuilder();
        foreach (PdfPage p in doc.Pages)
        {
            sb.Append(p.ExtractText().Text).Append('\n');
        }

        return sb.ToString();
    }

    [Fact]
    public async Task SampleInvoicePdfEqualsDirectHtmlPdf()
    {
        using var root = new PhysicalFileProvider(SampleRoot);
        RazorRenderer renderer = Create(root);
        RazorPdfResult viaRazor = await renderer.RenderPdfAsync<Invoice>(InvoiceApp.Sample.Parameters, InvoiceApp.Sample.PdfOptions);

        string html = await renderer.RenderHtmlAsync<Invoice>(InvoiceApp.Sample.Parameters);
        using var direct = new MemoryStream();
        HtmlPdfResult expected = HtmlPdf.ToPdf(html, direct, new HtmlPdfOptions
        {
            Output = InvoiceApp.Sample.PdfOptions.Output,
            BaseUrl = "http://localhost/",
            Resources = url => File.Exists(Path.Combine(SampleRoot, new Uri(url).AbsolutePath.TrimStart('/'))) ? File.ReadAllBytes(Path.Combine(SampleRoot, new Uri(url).AbsolutePath.TrimStart('/'))) : null,
        });

        Assert.Empty(viaRazor.MissingAssets);
        Assert.Equal(expected.PageCount, viaRazor.PageCount);
        Assert.Equal(1, viaRazor.PageCount);
        string text = AllText(viaRazor.Bytes!);
        Assert.Equal(AllText(direct.ToArray()), text);
        Assert.Contains("INV-2026-0418", text, StringComparison.Ordinal);
        Assert.Contains("Fabrikam Logistics LLC", text, StringComparison.Ordinal);
        Assert.Contains("2,212.00", text, StringComparison.Ordinal);
        Assert.Equal(direct.ToArray(), viaRazor.Bytes);
        if (Environment.GetEnvironmentVariable("ONERENDER_RAZOR_OUT") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "invoice.pdf"), viaRazor.Bytes!);
        }
    }

    [Fact]
    public async Task SampleInvoiceImageIsPixelIdenticalToDirectRender()
    {
        using var root = new PhysicalFileProvider(SampleRoot);
        RazorRenderer renderer = Create(root);
        var options = new HtmlRenderOptions { Width = 800, Height = 700 };
        RazorImageResult viaRazor = await renderer.RenderImageAsync<Invoice>(InvoiceApp.Sample.Parameters, options);

        string html = await renderer.RenderHtmlAsync<Invoice>(InvoiceApp.Sample.Parameters);
        RgbaImage expected = HtmlRender.ToImage(html, new HtmlRenderOptions
        {
            Width = 800,
            Height = 700,
            BaseUrl = "http://localhost/",
            Resources = url => File.Exists(Path.Combine(SampleRoot, new Uri(url).AbsolutePath.TrimStart('/'))) ? File.ReadAllBytes(Path.Combine(SampleRoot, new Uri(url).AbsolutePath.TrimStart('/'))) : null,
        });

        Assert.Empty(viaRazor.MissingAssets);
        Assert.Equal(expected.Width, viaRazor.Image.Width);
        Assert.Equal(expected.Pixels, viaRazor.Image.Pixels);
        // The style sheet applied: the page is not a blank white sheet.
        Assert.Contains(viaRazor.Image.Pixels.Where((_, i) => i % 4 == 0), b => b < 200);
        byte[] png = viaRazor.ToPng();
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        if (Environment.GetEnvironmentVariable("ONERENDER_RAZOR_OUT") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "invoice.png"), png);
        }
    }

    [Fact]
    public async Task ParametersReachTheComponent()
    {
        RazorRenderer renderer = Create(new MemoryFiles());
        string html = await renderer.RenderHtmlAsync<Page>(Parameters("Ada & <Grace>"));
        Assert.Contains("Hello, Ada &amp; &lt;Grace&gt;", html, StringComparison.Ordinal);
        RazorPdfResult pdf = await renderer.RenderPdfAsync<Page>(Parameters("Ada"));
        Assert.Contains("Hello, Ada", AllText(pdf.Bytes!), StringComparison.Ordinal);
        string none = await renderer.RenderHtmlAsync<Page>();
        Assert.Contains("Hello, nobody", none, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PdfOptionsPassThrough()
    {
        RazorRenderer renderer = Create(new MemoryFiles(), o => o.Title = "Wrapped");
        RazorPdfResult a4 = await renderer.RenderPdfAsync<Page>(Parameters("x"));
        RazorPdfResult small = await renderer.RenderPdfAsync<Page>(Parameters("x"), new HtmlPdfOptions { Page = new PdfPageSetup(300, 200, 20, 20, 20, 20) });
        using PdfDocument d1 = PdfDocument.Open(a4.Bytes!);
        using PdfDocument d2 = PdfDocument.Open(small.Bytes!);
        Assert.NotEqual(d1.Pages[0].MediaBox, d2.Pages[0].MediaBox);
    }

    [Fact]
    public async Task ImageOptionsPassThrough()
    {
        RazorRenderer renderer = Create(new MemoryFiles());
        RazorImageResult r = await renderer.RenderImageAsync<Page>(Parameters("x"), new HtmlRenderOptions { Width = 120, Height = 40, DevicePixelRatio = 2f });
        Assert.Equal(240, r.Image.Width);
        Assert.Equal(80, r.Image.Height);
    }

    [Fact]
    public async Task MissingAssetsYieldValuesNotExceptions()
    {
        var files = new MemoryFiles();
        RazorRenderer renderer = Create(files, o => o.Stylesheets.Add("css/missing.css"));
        var p = new Dictionary<string, object?> { ["Html"] = "<img src=\"img/gone.png\" width=\"10\" height=\"10\" alt=\"gone\">" };
        RazorPdfResult pdf = await renderer.RenderPdfAsync<Page>(p);
        Assert.Contains("css/missing.css", pdf.MissingAssets);
        Assert.Contains("img/gone.png", pdf.MissingAssets);
        Assert.NotEmpty(pdf.Diagnostics);
        RazorImageResult img = await renderer.RenderImageAsync<Page>(p);
        Assert.Contains("css/missing.css", img.MissingAssets);
    }

    [Fact]
    public async Task StaticWebAssetsUnderContentAreServed()
    {
        var files = new MemoryFiles().Add("_content/Acme.Ui/site.css", RedBodyCss);
        RazorRenderer renderer = Create(files, o => o.Stylesheets.Add("_content/Acme.Ui/site.css"));
        RazorImageResult r = await renderer.RenderImageAsync<Page>(Parameters("x"), new HtmlRenderOptions { Width = 100, Height = 100 });
        Assert.Empty(r.MissingAssets);
        Assert.Equal(255, r.Image.Pixels[(((90 * 100) + 90) * 4)]);
        Assert.Equal(0, r.Image.Pixels[(((90 * 100) + 90) * 4) + 1]);
    }

    [Fact]
    public async Task AdditionalAssetProvidersAreSearchedAfterTheWebRoot()
    {
        var lib = new MemoryFiles().Add("_content/Acme.Ui/site.css", RedBodyCss);
        RazorRenderer renderer = Create(new MemoryFiles(), o =>
        {
            o.AdditionalAssets.Add(lib);
            o.Stylesheets.Add("/_content/Acme.Ui/site.css?v=3");
        });
        RazorImageResult r = await renderer.RenderImageAsync<Page>(Parameters("x"), new HtmlRenderOptions { Width = 50, Height = 50 });
        Assert.Empty(r.MissingAssets);
        Assert.Equal(255, r.Image.Pixels[((45 * 50) + 45) * 4]);
    }

    [Theory]
    [InlineData("css/app.css", "css/app.css")]
    [InlineData("/css/app.css", "css/app.css")]
    [InlineData("http://localhost/css/app.css?v=1#x", "css/app.css")]
    [InlineData("//cdn.example/a/b.css", "a/b.css")]
    [InlineData("/_content/Lib/a%20b.css", "_content/Lib/a b.css")]
    [InlineData("../secret.txt", null)]
    [InlineData("http://localhost/a/../../b", null)]
    [InlineData("data:text/css,a{}", null)]
    [InlineData("ftp://host/x.css", null)]
    [InlineData("", null)]
    public void ToPathMapsAddresses(string url, string? expected) => Assert.Equal(expected, StaticAssetResolver.ToPath(url));

    [Fact]
    public void DataAddressesAreDecodedAndNeverReportedMissing()
    {
        var r = new StaticAssetResolver(new MemoryFiles());
        Assert.Equal("hi", System.Text.Encoding.UTF8.GetString(r.Resolve("data:text/plain;base64,aGk=")!));
        Assert.Equal("a b", System.Text.Encoding.UTF8.GetString(r.Resolve("data:text/plain,a%20b")!));
        Assert.Null(r.Resolve("data:text/plain;base64,***"));
        Assert.Null(r.Resolve("data:nocomma"));
    }

    [Fact]
    public async Task DataImagesRender()
    {
        byte[] png = File.ReadAllBytes(Path.Combine(SampleRoot, "img", "logo.png"));
        RazorRenderer renderer = Create(new MemoryFiles());
        var p = new Dictionary<string, object?> { ["Html"] = "<img style=\"display:block\" src=\"data:image/png;base64," + Convert.ToBase64String(png) + "\" width=\"40\" height=\"40\" alt=\"logo\">" };
        RazorImageResult r = await renderer.RenderImageAsync<Page>(p, new HtmlRenderOptions { Width = 60, Height = 60 });
        Assert.Empty(r.MissingAssets);
        int o = ((28 * 60) + 12) * 4; // inside the logo (at 8,8 after the body margin), left of its white glyph
        Assert.Equal(0x1f, r.Image.Pixels[o]);
    }

    [Fact]
    public async Task CancelledBeforeStartThrows()
    {
        RazorRenderer renderer = Create(new MemoryFiles());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderPdfAsync<Page>(null, null, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderImageAsync<Page>(null, null, cts.Token));
    }

    [Fact]
    public async Task CancelledWhileTheComponentIsRenderingThrows()
    {
        SlowPage.Gate = new TaskCompletionSource();
        RazorRenderer renderer = Create(new MemoryFiles());
        using var cts = new CancellationTokenSource();
        Task<RazorPdfResult> t = renderer.RenderPdfAsync<SlowPage>(null, null, cts.Token);
        Assert.False(t.IsCompleted);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t);
        SlowPage.Gate.SetResult();
    }

    [Fact]
    public async Task AsyncComponentsComplete()
    {
        SlowPage.Gate = new TaskCompletionSource();
        RazorRenderer renderer = Create(new MemoryFiles());
        Task<RazorPdfResult> t = renderer.RenderPdfAsync<SlowPage>();
        Assert.False(t.IsCompleted);
        SlowPage.Gate.SetResult();
        RazorPdfResult r = await t;
        Assert.Contains("done", AllText(r.Bytes!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamOverloadWritesToTheCallersStream()
    {
        RazorRenderer renderer = Create(new MemoryFiles());
        using var ms = new MemoryStream();
        RazorPdfResult r = await renderer.RenderPdfAsync<Page>(ms, Parameters("s"), new HtmlPdfOptions { Output = new PdfOutputOptions { LeaveOpen = true } });
        Assert.Null(r.Bytes);
        Assert.Equal(1, r.PageCount);
        Assert.True(ms.Length > 100);
    }

    private sealed class Env(IFileProvider webRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "/";
        public string EnvironmentName { get; set; } = Environments.Production;
        public string WebRootPath { get; set; } = "/";
        public IFileProvider WebRootFileProvider { get; set; } = webRoot;
    }

    [Fact]
    public async Task AddOneRenderUsesTheHostsWebRootAndScopesComponents()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<IWebHostEnvironment>(new Env(new MemoryFiles().Add("_content/Acme.Ui/site.css", RedBodyCss)));
        sc.AddScoped<Counter>();
        sc.AddOneRender(o => o.Stylesheets.Add("_content/Acme.Ui/site.css"));
        sc.AddOneRender();
        await using ServiceProvider sp = sc.BuildServiceProvider();
        RazorRenderer renderer = sp.GetRequiredService<RazorRenderer>();
        Assert.Same(renderer, sp.GetRequiredService<RazorRenderer>());

        RazorImageResult r = await renderer.RenderImageAsync<CounterPage>(null, new HtmlRenderOptions { Width = 40, Height = 40 });
        Assert.Empty(r.MissingAssets);
        Assert.Equal(255, r.Image.Pixels[((35 * 40) + 35) * 4]);
        // A fresh scope per call: the counter starts again.
        Assert.Contains("count 1", await renderer.RenderHtmlAsync<CounterPage>(), StringComparison.Ordinal);
        Assert.Contains("count 1", await renderer.RenderHtmlAsync<CounterPage>(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddOneRenderWithoutAWebRootFailsClearlyWhenResolved()
    {
        var sc = new ServiceCollection();
        sc.AddOneRender();
        using ServiceProvider sp = sc.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<RazorRenderer>());
    }
}
