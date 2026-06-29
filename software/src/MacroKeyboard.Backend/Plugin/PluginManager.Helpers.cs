using MacroKeyboard.Core.Models;
using MacroKeyboard.Shared.Plugin;
using Newtonsoft.Json;

namespace MacroKeyboard.Backend.Plugin;

public partial class PluginManager
{
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
            // Plugin not connected — drop the message. willAppear is queued separately
            // in NotifyWillAppearAsync and replayed when the plugin sends registerPlugin.
            _logger.LogDebug("WS→plugin [{PluginId}] event={Event}: plugin not connected, dropping",
                pluginId, msg.Event);
        }
    }

    private PluginMessage BuildWillAppearMessage(string actionId, string? settings, int buttonIndex, string context)
        => new()
        {
            Event   = "willAppear",
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
        };

    // Context format:
    //   Root button:   "pluginId:buttonIndex"          e.g. "com.ha.plugin:3"
    //   Folder button: "pluginId:F{folderId}:buttonIndex"  e.g. "com.ha.plugin:F2:3"
    private static string MakeContext(string pluginId, int buttonIndex, byte folderId = 0xFF)
        => folderId == 0xFF
            ? $"{pluginId}:{buttonIndex}"
            : $"{pluginId}:F{folderId}:{buttonIndex}";

    // Backward-compat overload — callers that don't need folderId.
    private static bool TryParseButtonContext(string? context, out string pluginId, out int buttonIndex)
        => TryParseButtonContext(context, out pluginId, out buttonIndex, out _);

    private static bool TryParseButtonContext(string? context, out string pluginId, out int buttonIndex, out byte folderId)
    {
        pluginId    = string.Empty;
        buttonIndex = 0;
        folderId    = 0xFF;
        if (string.IsNullOrEmpty(context)) return false;

        var lastColon = context.LastIndexOf(':');
        if (lastColon < 0) return false;
        if (!int.TryParse(context[(lastColon + 1)..], out buttonIndex)) return false;

        // prefix is everything before the last colon — either "pluginId" or "pluginId:F{N}"
        var prefix = context[..lastColon];
        var folderColon = prefix.LastIndexOf(':');
        if (folderColon >= 0)
        {
            var segment = prefix[(folderColon + 1)..];
            if (segment.Length > 1 && segment[0] == 'F' && byte.TryParse(segment[1..], out var fid))
            {
                folderId = fid;
                pluginId = prefix[..folderColon];
                return true;
            }
        }

        pluginId = prefix;
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
}
