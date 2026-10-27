using System.Net;
using System.Net.Sockets;

namespace OneRender.WptRunner.Chrome;

/// <summary>Serves a directory over loopback HTTP so root-relative URLs in test files resolve as they do under the suite's own server.</summary>
public sealed class StaticFileServer : IDisposable
{
    private readonly string _root;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    /// <summary>Starts serving <paramref name="root"/> on a free loopback port.</summary>
    public StaticFileServer(string root)
    {
        _root = Path.GetFullPath(root);
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        BaseUri = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUri + "/");
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The origin, for example <c>http://127.0.0.1:5000</c>, with no trailing slash.</summary>
    public string BaseUri { get; }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }

            _ = Task.Run(() => Handle(ctx));
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            string rel = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath).TrimStart('/');
            string full = Path.GetFullPath(Path.Combine(_root, rel));
            if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full))
            {
                ctx.Response.StatusCode = 404;
            }
            else
            {
                byte[] body = File.ReadAllBytes(full);
                ctx.Response.ContentType = MimeType(full);
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or UnauthorizedAccessException)
        {
            // The browser abandoned the request or the file vanished; nothing to report.
        }
        finally
        {
            try
            {
                ctx.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                // Already closed by the peer.
            }
        }
    }

    private static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".xht" or ".xhtml" => "application/xhtml+xml",
        ".xml" => "application/xml",
        ".svg" => "image/svg+xml",
        ".css" => "text/css",
        ".js" or ".mjs" => "text/javascript",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };

    /// <inheritdoc />
    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The accept loop ends with the listener.
        }

        _stop.Dispose();
    }
}
