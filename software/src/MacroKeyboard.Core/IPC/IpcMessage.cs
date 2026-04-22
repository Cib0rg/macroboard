namespace MacroKeyboard.Shared.IPC;

/// <summary>
/// Base class for IPC messages between Backend and UI/TrayApp
/// </summary>
public class IpcMessage
{
    public string MessageType { get; set; } = string.Empty;
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public object? Data { get; set; }
}

/// <summary>
/// IPC message types
/// </summary>
public static class IpcMessageTypes
{
    // Device messages
    public const string DeviceConnected = "device.connected";
    public const string DeviceDisconnected = "device.disconnected";
    public const string DeviceInfo = "device.info";
    
    // Profile messages
    public const string ProfileChanged = "profile.changed";
    public const string ProfileList = "profile.list";
    public const string ProfileSave = "profile.save";
    public const string ProfileLoad = "profile.load";
    public const string ProfileDelete = "profile.delete";
    
    // Button messages
    public const string ButtonPressed = "button.pressed";
    public const string ButtonReleased = "button.released";
    public const string ButtonLongPress = "button.longpress";
    public const string ButtonConfig = "button.config";
    
    // Encoder messages
    public const string EncoderRotated = "encoder.rotated";
    public const string EncoderPressed = "encoder.pressed";
    
    // System messages
    public const string Ping = "system.ping";
    public const string Pong = "system.pong";
    public const string Shutdown = "system.shutdown";
    public const string Status = "system.status";
    
    // Plugin messages
    public const string PluginRegistered = "plugin.registered";
    public const string PluginUnregistered = "plugin.unregistered";
    public const string PluginAction = "plugin.action";
}

/// <summary>
/// Response wrapper for IPC messages
/// </summary>
public class IpcResponse : IpcMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
