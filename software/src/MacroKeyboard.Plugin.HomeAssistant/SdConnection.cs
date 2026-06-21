using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MacroKeyboard.Plugin.HomeAssistant;

/// <summary>
/// Manages the WebSocket connection to the MacroKeyboard backend using
/// the Stream Deck plugin protocol.
/// </summary>
public sealed class SdConnection
{
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly int _port;
    private readonly string _pluginUuid;

    public event Action<JsonElement>? WillAppear;
    public event Action<JsonElement>? WillDisappear;
    public event Action<JsonElement>? KeyDown;
    public event Action<JsonElement>? DidReceiveSettings;
    public event Action<JsonElement>? DidReceiveGlobalSettings;
    public event Action<JsonElement>? SendToPlugin;
    public event Action<JsonElement>? PropertyInspectorDidAppear;
    public event Action<JsonElement>? PropertyInspectorDidDisappear;

    public SdConnection(int port, string pluginUuid)
    {
        _port = port;
        _pluginUuid = pluginUuid;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _ws.ConnectAsync(new Uri($"ws://localhost:{_port}"), ct);
        await SendRawAsync(JsonSerializer.Serialize(new
        {
            @event = "registerPlugin",
            uuid   = _pluginUuid
        }), ct);
    }

    /// <summary>Runs the receive loop until the socket closes or <paramref name="ct"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        var buffer    = new byte[65536];
        var msgBuffer = new MemoryStream();
        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            msgBuffer.SetLength(0);
            // Accumulate all frames of one logical message (same pattern as WebSocketServer)
            while (true)
            {
                WebSocketReceiveResult result;
                try { result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct); }
                catch (OperationCanceledException) { return; }

                if (result.MessageType == WebSocketMessageType.Close) return;
                if (result.MessageType == WebSocketMessageType.Text)
                    msgBuffer.Write(buffer, 0, result.Count);

                if (result.EndOfMessage) break;
            }

            if (msgBuffer.Length == 0) continue;
            var json = Encoding.UTF8.GetString(msgBuffer.GetBuffer(), 0, (int)msgBuffer.Length);
            DispatchMessage(json);
        }
    }

    private void DispatchMessage(string json)
    {
        try
        {
            var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var evtProp)) return;

            switch (evtProp.GetString())
            {
                case "willAppear":                    WillAppear?.Invoke(root);                    break;
                case "willDisappear":                 WillDisappear?.Invoke(root);                 break;
                case "keyDown":                       KeyDown?.Invoke(root);                       break;
                case "didReceiveSettings":            DidReceiveSettings?.Invoke(root);            break;
                case "didReceiveGlobalSettings":      DidReceiveGlobalSettings?.Invoke(root);      break;
                case "sendToPlugin":                  SendToPlugin?.Invoke(root);                  break;
                case "propertyInspectorDidAppear":    PropertyInspectorDidAppear?.Invoke(root);    break;
                case "propertyInspectorDidDisappear": PropertyInspectorDidDisappear?.Invoke(root); break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SD] Dispatch error: {ex.Message}");
        }
    }

    // ── Outbound SD API ───────────────────────────────────────────────────────

    public Task SetTitleAsync(string context, string title, CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            @event  = "setTitle",
            context,
            payload = new { title, target = 0 }
        }), ct);

    public Task SetImageAsync(string context, string dataUrl, CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            @event  = "setImage",
            context,
            payload = new { image = dataUrl, target = 0 }
        }), ct);

    public Task SetButtonDisplayAsync(string context, string text, bool isOn, CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            @event  = "mkSetButtonDisplay",
            context,
            payload = new { text, isOn }
        }), ct);

    public Task SetStateAsync(string context, int state, CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            @event  = "setState",
            context,
            payload = new { state }
        }), ct);

    public Task ShowAlertAsync(string context, CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            @event  = "showAlert",
            context
        }), ct);

    public Task ShowOkAsync(string context, CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            @event  = "showOk",
            context
        }), ct);

    public Task GetGlobalSettingsAsync(CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            @event  = "getGlobalSettings",
            context = _pluginUuid
        }), ct);

    public Task SetGlobalSettingsAsync(object payload, CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            @event  = "setGlobalSettings",
            context = _pluginUuid,
            payload
        }), ct);

    public Task SetSettingsAsync(string context, object payload, CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            @event = "setSettings",
            context,
            payload
        }), ct);

    public Task SendToPropertyInspectorAsync(string context, object payload, CancellationToken ct = default)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            @event  = "sendToPropertyInspector",
            context,
            payload
        }), ct);

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
}
