using Microsoft.Extensions.FileProviders;

namespace OneRender.Razor;

/// <summary>Options for <see cref="RazorRenderer"/>, set once through <c>AddOneRender</c> or the constructor.</summary>
public sealed class RazorRenderOptions
{
    /// <summary>
    /// The address documents are deemed to have; relative references in a component's markup resolve against it and are then
    /// looked up in the web root. Default <c>http://localhost/</c>.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost/";

    /// <summary>
    /// File providers searched after the web root, in order, for any address (for example a provider over a library's embedded files
    /// registered by the caller). Default empty.
    /// </summary>
    public IList<IFileProvider> AdditionalAssets { get; } = new List<IFileProvider>();

    /// <summary>
    /// Style sheets to link when the component renders a fragment rather than a whole document: addresses such as <c>css/app.css</c>
    /// or <c>_content/MyLibrary/site.css</c>. Ignored when the output already has an <c>&lt;html&gt;</c> element. Default empty.
    /// </summary>
    public IList<string> Stylesheets { get; } = new List<string>();

    /// <summary>The <c>&lt;title&gt;</c> of the wrapper document built around a fragment; it becomes the PDF title. Default none.</summary>
    public string? Title { get; set; }

    /// <summary>The <c>lang</c> of the wrapper document built around a fragment (the PDF's language). Default <c>en</c>.</summary>
    public string Language { get; set; } = "en";
}
