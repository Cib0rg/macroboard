using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MacroKeyboard.Shared.IPC;

/// <summary>
/// Base class for IPC messages between Backend and UI/TrayApp.
/// Uses Newtonsoft.Json with TypeNameHandling.None for safe deserialization.
/// The Data property is kept as JToken during deserialization so consumers
/// can call Data.ToObject&lt;T&gt;() to get the typed object.
/// </summary>
public class IpcMessage
{
    public string MessageType { get; set; } = string.Empty;
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Payload data. When serialized from C# objects, this will be the object.
    /// When deserialized from JSON, this will be a JToken (JObject/JArray/JValue)
    /// that can be converted to the expected type via ToObject&lt;T&gt;().
    /// </summary>
    public object? Data { get; set; }
    
    /// <summary>
    /// Helper to get Data as a typed object.
    /// Handles both direct type match and JToken deserialization.
    /// </summary>
    public T? GetData<T>() where T : class
    {
        if (Data is T typed)
            return typed;

        if (Data is JToken jToken)
        {
            try
            {
                return jToken.ToObject<T>();
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
    
    /// <summary>
    /// Helper to get Data as a dictionary (for simple key-value payloads).
    /// </summary>
    public Dictionary<string, object?>? GetDataAsDictionary()
    {
        if (Data is JObject jObj)
        {
            return jObj.ToObject<Dictionary<string, object?>>();
        }
        
        if (Data is Dictionary<string, object?> dict)
            return dict;
            
        return null;
    }
}

/// <summary>
/// IPC message types — constants for all supported message types
/// </summary>
public static class IpcMessageTypes
{
    // Device messages
    public const string DeviceConnected = "device.connected";
    public const string DeviceDisconnected = "device.disconnected";
    public const string DeviceInfo = "device.info";
    public const string GetDeviceInfo = "device.getinfo";
    
    // Profile messages
    public const string ProfileChanged = "profile.changed";
    public const string ProfileList = "profile.list";
    public const string GetProfileList = "profile.getlist";
    public const string ProfileSave = "profile.save";
    public const string ProfileLoad = "profile.load";
    public const string ProfileDelete = "profile.delete";
    public const string ProfileSendToDevice = "profile.sendtodevice";
    public const string ProfileLoadFromDevice = "profile.loadfromdevice";
    public const string ProfileGetInfo = "profile.getinfo";
    
    // Button messages
    public const string ButtonPressed = "button.pressed";
    public const string ButtonReleased = "button.released";
    public const string ButtonLongPress = "button.longpress";
    public const string ButtonConfig = "button.config";
    public const string SetButtonAction = "button.setaction";
    public const string GetButtonAction = "button.getaction";
    
    // LED messages
    public const string SetLedColor = "led.setcolor";
    public const string GetLedColor = "led.getcolor";
    
    // Display messages
    public const string SetDisplayBrightness = "display.setbrightness";
    public const string GetDisplayBrightness = "display.getbrightness";
    
    // Encoder messages
    public const string EncoderRotated = "encoder.rotated";
    public const string EncoderPressed = "encoder.pressed";
    
    // Folder messages
    public const string FolderEntered = "folder.entered";
    public const string FolderExited = "folder.exited";
    
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
    
    /// <summary>
    /// Create a success response for a given request
    /// </summary>
    public static IpcResponse Ok(IpcMessage request, object? data = null)
    {
        return new IpcResponse
        {
            MessageType = request.MessageType + ".response",
            RequestId = request.RequestId,
            Success = true,
            Data = data
        };
    }
    
    /// <summary>
    /// Create an error response for a given request
    /// </summary>
    public static IpcResponse Fail(IpcMessage request, string error)
    {
        return new IpcResponse
        {
            MessageType = request.MessageType + ".response",
            RequestId = request.RequestId,
            Success = false,
            Error = error
        };
    }
}
