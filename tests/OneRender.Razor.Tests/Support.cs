using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace OneRender.Razor.Tests;

/// <summary>A file provider over a dictionary of path to bytes.</summary>
internal sealed class MemoryFiles : IFileProvider
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public MemoryFiles Add(string path, string text) => Add(path, Encoding.UTF8.GetBytes(text));

    public MemoryFiles Add(string path, byte[] bytes)
    {
        _files[path] = bytes;
        return this;
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        string p = subpath.TrimStart('/');
        return _files.TryGetValue(p, out byte[]? b) ? new Info(p, b) : new NotFoundFileInfo(p);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private sealed class Info(string name, byte[] bytes) : IFileInfo
    {
        public bool Exists => true;
        public long Length => bytes.Length;
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream(bytes, writable: false);
    }
}

/// <summary>A component that renders the markup in <see cref="Html"/> inside a body, with an optional greeting parameter.</summary>
public sealed class Page : ComponentBase
{
    [Parameter] public string Html { get; set; } = string.Empty;

    [Parameter] public string Name { get; set; } = "nobody";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddMarkupContent(0, Html);
        builder.OpenElement(1, "p");
        builder.AddContent(2, "Hello, " + Name);
        builder.CloseElement();
    }
}

/// <summary>A component that waits until released, to test cancellation while rendering.</summary>
public sealed class SlowPage : ComponentBase
{
    public static TaskCompletionSource Gate { get; set; } = new();

    protected override Task OnInitializedAsync() => Gate.Task;

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "done");
}

/// <summary>A scoped service, to prove components get a scope per render.</summary>
public sealed class Counter
{
    public int Value { get; set; }
}

/// <summary>A component that injects <see cref="Counter"/> and bumps it.</summary>
public sealed class CounterPage : ComponentBase
{
    [Inject] public Counter Counter { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "count " + (++Counter.Value));
}
