using System.Collections.Generic;
using System.Threading;
using InvoiceApp;
using InvoiceApp.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OneRender.Razor;

// A web app that renders an invoice component to a PDF and a PNG. Run it and open /invoice.pdf or /invoice.png.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddOneRender();
WebApplication app = builder.Build();

app.MapGet("/invoice.pdf", async (RazorRenderer renderer, CancellationToken ct) =>
{
    RazorPdfResult pdf = await renderer.RenderPdfAsync<Invoice>(Sample.Parameters, Sample.PdfOptions, ct);
    return Results.File(pdf.Bytes!, "application/pdf");
});

app.MapGet("/invoice.png", async (RazorRenderer renderer, CancellationToken ct) =>
{
    RazorImageResult image = await renderer.RenderImageAsync<Invoice>(Sample.Parameters, new OneRender.Image.HtmlRenderOptions { Width = 800, Height = 700 }, ct);
    return Results.File(image.ToPng(), "image/png");
});

app.Run();
