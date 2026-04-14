namespace MacroKeyboard.Shared.IPC;

/// <summary>
/// Interface for IPC server (implemented by Backend)
/// </summary>
public interface IIpcServer
{
    /// <summary>
    /// Start the IPC server
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop the IPC server
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Broadcast message to all connected clients
    /// </summary>
    Task BroadcastAsync(IpcMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Event raised when a message is received from a client
    /// </summary>
    event EventHandler<IpcMessage>? MessageReceived;
    
    /// <summary>
    /// Event raised when a client connects
    /// </summary>
    event EventHandler<string>? ClientConnected;
    
    /// <summary>
    /// Event raised when a client disconnects
    /// </summary>
    event EventHandler<string>? ClientDisconnected;
}
