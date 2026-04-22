using MacroKeyboard.Shared.IPC;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MacroKeyboard.Backend.Services;

/// <summary>
/// IPC Server implementation using TCP sockets
/// </summary>
public class IpcServer : IIpcServer, IDisposable
{
    private readonly ILogger<IpcServer> _logger;
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private bool _isRunning;

    public event EventHandler<IpcMessage>? MessageReceived;
    public event EventHandler<string>? ClientConnected;
    public event EventHandler<string>? ClientDisconnected;

    public IpcServer(ILogger<IpcServer> logger, int port = 28195)
    {
        _logger = logger;
        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("IPC Server is already running");
            return;
        }

        _logger.LogInformation("Starting IPC Server on port {Port}...", _port);
        
        try
        {
            // Check if port is already in use
            if (IsPortInUse(_port))
            {
                _logger.LogError("Port {Port} is already in use. Another instance of Backend may be running.", _port);
                _logger.LogError("Please stop the other instance or change the port in appsettings.json");
                throw new InvalidOperationException($"Port {_port} is already in use");
            }
            
            _listener = new TcpListener(IPAddress.Loopback, _port);
            
            // Enable SO_REUSEADDR to allow quick restart
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            
            _listener.Start();
            
            _isRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _ = Task.Run(() => AcceptClientsAsync(_cts.Token), _cts.Token);
            
            _logger.LogInformation("IPC Server started successfully on port {Port}", _port);
        }
        catch (SocketException ex) when (ex.ErrorCode == 10048)
        {
            _logger.LogError("Port {Port} is already in use (SocketException 10048)", _port);
            _logger.LogError("Possible causes:");
            _logger.LogError("  1. Another instance of MacroKeyboard.Backend is running");
            _logger.LogError("  2. Another application is using port {Port}", _port);
            _logger.LogError("  3. Port is in TIME_WAIT state from recent shutdown");
            _logger.LogError("Solutions:");
            _logger.LogError("  - Stop other Backend instances");
            _logger.LogError("  - Change port in appsettings.json");
            _logger.LogError("  - Wait 1-2 minutes for TIME_WAIT to expire");
            throw;
        }
    }
    
    private bool IsPortInUse(int port)
    {
        try
        {
            using var testListener = new TcpListener(IPAddress.Loopback, port);
            testListener.Start();
            testListener.Stop();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        _logger.LogInformation("Stopping IPC Server...");
        
        _isRunning = false;
        _cts?.Cancel();
        _listener?.Stop();

        // Disconnect all clients
        foreach (var client in _clients.Values)
        {
            client.Close();
        }
        _clients.Clear();

        _logger.LogInformation("IPC Server stopped");
    }

    public async Task BroadcastAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonConvert.SerializeObject(message);
        var data = Encoding.UTF8.GetBytes(json + "\n");

        var disconnectedClients = new List<string>();

        foreach (var kvp in _clients)
        {
            try
            {
                if (kvp.Value.Connected)
                {
                    await kvp.Value.GetStream().WriteAsync(data, cancellationToken);
                }
                else
                {
                    disconnectedClients.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting to client {ClientId}", kvp.Key);
                disconnectedClients.Add(kvp.Key);
            }
        }

        // Remove disconnected clients
        foreach (var clientId in disconnectedClients)
        {
            if (_clients.TryRemove(clientId, out var client))
            {
                client.Close();
                ClientDisconnected?.Invoke(this, clientId);
            }
        }
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Accepting client connections...");

        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                var clientId = Guid.NewGuid().ToString();
                
                _clients[clientId] = client;
                _logger.LogInformation("Client connected: {ClientId}", clientId);
                
                ClientConnected?.Invoke(this, clientId);

                // Handle client in background
                _ = Task.Run(() => HandleClientAsync(clientId, client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client connection");
            }
        }

        _logger.LogInformation("Stopped accepting client connections");
    }

    private async Task HandleClientAsync(string clientId, TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
                
                if (bytesRead == 0)
                {
                    break; // Client disconnected
                }

                var data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageBuffer.Append(data);

                // Process complete messages (delimited by newline)
                var messages = messageBuffer.ToString().Split('\n');
                
                for (int i = 0; i < messages.Length - 1; i++)
                {
                    if (!string.IsNullOrWhiteSpace(messages[i]))
                    {
                        try
                        {
                            var message = JsonConvert.DeserializeObject<IpcMessage>(messages[i]);
                            if (message != null)
                            {
                                _logger.LogDebug("Received message from {ClientId}: {MessageType}", clientId, message.MessageType);
                                
                                // Fire event — IpcCommandHandler will process and send response
                                MessageReceived?.Invoke(this, message);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing message from {ClientId}", clientId);
                        }
                    }
                }

                // Keep the last incomplete message in buffer
                messageBuffer.Clear();
                messageBuffer.Append(messages[^1]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client {ClientId}", clientId);
        }
        finally
        {
            if (_clients.TryRemove(clientId, out _))
            {
                _logger.LogInformation("Client disconnected: {ClientId}", clientId);
                ClientDisconnected?.Invoke(this, clientId);
            }
            
            client.Close();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _listener?.Stop();
        
        foreach (var client in _clients.Values)
        {
            client.Close();
        }
        _clients.Clear();
    }
}
