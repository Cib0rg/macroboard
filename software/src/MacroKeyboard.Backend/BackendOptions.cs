namespace MacroKeyboard.Backend;

/// <summary>
/// Configuration options bound from the "Backend" section of appsettings.json.
/// </summary>
public class BackendOptions
{
    public const string Section = "Backend";

    public int IpcPort               { get; set; } = 28195;
    public int WebSocketPort         { get; set; } = 28196;
    public int PropertyInspectorPort { get; set; } = 8787;
    public bool AutoReconnect        { get; set; } = true;
    public int ReconnectInterval     { get; set; } = 5000;
}
