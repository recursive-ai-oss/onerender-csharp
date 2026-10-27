using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OneRender.WptRunner.Chrome;

/// <summary>
/// Launches a headless Chrome for Testing and screenshots URLs in a pool of tabs at a fixed viewport.
/// Chrome is a black box here: a reference renderer for calibrating the reftest runner.
/// </summary>
public sealed class ChromeRenderer : IDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly string _profileDir;
    private readonly CdpConnection _cdp;
    private readonly ConcurrentQueue<Tab> _idle = new();
    private readonly SemaphoreSlim _slots;
    private readonly int _width;
    private readonly int _height;

    private ChromeRenderer(Process process, string profileDir, CdpConnection cdp, string version, int tabs, int width, int height)
    {
        _process = process;
        _profileDir = profileDir;
        _cdp = cdp;
        Version = version;
        _slots = new SemaphoreSlim(tabs, tabs);
        _width = width;
        _height = height;
    }

    /// <summary>The product string Chrome reports, for example <c>HeadlessChrome/156.0.8078.47</c>.</summary>
    public string Version { get; }

    /// <summary>Starts Chrome and opens <paramref name="tabs"/> tabs.</summary>
    /// <param name="chromePath">Path to the <c>chrome</c> executable.</param>
    /// <param name="profileDir">An empty or new directory for the throwaway profile.</param>
    /// <param name="tabs">How many pages render at once.</param>
    /// <param name="width">Viewport width in CSS pixels.</param>
    /// <param name="height">Viewport height in CSS pixels.</param>
    /// <param name="fontDirectory">When set, Chrome sees only the fonts in this directory (through a private fontconfig file).</param>
    public static async Task<ChromeRenderer> StartAsync(
        string chromePath, string profileDir, int tabs, int width, int height, string? fontDirectory)
    {
        Directory.CreateDirectory(profileDir);
        var psi = new ProcessStartInfo(chromePath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string a in new[]
        {
            "--headless=new", "--no-sandbox", "--disable-gpu", "--remote-debugging-port=0",
            "--user-data-dir=" + profileDir, "--force-device-scale-factor=1", "--disable-dev-shm-usage",
            "--disable-background-networking", "--disable-extensions", "--no-first-run",
            "--no-default-browser-check", "--disable-lcd-text", "--font-render-hinting=none",
            "--disable-features=Translate,MediaRouter", $"--window-size={width},{height}", "about:blank",
        })
        {
            psi.ArgumentList.Add(a);
        }

        if (fontDirectory is not null)
        {
            string conf = Path.Combine(profileDir, "fonts.conf");
            File.WriteAllText(
                conf,
                "<?xml version=\"1.0\"?><!DOCTYPE fontconfig SYSTEM \"fonts.dtd\"><fontconfig><dir>"
                + System.Security.SecurityElement.Escape(Path.GetFullPath(fontDirectory))
                + "</dir><cachedir>" + System.Security.SecurityElement.Escape(Path.Combine(profileDir, "fc-cache"))
                + "</cachedir></fontconfig>");
            psi.Environment["FONTCONFIG_FILE"] = conf;
        }

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Chrome did not start.");
        process.ErrorDataReceived += static (_, _) => { };
        process.OutputDataReceived += static (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        try
        {
            string portFile = Path.Combine(profileDir, "DevToolsActivePort");
            for (int i = 0; i < 300 && !File.Exists(portFile); i++)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException("Chrome exited during startup.");
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            string[] lines = await File.ReadAllLinesAsync(portFile).ConfigureAwait(false);
            var ws = new Uri($"ws://127.0.0.1:{lines[0].Trim()}{lines[1].Trim()}");
            var cdp = await CdpConnection.ConnectAsync(ws, CancellationToken.None).ConfigureAwait(false);
            JsonElement ver = await cdp.SendAsync("Browser.getVersion", null, null, CommandTimeout).ConfigureAwait(false);
            string version = ver.GetProperty("product").GetString() ?? "unknown";
            var r = new ChromeRenderer(process, profileDir, cdp, version, tabs, width, height);
            for (int i = 0; i < tabs; i++)
            {
                r._idle.Enqueue(await r.OpenTabAsync().ConfigureAwait(false));
            }

            return r;
        }
        catch
        {
            KillQuietly(process);
            throw;
        }
    }

    /// <summary>Loads <paramref name="url"/>, waits for load and web fonts, and returns the first screenshot that matches the one before it, and returns the viewport pixels.</summary>
    public Screenshot Render(string url) => Use(tab => tab.CaptureAsync(url));

    /// <summary>
    /// Loads <paramref name="url"/> and prints it to PDF through the browser's own print path, 5 by 3 inches with half-inch margins unless the
    /// document's <c>@page</c> rules say otherwise (the WPT print-reftest defaults), with backgrounds. Returns the PDF bytes.
    /// </summary>
    public byte[] RenderPdf(string url) => Use(tab => tab.PrintAsync(url));

    private T Use<T>(Func<Tab, Task<T>> action)
    {
        _slots.Wait();
        _idle.TryDequeue(out Tab? tab);
        try
        {
            tab ??= OpenTabAsync().GetAwaiter().GetResult();
            T result = action(tab).GetAwaiter().GetResult();
            _idle.Enqueue(tab);
            return result;
        }
        catch
        {
            // A hung or crashed tab is replaced rather than reused.
            if (tab is not null)
            {
                try
                {
                    _cdp.SendAsync("Target.closeTarget", new JsonObject { ["targetId"] = tab.TargetId }, null, TimeSpan.FromSeconds(5))
                        .GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                {
                    // Already gone.
                }
            }

            throw;
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<Tab> OpenTabAsync()
    {
        JsonElement created = await _cdp.SendAsync(
            "Target.createTarget", new JsonObject { ["url"] = "about:blank" }, null, CommandTimeout).ConfigureAwait(false);
        string targetId = created.GetProperty("targetId").GetString()!;
        JsonElement att = await _cdp.SendAsync(
            "Target.attachToTarget", new JsonObject { ["targetId"] = targetId, ["flatten"] = true }, null, CommandTimeout)
            .ConfigureAwait(false);
        string session = att.GetProperty("sessionId").GetString()!;
        var tab = new Tab(_cdp, targetId, session);
        await _cdp.SendAsync("Page.enable", null, session, CommandTimeout).ConfigureAwait(false);
        await _cdp.SendAsync("Emulation.setFocusEmulationEnabled", new JsonObject { ["enabled"] = true }, session, CommandTimeout)
            .ConfigureAwait(false);
        await _cdp.SendAsync(
            "Emulation.setDeviceMetricsOverride",
            new JsonObject { ["width"] = _width, ["height"] = _height, ["deviceScaleFactor"] = 1, ["mobile"] = false },
            session,
            CommandTimeout).ConfigureAwait(false);
        return tab;
    }

    private static void KillQuietly(Process p)
    {
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    /// <summary>Closes the browser and removes the profile directory.</summary>
    public void Dispose()
    {
        try
        {
            _cdp.SendAsync("Browser.close", null, null, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            // Closing drops the connection; that is the expected outcome.
        }

        if (!_process.WaitForExit(5000))
        {
            KillQuietly(_process);
        }

        _cdp.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _process.Dispose();
        _slots.Dispose();
        try
        {
            Directory.Delete(_profileDir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of a scratch directory.
        }
    }

    private sealed class Tab
    {
        private readonly CdpConnection _cdp;
        private readonly string _session;
        private TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Tab(CdpConnection cdp, string targetId, string session)
        {
            _cdp = cdp;
            TargetId = targetId;
            _session = session;
            cdp.OnSessionEvent(session, (method, _) =>
            {
                if (method == "Page.loadEventFired")
                {
                    _loaded.TrySetResult();
                }
            });
        }

        public string TargetId { get; }

        public async Task<byte[]> PrintAsync(string url)
        {
            await LoadAsync(url).ConfigureAwait(false);
            JsonElement pdf = await _cdp.SendAsync(
                "Page.printToPDF",
                new JsonObject
                {
                    ["paperWidth"] = 5,
                    ["paperHeight"] = 3,
                    ["marginTop"] = 0.5,
                    ["marginBottom"] = 0.5,
                    ["marginLeft"] = 0.5,
                    ["marginRight"] = 0.5,
                    ["printBackground"] = true,
                    ["preferCSSPageSize"] = true,
                    ["displayHeaderFooter"] = false,
                },
                _session,
                CommandTimeout).ConfigureAwait(false);
            return Convert.FromBase64String(pdf.GetProperty("data").GetString()!);
        }

        public async Task<Screenshot> CaptureAsync(string url)
        {
            await LoadAsync(url).ConfigureAwait(false);
            return await StableShotAsync().ConfigureAwait(false);
        }

        private async Task LoadAsync(string url)
        {
            _loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            JsonElement nav = await _cdp.SendAsync("Page.navigate", new JsonObject { ["url"] = url }, _session, CommandTimeout)
                .ConfigureAwait(false);
            if (nav.TryGetProperty("errorText", out var err))
            {
                throw new IOException("navigation failed: " + err.GetString());
            }

            await _loaded.Task.WaitAsync(CommandTimeout).ConfigureAwait(false);
            // Web fonts loaded before the screenshot, as WPT's reftest harness waits. No animation-frame wait: tabs that are
            // not in front never get frames, and the screenshot itself forces a fresh one.
            await _cdp.SendAsync(
                "Runtime.evaluate",
                new JsonObject
                {
                    ["expression"] = "document.fonts.ready.then(() => true)",
                    ["awaitPromise"] = true,
                },
                _session,
                CommandTimeout).ConfigureAwait(false);
        }

        private async Task<Screenshot> StableShotAsync()
        {
            // Images (SVG backgrounds especially) can finish decoding after load, so shoot until two frames in a row agree.
            Screenshot previous = await ShootAsync().ConfigureAwait(false);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Screenshot next = await ShootAsync().ConfigureAwait(false);
                if (next.Rgba.AsSpan().SequenceEqual(previous.Rgba))
                {
                    return next;
                }

                previous = next;
                await Task.Delay(50).ConfigureAwait(false);
            }

            return previous;
        }

        private async Task<Screenshot> ShootAsync()
        {
            JsonElement shot = await _cdp.SendAsync(
                "Page.captureScreenshot", new JsonObject { ["format"] = "png" }, _session, CommandTimeout).ConfigureAwait(false);
            return PngReader.Decode(Convert.FromBase64String(shot.GetProperty("data").GetString()!));
        }
    }
}
