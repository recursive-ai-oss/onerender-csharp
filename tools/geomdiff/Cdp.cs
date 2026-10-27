using System.Collections.Concurrent;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OneRender.GeomDiff;

internal sealed class CdpException(string message) : Exception(message)
{
}

/// <summary>A minimal DevTools Protocol client over one WebSocket (flattened sessions, string-built parameters).</summary>
internal sealed class CdpConnection : IDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private int _nextId;
    private Task? _reader;

    /// <summary>Raised on the read loop for every protocol event: method, session id (or null), params.</summary>
    public event Action<string, string?, JsonElement>? EventReceived;

    /// <summary>Raised once when the connection ends.</summary>
    public event Action? Closed;

    public async Task ConnectAsync(Uri uri, CancellationToken ct)
    {
        await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);
        _reader = Task.Run(ReadLoopAsync, CancellationToken.None);
    }

    public async Task<JsonElement> SendAsync(string method, string? paramsJson = null, string? session = null, int timeoutMs = 30000, CancellationToken ct = default)
    {
        int id = Interlocked.Increment(ref _nextId);
        TaskCompletionSource<JsonElement> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        ArrayBufferWriter<byte> buf = new();
        using (Utf8JsonWriter w = new(buf))
        {
            w.WriteStartObject();
            w.WriteNumber("id", id);
            w.WriteString("method", method);
            if (paramsJson != null)
            {
                w.WritePropertyName("params");
                w.WriteRawValue(paramsJson);
            }
            if (session != null)
            {
                w.WriteString("sessionId", session);
            }
            w.WriteEndObject();
        }
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _ws.SendAsync(buf.WrittenMemory, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);
        try
        {
            return await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _pending.TryRemove(id, out _);
            throw new CdpException("timeout waiting for " + method);
        }
    }

    private async Task ReadLoopAsync()
    {
        byte[] chunk = new byte[64 * 1024];
        try
        {
            while (_ws.State == WebSocketState.Open)
            {
                using MemoryStream ms = new();
                ValueWebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(chunk.AsMemory(), _cts.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    ms.Write(chunk, 0, r.Count);
                }
                while (!r.EndOfMessage);
                Dispatch(ms.ToArray());
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The browser went away; pending calls are failed below.
        }
        finally
        {
            foreach (KeyValuePair<int, TaskCompletionSource<JsonElement>> p in _pending)
            {
                p.Value.TrySetException(new CdpException("connection closed"));
            }
            Closed?.Invoke();
        }
    }

    private void Dispatch(byte[] message)
    {
        using JsonDocument doc = JsonDocument.Parse(message);
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("id", out JsonElement idEl))
        {
            if (_pending.TryRemove(idEl.GetInt32(), out TaskCompletionSource<JsonElement>? tcs))
            {
                if (root.TryGetProperty("error", out JsonElement err))
                {
                    tcs.TrySetException(new CdpException(err.GetRawText()));
                }
                else
                {
                    tcs.TrySetResult(root.GetProperty("result").Clone());
                }
            }
            return;
        }
        if (root.TryGetProperty("method", out JsonElement m))
        {
            string? session = root.TryGetProperty("sessionId", out JsonElement s) ? s.GetString() : null;
            JsonElement p = root.TryGetProperty("params", out JsonElement pe) ? pe : default;
            EventReceived?.Invoke(m.GetString() ?? "", session, p);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _ws.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        _cts.Dispose();
    }
}

internal sealed class ChromeOptions
{
    public string ChromePath { get; set; } = "";
    public string FontConfigFile { get; set; } = "";
    public string WorkDir { get; set; } = "";
    public string Name { get; set; } = "chrome";
    /// <summary>Null = try with the sandbox, fall back to none; true/false force it.</summary>
    public bool? NoSandbox { get; set; }
}

/// <summary>A headless Chrome child process with a DevTools connection. Only the process this object started is ever killed.</summary>
internal sealed class ChromeInstance : IDisposable
{
    public static readonly string[] Flags =
    [
        "--headless=new", "--remote-debugging-port=0", "--no-first-run", "--no-default-browser-check", "--disable-gpu",
        "--disable-background-networking", "--disable-component-update", "--disable-sync", "--disable-extensions",
        "--disable-default-apps", "--disable-breakpad", "--disable-client-side-phishing-detection",
        "--metrics-recording-only", "--mute-audio", "--force-color-profile=srgb", "--force-device-scale-factor=1",
        "--lang=en-US", "--hide-scrollbars",
        // Linear (unhinted) glyph advances with subpixel positioning: the metrics an outline-based text engine produces.
        "--font-render-hinting=none", "--disable-lcd-text",
        "--disable-features=IsolateOrigins,site-per-process,Translate,OptimizationHints,MediaRouter,HttpsUpgrades,HttpsFirstBalancedModeAutoEnable",
        "--disable-site-isolation-trials",
        // Whatever escapes request interception goes to a dead proxy and an empty resolver.
        "--proxy-server=http://127.0.0.1:9", "--host-resolver-rules=MAP * ~NOTFOUND",
    ];

    private readonly Process _process;
    public CdpConnection Cdp { get; }
    public bool Sandboxed { get; }
    public string Version { get; private set; } = "";
    public string UserAgent { get; private set; } = "";
    public int PagesServed { get; set; }
    public bool Dead { get; private set; }
    public int RootPid => _process.Id;

    private ChromeInstance(Process process, CdpConnection cdp, bool sandboxed)
    {
        _process = process;
        Cdp = cdp;
        Sandboxed = sandboxed;
        cdp.Closed += () => Dead = true;
    }

    public static async Task<ChromeInstance> StartAsync(ChromeOptions o, CancellationToken ct)
    {
        if (o.NoSandbox != true)
        {
            try
            {
                return await StartOnceAsync(o, false, ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException) when (o.NoSandbox == null)
            {
                // Some hosts forbid the user namespaces the sandbox needs. Only frozen, offline content is ever loaded.
            }
        }
        return await StartOnceAsync(o, true, ct).ConfigureAwait(false);
    }

    private static async Task<ChromeInstance> StartOnceAsync(ChromeOptions o, bool noSandbox, CancellationToken ct)
    {
        string profile = Path.Combine(o.WorkDir, o.Name + "-profile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(profile);
        ProcessStartInfo psi = new(o.ChromePath) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string f in Flags)
        {
            psi.ArgumentList.Add(f);
        }
        psi.ArgumentList.Add("--user-data-dir=" + profile);
        if (noSandbox)
        {
            psi.ArgumentList.Add("--no-sandbox");
        }
        psi.ArgumentList.Add("about:blank");
        psi.Environment["FONTCONFIG_FILE"] = o.FontConfigFile;
        psi.Environment["HOME"] = profile;
        psi.Environment["XDG_CACHE_HOME"] = Path.Combine(profile, "cache");
        psi.Environment["XDG_CONFIG_HOME"] = Path.Combine(profile, "config");
        psi.Environment.Remove("FONTCONFIG_PATH");
        Process p = Process.Start(psi) ?? throw new InvalidOperationException("Chrome did not start");
        StringBuilder err = new();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null && err.Length < 4000) { err.AppendLine(e.Data); } };
        p.OutputDataReceived += (_, _) => { };
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        try
        {
            string portFile = Path.Combine(profile, "DevToolsActivePort");
            for (int i = 0; i < 600 && !File.Exists(portFile); i++)
            {
                if (p.HasExited)
                {
                    throw new InvalidOperationException("Chrome exited at start: " + err);
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            if (!File.Exists(portFile))
            {
                throw new InvalidOperationException("Chrome did not open a DevTools port");
            }
            string[] lines = File.ReadAllLines(portFile);
            CdpConnection cdp = new();
            await cdp.ConnectAsync(new Uri("ws://127.0.0.1:" + lines[0].Trim() + lines[1].Trim()), ct).ConfigureAwait(false);
            ChromeInstance inst = new(p, cdp, !noSandbox);
            JsonElement v = await cdp.SendAsync("Browser.getVersion", ct: ct).ConfigureAwait(false);
            inst.Version = v.GetProperty("product").GetString() ?? "";
            inst.UserAgent = v.GetProperty("userAgent").GetString() ?? "";
            return inst;
        }
        catch
        {
            Kill(p);
            throw;
        }
    }

    private static void Kill(Process p)
    {
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        p.Dispose();
    }

    public void Dispose()
    {
        Cdp.Dispose();
        Kill(_process);
    }
}

internal static class J
{
    /// <summary>A JSON string literal.</summary>
    public static string S(string value) => JsonEncodedText.Encode(value).ToString().Insert(0, "\"") + "\"";

    public static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
