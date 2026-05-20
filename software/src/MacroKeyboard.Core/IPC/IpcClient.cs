using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MacroKeyboard.Shared.IPC;

/// <summary>
/// IPC Client для подключения к Backend Service.
/// Supports automatic reconnection with exponential backoff.
/// </summary>
public class IpcClient : IIpcClient, IDisposable
{
    private readonly ILogger<IpcClient> _logger;
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _reconnectCts;
    private bool _isConnected;
    private bool _autoReconnect;
    private bool _disposed;

    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    public event EventHandler<IpcMessage>? MessageReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<int>? Reconnecting;

    public bool IsConnected => _isConnected && _client?.Connected == true;

    public IpcClient(ILogger<IpcClient> logger, string host = "localhost", int port = 28195)
    {
        _logger = logger;
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            _logger.LogWarning("Already connected to IPC server");
            return;
        }

        _autoReconnect = true;
        _reconnectCts?.Cancel();
        _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await ConnectInternalAsync(cancellationToken);
        }
        catch
        {
            // Initial connection failed — start background reconnect loop
            if (_autoReconnect && !_disposed)
            {
                _ = Task.Run(() => ReconnectLoopAsync());
            }
            throw;
        }
    }

    private async Task ConnectInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Connecting to IPC server at {Host}:{Port}...", _host, _port);

            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port, cancellationToken);
            _stream = _client.GetStream();

            _isConnected = true;
            _cts = new CancellationTokenSource();

            _logger.LogInformation("Connected to IPC server");
            Connected?.Invoke(this, EventArgs.Empty);

            // Start receiving messages
            _ = Task.Run(() => ReceiveMessagesAsync(_cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to IPC server");
            CleanupConnection();
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // Stop auto-reconnect when explicitly disconnecting
        _autoReconnect = false;
        _reconnectCts?.Cancel();

        await DisconnectInternalAsync(cancellationToken);
    }

    private async Task DisconnectInternalAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return;
        }

        _logger.LogInformation("Disconnecting from IPC server...");

        _isConnected = false;
        _cts?.Cancel();

        if (_stream != null)
        {
            try
            {
                await _stream.FlushAsync(cancellationToken);
            }
            catch { /* ignore flush errors during disconnect */ }
            
            try { _stream.Close(); } catch { }
            try { _stream.Dispose(); } catch { }
            _stream = null;
        }

        if (_client != null)
        {
            try { _client.Close(); } catch { }
            try { _client.Dispose(); } catch { }
            _client = null;
        }

        _logger.LogInformation("Disconnected from IPC server");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void CleanupConnection()
    {
        _isConnected = false;
        _cts?.Cancel();

        try { _stream?.Close(); } catch { }
        try { _stream?.Dispose(); } catch { }
        _stream = null;

        try { _client?.Close(); } catch { }
        try { _client?.Dispose(); } catch { }
        _client = null;
    }

    public async Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _stream == null)
        {
            throw new InvalidOperationException("Not connected to IPC server");
        }

        try
        {
            var json = JsonConvert.SerializeObject(message);
            var data = Encoding.UTF8.GetBytes(json + "\n");

            await _stream.WriteAsync(data, cancellationToken);
            await _stream.FlushAsync(cancellationToken);

            _logger.LogDebug("Sent message: {MessageType}", message.MessageType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            throw;
        }
    }

    public async Task<IpcResponse> SendAndWaitAsync(IpcMessage message, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<IpcResponse>();
        var requestId = message.RequestId;

        void OnMessageReceived(object? sender, IpcMessage msg)
        {
            if (msg is IpcResponse response && response.RequestId == requestId)
            {
                tcs.TrySetResult(response);
            }
        }

        MessageReceived += OnMessageReceived;

        try
        {
            await SendAsync(message, cancellationToken);

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            linkedCts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        finally
        {
            MessageReceived -= OnMessageReceived;
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream != null)
            {
                var bytesRead = await _stream.ReadAsync(buffer, cancellationToken);

                if (bytesRead == 0)
                {
                    _logger.LogWarning("Connection closed by server");
                    break;
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
                            // Try to deserialize as IpcResponse first (has Success/Error fields)
                            // If it has "Success" field, it's a response; otherwise it's a plain message
                            var jObj = Newtonsoft.Json.Linq.JObject.Parse(messages[i]);
                            IpcMessage message;
                            
                            if (jObj.ContainsKey("Success") || jObj.ContainsKey("success"))
                            {
                                message = jObj.ToObject<IpcResponse>()!;
                            }
                            else
                            {
                                message = jObj.ToObject<IpcMessage>()!;
                            }
                            
                            _logger.LogDebug("Received message: {MessageType} (type: {Type})",
                                message.MessageType, message.GetType().Name);
                            MessageReceived?.Invoke(this, message);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error parsing message");
                        }
                    }
                }

                // Keep the last incomplete message in buffer
                messageBuffer.Clear();
                messageBuffer.Append(messages[^1]);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Message receiving cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error receiving messages");
        }
        finally
        {
            if (_isConnected)
            {
                _isConnected = false;
                CleanupConnection();
                Disconnected?.Invoke(this, EventArgs.Empty);

                // Trigger auto-reconnect if enabled
                if (_autoReconnect && !_disposed)
                {
                    _ = Task.Run(() => ReconnectLoopAsync());
                }
            }
        }
    }

    private async Task ReconnectLoopAsync()
    {
        var delay = InitialReconnectDelay;
        var attempt = 0;

        while (_autoReconnect && !_disposed)
        {
            var token = _reconnectCts?.Token ?? CancellationToken.None;
            if (token.IsCancellationRequested)
                break;

            attempt++;
            _logger.LogInformation(
                "Reconnection attempt {Attempt} in {Delay}s...", attempt, delay.TotalSeconds);
            Reconnecting?.Invoke(this, attempt);

            try
            {
                await Task.Delay(delay, token);
                await ConnectInternalAsync(token);

                // If we get here, connection succeeded
                _logger.LogInformation("Reconnected to IPC server after {Attempt} attempt(s)", attempt);
                return;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Reconnection cancelled");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnection attempt {Attempt} failed", attempt);

                // Exponential backoff with cap
                delay = TimeSpan.FromSeconds(
                    Math.Min(delay.TotalSeconds * 2, MaxReconnectDelay.TotalSeconds));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _autoReconnect = false;
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();

        DisconnectInternalAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}
