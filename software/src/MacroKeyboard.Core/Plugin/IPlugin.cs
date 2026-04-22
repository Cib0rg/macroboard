namespace MacroKeyboard.Shared.Plugin;

/// <summary>
/// Interface that managed plugins must implement
/// </summary>
public interface IPlugin : IDisposable
{
    /// <summary>
    /// Initialize the plugin with context
    /// </summary>
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Start the plugin
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop the plugin
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Handle button press event
    /// </summary>
    Task OnButtonPressedAsync(int buttonIndex, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Handle button release event
    /// </summary>
    Task OnButtonReleasedAsync(int buttonIndex, CancellationToken cancellationToken = default);
}
