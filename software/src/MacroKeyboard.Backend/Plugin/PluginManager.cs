using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Services;
using MacroKeyboard.Shared.Plugin;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace MacroKeyboard.Backend.Plugin;

/// <summary>
/// Manages plugin lifecycle, routing, and Stream Deck protocol handling.
/// </summary>
public partial class PluginManager : IDisposable
{
    private readonly ILogger<PluginManager> _logger;
    private readonly string _pluginsDirectory;
    private readonly IDeviceService _deviceService;
    private readonly WebSocketServer _webSocketServer;
    private readonly ImageService _imageService;
    private readonly PropertyInspectorServer _piServer;

    private readonly ConcurrentDictionary<string, PluginInstance> _plugins = new();
    private readonly ConcurrentDictionary<string, PluginManifest> _manifests = new();
    private readonly ConcurrentDictionary<string, string> _pluginDirectories = new();

    // connectionId → pluginId (populated after registerPlugin handshake)
    private readonly ConcurrentDictionary<string, string> _connectionToPlugin = new();

    // context (pluginId:buttonIdx) → PI connectionId
    private readonly ConcurrentDictionary<string, string> _piConnections = new();

    // context (pluginId:buttonIndex) → current state index for multi-state actions
    private readonly ConcurrentDictionary<string, int> _actionStates = new();

    // context (pluginId:buttonIndex) → actionId; populated on willAppear so PI registration can send propertyInspectorDidAppear
    private readonly ConcurrentDictionary<string, string> _contextToActionId = new();

    private const string DeviceId = "MK_DEVICE_0";

    // Tolerates both "Category": "Foo" and "Category": ["Foo", "Bar"] in SD manifests
    private static readonly JsonSerializerSettings _manifestSerializerSettings = new()
    {
        Converters = { new StringOrArrayConverter() }
    };

    public PluginManager(
        ILogger<PluginManager> logger,
        IDeviceService deviceService,
        WebSocketServer webSocketServer,
        ImageService imageService,
        PropertyInspectorServer piServer,
        string pluginsDirectory)
    {
        _logger = logger;
        _deviceService = deviceService;
        _webSocketServer = webSocketServer;
        _imageService = imageService;
        _piServer = piServer;
        _pluginsDirectory = pluginsDirectory;

        _webSocketServer.MessageReceived += OnPluginMessageReceived;
        _webSocketServer.ConnectionClosed += OnConnectionClosed;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public IEnumerable<PluginManifest> GetPlugins() => _manifests.Values;
    public PluginManifest? GetPlugin(string pluginId)
        => _manifests.TryGetValue(pluginId, out var m) ? m : null;
    public string? GetPluginDirectory(string pluginId)
        => _pluginDirectories.TryGetValue(pluginId, out var d) ? d : null;

    /// <summary>
    /// Returns the settings last written by the plugin or PI via setSettings for a specific
    /// action instance (sidecar file). Returns null if never set.
    /// </summary>
    public async Task<string?> GetActionSettingsAsync(string pluginId, int buttonIndex)
    {
        var context = MakeContext(pluginId, buttonIndex);
        var path    = GetActionSettingsPath(pluginId, context);
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path);
    }

    public IEnumerable<(string PluginId, string PluginName, PluginAction Action, string? ManifestPiPath)> GetLoadedActions()
    {
        foreach (var manifest in _manifests.Values)
            foreach (var action in manifest.Actions)
                yield return (manifest.Id, manifest.Name, action, manifest.PropertyInspectorPath);
    }

    public void Dispose()
    {
        _webSocketServer.MessageReceived -= OnPluginMessageReceived;
        _webSocketServer.ConnectionClosed -= OnConnectionClosed;
        foreach (var instance in _plugins.Values)
            instance.Dispose();
        _plugins.Clear();
    }
}
