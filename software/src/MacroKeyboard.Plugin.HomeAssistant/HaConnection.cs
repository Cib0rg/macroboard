using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MacroKeyboard.Plugin.HomeAssistant;

public record HaState(string EntityId, string State, string FriendlyName);

/// <summary>
/// Connects to the Home Assistant WebSocket API, authenticates,
/// subscribes to state_changed events, and provides service call support.
/// </summary>
public sealed class HaConnection : IAsyncDisposable
{
    private ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _nextId = 1;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private CancellationTokenSource _cts = new();
    private Task? _readLoop;

    public bool IsConnected => _ws.State == WebSocketState.Open;

    public event Action<HaState>? StateChanged;
    public event Action? Connected;
    public event Action? Disconnected;

    public async Task ConnectAsync(string haUrl, string token, CancellationToken ct = default)
    {
        // Normalise URL → ws:// scheme with /api/websocket path
        var wsUrl = haUrl
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://",  "ws://",  StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        if (!wsUrl.EndsWith("/api/websocket", StringComparison.OrdinalIgnoreCase))
            wsUrl += "/api/websocket";

        _ws  = new ClientWebSocket();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await _ws.ConnectAsync(new Uri(wsUrl), ct);

        // ── Auth flow ──────────────────────────────────────────────────────────
        var msg = await ReceiveOneAsync(ct);
        var msgType = msg.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (msgType != "auth_required")
            throw new InvalidOperationException($"Expected auth_required, got: {msgType}");

        await SendRawAsync(JsonSerializer.Serialize(new { type = "auth", access_token = token }), ct);

        var authResult = await ReceiveOneAsync(ct);
        var authType = authResult.TryGetProperty("type", out var at) ? at.GetString() : null;
        if (authType == "auth_invalid")
        {
            var msg2 = authResult.TryGetProperty("message", out var m) ? m.GetString() : "(no message)";
            throw new UnauthorizedAccessException($"HA auth failed: {msg2}");
        }
        if (authType != "auth_ok")
            throw new InvalidOperationException($"Unexpected auth response: {authType}");

        // Subscribe to state_changed events
        var subId = NextId();
        await SendRawAsync(JsonSerializer.Serialize(new
        {
            id         = subId,
            type       = "subscribe_events",
            event_type = "state_changed"
        }), ct);

        // The subscribe response will arrive in the read loop — just start the loop.
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);

        Connected?.Invoke();
        Console.WriteLine($"[HA] Connected to {haUrl}");
    }

    /// <summary>Fetches all current entity states.</summary>
    public async Task<Dictionary<string, HaState>> GetAllStatesAsync(CancellationToken ct = default)
    {
        var id  = NextId();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[id] = tcs;

        await SendRawAsync(JsonSerializer.Serialize(new { id, type = "get_states" }), ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var reg = timeout.Token.Register(() => tcs.TrySetCanceled(timeout.Token));

        var response = await tcs.Task;
        var result   = new Dictionary<string, HaState>(StringComparer.OrdinalIgnoreCase);

        if (response.TryGetProperty("result", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var entityId     = item.TryGetProperty("entity_id",  out var eid)  ? eid.GetString()  ?? "" : "";
                var stateVal     = item.TryGetProperty("state",       out var sv)   ? sv.GetString()   ?? "" : "";
                var friendlyName = "";
                if (item.TryGetProperty("attributes", out var attrs)
                    && attrs.TryGetProperty("friendly_name", out var fn))
                    friendlyName = fn.GetString() ?? "";

                if (!string.IsNullOrEmpty(entityId))
                    result[entityId] = new HaState(entityId, stateVal, friendlyName);
            }
        }

        return result;
    }

    /// <summary>Calls a HA service against the given entity.</summary>
    public Task CallServiceAsync(
        string domain, string service, string entityId,
        CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            id      = NextId(),
            type    = "call_service",
            domain,
            service,
            target  = new { entity_id = entityId }
        }), ct);

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        Console.WriteLine("[HA] Read loop started");
        var buffer    = new byte[65536];
        var msgBuffer = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                msgBuffer.SetLength(0);
                if (!await AccumulateMessageAsync(buffer, msgBuffer, ct)) break;
                if (msgBuffer.Length > 0)
                    HandleMessage(Encoding.UTF8.GetString(msgBuffer.GetBuffer(), 0, (int)msgBuffer.Length));
            }
        }
        finally
        {
            Disconnected?.Invoke();
            Console.WriteLine("[HA] Disconnected");
        }
    }

    // Reads one complete (possibly multi-frame) WebSocket text message into ms.
    // Returns false if the socket closed or errored.
    private async Task<bool> AccumulateMessageAsync(byte[] buffer, MemoryStream ms, CancellationToken ct)
    {
        WebSocketReceiveResult result;
        do
        {
            try   { result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct); }
            catch { return false; }

            if (result.MessageType == WebSocketMessageType.Close) return false;
            if (result.MessageType == WebSocketMessageType.Text)
                ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return true;
    }

    private void HandleMessage(string json)
    {
        try
        {
            var root    = JsonDocument.Parse(json).RootElement;
            var type    = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == "result")
            {
                var id = root.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : -1;
                TaskCompletionSource<JsonElement>? tcs;
                lock (_pending) _pending.TryGetValue(id, out tcs);
                if (tcs != null)
                {
                    lock (_pending) _pending.Remove(id);
                    tcs.TrySetResult(root);
                }
                return;
            }

            if (type != "event") return;
            if (!root.TryGetProperty("event", out var evt)) return;

            var eventType = evt.TryGetProperty("event_type", out var et) ? et.GetString() : null;
            if (eventType != "state_changed") return;
            if (!evt.TryGetProperty("data", out var data)) return;

            var entityId = data.TryGetProperty("entity_id", out var eid) ? eid.GetString() ?? "" : "";
            if (!data.TryGetProperty("new_state", out var ns) || ns.ValueKind == JsonValueKind.Null) return;

            var stateVal     = ns.TryGetProperty("state", out var sv) ? sv.GetString() ?? "" : "";
            var friendlyName = "";
            if (ns.TryGetProperty("attributes", out var attrs)
                && attrs.TryGetProperty("friendly_name", out var fn))
                friendlyName = fn.GetString() ?? "";

            StateChanged?.Invoke(new HaState(entityId, stateVal, friendlyName));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HA] Message parse error: {ex.Message}");
        }
    }

    private async Task<JsonElement> ReceiveOneAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];
        using var ms = new MemoryStream();
        if (!await AccumulateMessageAsync(buffer, ms, ct))
            throw new WebSocketException("Connection closed unexpectedly during auth");
        ms.Position = 0;
        return JsonDocument.Parse(ms).RootElement;
    }

    private int NextId() => Interlocked.Increment(ref _nextId);

    private async Task SendRawAsync(string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_readLoop != null)
            try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        if (_ws.State == WebSocketState.Open)
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None); } catch { }
        _ws.Dispose();
        _sendLock.Dispose();
        _cts.Dispose();
    }
}
