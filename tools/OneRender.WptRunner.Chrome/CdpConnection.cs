using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OneRender.WptRunner.Chrome;

/// <summary>A minimal Chrome DevTools Protocol client over a WebSocket, with flat target sessions.</summary>
public sealed class CdpConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, Action<string, JsonElement>> _sessionEvents = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task _reader = Task.CompletedTask;
    private int _nextId;

    /// <summary>Connects to a browser-level DevTools WebSocket URL.</summary>
    public static async Task<CdpConnection> ConnectAsync(Uri url, CancellationToken ct)
    {
        var c = new CdpConnection();
        await c._socket.ConnectAsync(url, ct).ConfigureAwait(false);
        c._reader = Task.Run(c.ReadLoopAsync, CancellationToken.None);
        return c;
    }

    /// <summary>Registers the handler that receives events for <paramref name="sessionId"/> (method name, params).</summary>
    public void OnSessionEvent(string sessionId, Action<string, JsonElement> handler) => _sessionEvents[sessionId] = handler;

    /// <summary>Sends a command and returns its <c>result</c> object; throws if the browser reports an error.</summary>
    public async Task<JsonElement> SendAsync(string method, JsonObject? parameters, string? sessionId, TimeSpan timeout)
    {
        int id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var msg = new JsonObject { ["id"] = id, ["method"] = method };
        if (parameters is not null)
        {
            msg["params"] = parameters;
        }

        if (sessionId is not null)
        {
            msg["sessionId"] = sessionId;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(msg.ToJsonString());
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await _sendLock.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeoutCts.Token).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            throw new TimeoutException($"DevTools command {method} timed out after {timeout.TotalSeconds:0}s.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        byte[] buffer = new byte[1 << 16];
        using var ms = new MemoryStream();
        try
        {
            while (!_cts.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await _socket.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    ms.Write(buffer, 0, r.Count);
                }
                while (!r.EndOfMessage);

                using var doc = JsonDocument.Parse(ms.ToArray());
                Dispatch(doc.RootElement);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // Connection closed: fail whatever is still waiting.
        }
        finally
        {
            foreach (var p in _pending.Values)
            {
                p.TrySetException(new IOException("DevTools connection closed."));
            }
        }
    }

    private void Dispatch(JsonElement root)
    {
        if (root.TryGetProperty("id", out var idEl))
        {
            if (_pending.TryGetValue(idEl.GetInt32(), out var tcs))
            {
                if (root.TryGetProperty("error", out var err))
                {
                    tcs.TrySetException(new IOException("DevTools error: " + err.GetRawText()));
                }
                else
                {
                    tcs.TrySetResult(root.TryGetProperty("result", out var res) ? res.Clone() : default);
                }
            }
        }
        else if (root.TryGetProperty("method", out var m)
                 && root.TryGetProperty("sessionId", out var s)
                 && _sessionEvents.TryGetValue(s.GetString() ?? string.Empty, out var handler))
        {
            handler(m.GetString() ?? string.Empty, root.TryGetProperty("params", out var p) ? p.Clone() : default);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            _socket.Abort();
            await _reader.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // Shutting down anyway.
        }

        _socket.Dispose();
        _cts.Dispose();
        _sendLock.Dispose();
    }
}
