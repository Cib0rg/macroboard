namespace MacroKeyboard.Shared.Plugin;

/// <summary>
/// Context provided to plugins for interacting with the device
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// Plugin unique identifier
    /// </summary>
    string PluginId { get; }
    
    /// <summary>
    /// Set button image
    /// </summary>
    Task SetButtonImageAsync(int buttonIndex, byte[] imageData, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Set button title (rendered as text on image)
    /// </summary>
    Task SetButtonTitleAsync(int buttonIndex, string title, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Set LED color for a button
    /// </summary>
    Task SetLedColorAsync(int buttonIndex, byte r, byte g, byte b, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Show alert on button (flash LED)
    /// </summary>
    Task ShowAlertAsync(int buttonIndex, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Log message from plugin
    /// </summary>
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? exception = null);
    
    /// <summary>
    /// Get plugin settings
    /// </summary>
    Task<T?> GetSettingsAsync<T>(CancellationToken cancellationToken = default) where T : class;
    
    /// <summary>
    /// Save plugin settings
    /// </summary>
    Task SaveSettingsAsync<T>(T settings, CancellationToken cancellationToken = default) where T : class;
}
