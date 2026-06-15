using MacroKeyboard.Shared.Plugin;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace MacroKeyboard.Backend.Plugin;

/// <summary>
/// WebSocket server for Stream Deck API compatibility.
/// Plugins connect here to receive events and send commands.
/// </summary>
public class WebSocketServer : IDisposable
{
    private readonly ILogger<WebSocketServer> _logger;
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
    private bool _isRunning;

    /// <summary>
    /// Raised when a plugin sends a message.
    /// ConnectionId identifies the sender so replies can be targeted.
    /// </summary>
    public event EventHandler<PluginMessageEventArgs>? MessageReceived;

    /// <summary>
    /// Raised after a connection is removed (disconnected or errored).
    /// The string argument is the connectionId that was removed.
    /// </summary>
    public event EventHandler<string>? ConnectionClosed;

    public WebSocketServer(ILogger<WebSocketServer> logger, int port = 28196)
    {
        _logger = logger;
        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("WebSocket server is already running");
            return;
        }

        _logger.LogInformation("Starting WebSocket server on port {Port}...", _port);

        _listener = new HttpListener();
        // Add both localhost and 127.0.0.1 — on Windows, localhost may resolve to ::1 (IPv6)
        // but plugins and WebView typically connect to 127.0.0.1 (IPv4).
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();

        _isRunning = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(() => AcceptConnectionsAsync(_cts.Token), _cts.Token);

        _logger.LogInformation("WebSocket server started on port {Port}", _port);
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

        _logger.LogInformation("Stopping WebSocket server...");

        _isRunning = false;
        _cts?.Cancel();
        _listener?.Stop();

        foreach (var ws in _connections.Values)
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", cancellationToken);
            ws.Dispose();
        }
        _connections.Clear();

        _logger.LogInformation("WebSocket server stopped");
    }

    /// <summary>Send a message to all connected plugins.</summary>
    public async Task BroadcastAsync(PluginMessage message, CancellationToken cancellationToken = default)
    {
        var buffer = Serialize(message);
        var dead = new List<string>();

        foreach (var kvp in _connections)
        {
            if (!await TrySendAsync(kvp.Key, kvp.Value, buffer, cancellationToken))
                dead.Add(kvp.Key);
        }

        foreach (var id in dead)
            RemoveConnection(id);
    }

    /// <summary>Send a message to a specific connection (targeted reply).</summary>
    public async Task SendToConnectionAsync(string connectionId, PluginMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(connectionId, out var ws)) return;
        var buffer = Serialize(message);
        if (!await TrySendAsync(connectionId, ws, buffer, cancellationToken))
            RemoveConnection(connectionId);
    }

    private static byte[] Serialize(PluginMessage message)
        => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        }));

    private async Task<bool> TrySendAsync(string connectionId, WebSocket ws, byte[] buffer,
        CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return false;
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to connection {ConnectionId}", connectionId);
            return false;
        }
    }

    private void RemoveConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var ws))
            ws.Dispose();
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Accepting WebSocket connections on port {Port}...", _port);

        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                var context = await _listener!.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    var origin = context.Request.Headers["Origin"] ?? "(none)";
                    var ua     = context.Request.Headers["User-Agent"] ?? "(none)";
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    var connectionId = Guid.NewGuid().ToString("N");
                    _connections[connectionId] = wsContext.WebSocket;
                    _logger.LogInformation("WS connected: id={ConnectionId} origin={Origin} ua={UA}",
                        connectionId, origin, ua);
                    _ = Task.Run(() => HandleConnectionAsync(connectionId, wsContext.WebSocket, cancellationToken), cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Non-WS request to WS port from {Remote}: {Method} {Url}",
                        context.Request.RemoteEndPoint, context.Request.HttpMethod, context.Request.Url);
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (_isRunning)
            {
                _logger.LogError(ex, "Error accepting WebSocket connection");
            }
        }

        _logger.LogInformation("Stopped accepting connections");
    }

    private async Task HandleConnectionAsync(string connectionId, WebSocket webSocket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[65536];

        try
        {
            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try
                    {
                        var msg = JsonConvert.DeserializeObject<PluginMessage>(json);
                        if (msg != null)
                        {
                            _logger.LogInformation("WS← [{ConnectionId}] event={Event} ctx={Context}",
                                connectionId[..8], msg.Event, msg.EffectiveContext ?? "-");
                            MessageReceived?.Invoke(this, new PluginMessageEventArgs(connectionId, msg));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing message from {ConnectionId}", connectionId);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling connection {ConnectionId}", connectionId);
        }
        finally
        {
            RemoveConnection(connectionId);
            ConnectionClosed?.Invoke(this, connectionId);
            _logger.LogInformation("Plugin disconnected: {ConnectionId}", connectionId);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _listener?.Stop();
        foreach (var ws in _connections.Values)
            ws.Dispose();
        _connections.Clear();
    }
}

/// <summary>Event args carrying both the sender connection ID and the parsed message.</summary>
public class PluginMessageEventArgs : EventArgs
{
    public string ConnectionId { get; }
    public PluginMessage Message { get; }

    public PluginMessageEventArgs(string connectionId, PluginMessage message)
    {
        ConnectionId = connectionId;
        Message = message;
    }
}

/// <summary>Message format for plugin communication (Stream Deck API compatible).</summary>
public class PluginMessage
{
    [JsonProperty("event")]
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// Connection identifier. Used as context for most events.
    /// For registerPlugin / registerPropertyInspector the SD SDK sends uuid instead — see Uuid.
    /// </summary>
    [JsonProperty("context", NullValueHandling = NullValueHandling.Ignore)]
    public string? Context { get; set; }

    /// <summary>
    /// SD SDK sends registerPlugin and registerPropertyInspector with a "uuid" field, not "context".
    /// Use EffectiveContext to get whichever is set.
    /// </summary>
    [JsonProperty("uuid", NullValueHandling = NullValueHandling.Ignore)]
    public string? Uuid { get; set; }

    [JsonIgnore]
    public string? EffectiveContext => Context ?? Uuid;

    [JsonProperty("action", NullValueHandling = NullValueHandling.Ignore)]
    public string? Action { get; set; }

    [JsonProperty("device", NullValueHandling = NullValueHandling.Ignore)]
    public string? Device { get; set; }

    [JsonProperty("payload", NullValueHandling = NullValueHandling.Ignore)]
    public object? Payload { get; set; }
}
