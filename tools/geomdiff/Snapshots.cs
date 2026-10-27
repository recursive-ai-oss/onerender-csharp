using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OneRender.GeomDiff;

internal static class Snapshots
{
    public const string StaticHost = "https://pages.test/";

    /// <summary>Key for URL lookups: lower-case scheme and host, default port dropped, empty path is "/", no fragment.</summary>
    public static string UrlKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? u))
        {
            return url;
        }
        return u.Scheme + "://" + u.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped) + u.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);
    }

    public static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static string PageDir(string root, string id) => Path.Combine(root, id);

    public static List<string> PageIds(string root)
    {
        List<string> ids = [];
        foreach (string d in Directory.EnumerateDirectories(root))
        {
            if (File.Exists(Path.Combine(d, "page.json")))
            {
                ids.Add(Path.GetFileName(d));
            }
        }
        ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    public static (SnapshotPage Page, string Html, Dictionary<string, (byte[] Body, string Type)> Resources) Load(string root, string id)
    {
        string dir = PageDir(root, id);
        SnapshotPage page = Store.Read(Path.Combine(dir, "page.json"), Ctx.Default.SnapshotPage);
        string html = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir, "index.html")));
        Dictionary<string, (byte[], string)> res = new(StringComparer.Ordinal);
        foreach (SnapshotResource r in page.Resources)
        {
            res[UrlKey(r.Url)] = (File.ReadAllBytes(Path.Combine(dir, r.File)), r.ContentType);
        }
        return (page, html, res);
    }

    /// <summary>Replays a frozen page: its own HTML and recorded resources; stylesheets and sub-documents it does not hold are empty; everything else fails.</summary>
    public static Router Replay(SnapshotPage page, string html, Dictionary<string, (byte[] Body, string Type)> resources)
    {
        byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
        string mainKey = UrlKey(page.Url);
        return (url, type, isMain, method) =>
        {
            string key = UrlKey(url);
            if (type == "Document" && isMain && key == mainKey)
            {
                return new Fulfill(200, "text/html; charset=utf-8", htmlBytes);
            }
            if (method == "GET" && type is "Stylesheet" or "Document" && resources.TryGetValue(key, out (byte[] Body, string Type) r))
            {
                return new Fulfill(200, r.Type, r.Body);
            }
            if (type == "Stylesheet")
            {
                return new Fulfill(200, "text/css", []);
            }
            if (type == "Document" && !isMain)
            {
                return new Fulfill(200, "text/html", []);
            }
            return new Fail();
        };
    }

    /// <summary>Builds snapshots for hand-written pages: every <c>src/&lt;id&gt;/index.html</c> with the files beside it.</summary>
    public static int BuildStatic(string src, string output, string idPrefix)
    {
        int n = 0;
        foreach (string d in Directory.EnumerateDirectories(src).Order(StringComparer.Ordinal))
        {
            string index = Path.Combine(d, "index.html");
            if (!File.Exists(index))
            {
                continue;
            }
            string id = idPrefix + Path.GetFileName(d);
            string dest = Path.Combine(output, id);
            Directory.CreateDirectory(dest);
            File.Copy(index, Path.Combine(dest, "index.html"), overwrite: true);
            SnapshotPage page = new() { Id = id, Url = StaticHost + id + "/index.html" };
            foreach (string f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string rel = Path.GetRelativePath(d, f).Replace('\\', '/');
                if (rel == "index.html")
                {
                    continue;
                }
                string file = "res/" + rel;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dest, file))!);
                File.Copy(f, Path.Combine(dest, file), overwrite: true);
                page.Resources.Add(new SnapshotResource { Url = StaticHost + id + "/" + rel, File = file, ContentType = ContentTypeFor(rel) });
            }
            Store.Write(Path.Combine(dest, "page.json"), page, PrettyCtx.Default.SnapshotPage);
            n++;
        }
        return n;
    }

    private static string ContentTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".css" => "text/css",
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        _ => "application/octet-stream",
    };
}

/// <summary>A read-only view of a frozen crawl: manifest records and stored bodies.</summary>
internal sealed class CorpusStore
{
    public sealed class Record
    {
        public string Url = "";
        public int Status;
        public string Sha = "";
        public string ContentType = "";
        public string ContentEncoding = "";
        public string Location = "";
        public long BodyBytes;
        public string SourceKind = "";
        public string Kind = "";
    }

    private readonly string _root;
    private readonly Dictionary<string, Record> _index = new(StringComparer.Ordinal);
    public List<Record> Pages { get; } = [];

    public CorpusStore(string root)
    {
        _root = root;
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(Path.Combine(root, "manifest.jsonl")))
        {
            if (line.Length == 0)
            {
                continue;
            }
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement e = doc.RootElement;
            if (!e.TryGetProperty("url", out JsonElement urlEl) || urlEl.ValueKind != JsonValueKind.String
                || !e.TryGetProperty("status", out JsonElement st) || st.ValueKind != JsonValueKind.Number)
            {
                continue;
            }
            Record r = new()
            {
                Url = urlEl.GetString()!,
                Status = st.GetInt32(),
                Sha = Str(e, "body_sha256"),
                ContentType = Str(e, "content_type"),
                Kind = Str(e, "kind"),
                BodyBytes = e.TryGetProperty("body_bytes", out JsonElement bb) && bb.ValueKind == JsonValueKind.Number ? bb.GetInt64() : 0,
            };
            if (e.TryGetProperty("source", out JsonElement src) && src.ValueKind == JsonValueKind.Object)
            {
                r.SourceKind = Str(src, "kind");
            }
            if (e.TryGetProperty("headers", out JsonElement hs) && hs.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement h in hs.EnumerateArray())
                {
                    if (h.ValueKind == JsonValueKind.Array && h.GetArrayLength() == 2)
                    {
                        string k = h[0].GetString() ?? "";
                        if (string.Equals(k, "content-encoding", StringComparison.OrdinalIgnoreCase))
                        {
                            r.ContentEncoding = (h[1].GetString() ?? "").Trim().ToLowerInvariant();
                        }
                        else if (string.Equals(k, "location", StringComparison.OrdinalIgnoreCase))
                        {
                            r.Location = h[1].GetString() ?? "";
                        }
                    }
                }
            }
            string key = Snapshots.UrlKey(r.Url);
            _index.TryAdd(key, r);
            if (r.Kind == "page" && r.Status == 200 && r.Sha.Length > 0 && r.ContentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)
                && seen.Add(r.Sha) && File.Exists(BodyPath(r.Sha)))
            {
                Pages.Add(r);
            }
        }
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    public string BodyPath(string sha) => Path.Combine(_root, "bodies", sha[..2], sha);

    public Record? Find(string url) => _index.GetValueOrDefault(Snapshots.UrlKey(url));

    /// <summary>The stored body with any transfer compression undone, or null when it cannot be decoded.</summary>
    public byte[]? Body(Record r)
    {
        if (r.Sha.Length == 0)
        {
            return [];
        }
        byte[] raw = File.ReadAllBytes(BodyPath(r.Sha));
        try
        {
            switch (r.ContentEncoding)
            {
                case "" or "identity":
                    return raw;
                case "gzip" or "x-gzip":
                    using (GZipStream gz = new(new MemoryStream(raw), CompressionMode.Decompress))
                    using (MemoryStream o = new())
                    {
                        gz.CopyTo(o);
                        return o.ToArray();
                    }
                case "deflate":
                    using (ZLibStream z = new(new MemoryStream(raw), CompressionMode.Decompress))
                    using (MemoryStream o = new())
                    {
                        z.CopyTo(o);
                        return o.ToArray();
                    }
                case "br":
                    using (BrotliStream b = new(new MemoryStream(raw), CompressionMode.Decompress))
                    using (MemoryStream o = new())
                    {
                        b.CopyTo(o);
                        return o.ToArray();
                    }
                default:
                    return null;
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Follows stored redirects (at most five) to the record that holds a body.</summary>
    public Record? Resolve(string url)
    {
        Record? r = Find(url);
        for (int i = 0; i < 5 && r != null && r.Status is >= 300 and < 400 && r.Location.Length > 0; i++)
        {
            string next = Uri.TryCreate(new Uri(r.Url), r.Location, out Uri? u) ? u.AbsoluteUri : r.Location;
            r = Find(next);
        }
        return r != null && r.Status == 200 ? r : null;
    }
}

internal static class SnapshotBuilder
{
    private const string SerializeExpression =
        "(()=>{for(const s of Array.from(document.querySelectorAll('script')))s.remove();"
        + "const d=document.doctype?new XMLSerializer().serializeToString(document.doctype):'';"
        + "return d+document.documentElement.outerHTML;})()";

    /// <summary>Picks pages with equal allocation per stratum (crawl source x body-size third), in body-hash order.</summary>
    public static List<CorpusStore.Record> Sample(CorpusStore corpus, int count, out string description)
    {
        List<CorpusStore.Record> pages = corpus.Pages;
        long[] sizes = [.. pages.Select(static p => p.BodyBytes).Order()];
        long t1 = sizes[sizes.Length / 3];
        long t2 = sizes[sizes.Length * 2 / 3];
        SortedDictionary<string, List<CorpusStore.Record>> strata = new(StringComparer.Ordinal);
        foreach (CorpusStore.Record p in pages)
        {
            string size = p.BodyBytes <= t1 ? "small" : p.BodyBytes <= t2 ? "medium" : "large";
            string key = p.SourceKind + "/" + size;
            if (!strata.TryGetValue(key, out List<CorpusStore.Record>? l))
            {
                strata[key] = l = [];
            }
            l.Add(p);
        }
        List<CorpusStore.Record> picked = [];
        StringBuilder sb = new();
        int remaining = count;
        int left = strata.Count;
        foreach (KeyValuePair<string, List<CorpusStore.Record>> s in strata.OrderBy(static s => s.Value.Count))
        {
            // Smaller strata first, so a stratum that cannot fill its share hands the rest to the larger ones.
            int take = Math.Min(s.Value.Count, remaining / left);
            picked.AddRange(s.Value.OrderBy(static p => p.Sha, StringComparer.Ordinal).Take(take));
            sb.Append(CultureInfo.InvariantCulture, $"{s.Key}: {take} of {s.Value.Count}; ");
            remaining -= take;
            left--;
        }
        picked.Sort(static (a, b) => string.CompareOrdinal(a.Sha, b.Sha));
        description = $"size thirds at {t1} and {t2} bytes; {sb}";
        return picked;
    }

    public static async Task BuildOneAsync(ChromeInstance chrome, CorpusStore corpus, CorpusStore.Record rec, string output, CancellationToken ct)
    {
        string id = rec.Sha;
        string mainKey = Snapshots.UrlKey(rec.Url);
        Dictionary<string, (byte[] Body, string Type, string Url)> served = new(StringComparer.Ordinal);
        object gate = new();
        Router router = (url, type, isMain, method) =>
        {
            string key = Snapshots.UrlKey(url);
            if (method == "GET" && type is "Document" or "Stylesheet")
            {
                if (type == "Document" && isMain && key == mainKey)
                {
                    byte[]? body = corpus.Body(rec);
                    if (body != null)
                    {
                        return new Fulfill(200, rec.ContentType, body);
                    }
                }
                else if (type == "Stylesheet" && corpus.Resolve(url) is { } css && corpus.Body(css) is { } cssBody)
                {
                    lock (gate)
                    {
                        served[key] = (cssBody, string.IsNullOrEmpty(css.ContentType) ? "text/css" : css.ContentType, url);
                    }
                    return new Fulfill(200, string.IsNullOrEmpty(css.ContentType) ? "text/css" : css.ContentType, cssBody);
                }
            }
            if (type == "Stylesheet")
            {
                return new Fulfill(200, "text/css", []);
            }
            if (type == "Document" && !isMain)
            {
                return new Fulfill(200, "text/html", []);
            }
            return new Fail();
        };
        using Tab tab = await Tab.OpenAsync(chrome, router, new ViewportSize { Width = 1280, Height = 800 }, ct).ConfigureAwait(false);
        await tab.NavigateAsync(rec.Url, 60000, 300, ct).ConfigureAwait(false);
        string html = await tab.EvaluateStringAsync(SerializeExpression, ct).ConfigureAwait(false);
        string dir = Snapshots.PageDir(output, id);
        Directory.CreateDirectory(Path.Combine(dir, "res"));
        File.WriteAllBytes(Path.Combine(dir, "index.html"), new UTF8Encoding(false).GetBytes(html));
        SnapshotPage page = new() { Id = id, Url = rec.Url };
        lock (gate)
        {
            foreach (KeyValuePair<string, (byte[] Body, string Type, string Url)> s in served.OrderBy(static s => s.Key, StringComparer.Ordinal))
            {
                string file = "res/" + Snapshots.Sha256Hex(s.Value.Body)[..32] + ".css";
                File.WriteAllBytes(Path.Combine(dir, file), s.Value.Body);
                page.Resources.Add(new SnapshotResource { Url = s.Value.Url, File = file, ContentType = s.Value.Type });
            }
        }
        // page.json goes last: a page directory without it is an unfinished one and is redone.
        Store.Write(Path.Combine(dir, "page.json"), page, PrettyCtx.Default.SnapshotPage);
    }
}
