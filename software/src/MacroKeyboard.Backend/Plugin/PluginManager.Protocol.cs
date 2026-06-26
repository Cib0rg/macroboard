using MacroKeyboard.Backend;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Services;
using MacroKeyboard.Shared.Plugin;
using Newtonsoft.Json;

namespace MacroKeyboard.Backend.Plugin;

public partial class PluginManager
{
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
            case "setTitle":              await HandleSetTitleAsync(msg); break;
            case "setImage":              await HandleSetImageAsync(msg); break;
            case "mkSetButtonDisplay":    await HandleSetButtonDisplayAsync(msg); break;
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
                    size = new { columns = DeviceConstants.Columns, rows = DeviceConstants.Rows }
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
            if (processed == null)
            {
                _logger.LogWarning("setImage: ProcessImageBytesForButtonAsync returned null for button {Idx} ({Bytes} raw bytes)", buttonIndex, rawBytes.Length);
                return;
            }
            await _deviceService.SendButtonImageAsync(0, (byte)buttonIndex, processed, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "setImage: failed to process/send image for button {Idx}", buttonIndex);
        }
    }

    private async Task HandleSetButtonDisplayAsync(PluginMessage msg)
    {
        if (!TryParseButtonContext(msg.Context, out _, out var buttonIndex)) return;
        var payload = ParsePayload(msg.Payload);
        if (payload == null) return;

        var text = payload.GetValueOrDefault("text")?.ToString() ?? string.Empty;

        try
        {
            var imageBytes = await _imageService.CreatePluginStateImageAsync(text);
            await _deviceService.SendButtonImageAsync(0, (byte)buttonIndex, imageBytes, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mkSetButtonDisplay: failed to generate/send image for button {Idx}", buttonIndex);
        }
    }

    private async Task HandleShowAlertAsync(PluginMessage msg)
    {
        if (!TryParseButtonContext(msg.Context, out _, out var buttonIndex)) return;
        await FlashLedAsync((byte)buttonIndex, r: 255, g: 165, b: 0, onMs: 200, offMs: 100, times: 2);
    }

    private async Task HandleShowOkAsync(PluginMessage msg)
    {
        if (!TryParseButtonContext(msg.Context, out _, out var buttonIndex)) return;
        // Flash green, then restore whatever LED color the profile had set
        var prev = await _deviceService.GetLedColorAsync(0, (byte)buttonIndex);
        await _deviceService.SetLedColorAsync(0, (byte)buttonIndex, new LedConfig { R = 0, G = 220, B = 0, Brightness = 100 });
        await Task.Delay(350);
        await _deviceService.SetLedColorAsync(0, (byte)buttonIndex,
            prev ?? new LedConfig { R = 0, G = 0, B = 0, Brightness = 0 });
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
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to read action settings: {Path}", actionPath); }
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
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to read global settings: {Path}", globalPath); }
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
}
