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
    /// Plugin type: "executable" (Node.js, Python, etc.) or "managed" (C# DLL).
    /// Stream Deck manifests omit this field — defaults to "executable".
    /// </summary>
    public string Type { get; set; } = "executable";

    // ── Our format fields ─────────────────────────────────────────────────────

    /// <summary>Entry point for executable plugins (e.g., "index.js", "main.py")</summary>
    public string? EntryPoint { get; set; }

    /// <summary>Assembly name for managed plugins (e.g., "MyPlugin.dll")</summary>
    public string? Assembly { get; set; }

    /// <summary>Runtime required (e.g., "node", "python3", "dotnet")</summary>
    public string? Runtime { get; set; }

    /// <summary>Minimum backend version required</summary>
    public string MinimumBackendVersion { get; set; } = "1.0.0";

    // ── Stream Deck format fields ─────────────────────────────────────────────

    /// <summary>Stream Deck SDK version (present in SD manifests, e.g. 2)</summary>
    public int SDKVersion { get; set; }

    /// <summary>Stream Deck: cross-platform entry point</summary>
    public string? CodePath { get; set; }

    /// <summary>Stream Deck: Windows-specific entry point (overrides CodePath on Windows)</summary>
    public string? CodePathWin { get; set; }

    /// <summary>Stream Deck: macOS-specific entry point (overrides CodePath on macOS)</summary>
    public string? CodePathMac { get; set; }

    /// <summary>
    /// Stream Deck: default property inspector HTML for all actions that don't specify their own.
    /// Many SD plugins declare a single top-level PropertyInspectorPath instead of per-action.
    /// </summary>
    public string? PropertyInspectorPath { get; set; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Effective entry point for the current OS.
    /// Priority: CodePathWin/Mac (platform) → CodePath → EntryPoint.
    /// </summary>
    public string? EffectiveEntryPoint
    {
        get
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows)
                && !string.IsNullOrEmpty(CodePathWin))
                return CodePathWin;

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.OSX)
                && !string.IsNullOrEmpty(CodePathMac))
                return CodePathMac;

            return CodePath ?? EntryPoint;
        }
    }

    /// <summary>True when this manifest uses the Stream Deck format</summary>
    public bool IsStreamDeckFormat => SDKVersion > 0 || CodePath != null || CodePathWin != null || CodePathMac != null;

    /// <summary>Actions provided by this plugin</summary>
    public PluginAction[] Actions { get; set; } = Array.Empty<PluginAction>();
}

/// <summary>
/// Plugin action definition
/// </summary>
public class PluginAction
{
    /// <summary>Our format: short action identifier</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Stream Deck format: globally unique action UUID (e.g. "com.myplugin.myaction")</summary>
    public string? UUID { get; set; }

    /// <summary>Effective identifier: UUID (SD) takes precedence over Id (ours)</summary>
    public string EffectiveId => UUID ?? Id;

    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;

    /// <summary>Property inspector HTML file (for configuration UI)</summary>
    public string? PropertyInspector { get; set; }

    /// <summary>Stream Deck: property inspector path (alias)</summary>
    public string? PropertyInspectorPath { get; set; }

    /// <summary>Effective PI path</summary>
    public string? EffectivePropertyInspectorPath => PropertyInspector ?? PropertyInspectorPath;

    /// <summary>States for multi-state actions</summary>
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
