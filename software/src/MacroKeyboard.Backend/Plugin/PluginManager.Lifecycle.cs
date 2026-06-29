using MacroKeyboard.Backend;
using MacroKeyboard.Shared.Plugin;

namespace MacroKeyboard.Backend.Plugin;

public partial class PluginManager
{
    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task StartPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var instance))
            throw new InvalidOperationException($"Plugin not found: {pluginId}");

        if (instance is ExecutablePluginInstance exe)
            exe.Crashed += (_, _) => _ = HandlePluginCrashedAsync(pluginId);

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
        _pendingWillAppear.Clear();

        await LoadPluginsAsync(cancellationToken);

        foreach (var id in _plugins.Keys)
        {
            try { await StartPluginAsync(id, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error starting plugin {Id} after reload", id); }
        }

        _logger.LogInformation("Plugin reload complete: {Count} plugins loaded", _plugins.Count);
    }

    // ── Button dispatch ───────────────────────────────────────────────────────

    public async Task DispatchButtonPressAsync(
        string pluginId, string actionId, string? settings, int buttonIndex,
        byte folderId = 0xFF, CancellationToken ct = default)
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
            var context = MakeContext(pluginId, buttonIndex, folderId);
            await SendToPluginAsync(pluginId, new PluginMessage
            {
                Event   = "keyDown",
                Action  = actionId,
                Context = context,
                Device  = DeviceId,
                Payload = new
                {
                    settings          = DeserializeSettings(settings),
                    coordinates       = new { column = buttonIndex % DeviceConstants.Columns, row = buttonIndex / DeviceConstants.Columns },
                    state             = _actionStates.TryGetValue(context, out var s) ? s : 0,
                    userDesiredState  = -1,
                    isInMultiAction   = false
                }
            }, ct);
        }
    }

    public async Task DispatchButtonReleaseAsync(
        string pluginId, string actionId, string? settings, int buttonIndex,
        byte folderId = 0xFF, CancellationToken ct = default)
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
            var context = MakeContext(pluginId, buttonIndex, folderId);
            await SendToPluginAsync(pluginId, new PluginMessage
            {
                Event   = "keyUp",
                Action  = actionId,
                Context = context,
                Device  = DeviceId,
                Payload = new
                {
                    settings         = DeserializeSettings(settings),
                    coordinates      = new { column = buttonIndex % DeviceConstants.Columns, row = buttonIndex / DeviceConstants.Columns },
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
        byte folderId = 0xFF, CancellationToken ct = default)
    {
        var context = MakeContext(pluginId, buttonIndex, folderId);
        _contextToActionId[context] = actionId;

        // If plugin is registered, send immediately; otherwise queue for replay on registerPlugin.
        var isConnected = _connectionToPlugin.Any(kvp => kvp.Value == pluginId);
        if (isConnected)
        {
            await SendToPluginAsync(pluginId, BuildWillAppearMessage(actionId, settings, buttonIndex, context), ct);
        }
        else
        {
            _logger.LogInformation("[{PluginId}] willAppear queued for button {Idx} folder={Folder} (plugin not connected yet)",
                pluginId, buttonIndex, folderId == 0xFF ? "root" : $"F{folderId}");
            _pendingWillAppear
                .GetOrAdd(pluginId, _ => new System.Collections.Concurrent.ConcurrentQueue<PendingWillAppear>())
                .Enqueue(new PendingWillAppear(actionId, settings, buttonIndex, folderId));
        }
    }

    private async Task HandlePluginCrashedAsync(string pluginId)
    {
        _logger.LogError("[{PluginId}] Plugin process crashed — showing error ring on all plugin buttons", pluginId);

        // Remove stale WS registration so future messages aren't sent to a dead connection
        var staleConns = _connectionToPlugin
            .Where(kvp => kvp.Value == pluginId)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var conn in staleConns)
            _connectionToPlugin.TryRemove(conn, out _);

        // Show red error ring on every known button for this plugin
        foreach (var (context, _) in _contextToActionId.ToList())
        {
            if (!TryParseButtonContext(context, out var ctxPluginId, out var buttonIndex)) continue;
            if (ctxPluginId != pluginId) continue;
            try
            {
                var errorImage = await _imageService.CreateErrorRingAsync("PLUGIN\nCRASHED");
                await _deviceService.SendButtonImageAsync(0, (byte)buttonIndex, errorImage, null, default, noStore: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{PluginId}] Failed to show error ring for button {Idx}", pluginId, buttonIndex);
            }
        }
    }

    public async Task NotifyWillDisappearAsync(
        string pluginId, string actionId, string? settings, int buttonIndex,
        byte folderId = 0xFF, CancellationToken ct = default)
    {
        var context = MakeContext(pluginId, buttonIndex, folderId);
        await SendToPluginAsync(pluginId, new PluginMessage
        {
            Event   = "willDisappear",
            Action  = actionId,
            Context = context,
            Device  = DeviceId,
            Payload = new
            {
                settings        = DeserializeSettings(settings),
                coordinates     = new { column = buttonIndex % DeviceConstants.Columns, row = buttonIndex / DeviceConstants.Columns },
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
                    size    = new { columns = DeviceConstants.Columns, rows = DeviceConstants.Rows }
                }
            }
        }, ct);

    public Task NotifyDeviceDisconnectedAsync(CancellationToken ct = default)
        => _webSocketServer.BroadcastAsync(new PluginMessage { Event = "deviceDidDisconnect", Device = DeviceId }, ct);
}
