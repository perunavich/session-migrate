using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;

namespace SessionMigrate.Core.Cdp;

// Minimal Chrome DevTools Protocol client over a raw WebSocket. Discovers the browser WebSocket from
// /json/version, sends JSON-RPC commands, and dispatches responses by id (CDP events, which carry no
// id, are ignored). Pairs with launching Chrome using --remote-allow-origins=*.
public sealed class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private Task _receiveLoop = Task.CompletedTask;
    private int _nextId;

    private CdpClient(ClientWebSocket socket) => _socket = socket;

    public static async Task<CdpClient> ConnectAsync(int port, CancellationToken cancellationToken = default)
    {
        string webSocketUrl = await GetWebSocketUrlAsync(port, cancellationToken);
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "http://localhost");
        await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken);

        var client = new CdpClient(socket);
        client._receiveLoop = client.ReceiveLoopAsync();
        return client;
    }

    // Returns the command's result object; throws on a CDP error.
    public async Task<JsonElement> SendAsync(
        string method, object? parameters = null, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        int id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var message = new Dictionary<string, object?> { ["id"] = id, ["method"] = method };
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        if (sessionId is not null)
        {
            message["sessionId"] = sessionId;
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        catch
        {
            // The reply can never arrive — don't leak the pending entry.
            _pending.TryRemove(id, out _);
            throw;
        }

        using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
        {
            return await tcs.Task;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
        }

        _socket.Dispose();
        _cts.Dispose();
    }

    private static async Task<string> GetWebSocketUrlAsync(int port, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (int attempt = 0; attempt < 80; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/version", cancellationToken);
                string? url = JsonDocument.Parse(json).RootElement
                    .GetProperty("webSocketDebuggerUrl").GetString();
                if (!string.IsNullOrEmpty(url))
                {
                    return url;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or KeyNotFoundException)
            {
                // DevTools not up yet — retry.
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException($"DevTools did not come up on port {port}");
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[65536];
        while (!_cts.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await _socket.ReceiveAsync(buffer, _cts.Token);
                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            try
            {
                Dispatch(message.ToArray());
            }
            catch (JsonException)
            {
                // A malformed frame must not kill the loop and hang every pending command.
            }
        }

        foreach (TaskCompletionSource<JsonElement> pending in _pending.Values)
        {
            pending.TrySetCanceled();
        }
    }

    private void Dispatch(byte[] frame)
    {
        using JsonDocument doc = JsonDocument.Parse(frame);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("id", out JsonElement idElement) ||
            idElement.ValueKind != JsonValueKind.Number ||
            !idElement.TryGetInt32(out int id) ||
            !_pending.TryRemove(id, out TaskCompletionSource<JsonElement>? tcs))
        {
            return; // an event (no id), a non-Int32 id, or an unknown id — ignore.
        }

        if (root.TryGetProperty("error", out JsonElement error))
        {
            tcs.TrySetException(new InvalidOperationException($"CDP error: {error}"));
        }
        else if (root.TryGetProperty("result", out JsonElement resultElement))
        {
            tcs.TrySetResult(resultElement.Clone());
        }
        else
        {
            tcs.TrySetResult(default);
        }
    }
}
