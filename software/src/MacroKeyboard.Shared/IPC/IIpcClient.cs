namespace MacroKeyboard.Shared.IPC;

/// <summary>
/// Interface for IPC client (implemented by UI/TrayApp)
/// </summary>
public interface IIpcClient
{
    /// <summary>
    /// Connect to the IPC server
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disconnect from the IPC server
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send a message to the server
    /// </summary>
    Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send a message and wait for response
    /// </summary>
    Task<IpcResponse> SendAndWaitAsync(IpcMessage message, TimeSpan timeout, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if connected to server
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Event raised when a message is received from the server
    /// </summary>
    event EventHandler<IpcMessage>? MessageReceived;
    
    /// <summary>
    /// Event raised when connection is established
    /// </summary>
    event EventHandler? Connected;
    
    /// <summary>
    /// Event raised when connection is lost
    /// </summary>
    event EventHandler? Disconnected;
}
