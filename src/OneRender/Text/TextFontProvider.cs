using OneRender.Layout;
using OneRender.Style;

namespace OneRender.Text;

/// <summary>
/// The engine's <see cref="IFontProvider"/>: converts computed values to <see cref="FontRequest"/>s (cached per distinct
/// font properties), matches them with a <see cref="FontCatalog"/> and shapes through a <see cref="TextShaper"/>.
/// After the first use of a style, <see cref="Resolve"/>, <see cref="Shape"/> (cache hit) and <see cref="GetMetrics"/> allocate nothing.
/// Not thread-safe.
/// </summary>
public sealed class TextFontProvider : IFontProvider
{
    /// <summary>The most distinct font property sets remembered; past it the table is cleared and rebuilt on demand.</summary>
    public const int MaxRequests = 4096;

    private readonly Dictionary<RequestKey, FontRequest> _requests = new();

    /// <summary>Creates a provider with its own shaper over <paramref name="catalog"/>.</summary>
    /// <param name="catalog">The faces. Add the default fonts, <c>@font-face</c> rules and a resolver to it before layout.</param>
    /// <param name="cacheCapacity">Capacity of the shaped-run cache (see <see cref="TextShaper"/>). Default 4096.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
    public TextFontProvider(FontCatalog catalog, int cacheCapacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Catalog = catalog;
        Shaper = new TextShaper(catalog, cacheCapacity);
    }

    /// <inheritdoc/>
    public FontCatalog Catalog { get; }

    /// <summary>The shaper behind <see cref="Shape"/>; use it for <see cref="TextShaper.ShapeInto"/> into retained buffers.</summary>
    public TextShaper Shaper { get; }

    /// <summary>
    /// The request for a style: <c>font-family</c> (generic keywords as generics), weight, style, oblique angle, stretch and size.
    /// A <c>font-size</c> of 0 or less is raised to 0.001 px, since a request needs a positive size; weight is clamped to 1..1000.
    /// Equal font properties return the same object.
    /// </summary>
    /// <param name="style">The computed values.</param>
    /// <returns>The cached request.</returns>
    public FontRequest GetRequest(in ComputedValues style)
    {
        var key = new RequestKey(style.FontFamily, style.FontWeight, style.FontStyle, style.ObliqueAngle, style.FontStretch, style.FontSize);
        if (_requests.TryGetValue(key, out FontRequest? hit))
        {
            return hit;
        }

        if (_requests.Count >= MaxRequests)
        {
            _requests.Clear();
        }

        FontFamilyList list = style.FontFamily ?? FontFamilyList.Default;
        var names = new FontFamilyName[list.Count];
        for (int i = 0; i < names.Length; i++)
        {
            GenericFamily g = list.IsGeneric(i) ? FontFamilyName.GenericFromKeyword(list[i]) : GenericFamily.None;
            names[i] = g != GenericFamily.None ? FontFamilyName.OfGeneric(g) : new FontFamilyName(list[i]);
        }

        double size = style.FontSize > 0 && double.IsFinite(style.FontSize) ? style.FontSize : 0.001;
        double weight = double.IsNaN(style.FontWeight) ? 400 : Math.Clamp(style.FontWeight, 1, 1000);
        double stretch = style.FontStretch > 0 && double.IsFinite(style.FontStretch) ? style.FontStretch : 100;
        double angle = double.IsNaN(style.ObliqueAngle) ? 14 : Math.Clamp(style.ObliqueAngle, -90, 90);
        var request = new FontRequest(names, (float)size, (float)weight, style.FontStyle, (float)angle, (float)stretch);
        _requests[key] = request;
        return request;
    }

    /// <inheritdoc/>
    public ResolvedFontList Resolve(in ComputedValues style) => Catalog.Resolve(GetRequest(in style));

    /// <inheritdoc/>
    public ShapedText Shape(ReadOnlySpan<char> text, in ComputedValues style, bool rightToLeft = false) => Shaper.Shape(text, GetRequest(in style), rightToLeft);

    /// <inheritdoc/>
    public FontMetricsInfo GetMetrics(in ComputedValues style) => Resolve(in style).GetMetrics((float)GetRequest(in style).Size);

    private readonly struct RequestKey(FontFamilyList? families, double weight, FontStyleKind style, double angle, double stretch, double size) : IEquatable<RequestKey>
    {
        private readonly FontFamilyList? _families = families;
        private readonly double _weight = weight;
        private readonly double _angle = angle;
        private readonly double _stretch = stretch;
        private readonly double _size = size;
        private readonly FontStyleKind _style = style;

        public bool Equals(RequestKey o) =>
            (ReferenceEquals(_families, o._families) || (_families is not null && _families.Equals(o._families)))
            && _weight.Equals(o._weight) && _style == o._style && _angle.Equals(o._angle) && _stretch.Equals(o._stretch) && _size.Equals(o._size);

        public override bool Equals(object? obj) => obj is RequestKey k && Equals(k);

        public override int GetHashCode() => HashCode.Combine(_families?.GetHashCode() ?? 0, _weight, (int)_style, _angle, _stretch, _size);
    }
}
