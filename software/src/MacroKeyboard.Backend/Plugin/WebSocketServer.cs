using MacroKeyboard.Backend;
using MacroKeyboard.Shared.Plugin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    // WebSocket.SendAsync does not support concurrent calls on the same instance.
    // PluginConnection wraps the socket with a SemaphoreSlim that serialises all sends.
    private sealed class PluginConnection(WebSocket ws) : IDisposable
    {
        public readonly WebSocket   WebSocket = ws;
        public readonly SemaphoreSlim SendLock = new(1, 1);
        public void Dispose() { SendLock.Dispose(); WebSocket.Dispose(); }
    }

    private readonly ConcurrentDictionary<string, PluginConnection> _connections = new();
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

    /// <summary>Exposed so plugin launchers can read the configured port without DI.</summary>
    public static int Port { get; private set; } = 28196;

    public WebSocketServer(ILogger<WebSocketServer> logger, IOptions<BackendOptions> options)
    {
        _logger = logger;
        _port   = options.Value.WebSocketPort;
        Port    = _port;
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

        foreach (var conn in _connections.Values)
        {
            if (conn.WebSocket.State == WebSocketState.Open)
                await conn.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", cancellationToken);
            conn.Dispose();
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
        if (!_connections.TryGetValue(connectionId, out var conn)) return;
        var buffer = Serialize(message);
        if (!await TrySendAsync(connectionId, conn, buffer, cancellationToken))
            RemoveConnection(connectionId);
    }

    private static byte[] Serialize(PluginMessage message)
        => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        }));

    private async Task<bool> TrySendAsync(string connectionId, PluginConnection conn, byte[] buffer,
        CancellationToken ct)
    {
        if (conn.WebSocket.State != WebSocketState.Open) return false;
        await conn.SendLock.WaitAsync(ct);
        try
        {
            await conn.WebSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to connection {ConnectionId}", connectionId);
            return false;
        }
        finally
        {
            conn.SendLock.Release();
        }
    }

    private void RemoveConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var conn))
            conn.Dispose();
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
                    var conn = new PluginConnection(wsContext.WebSocket);
                    _connections[connectionId] = conn;
                    _logger.LogInformation("WS connected: id={ConnectionId} origin={Origin} ua={UA}",
                        connectionId, origin, ua);
                    _ = Task.Run(() => HandleConnectionAsync(connectionId, conn.WebSocket, cancellationToken), cancellationToken);
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
        var buffer    = new byte[65536];
        var msgBuffer = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                // Accumulate all frames of one logical WebSocket message before parsing.
                // A single plugin message can span multiple ReceiveAsync calls when the
                // payload exceeds the TCP receive window (e.g. large entity-list JSON).
                msgBuffer.SetLength(0);
                bool closed = false;

                while (true)
                {
                    WebSocketReceiveResult result;
                    try { result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken); }
                    catch (OperationCanceledException) { goto done; }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                        closed = true;
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                        msgBuffer.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage) break;
                }

                if (closed) break;
                if (msgBuffer.Length == 0) continue;

                var json = Encoding.UTF8.GetString(msgBuffer.GetBuffer(), 0, (int)msgBuffer.Length);
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
                    _logger.LogError(ex, "Error parsing message from {ConnectionId} ({Bytes} bytes)",
                        connectionId, msgBuffer.Length);
                }
            }
            done:;
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
        foreach (var conn in _connections.Values)
            conn.Dispose();
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
