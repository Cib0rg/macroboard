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
}
