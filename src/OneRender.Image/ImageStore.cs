using OneImage;
using OneRender.Painting;
using OneRender.Tree;
using OneRender.Style;
using ImageInfo = OneRender.Painting.ImageInfo;

namespace OneRender.Image;

/// <summary>
/// Decodes the images a document refers to through oneimage, from bytes the host supplies, and answers all three questions the
/// engine and the rasterizer ask: how big is it (layout), what is its handle (painting), and what are its pixels (rasterizing).
/// Nothing is fetched here: <c>resources</c> is the host's lookup.
/// </summary>
/// <remarks>Each address is looked up and decoded once; a failure is remembered too. Not thread-safe.</remarks>
public sealed class ImageStore : IImageSource, IImageResolver, IResourceResolver
{
    private readonly Func<string, byte[]?> _resources;
    private readonly string? _baseUrl;
    private readonly Dictionary<string, int> _byAddress = new(StringComparer.Ordinal);
    private readonly List<RgbaImage?> _images = [];

    /// <summary>Creates a store.</summary>
    /// <param name="resources">Returns the encoded bytes of an image by absolute address, or null when the host does not have it.</param>
    /// <param name="baseUrl">The document's address; relative references resolve against it before the host is asked. May be null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is null.</exception>
    public ImageStore(Func<string, byte[]?> resources, string? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _resources = resources;
        _baseUrl = baseUrl;
    }

    /// <summary>Registers decoded pixels for an address, replacing whatever the resolver would give.</summary>
    /// <param name="address">The address as written in the document.</param>
    /// <param name="image">The pixels.</param>
    public void Add(string address, RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(image);
        _byAddress[address] = _images.Count;
        _images.Add(image);
    }

    /// <inheritdoc/>
    public bool TryGetImage(string address, out ImageInfo image)
    {
        image = default;
        int index = Lookup(address);
        RgbaImage? img = _images[index];
        if (img is null)
        {
            return false;
        }

        image = new ImageInfo(new ImageHandle(index), img.Width, img.Height);
        return true;
    }

    /// <inheritdoc/>
    public RgbaImage? Resolve(ImageHandle handle) =>
        (uint)handle.Value < (uint)_images.Count ? _images[handle.Value] : null;

    /// <inheritdoc/>
    public bool TryGetNaturalSize(ReplacedKind kind, ReadOnlySpan<char> source, out double width, out double height)
    {
        width = height = 0;
        if (kind != ReplacedKind.Image || source.IsEmpty || !TryGetImage(source.ToString(), out ImageInfo info))
        {
            return false;
        }

        width = info.Width;
        height = info.Height;
        return true;
    }

    private int Lookup(string address)
    {
        if (_byAddress.TryGetValue(address, out int index))
        {
            return index;
        }

        RgbaImage? decoded = null;
        byte[]? bytes = _resources(ResourceAddress.Resolve(_baseUrl, address));
        if (bytes is not null)
        {
            try
            {
                OneImage.Image img = OneImage.Image.Load(bytes);
                Rgba32[] px = img.ToArray<Rgba32>();
                byte[] raw = new byte[px.Length * 4];
                for (int i = 0; i < px.Length; i++)
                {
                    raw[i * 4] = px[i].R;
                    raw[(i * 4) + 1] = px[i].G;
                    raw[(i * 4) + 2] = px[i].B;
                    raw[(i * 4) + 3] = px[i].A;
                }

                decoded = new RgbaImage(img.Width, img.Height, raw);
            }
            catch (ImageException)
            {
                decoded = null; // an undecodable image is a missing one
            }
        }

        index = _images.Count;
        _images.Add(decoded);
        _byAddress[address] = index;
        return index;
    }
}
