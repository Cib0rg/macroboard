namespace MacroKeyboard.Shared.Events;

/// <summary>
/// Event arguments for device events
/// </summary>
public class DeviceEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
}

/// <summary>
/// Event arguments for button events
/// </summary>
public class ButtonEventArgs : EventArgs
{
    public byte ButtonIndex { get; set; }
    public ButtonEventType EventType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Button event types
/// </summary>
public enum ButtonEventType
{
    Pressed,
    Released,
    LongPress
}

/// <summary>
/// Event arguments for encoder events
/// </summary>
public class EncoderEventArgs : EventArgs
{
    public sbyte Delta { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Event arguments for profile change events
/// </summary>
public class ProfileChangedEventArgs : EventArgs
{
    public byte ProfileIndex { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
