namespace MacroKeyboard.Shared.Plugin;

/// <summary>
/// Plugin action descriptor sent from Backend to UI via IPC plugin.list.
/// Carries everything the UI needs to show a plugin action in the palette.
/// </summary>
public class PluginActionInfo
{
    public string PluginId { get; set; } = string.Empty;
    public string PluginName { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;

    /// <summary>Absolute file path to the action icon, resolved by the backend. Null if not found.</summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Full HTTP URL for the Property Inspector page, or null if this action has no PI.
    /// Served by PropertyInspectorServer at http://localhost:8787/plugins/{pluginId}/{piPath}
    /// </summary>
    public string? PropertyInspectorUrl { get; set; }
}
