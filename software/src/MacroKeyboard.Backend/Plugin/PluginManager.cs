using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Services;
using MacroKeyboard.Shared.Plugin;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;

namespace MacroKeyboard.Backend.Plugin;

/// <summary>
/// Manages plugin lifecycle, routing, and Stream Deck protocol handling.
/// </summary>
public class PluginManager : IDisposable
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
    private const int DeviceColumns = 5;
    private const int DeviceRows = 2;

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

    // ── Plugin discovery ──────────────────────────────────────────────────────

    public async Task LoadPluginsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading plugins from {Dir}", _pluginsDirectory);

        if (!Directory.Exists(_pluginsDirectory))
        {
            _logger.LogWarning("Plugins directory does not exist: {Dir}", _pluginsDirectory);
            Directory.CreateDirectory(_pluginsDirectory);
            return;
        }

        // Extract any .streamDeckPlugin archives that haven't been unpacked yet.
        // Track which directories came from archives so the directory scan below doesn't double-load them.
        var loadedFromArchive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var archivePath in Directory.GetFiles(_pluginsDirectory)
                     .Where(f => f.EndsWith(".streamDeckPlugin", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var pluginDir = await ExtractStreamDeckPluginAsync(archivePath, cancellationToken);
                if (pluginDir != null)
                {
                    loadedFromArchive.Add(pluginDir);
                    await LoadPluginAsync(pluginDir, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract/load plugin archive {File}", Path.GetFileName(archivePath));
            }
        }

        // Load already-extracted plugin directories (skip ones we just loaded from archives).
        foreach (var pluginDir in Directory.GetDirectories(_pluginsDirectory))
        {
            if (loadedFromArchive.Contains(pluginDir)) continue;
            try { await LoadPluginAsync(pluginDir, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to load plugin from {Dir}", pluginDir); }
        }

        _logger.LogInformation("Loaded {Count} plugins", _plugins.Count);
    }

    /// <summary>
    /// Extracts a .streamDeckPlugin archive (ZIP) to the plugins directory.
    /// Returns the path to the extracted plugin directory, or null on failure.
    /// Re-uses an existing directory if the archive was already unpacked.
    /// </summary>
    private async Task<string?> ExtractStreamDeckPluginAsync(string archivePath, CancellationToken cancellationToken)
    {
        var archiveFileName = Path.GetFileName(archivePath);

        using var archive = ZipFile.OpenRead(archivePath);

        // Detect the single top-level folder that most .streamDeckPlugin archives wrap their files in.
        // E.g. "com.rgpaul.vlc.streamDeckPlugin" typically contains "com.rgpaul.vlc.sdPlugin/" at the root.
        var topLevelDirs = archive.Entries
            .Where(e => e.FullName.Contains('/'))
            .Select(e => e.FullName[..e.FullName.IndexOf('/')])
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool hasSingleRoot = topLevelDirs.Count == 1;
        string pluginDirName;

        if (hasSingleRoot)
        {
            // Archive has one top-level folder — use it as the plugin directory name.
            pluginDirName = topLevelDirs[0];
        }
        else
        {
            // Files are at the archive root — create a directory from the archive name.
            var baseName = archiveFileName.EndsWith(".streamDeckPlugin", StringComparison.OrdinalIgnoreCase)
                ? archiveFileName[..^".streamDeckPlugin".Length]
                : Path.GetFileNameWithoutExtension(archiveFileName);
            pluginDirName = baseName + ".sdPlugin";
        }

        var targetDir = Path.Combine(_pluginsDirectory, pluginDirName);

        if (Directory.Exists(targetDir))
        {
            _logger.LogDebug("Plugin archive already extracted: {Archive} → {Dir}", archiveFileName, pluginDirName);
            return targetDir;
        }

        _logger.LogInformation("Extracting plugin archive: {Archive} → {Dir}", archiveFileName, pluginDirName);
        Directory.CreateDirectory(targetDir);
        var canonicalTarget = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
        int fileCount = 0;

        foreach (var entry in archive.Entries)
        {
            // Strip the single top-level prefix when present so files land directly in targetDir.
            string relPath = hasSingleRoot
                ? entry.FullName[(entry.FullName.IndexOf('/') + 1)..]
                : entry.FullName;

            if (relPath.Length == 0) continue;
            relPath = relPath.Replace('/', Path.DirectorySeparatorChar);

            var destPath = Path.GetFullPath(Path.Combine(targetDir, relPath));

            // Guard against path-traversal entries.
            if (!destPath.StartsWith(canonicalTarget, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipping unsafe archive entry: {Entry}", entry.FullName);
                continue;
            }

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                await Task.Run(() => entry.ExtractToFile(destPath, overwrite: false), cancellationToken);
                fileCount++;
            }
        }

        _logger.LogInformation("Extracted {Dir}: {Count} files", pluginDirName, fileCount);
        return targetDir;
    }

    private async Task LoadPluginAsync(string pluginDir, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(pluginDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning("No manifest.json in {Dir}", pluginDir);
            return;
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonConvert.DeserializeObject<PluginManifest>(manifestJson, _manifestSerializerSettings);

        if (manifest == null)
        {
            _logger.LogWarning("Failed to parse manifest: {Path}", manifestPath);
            return;
        }

        // Stream Deck manifests don't have an Id field — derive from folder name.
        // SD folders are named like "com.elgato.counter.sdPlugin"; strip the .sdPlugin suffix.
        if (string.IsNullOrEmpty(manifest.Id))
        {
            var folderName = Path.GetFileName(pluginDir);
            manifest.Id = folderName.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase)
                ? folderName[..^".sdPlugin".Length]
                : folderName;
        }

        // Stream Deck manifests: default type to executable
        if (manifest.IsStreamDeckFormat && string.IsNullOrEmpty(manifest.Type))
            manifest.Type = "executable";

        _logger.LogInformation("Loading plugin: {Name} v{Version} [{Type}]",
            manifest.Name, manifest.Version, manifest.Type);

        _manifests[manifest.Id]    = manifest;
        _pluginDirectories[manifest.Id] = pluginDir;
        _piServer.RegisterPlugin(manifest.Id, pluginDir);

        PluginInstance? instance = manifest.Type switch
        {
            "executable" => new ExecutablePluginInstance(manifest, pluginDir, _logger),
            "managed"    => new ManagedPluginInstance(manifest, pluginDir, _logger, _deviceService),
            _            => null
        };

        if (instance != null)
        {
            _plugins[manifest.Id] = instance;
            _logger.LogInformation("Plugin loaded: {Id}", manifest.Id);
        }
        else
        {
            _logger.LogWarning("Unsupported plugin type '{Type}' for {Id}", manifest.Type, manifest.Id);
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task StartPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var instance))
            throw new InvalidOperationException($"Plugin not found: {pluginId}");

        await instance.StartAsync(cancellationToken);
        _logger.LogInformation("Plugin started: {Id}", pluginId);
    }

    public async Task StopPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var instance))
            throw new InvalidOperationException($"Plugin not found: {pluginId}");

        await instance.StopAsync(cancellationToken);
        _logger.LogInformation("Plugin stopped: {Id}", pluginId);
    }

    public async Task ReloadPluginsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reloading plugins...");

        foreach (var (id, instance) in _plugins)
        {
            try { await instance.StopAsync(cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error stopping plugin {Id} before reload", id); }
        }

        _plugins.Clear();
        _manifests.Clear();
        _pluginDirectories.Clear();
        _connectionToPlugin.Clear();
        _piConnections.Clear();
        _actionStates.Clear();
        _contextToActionId.Clear();

        await LoadPluginsAsync(cancellationToken);

        foreach (var id in _plugins.Keys)
        {
            try { await StartPluginAsync(id, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error starting plugin {Id} after reload", id); }
        }

        _logger.LogInformation("Plugin reload complete: {Count} plugins loaded", _plugins.Count);
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

    // ── Button dispatch ───────────────────────────────────────────────────────

    public async Task DispatchButtonPressAsync(
        string pluginId, string actionId, string? settings, int buttonIndex,
        CancellationToken ct = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var instance))
        {
            _logger.LogWarning("DispatchButtonPress: plugin not found: {Id}", pluginId);
            await FlashLedAsync((byte)buttonIndex, r: 255, g: 0, b: 0, onMs: 200, offMs: 100, times: 2);
            return;
        }

        if (instance is ManagedPluginInstance managed)
        {
            await managed.OnButtonPressedAsync(actionId, settings, buttonIndex, ct);
        }
        else
        {
            var context = MakeContext(pluginId, buttonIndex);
            await SendToPluginAsync(pluginId, new PluginMessage
            {
                Event   = "keyDown",
                Action  = actionId,
                Context = context,
                Device  = DeviceId,
                Payload = new
                {
                    settings          = DeserializeSettings(settings),
                    coordinates       = new { column = buttonIndex % DeviceColumns, row = buttonIndex / DeviceColumns },
                    state             = _actionStates.TryGetValue(context, out var s) ? s : 0,
                    userDesiredState  = -1,
                    isInMultiAction   = false
                }
            }, ct);
        }
    }

    public async Task DispatchButtonReleaseAsync(
        string pluginId, string actionId, string? settings, int buttonIndex,
        CancellationToken ct = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var instance))
        {
            _logger.LogWarning("DispatchButtonRelease: plugin not found: {Id}", pluginId);
            return;
        }

        if (instance is ManagedPluginInstance managed)
        {
            await managed.OnButtonReleasedAsync(actionId, settings, buttonIndex, ct);
        }
        else
        {
            var context = MakeContext(pluginId, buttonIndex);
            await SendToPluginAsync(pluginId, new PluginMessage
            {
                Event   = "keyUp",
                Action  = actionId,
                Context = context,
                Device  = DeviceId,
                Payload = new
                {
                    settings         = DeserializeSettings(settings),
                    coordinates      = new { column = buttonIndex % DeviceColumns, row = buttonIndex / DeviceColumns },
                    state            = _actionStates.TryGetValue(context, out var s) ? s : 0,
                    userDesiredState = -1,
                    isInMultiAction  = false
                }
            }, ct);
        }
    }

    // ── Lifecycle notifications (called by BackendService) ────────────────────

    public async Task NotifyWillAppearAsync(
        string pluginId, string actionId, string? settings, int buttonIndex,
        CancellationToken ct = default)
    {
        var context = MakeContext(pluginId, buttonIndex);
        _contextToActionId[context] = actionId;
        await SendToPluginAsync(pluginId, new PluginMessage
        {
            Event   = "willAppear",
            Action  = actionId,
            Context = context,
            Device  = DeviceId,
            Payload = new
            {
                settings        = DeserializeSettings(settings),
                coordinates     = new { column = buttonIndex % DeviceColumns, row = buttonIndex / DeviceColumns },
                state           = _actionStates.TryGetValue(context, out var s) ? s : 0,
                isInMultiAction = false
            }
        }, ct);
    }

    public async Task NotifyWillDisappearAsync(
        string pluginId, string actionId, string? settings, int buttonIndex,
        CancellationToken ct = default)
    {
        var context = MakeContext(pluginId, buttonIndex);
        await SendToPluginAsync(pluginId, new PluginMessage
        {
            Event   = "willDisappear",
            Action  = actionId,
            Context = context,
            Device  = DeviceId,
            Payload = new
            {
                settings        = DeserializeSettings(settings),
                coordinates     = new { column = buttonIndex % DeviceColumns, row = buttonIndex / DeviceColumns },
                state           = _actionStates.TryGetValue(context, out var s) ? s : 0,
                isInMultiAction = false
            }
        }, ct);
    }

    public Task NotifyDeviceConnectedAsync(CancellationToken ct = default)
        => _webSocketServer.BroadcastAsync(new PluginMessage
        {
            Event  = "deviceDidConnect",
            Device = DeviceId,
            Payload = new
            {
                deviceInfo = new
                {
                    name    = "MacroKeyboard",
                    type    = 0,
                    size    = new { columns = DeviceColumns, rows = DeviceRows }
                }
            }
        }, ct);

    public Task NotifyDeviceDisconnectedAsync(CancellationToken ct = default)
        => _webSocketServer.BroadcastAsync(new PluginMessage { Event = "deviceDidDisconnect", Device = DeviceId }, ct);

    // ── Inbound message handler ───────────────────────────────────────────────

    private void OnPluginMessageReceived(object? sender, PluginMessageEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try { await HandlePluginMessageAsync(e.ConnectionId, e.Message); }
            catch (Exception ex) { _logger.LogError(ex, "Error handling plugin event: {Event}", e.Message.Event); }
        });
    }

    private async Task HandlePluginMessageAsync(string connectionId, PluginMessage msg)
    {
        switch (msg.Event)
        {
            case "registerPlugin":     await HandleRegisterPluginAsync(connectionId, msg); break;
            case "setTitle":           await HandleSetTitleAsync(msg); break;
            case "setImage":           await HandleSetImageAsync(msg); break;
            case "showAlert":          await HandleShowAlertAsync(msg); break;
            case "showOk":             await HandleShowOkAsync(msg); break;
            case "setState":           HandleSetState(msg); break;
            case "setSettings":        await HandleSetSettingsAsync(msg); break;
            case "getSettings":        await HandleGetSettingsAsync(connectionId, msg); break;
            case "setGlobalSettings":  await HandleSetGlobalSettingsAsync(msg); break;
            case "getGlobalSettings":  await HandleGetGlobalSettingsAsync(connectionId, msg); break;
            case "openUrl":                  HandleOpenUrl(msg); break;
            case "logMessage":               HandleLogMessage(msg); break;
            case "registerPropertyInspector": await HandleRegisterPropertyInspectorAsync(connectionId, msg); break;
            case "sendToPlugin":             await HandleSendToPluginFromPiAsync(msg); break;
            case "sendToPropertyInspector":  await HandleSendToPropertyInspectorAsync(msg); break;
            default:
                _logger.LogDebug("Unhandled plugin event '{Event}' from {Connection}", msg.Event, connectionId);
                break;
        }
    }

    private async Task HandleRegisterPluginAsync(string connectionId, PluginMessage msg)
    {
        // SD protocol: { "event": "registerPlugin", "uuid": "<pluginId>" }
        // The official SDK sends the plugin id in the "uuid" field; some send it in "context".
        var pluginId = msg.EffectiveContext;
        if (string.IsNullOrEmpty(pluginId))
        {
            var p = ParsePayload(msg.Payload);
            pluginId = p?.GetValueOrDefault("uuid")?.ToString();
        }

        if (string.IsNullOrEmpty(pluginId) || !_manifests.ContainsKey(pluginId))
        {
            _logger.LogWarning("registerPlugin: unknown or missing pluginId '{Id}'", pluginId);
            return;
        }

        _connectionToPlugin[connectionId] = pluginId;
        _logger.LogInformation("Plugin registered: {Id} on connection {Conn}", pluginId, connectionId);

        // Send lifecycle events to the newly registered plugin
        await _webSocketServer.SendToConnectionAsync(connectionId, new PluginMessage
        {
            Event = "applicationDidLaunch",
            Payload = new { application = new { version = "1.0.0" } }
        });

        await _webSocketServer.SendToConnectionAsync(connectionId, new PluginMessage
        {
            Event  = "deviceDidConnect",
            Device = DeviceId,
            Payload = new
            {
                deviceInfo = new
                {
                    name = "MacroKeyboard",
                    type = 0,
                    size = new { columns = DeviceColumns, rows = DeviceRows }
                }
            }
        });

        // Send stored global settings to the plugin immediately after registration so it can
        // initialise state (e.g. load the VLC password) without waiting for the PI to open.
        var globalPath = GetGlobalSettingsPath(pluginId);
        if (File.Exists(globalPath))
        {
            try
            {
                var globalJson = await File.ReadAllTextAsync(globalPath);
                var globalData = JsonConvert.DeserializeObject(globalJson);
                _logger.LogInformation("[{PluginId}] Sending stored global settings on registration", pluginId);
                await _webSocketServer.SendToConnectionAsync(connectionId, new PluginMessage
                {
                    Event   = "didReceiveGlobalSettings",
                    Payload = new { settings = globalData ?? (object)new { } }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{PluginId}] Could not read global settings for initial push", pluginId);
            }
        }
        else
        {
            _logger.LogInformation("[{PluginId}] No global settings file yet — plugin will start with empty settings", pluginId);
        }
    }

    private async Task HandleSetTitleAsync(PluginMessage msg)
    {
        if (!TryParseButtonContext(msg.Context, out _, out var buttonIndex)) return;
        var payload  = ParsePayload(msg.Payload);
        var title    = payload?.GetValueOrDefault("title")?.ToString() ?? string.Empty;
        await _deviceService.SetButtonNameAsync(0, (byte)buttonIndex, title);
    }

    private async Task HandleSetImageAsync(PluginMessage msg)
    {
        if (!TryParseButtonContext(msg.Context, out _, out var buttonIndex)) return;

        var payload = ParsePayload(msg.Payload);
        var image   = payload?.GetValueOrDefault("image")?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(image)) return;

        // Strip data URL prefix (data:image/png;base64,... or data:image/jpeg;base64,...)
        var commaIdx = image.IndexOf(',');
        if (commaIdx >= 0) image = image[(commaIdx + 1)..];

        try
        {
            var rawBytes = Convert.FromBase64String(image);
            var pluginRing = SixLabors.ImageSharp.Color.FromRgb(0x8B, 0x5C, 0xF6); // #8B5CF6 purple
            var processed  = await _imageService.ProcessImageBytesForButtonAsync(rawBytes, pluginRing);
            if (processed == null) return;
            await _deviceService.SendButtonImageAsync(0, (byte)buttonIndex, processed, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "setImage: failed to process/send image for button {Idx}", buttonIndex);
        }
    }

    private async Task HandleShowAlertAsync(PluginMessage msg)
    {
        if (!TryParseButtonContext(msg.Context, out _, out var buttonIndex)) return;
        // Two orange flashes
        await FlashLedAsync((byte)buttonIndex, r: 255, g: 165, b: 0, onMs: 200, offMs: 100, times: 2);
    }

    private async Task HandleShowOkAsync(PluginMessage msg)
    {
        if (!TryParseButtonContext(msg.Context, out _, out var buttonIndex)) return;
        // Single green flash
        await FlashLedAsync((byte)buttonIndex, r: 0, g: 220, b: 0, onMs: 500, offMs: 0, times: 1);
    }

    private void HandleSetState(PluginMessage msg)
    {
        if (msg.Context == null) return;
        var payload = ParsePayload(msg.Payload);
        if (payload?.TryGetValue("state", out var stateObj) == true
            && stateObj != null
            && int.TryParse(stateObj.ToString(), out var state))
        {
            _actionStates[msg.Context] = state;
            _logger.LogDebug("setState: {Context} → {State}", msg.Context, state);
        }
    }

    private async Task HandleSetSettingsAsync(PluginMessage msg)
    {
        if (msg.Context == null) return;
        if (!TryParseButtonContext(msg.Context, out var pluginId, out _)) return;

        var path = GetActionSettingsPath(pluginId, msg.Context);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = msg.Payload is string s ? s : JsonConvert.SerializeObject(msg.Payload);
        await File.WriteAllTextAsync(path, json);
    }

    private async Task HandleGetSettingsAsync(string connectionId, PluginMessage msg)
    {
        if (msg.Context == null) return;
        if (!TryParseButtonContext(msg.Context, out var pluginId, out _)) return;

        var path     = GetActionSettingsPath(pluginId, msg.Context);
        object? data = null;
        if (File.Exists(path))
        {
            var json = await File.ReadAllTextAsync(path);
            data = JsonConvert.DeserializeObject(json);
        }

        await _webSocketServer.SendToConnectionAsync(connectionId, new PluginMessage
        {
            Event   = "didReceiveSettings",
            Action  = msg.Action,
            Context = msg.Context,
            Device  = DeviceId,
            Payload = new { settings = data ?? (object)new { } }
        });
    }

    private async Task HandleSetGlobalSettingsAsync(PluginMessage msg)
    {
        // Context may be plain pluginId OR pluginId:buttonIndex — normalise to just pluginId
        var pluginId = GetPluginIdFromContext(msg.Context ?? msg.Uuid);
        if (string.IsNullOrEmpty(pluginId)) return;

        var path = GetGlobalSettingsPath(pluginId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = msg.Payload is string s ? s : JsonConvert.SerializeObject(msg.Payload);
        await File.WriteAllTextAsync(path, json);
        _logger.LogInformation("[{PluginId}] Global settings saved ({Bytes} bytes)", pluginId, json.Length);

        // SD protocol: after setGlobalSettings, send didReceiveGlobalSettings to both
        // the plugin process and any open PIs so they all have the same in-memory state.
        var data = JsonConvert.DeserializeObject(json);
        var notification = new PluginMessage
        {
            Event   = "didReceiveGlobalSettings",
            Payload = new { settings = data ?? (object)new { } }
        };

        await SendToPluginAsync(pluginId, notification, CancellationToken.None);

        // Also notify any PI connections open for this plugin
        foreach (var kvp in _piConnections.Where(k => k.Key.StartsWith(pluginId, StringComparison.Ordinal)))
            await _webSocketServer.SendToConnectionAsync(kvp.Value, notification);
    }

    private async Task HandleGetGlobalSettingsAsync(string connectionId, PluginMessage msg)
    {
        // Context may be plain pluginId OR pluginId:buttonIndex — normalise to just pluginId
        var pluginId = GetPluginIdFromContext(msg.Context);
        if (string.IsNullOrEmpty(pluginId)) return;

        var path     = GetGlobalSettingsPath(pluginId);
        object? data = null;
        if (File.Exists(path))
        {
            var json = await File.ReadAllTextAsync(path);
            data = JsonConvert.DeserializeObject(json);
        }

        await _webSocketServer.SendToConnectionAsync(connectionId, new PluginMessage
        {
            Event   = "didReceiveGlobalSettings",
            Payload = new { settings = data ?? (object)new { } }
        });
    }

    private void HandleOpenUrl(PluginMessage msg)
    {
        var payload = ParsePayload(msg.Payload);
        var url     = payload?.GetValueOrDefault("url")?.ToString();
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "openUrl: failed to open {Url}", url);
        }
    }

    private void HandleLogMessage(PluginMessage msg)
    {
        var payload  = ParsePayload(msg.Payload);
        var message  = payload?.GetValueOrDefault("message")?.ToString() ?? "(empty)";
        var pluginId = GetPluginIdFromContext(msg.Context) ?? "plugin";
        _logger.LogInformation("[{PluginId}] {Message}", pluginId, message);
    }

    // ── Property Inspector handlers ───────────────────────────────────────────

    private async Task HandleRegisterPropertyInspectorAsync(string connectionId, PluginMessage msg)
    {
        // SD SDK sends PI uuid in the "uuid" field, not "context"
        var context = msg.EffectiveContext;
        if (string.IsNullOrEmpty(context)) return;

        _piConnections[context] = connectionId;
        _logger.LogInformation("Property Inspector registered: context={Context} conn={Conn}", context, connectionId);

        TryParseButtonContext(context, out var pluginId, out _);

        // 1. Send action-specific settings so PI can initialise its fields
        var actionPath = GetActionSettingsPath(pluginId, context);
        object? settings = null;
        if (File.Exists(actionPath))
        {
            try { settings = JsonConvert.DeserializeObject(await File.ReadAllTextAsync(actionPath)); }
            catch { }
        }

        await _webSocketServer.SendToConnectionAsync(connectionId, new PluginMessage
        {
            Event   = "didReceiveSettings",
            Context = context,
            Payload = new { settings = settings ?? (object)new { } }
        });

        // 2. Send global settings so PI can populate plugin-wide fields (e.g. password)
        var globalPath = GetGlobalSettingsPath(pluginId);
        object? globalSettings = null;
        if (File.Exists(globalPath))
        {
            try { globalSettings = JsonConvert.DeserializeObject(await File.ReadAllTextAsync(globalPath)); }
            catch { }
        }

        await _webSocketServer.SendToConnectionAsync(connectionId, new PluginMessage
        {
            Event   = "didReceiveGlobalSettings",
            Payload = new { settings = globalSettings ?? (object)new { } }
        });

        // 3. Notify the plugin that PI has appeared so it can push status/state to the PI
        var actionId = _contextToActionId.TryGetValue(context, out var aid)
            ? aid
            : (_manifests.TryGetValue(pluginId, out var m) && m.Actions.Length > 0
                ? m.Actions[0].EffectiveId
                : string.Empty);

        var pluginConnected = _connectionToPlugin.Any(kvp => kvp.Value == pluginId);
        _logger.LogInformation(
            "PI registered: ctx={Context} hasActionSettings={HasSettings} hasGlobal={HasGlobal} pluginConnected={Connected} actionId={ActionId}",
            context, settings != null, globalSettings != null, pluginConnected, actionId);

        await SendToPluginAsync(pluginId, new PluginMessage
        {
            Event   = "propertyInspectorDidAppear",
            Action  = actionId,
            Context = context,
            Device  = DeviceId
        }, CancellationToken.None);
    }

    private async Task HandleSendToPluginFromPiAsync(PluginMessage msg)
    {
        // PI → plugin: forward the message to the plugin's WS connection
        if (!TryParseButtonContext(msg.Context, out var pluginId, out _)) return;
        var connId = _connectionToPlugin.FirstOrDefault(kvp => kvp.Value == pluginId).Key;
        if (connId != null)
            await _webSocketServer.SendToConnectionAsync(connId, msg);
    }

    private async Task HandleSendToPropertyInspectorAsync(PluginMessage msg)
    {
        // Plugin → PI: forward to the PI's WS connection
        if (msg.Context == null) return;
        if (_piConnections.TryGetValue(msg.Context, out var piConn))
            await _webSocketServer.SendToConnectionAsync(piConn, msg);
    }

    private void OnConnectionClosed(object? sender, string connectionId)
    {
        // Clean up any PI registrations that used this connection and notify plugins
        var staleCtx = _piConnections
            .Where(kvp => kvp.Value == connectionId)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var ctx in staleCtx)
        {
            _piConnections.TryRemove(ctx, out _);
            TryParseButtonContext(ctx, out var pluginId, out _);
            if (!string.IsNullOrEmpty(pluginId))
            {
                var actionId = _contextToActionId.TryGetValue(ctx, out var aid)
                    ? aid
                    : (_manifests.TryGetValue(pluginId, out var m) && m.Actions.Length > 0
                        ? m.Actions[0].EffectiveId
                        : string.Empty);
                _ = SendToPluginAsync(pluginId, new PluginMessage
                {
                    Event   = "propertyInspectorDidDisappear",
                    Action  = actionId,
                    Context = ctx,
                    Device  = DeviceId
                }, CancellationToken.None);
            }
        }

        // Clean up plugin registration
        _connectionToPlugin.TryRemove(connectionId, out _);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendToPluginAsync(string pluginId, PluginMessage msg, CancellationToken ct)
    {
        var connId = _connectionToPlugin.FirstOrDefault(kvp => kvp.Value == pluginId).Key;
        if (connId != null)
        {
            _logger.LogDebug("WS→plugin [{PluginId}] event={Event} ctx={Context}",
                pluginId, msg.Event, msg.Context ?? "-");
            await _webSocketServer.SendToConnectionAsync(connId, msg, ct);
        }
        else
        {
            _logger.LogWarning("WS→plugin [{PluginId}] event={Event}: plugin not connected, broadcasting",
                pluginId, msg.Event);
            await _webSocketServer.BroadcastAsync(msg, ct);
        }
    }

    private static string MakeContext(string pluginId, int buttonIndex)
        => $"{pluginId}:{buttonIndex}";

    private static bool TryParseButtonContext(string? context, out string pluginId, out int buttonIndex)
    {
        pluginId    = string.Empty;
        buttonIndex = 0;
        if (string.IsNullOrEmpty(context)) return false;

        var lastColon = context.LastIndexOf(':');
        if (lastColon < 0) return false;
        if (!int.TryParse(context[(lastColon + 1)..], out buttonIndex)) return false;

        pluginId = context[..lastColon];
        return true;
    }

    private static string? GetPluginIdFromContext(string? context)
    {
        if (string.IsNullOrEmpty(context)) return null;
        var idx = context.LastIndexOf(':');
        if (idx > 0)  return context[..idx];  // "com.rgpaul.vlc:3" → "com.rgpaul.vlc"
        if (idx == 0) return null;             // ":3" — malformed, no pluginId part
        return context;                         // "com.rgpaul.vlc" — already just the pluginId
    }

    private static Dictionary<string, object?>? ParsePayload(object? payload)
    {
        if (payload == null) return null;
        var json = payload is string s ? s : JsonConvert.SerializeObject(payload);
        return JsonConvert.DeserializeObject<Dictionary<string, object?>>(json);
    }

    private static object? DeserializeSettings(string? settings)
        => string.IsNullOrEmpty(settings) ? null : JsonConvert.DeserializeObject(settings);

    private async Task FlashLedAsync(byte buttonIndex, byte r, byte g, byte b, int onMs, int offMs, int times)
    {
        for (int i = 0; i < times; i++)
        {
            await _deviceService.SetLedColorAsync(0, buttonIndex, new LedConfig { R = r, G = g, B = b, Brightness = 100 });
            if (onMs > 0) await Task.Delay(onMs);
            await _deviceService.SetLedColorAsync(0, buttonIndex, new LedConfig { R = 0, G = 0, B = 0, Brightness = 0 });
            if (offMs > 0 && i < times - 1) await Task.Delay(offMs);
        }
    }

    private static string GetActionSettingsPath(string pluginId, string context)
    {
        var safe = string.Concat(context.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MacroKeyboard", "plugins", pluginId, "actions", $"{safe}.json");
    }

    private static string GetGlobalSettingsPath(string pluginId) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MacroKeyboard", "plugins", pluginId, "global.json");

    public void Dispose()
    {
        _webSocketServer.MessageReceived -= OnPluginMessageReceived;
        _webSocketServer.ConnectionClosed -= OnConnectionClosed;
        foreach (var instance in _plugins.Values)
            instance.Dispose();
        _plugins.Clear();
    }
}

// ── Plugin instance base ──────────────────────────────────────────────────────

public abstract class PluginInstance : IDisposable
{
    protected readonly PluginManifest Manifest;
    protected readonly string PluginDirectory;
    protected readonly ILogger Logger;
    protected bool IsRunning;

    protected PluginInstance(PluginManifest manifest, string pluginDirectory, ILogger logger)
    {
        Manifest        = manifest;
        PluginDirectory = pluginDirectory;
        Logger          = logger;
    }

    public abstract Task StartAsync(CancellationToken cancellationToken = default);
    public abstract Task StopAsync(CancellationToken cancellationToken = default);
    public abstract void Dispose();
}

// ── Executable plugin (Node.js, Python, etc.) ─────────────────────────────────

public class ExecutablePluginInstance : PluginInstance
{
    private System.Diagnostics.Process? _process;

    public ExecutablePluginInstance(PluginManifest manifest, string pluginDirectory, ILogger logger)
        : base(manifest, pluginDirectory, logger) { }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            Logger.LogWarning("Plugin {Id} is already running", Manifest.Id);
            return;
        }

        var entryPoint = Manifest.EffectiveEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
            throw new InvalidOperationException($"No entry point for plugin {Manifest.Id}");

        var entryPointPath = Path.Combine(PluginDirectory, entryPoint);

        if (!File.Exists(entryPointPath))
        {
            Logger.LogError("[{Id}] Entry point not found: {Path}", Manifest.Id, entryPointPath);
            throw new FileNotFoundException($"Plugin entry point not found: {entryPointPath}");
        }

        // Build Stream Deck -info JSON
        var infoJson = JsonConvert.SerializeObject(new
        {
            application = new
            {
                font            = "Arial",
                language        = "en",
                platform        = "windows",
                platformVersion = "10.0.0",
                version         = "1.0.0"
            },
            plugin         = new { uuid = Manifest.Id, version = Manifest.Version },
            devicePixelRatio = 1,
            colors           = new { },
            devices          = new[]
            {
                new
                {
                    id   = "MK_DEVICE_0",
                    name = "MacroKeyboard",
                    size = new { columns = 5, rows = 2 },
                    type = 0
                }
            }
        });

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            WorkingDirectory       = PluginDirectory,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        if (string.IsNullOrEmpty(Manifest.Runtime))
        {
            startInfo.FileName = entryPointPath;
        }
        else
        {
            // Interpreted runtime (node, python3, …)
            startInfo.FileName = Manifest.Runtime;
            startInfo.ArgumentList.Add(entryPointPath);
        }

        // Stream Deck standard CLI args
        startInfo.ArgumentList.Add("-port");
        startInfo.ArgumentList.Add("28196");
        startInfo.ArgumentList.Add("-pluginUUID");
        startInfo.ArgumentList.Add(Manifest.Id);
        startInfo.ArgumentList.Add("-registerEvent");
        startInfo.ArgumentList.Add("registerPlugin");
        startInfo.ArgumentList.Add("-info");
        startInfo.ArgumentList.Add(infoJson);

        Logger.LogInformation("[{Id}] Launching: {Exe} -port 28196 -pluginUUID {Uuid} -registerEvent registerPlugin -info <json>",
            Manifest.Id, startInfo.FileName, Manifest.Id);

        _process = new System.Diagnostics.Process
        {
            StartInfo            = startInfo,
            EnableRaisingEvents  = true
        };

        _process.Exited += (_, _) =>
        {
            var code = -1;
            try { code = _process?.ExitCode ?? -1; } catch { }
            IsRunning = false;
            if (code == 0)
                Logger.LogInformation("[{Id}] Process exited cleanly (code 0)", Manifest.Id);
            else
                Logger.LogError("[{Id}] Process exited unexpectedly with code {Code}", Manifest.Id, code);
        };

        try
        {
            _process.Start();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Id}] Failed to start process: {Exe}", Manifest.Id, startInfo.FileName);
            throw;
        }

        // Task-based stream reading captures output even when the process exits immediately.
        // BeginOutputReadLine callbacks may be too late when the process lives < 300ms.
        var stdoutTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await _process.StandardOutput.ReadLineAsync()) != null)
                if (line.Length > 0)
                    Logger.LogInformation("[{Id}:stdout] {Out}", Manifest.Id, line);
        });
        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await _process.StandardError.ReadLineAsync()) != null)
                if (line.Length > 0)
                    Logger.LogWarning("[{Id}:stderr] {Err}", Manifest.Id, line);
        });

        // Brief pause to detect an immediate crash (plugins that refuse to start exit in < 500ms)
        await Task.Delay(600, cancellationToken);
        if (_process.HasExited)
        {
            // Drain any buffered output before logging the failure
            await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(500));
            Logger.LogError("[{Id}] Process exited immediately after launch (code {Code}). Check that the plugin binary is valid and all dependencies are present.",
                Manifest.Id, _process.ExitCode);
            IsRunning = false;
            return;
        }

        IsRunning = true;
        Logger.LogInformation("[{Id}] Plugin process running (PID {Pid})", Manifest.Id, _process.Id);
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _process == null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (Exception ex) { Logger.LogError(ex, "Error stopping plugin {Id}", Manifest.Id); }
        finally
        {
            IsRunning = false;
            _process?.Dispose();
            _process = null;
        }
    }

    public override void Dispose() => StopAsync().GetAwaiter().GetResult();
}

// ── Managed plugin (.NET DLL) ─────────────────────────────────────────────────

public class ManagedPluginInstance : PluginInstance
{
    private AssemblyLoadContext? _loadContext;
    private IPlugin?             _pluginInstance;
    private readonly IDeviceService _deviceService;

    public ManagedPluginInstance(PluginManifest manifest, string pluginDirectory,
        ILogger logger, IDeviceService deviceService)
        : base(manifest, pluginDirectory, logger)
    {
        _deviceService = deviceService;
    }

    public Task OnButtonPressedAsync(string actionId, string? settings, int buttonIndex, CancellationToken ct)
        => _pluginInstance?.OnButtonPressedAsync(actionId, settings, buttonIndex, ct) ?? Task.CompletedTask;

    public Task OnButtonReleasedAsync(string actionId, string? settings, int buttonIndex, CancellationToken ct)
        => _pluginInstance?.OnButtonReleasedAsync(actionId, settings, buttonIndex, ct) ?? Task.CompletedTask;

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        var assemblyPath = Path.Combine(PluginDirectory, Manifest.EntryPoint
            ?? throw new InvalidOperationException($"No EntryPoint for managed plugin {Manifest.Id}"));

        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Assembly not found: {assemblyPath}");

        Logger.LogInformation("Loading managed plugin: {Path}", assemblyPath);

        _loadContext = new AssemblyLoadContext($"Plugin_{Manifest.Id}", isCollectible: true);
        var assembly = _loadContext.LoadFromAssemblyPath(assemblyPath);

        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            ?? throw new InvalidOperationException($"No IPlugin implementation in {assemblyPath}");

        _pluginInstance = Activator.CreateInstance(pluginType) as IPlugin
            ?? throw new InvalidOperationException($"Cannot create {pluginType.FullName}");

        var context = new PluginContext(Manifest.Id, Logger, _deviceService);
        await _pluginInstance.InitializeAsync(context, cancellationToken);
        await _pluginInstance.StartAsync(cancellationToken);

        IsRunning = true;
        Logger.LogInformation("Started managed plugin: {Id}", Manifest.Id);
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _pluginInstance == null) return;
        try
        {
            await _pluginInstance.StopAsync(cancellationToken);
            _pluginInstance.Dispose();
            _pluginInstance = null;
            _loadContext?.Unload();
            _loadContext = null;
            IsRunning = false;
            Logger.LogInformation("Stopped managed plugin: {Id}", Manifest.Id);
        }
        catch (Exception ex) { Logger.LogError(ex, "Error stopping managed plugin {Id}", Manifest.Id); }
    }

    public override void Dispose() => StopAsync().GetAwaiter().GetResult();
}

// ── Plugin context (managed plugin ↔ device bridge) ──────────────────────────

// ── Manifest JSON helper ──────────────────────────────────────────────────────

/// <summary>
/// Deserializes a JSON field that may be either a plain string or an array of strings
/// into a string[]. Handles real-world SD manifests where "Category" is a single string.
/// </summary>
internal sealed class StringOrArrayConverter : JsonConverter<string[]>
{
    public override string[]? ReadJson(JsonReader reader, Type objectType,
        string[]? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.StartArray)
            return serializer.Deserialize<string[]>(reader) ?? Array.Empty<string>();

        if (reader.TokenType == JsonToken.Null)
            return Array.Empty<string>();

        var single = reader.Value?.ToString();
        return single != null ? new[] { single } : Array.Empty<string>();
    }

    public override void WriteJson(JsonWriter writer, string[]? value, JsonSerializer serializer)
        => serializer.Serialize(writer, value);
}

internal class PluginContext : IPluginContext
{
    private readonly ILogger _logger;
    private readonly IDeviceService _deviceService;

    public string PluginId { get; }

    public PluginContext(string pluginId, ILogger logger, IDeviceService deviceService)
    {
        PluginId       = pluginId;
        _logger        = logger;
        _deviceService = deviceService;
    }

    public Task SetButtonImageAsync(int buttonIndex, byte[] imageData, CancellationToken ct = default)
        => _deviceService.SendButtonImageAsync(0, (byte)buttonIndex, imageData, null, ct);

    public Task SetButtonTitleAsync(int buttonIndex, string title, CancellationToken ct = default)
        => _deviceService.SetButtonNameAsync(0, (byte)buttonIndex, title, ct);

    public Task SetLedColorAsync(int buttonIndex, byte r, byte g, byte b, CancellationToken ct = default)
        => _deviceService.SetLedColorAsync(0, (byte)buttonIndex, new LedConfig { R = r, G = g, B = b, Brightness = 100 }, ct);

    public async Task ShowAlertAsync(int buttonIndex, CancellationToken ct = default)
    {
        await _deviceService.SetLedColorAsync(0, (byte)buttonIndex, new LedConfig { R = 255, G = 0, B = 0, Brightness = 100 }, ct);
        await Task.Delay(200, ct);
        await _deviceService.SetLedColorAsync(0, (byte)buttonIndex, new LedConfig { R = 0, G = 0, B = 0, Brightness = 0 }, ct);
    }

    public void LogInfo(string message)    => _logger.LogInformation("[{Id}] {Msg}", PluginId, message);
    public void LogWarning(string message) => _logger.LogWarning("[{Id}] {Msg}", PluginId, message);
    public void LogError(string message, Exception? ex = null)
        => _logger.LogError(ex, "[{Id}] {Msg}", PluginId, message);

    public async Task<T?> GetSettingsAsync<T>(CancellationToken ct = default) where T : class
    {
        var path = SettingsPath();
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex) { _logger.LogError(ex, "[{Id}] Failed to load settings", PluginId); return null; }
    }

    public async Task SaveSettingsAsync<T>(T settings, CancellationToken ct = default) where T : class
    {
        var path = SettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(settings, Formatting.Indented), ct);
    }

    private string SettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MacroKeyboard", "plugins", PluginId, "settings.json");
}
