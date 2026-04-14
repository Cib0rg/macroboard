using MacroKeyboard.Shared.Plugin;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace MacroKeyboard.Backend.Plugin;

/// <summary>
/// WebSocket server for Stream Deck API compatibility
/// Plugins connect to this server to communicate with the device
/// </summary>
public class WebSocketServer : IDisposable
{
    private readonly ILogger<WebSocketServer> _logger;
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
    private bool _isRunning;

    public event EventHandler<PluginMessage>? MessageReceived;

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
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();

        _isRunning = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(() => AcceptConnectionsAsync(_cts.Token), _cts.Token);

        _logger.LogInformation("WebSocket server started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        _logger.LogInformation("Stopping WebSocket server...");

        _isRunning = false;
        _cts?.Cancel();
        _listener?.Stop();

        // Close all connections
        foreach (var ws in _connections.Values)
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", cancellationToken);
            }
            ws.Dispose();
        }
        _connections.Clear();

        _logger.LogInformation("WebSocket server stopped");
    }

    public async Task BroadcastAsync(PluginMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonConvert.SerializeObject(message);
        var buffer = Encoding.UTF8.GetBytes(json);

        var disconnectedConnections = new List<string>();

        foreach (var kvp in _connections)
        {
            try
            {
                if (kvp.Value.State == WebSocketState.Open)
                {
                    await kvp.Value.SendAsync(
                        new ArraySegment<byte>(buffer),
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken);
                }
                else
                {
                    disconnectedConnections.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting to connection {ConnectionId}", kvp.Key);
                disconnectedConnections.Add(kvp.Key);
            }
        }

        // Remove disconnected connections
        foreach (var connectionId in disconnectedConnections)
        {
            if (_connections.TryRemove(connectionId, out var ws))
            {
                ws.Dispose();
            }
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Accepting WebSocket connections...");

        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                var context = await _listener!.GetContextAsync();
                
                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    var connectionId = Guid.NewGuid().ToString();
                    
                    _connections[connectionId] = wsContext.WebSocket;
                    _logger.LogInformation("WebSocket connection established: {ConnectionId}", connectionId);

                    // Handle connection in background
                    _ = Task.Run(() => HandleConnectionAsync(connectionId, wsContext.WebSocket, cancellationToken), cancellationToken);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting WebSocket connection");
            }
        }

        _logger.LogInformation("Stopped accepting WebSocket connections");
    }

    private async Task HandleConnectionAsync(string connectionId, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

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
                        var message = JsonConvert.DeserializeObject<PluginMessage>(json);
                        if (message != null)
                        {
                            _logger.LogDebug("Received message from {ConnectionId}: {Event}", connectionId, message.Event);
                            MessageReceived?.Invoke(this, message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message from {ConnectionId}", connectionId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket connection {ConnectionId}", connectionId);
        }
        finally
        {
            if (_connections.TryRemove(connectionId, out _))
            {
                _logger.LogInformation("WebSocket connection closed: {ConnectionId}", connectionId);
            }
            
            webSocket.Dispose();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _listener?.Stop();
        
        foreach (var ws in _connections.Values)
        {
            ws.Dispose();
        }
        _connections.Clear();
    }
}

/// <summary>
/// Message format for plugin communication (Stream Deck API compatible)
/// </summary>
public class PluginMessage
{
    public string Event { get; set; } = string.Empty;
    public string? Context { get; set; }
    public string? Action { get; set; }
    public string? Device { get; set; }
    public object? Payload { get; set; }
}
