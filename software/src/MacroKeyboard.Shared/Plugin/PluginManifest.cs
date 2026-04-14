namespace MacroKeyboard.Shared.Plugin;

/// <summary>
/// Plugin manifest (manifest.json)
/// </summary>
public class PluginManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string[] Category { get; set; } = Array.Empty<string>();
    public string Url { get; set; } = string.Empty;
    
    /// <summary>
    /// Plugin type: "executable" (Node.js, Python, etc.) or "managed" (C# DLL)
    /// </summary>
    public string Type { get; set; } = "executable";
    
    /// <summary>
    /// Entry point for executable plugins (e.g., "index.js", "main.py")
    /// </summary>
    public string? EntryPoint { get; set; }
    
    /// <summary>
    /// Assembly name for managed plugins (e.g., "MyPlugin.dll")
    /// </summary>
    public string? Assembly { get; set; }
    
    /// <summary>
    /// Runtime required (e.g., "node", "python3", "dotnet")
    /// </summary>
    public string? Runtime { get; set; }
    
    /// <summary>
    /// Minimum backend version required
    /// </summary>
    public string MinimumBackendVersion { get; set; } = "1.0.0";
    
    /// <summary>
    /// Actions provided by this plugin
    /// </summary>
    public PluginAction[] Actions { get; set; } = Array.Empty<PluginAction>();
}

/// <summary>
/// Plugin action definition
/// </summary>
public class PluginAction
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;
    
    /// <summary>
    /// Property inspector HTML file (for configuration UI)
    /// </summary>
    public string? PropertyInspector { get; set; }
    
    /// <summary>
    /// States for multi-state actions
    /// </summary>
    public ActionState[]? States { get; set; }
}

/// <summary>
/// Action state (for toggle buttons, etc.)
/// </summary>
public class ActionState
{
    public int State { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
}
