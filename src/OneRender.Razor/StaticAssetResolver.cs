using System.IO;
using Microsoft.Extensions.FileProviders;

namespace OneRender.Razor;

/// <summary>
/// Serves the files of a web application's <c>wwwroot</c> and its static web assets to the renderer: style sheets, images and fonts
/// named by a document. The engine never reads files itself; this is the host side of its resource seam.
/// </summary>
/// <remarks>
/// Addresses are looked up as paths below the web root: <c>css/app.css</c>, <c>/img/logo.png</c>, <c>http://localhost/css/app.css</c> and
/// <c>_content/MyLibrary/site.css</c> all work. A query string or fragment is ignored and percent-escapes are decoded. A path with a
/// <c>..</c> segment is refused. Static web assets of referenced libraries live under <c>_content/&lt;library&gt;/</c>; whether the
/// provider you pass sees them is up to it (the web host's <c>WebRootFileProvider</c> does, in development and when published).
/// </remarks>
public sealed class StaticAssetResolver
{
    private readonly IFileProvider[] _providers;

    /// <summary>Creates a resolver that asks <paramref name="providers"/> in order and returns the first file found.</summary>
    /// <param name="providers">At least one file provider, rooted at the web root.</param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> or an element is null.</exception>
    /// <exception cref="ArgumentException">There are no providers.</exception>
    public StaticAssetResolver(params IFileProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (providers.Length == 0)
        {
            throw new ArgumentException("At least one file provider is required.", nameof(providers));
        }

        foreach (IFileProvider p in providers)
        {
            ArgumentNullException.ThrowIfNull(p);
        }

        _providers = (IFileProvider[])providers.Clone();
    }

    /// <summary>Reads the file an address names.</summary>
    /// <param name="url">An absolute address, a rooted path or a relative path (see the type remarks).</param>
    /// <returns>
    /// The file's bytes, or null when no provider has it, the address is not a plain path, or the file cannot be read. A <c>data:</c>
    /// address is decoded in place (base64 or percent-escaped), and null when it is malformed.
    /// </returns>
    public byte[]? Resolve(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeDataUrl(url);
        }

        string? path = ToPath(url);
        if (path is null)
        {
            return null;
        }

        foreach (IFileProvider provider in _providers)
        {
            IFileInfo file;
            try
            {
                file = provider.GetFileInfo(path);
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!file.Exists || file.IsDirectory)
            {
                continue;
            }

            try
            {
                using Stream s = file.CreateReadStream();
                var ms = new MemoryStream(file.Length > 0 && file.Length < int.MaxValue ? (int)file.Length : 0);
                s.CopyTo(ms);
                return ms.ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return null;
    }

    // RFC 2397: data:[<mediatype>][;base64],<data>. The engine hands every address to the host, data addresses included.
    private static byte[]? DecodeDataUrl(string url)
    {
        int comma = url.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            return null;
        }

        bool base64 = url.AsSpan(5, comma - 5).EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        string payload = url[(comma + 1)..];
        try
        {
            if (base64)
            {
                return Convert.FromBase64String(Uri.UnescapeDataString(payload));
            }

            return System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Turns an address into a path below the web root, with no leading slash.</summary>
    /// <param name="url">The address.</param>
    /// <returns>The path, or null for an empty address, a <c>data:</c> address, an address with a <c>..</c> segment, or a scheme the host does not serve.</returns>
    public static string? ToPath(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        string s = url.Trim();
        if (s.Length == 0 || s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0)
        {
            string name = s[..scheme];
            if (!name.Equals("http", StringComparison.OrdinalIgnoreCase) && !name.Equals("https", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("app", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            int slash = s.IndexOf('/', scheme + 3);
            s = slash < 0 ? string.Empty : s[slash..];
        }
        else if (s.StartsWith("//", StringComparison.Ordinal))
        {
            int slash = s.IndexOf('/', 2);
            s = slash < 0 ? string.Empty : s[slash..];
        }

        int cut = s.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            s = s[..cut];
        }

        try
        {
            s = Uri.UnescapeDataString(s);
        }
        catch (UriFormatException)
        {
            return null;
        }

        s = s.Replace('\\', '/').TrimStart('/');
        if (s.Length == 0)
        {
            return null;
        }

        foreach (string segment in s.Split('/'))
        {
            if (segment == ".." || segment.Contains('\0', StringComparison.Ordinal))
            {
                return null;
            }
        }

        return s;
    }
}
