using System.Collections.Concurrent;
using System.Text.Json;

namespace OneRender.GeomDiff;

internal abstract record Answer;

internal sealed record Fulfill(int Code, string ContentType, byte[] Body) : Answer;

internal sealed record Fail : Answer;

/// <summary>Decides every request the page makes. Nothing reaches a network.</summary>
internal delegate Answer Router(string url, string resourceType, bool isMainFrame, string method);

/// <summary>One page in its own browser context, with request interception and scripts switched off.</summary>
internal sealed class Tab : IDisposable
{
    private readonly ChromeInstance _chrome;
    private readonly Router _router;
    private string _context = "";
    private string _target = "";
    private string _frame = "";
    private TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Session { get; private set; } = "";

    private Tab(ChromeInstance chrome, Router router)
    {
        _chrome = chrome;
        _router = router;
    }

    public static async Task<Tab> OpenAsync(ChromeInstance chrome, Router router, ViewportSize vp, CancellationToken ct)
    {
        Tab t = new(chrome, router);
        CdpConnection c = chrome.Cdp;
        c.EventReceived += t.OnEvent;
        JsonElement ctx = await c.SendAsync("Target.createBrowserContext", "{\"disposeOnDetach\":true}", ct: ct).ConfigureAwait(false);
        t._context = ctx.GetProperty("browserContextId").GetString()!;
        JsonElement tgt = await c.SendAsync("Target.createTarget", "{\"url\":\"about:blank\",\"browserContextId\":" + J.S(t._context) + "}", ct: ct).ConfigureAwait(false);
        t._target = tgt.GetProperty("targetId").GetString()!;
        JsonElement att = await c.SendAsync("Target.attachToTarget", "{\"targetId\":" + J.S(t._target) + ",\"flatten\":true}", ct: ct).ConfigureAwait(false);
        t.Session = att.GetProperty("sessionId").GetString()!;
        await t.SendAsync("Page.enable", ct: ct).ConfigureAwait(false);
        JsonElement tree = await t.SendAsync("Page.getFrameTree", ct: ct).ConfigureAwait(false);
        t._frame = tree.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString()!;
        await t.SendAsync("Emulation.setScriptExecutionDisabled", "{\"value\":true}", ct: ct).ConfigureAwait(false);
        await t.SendAsync("Emulation.setDeviceMetricsOverride",
            "{\"width\":" + J.N(vp.Width) + ",\"height\":" + J.N(vp.Height) + ",\"deviceScaleFactor\":1,\"mobile\":false}", ct: ct).ConfigureAwait(false);
        if (Environment.GetEnvironmentVariable("GEOMDIFF_PREFERS_DARK") == "1")
        {
            // Pages that opt in with `color-scheme: light dark` resolve against the host's preference; the engine side is told the same.
            await t.SendAsync("Emulation.setEmulatedMedia", "{\"features\":[{\"name\":\"prefers-color-scheme\",\"value\":\"dark\"}]}", ct: ct).ConfigureAwait(false);
        }

        await t.SendAsync("Fetch.enable", "{\"patterns\":[{\"urlPattern\":\"*\"}]}", ct: ct).ConfigureAwait(false);
        return t;
    }

    public Task<JsonElement> SendAsync(string method, string? json = null, int timeoutMs = 30000, CancellationToken ct = default)
        => _chrome.Cdp.SendAsync(method, json, Session, timeoutMs, ct);

    public async Task NavigateAsync(string url, int timeoutMs, int settleMs, CancellationToken ct, bool measure = false)
    {
        _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        JsonElement r = await SendAsync("Page.navigate", "{\"url\":" + J.S(url) + "}", timeoutMs, ct).ConfigureAwait(false);
        if (r.TryGetProperty("errorText", out JsonElement err))
        {
            throw new CdpException("navigation failed: " + err.GetString());
        }
        try
        {
            await _loaded.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new CdpException("page did not finish loading");
        }
        // Layout that follows the load event (late style recalculation) settles here.
        await Task.Delay(settleMs, ct).ConfigureAwait(false);
        await SettleAsync(measure, ct).ConfigureAwait(false);
    }

    // Images that were blocked report their failure after the load event and change the layout when they do; animations and
    // transitions move boxes with time. Neither may make two captures of one page differ, so wait for the images and put every
    // animation at its end state (running forever ones back at their base style) before measuring.
    private const string ReadyExpression =
        "Array.from(document.images).every(function(i){return i.complete||i.loading==='lazy';})&&document.fonts.status!=='loading'";

    private const string FinishAnimationsExpression =
        "(function(){document.getAnimations().forEach(function(a){try{var e=a.effect&&a.effect.getComputedTiming().endTime;"
        + "if(e===Infinity||e===undefined){a.cancel();}else{a.finish();}}catch(x){a.cancel();}});"
        + "void document.documentElement.offsetHeight;return 'ok';})()";

    private async Task SettleAsync(bool measure, CancellationToken ct)
    {
        for (int i = 0; i < 100; i++)
        {
            JsonElement r = await SendAsync("Runtime.evaluate", "{\"expression\":" + J.S(ReadyExpression) + ",\"returnByValue\":true}", ct: ct).ConfigureAwait(false);
            if (r.TryGetProperty("result", out JsonElement v) && v.TryGetProperty("value", out JsonElement b) && b.ValueKind == JsonValueKind.True)
            {
                break;
            }
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        await EvaluateStringAsync(FinishAnimationsExpression, ct).ConfigureAwait(false);
        if (measure)
        {
            // content-visibility:auto skips the layout of content away from the viewport, and when it does so varies from run to
            // run. The reference is measured with every box laid out (the document itself is not changed by a capture).
            // An inspector style sheet, so no element is added to the document.
            await SendAsync("DOM.enable", ct: ct).ConfigureAwait(false);
            await SendAsync("CSS.enable", ct: ct).ConfigureAwait(false);
            JsonElement sheet = await SendAsync("CSS.createStyleSheet", "{\"frameId\":" + J.S(_frame) + "}", ct: ct).ConfigureAwait(false);
            await SendAsync("CSS.setStyleSheetText", "{\"styleSheetId\":" + J.S(sheet.GetProperty("styleSheetId").GetString()!) + ",\"text\":\"*,*::before,*::after{content-visibility:visible !important}\"}", ct: ct).ConfigureAwait(false);
            await EvaluateStringAsync(FinishAnimationsExpression, ct).ConfigureAwait(false);
        }
        await Task.Delay(100, ct).ConfigureAwait(false);
    }

    public async Task<string> EvaluateStringAsync(string expression, CancellationToken ct)
    {
        JsonElement r = await SendAsync("Runtime.evaluate",
            "{\"expression\":" + J.S(expression) + ",\"returnByValue\":true}", ct: ct).ConfigureAwait(false);
        if (r.TryGetProperty("exceptionDetails", out JsonElement ex))
        {
            throw new CdpException("evaluate failed: " + ex.GetRawText());
        }
        return r.GetProperty("result").GetProperty("value").GetString() ?? "";
    }

    private void OnEvent(string method, string? session, JsonElement p)
    {
        if (session != Session)
        {
            return;
        }
        if (method == "Page.loadEventFired")
        {
            _loaded.TrySetResult();
        }
        else if (method == "Fetch.requestPaused")
        {
            HandleRequest(p);
        }
    }

    private void HandleRequest(JsonElement p)
    {
        string id = p.GetProperty("requestId").GetString()!;
        JsonElement req = p.GetProperty("request");
        string url = req.GetProperty("url").GetString()!;
        string type = p.TryGetProperty("resourceType", out JsonElement rt) ? rt.GetString() ?? "Other" : "Other";
        bool main = p.TryGetProperty("frameId", out JsonElement fid) && fid.GetString() == _frame;
        string method = req.TryGetProperty("method", out JsonElement m) ? m.GetString() ?? "GET" : "GET";
        Answer a = _router(url, type, main, method);
        Task send;
        if (a is Fulfill f)
        {
            string headers = "[{\"name\":\"Content-Type\",\"value\":" + J.S(f.ContentType) + "},{\"name\":\"Access-Control-Allow-Origin\",\"value\":\"*\"}]";
            send = SendAsync("Fetch.fulfillRequest",
                "{\"requestId\":" + J.S(id) + ",\"responseCode\":" + J.N(f.Code) + ",\"responseHeaders\":" + headers + ",\"body\":" + J.S(Convert.ToBase64String(f.Body)) + "}");
        }
        else
        {
            send = SendAsync("Fetch.failRequest", "{\"requestId\":" + J.S(id) + ",\"errorReason\":\"BlockedByClient\"}");
        }
        // The page may already be gone when the answer lands; that is not an error.
        _ = send.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _chrome.Cdp.EventReceived -= OnEvent;
        if (_chrome.Dead)
        {
            return;
        }
        try
        {
            using CancellationTokenSource cts = new(5000);
            _chrome.Cdp.SendAsync("Target.closeTarget", "{\"targetId\":" + J.S(_target) + "}", ct: cts.Token).GetAwaiter().GetResult();
            _chrome.Cdp.SendAsync("Target.disposeBrowserContext", "{\"browserContextId\":" + J.S(_context) + "}", ct: cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is CdpException or OperationCanceledException or System.Net.WebSockets.WebSocketException)
        {
            // The chrome is recycled after a failed page anyway.
        }
    }
}

/// <summary>Runs work items over several Chrome processes, replacing one that dies or has served enough pages.</summary>
internal static class Pool
{
    public static async Task<List<string>> RunAsync<T>(IReadOnlyList<T> items, Func<T, string> name, int jobs, ChromeOptions options, int recycleAfter,
        Func<ChromeInstance, T, Task> work, CancellationToken ct, Action<string>? log = null)
    {
        ConcurrentQueue<T> queue = new(items);
        ConcurrentBag<string> failures = [];
        int done = 0;
        Task[] workers = new Task[Math.Max(1, jobs)];
        for (int w = 0; w < workers.Length; w++)
        {
            int index = w;
            workers[w] = Task.Run(async () =>
            {
                ChromeInstance? chrome = null;
                try
                {
                    while (queue.TryDequeue(out T? item))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (chrome == null || chrome.Dead || chrome.PagesServed >= recycleAfter)
                        {
                            chrome?.Dispose();
                            ChromeOptions o = new() { ChromePath = options.ChromePath, FontConfigFile = options.FontConfigFile, WorkDir = options.WorkDir, Name = "chrome" + index, NoSandbox = options.NoSandbox };
                            chrome = await ChromeInstance.StartAsync(o, ct).ConfigureAwait(false);
                        }
                        try
                        {
                            await work(chrome, item).ConfigureAwait(false);
                        }
                        catch (Exception e) when (e is CdpException or System.Net.WebSockets.WebSocketException or InvalidDataException or IOException)
                        {
                            failures.Add(name(item) + ": " + e.Message.Split('\n')[0]);
                            chrome.Dispose();
                            chrome = null;
                        }
                        if (chrome != null)
                        {
                            chrome.PagesServed++;
                        }
                        int n = Interlocked.Increment(ref done);
                        if (n % 25 == 0)
                        {
                            log?.Invoke(n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }
                }
                finally
                {
                    chrome?.Dispose();
                }
            }, CancellationToken.None);
        }
        await Task.WhenAll(workers).ConfigureAwait(false);
        List<string> list = [.. failures];
        list.Sort(StringComparer.Ordinal);
        return list;
    }
}
