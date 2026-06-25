using MacroKeyboard.Backend;
using MacroKeyboard.Backend.Plugin;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Services;
using MacroKeyboard.Shared.IPC;
using MacroKeyboard.Shared.Plugin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MacroKeyboard.Backend.Services;

/// <summary>
/// Handles IPC commands from UI clients.
/// Routes messages to the appropriate device/profile services and sends responses.
/// </summary>
public class IpcCommandHandler
{
    private readonly IDeviceService _deviceService;
    private readonly ProfileService _profileService;
    private readonly IIpcServer _ipcServer;
    private readonly PluginManager _pluginManager;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<IpcCommandHandler> _logger;

    public IpcCommandHandler(
        IDeviceService deviceService,
        ProfileService profileService,
        IIpcServer ipcServer,
        PluginManager pluginManager,
        IHostApplicationLifetime lifetime,
        ILogger<IpcCommandHandler> logger)
    {
        _deviceService = deviceService;
        _profileService = profileService;
        _ipcServer = ipcServer;
        _pluginManager = pluginManager;
        _lifetime = lifetime;
        _logger = logger;

        // Subscribe to incoming IPC messages
        _ipcServer.MessageReceived += (s, msg) => OnMessageReceived(s, msg).FireAndForget(_logger);
    }

    private async Task OnMessageReceived(object? sender, IpcMessage message)
    {
        try
        {
            _logger.LogDebug("Processing IPC command: {MessageType}", message.MessageType);

            IpcResponse response = message.MessageType switch
            {
                IpcMessageTypes.GetDeviceInfo => await HandleGetDeviceInfo(message),
                IpcMessageTypes.Ping => HandlePing(message),
                IpcMessageTypes.GetProfileList => await HandleGetProfileList(message),
                IpcMessageTypes.ProfileSave => await HandleProfileSave(message),
                IpcMessageTypes.ProfileLoad => await HandleProfileLoad(message),
                IpcMessageTypes.ProfileDelete => await HandleProfileDelete(message),
                IpcMessageTypes.ProfileSendToDevice => await HandleProfileSendToDevice(message),
                IpcMessageTypes.ProfileLoadFromDevice => await HandleProfileLoadFromDevice(message),
                IpcMessageTypes.ProfileGetInfo => await HandleProfileGetInfo(message),
                IpcMessageTypes.SetButtonAction => await HandleSetButtonAction(message),
                IpcMessageTypes.GetButtonAction => await HandleGetButtonAction(message),
                IpcMessageTypes.SetButtonName   => await HandleSetButtonName(message),
                IpcMessageTypes.SetButtonImage  => await HandleSetButtonImage(message),
                IpcMessageTypes.ClearButtonImage => await HandleClearButtonImage(message),
                IpcMessageTypes.SetLedColor => await HandleSetLedColor(message),
                IpcMessageTypes.GetLedColor => await HandleGetLedColor(message),
                IpcMessageTypes.SetDisplayBrightness => await HandleSetDisplayBrightness(message),
                IpcMessageTypes.PluginList => HandlePluginList(message),
                IpcMessageTypes.PluginGetSettings => await HandlePluginGetSettings(message),
                IpcMessageTypes.PluginReload => await HandlePluginReload(message),
                IpcMessageTypes.SystemRestart => HandleSystemRestart(message),
                _ => HandleUnknownCommand(message)
            };

            await _ipcServer.SendToClientAsync(message.SourceClientId!, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling IPC command: {MessageType}", message.MessageType);

            try
            {
                var errorResponse = IpcResponse.Fail(message, ex.Message);
                await _ipcServer.SendToClientAsync(message.SourceClientId!, errorResponse);
            }
            catch (Exception broadcastEx)
            {
                _logger.LogError(broadcastEx, "Error sending error response");
            }
        }
    }

    private async Task<IpcResponse> HandleGetDeviceInfo(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
        {
            return IpcResponse.Fail(message, "Device not connected");
        }

        var deviceInfo = await _deviceService.GetDeviceInfoAsync();
        return IpcResponse.Ok(message, deviceInfo);
    }

    private IpcResponse HandlePing(IpcMessage message)
    {
        return IpcResponse.Ok(message, new
        {
            DeviceConnected = _deviceService.IsConnected,
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task<IpcResponse> HandleGetProfileList(IpcMessage message)
    {
        var profiles = await _profileService.GetAllProfilesAsync();
        return IpcResponse.Ok(message, profiles);
    }

    private async Task<IpcResponse> HandleProfileSave(IpcMessage message)
    {
        var profile = message.GetData<Profile>();
        if (profile == null)
        {
            return IpcResponse.Fail(message, "Invalid profile data");
        }

        var success = await _profileService.UpdateProfileAsync(profile);
        return success 
            ? IpcResponse.Ok(message) 
            : IpcResponse.Fail(message, "Failed to save profile");
    }

    private async Task<IpcResponse> HandleProfileLoad(IpcMessage message)
    {
        var data = message.GetDataAsDictionary();
        if (data == null || !data.ContainsKey("profileId"))
        {
            return IpcResponse.Fail(message, "Missing profileId");
        }

        var profileId = Convert.ToByte(data["profileId"]);
        var profile = await _profileService.GetProfileAsync(profileId);
        
        return profile != null 
            ? IpcResponse.Ok(message, profile) 
            : IpcResponse.Fail(message, $"Profile {profileId} not found");
    }

    private async Task<IpcResponse> HandleProfileDelete(IpcMessage message)
    {
        var data = message.GetDataAsDictionary();
        if (data == null || !data.ContainsKey("profileId"))
        {
            return IpcResponse.Fail(message, "Missing profileId");
        }

        var profileId = Convert.ToByte(data["profileId"]);

        // Delete from local storage only — device holds a single slot and is never deleted remotely
        var localSuccess = await _profileService.DeleteProfileAsync(profileId);

        return localSuccess
            ? IpcResponse.Ok(message)
            : IpcResponse.Fail(message, $"Failed to delete profile {profileId}");
    }

    private async Task<IpcResponse> HandleProfileSendToDevice(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
        {
            return IpcResponse.Fail(message, "Device not connected");
        }

        var profile = message.GetData<Profile>();
        if (profile == null)
        {
            // Try to get profileId and load from repository
            var data = message.GetDataAsDictionary();
            if (data != null && data.ContainsKey("profileId"))
            {
                var profileId = Convert.ToByte(data["profileId"]);
                profile = await _profileService.GetProfileAsync(profileId);
            }
        }

        if (profile == null)
        {
            return IpcResponse.Fail(message, "Invalid profile data or profileId");
        }

        _logger.LogInformation("Sending profile {ProfileId} ({Name}) to device", 
            profile.ProfileId, profile.Name);

        var success = await _profileService.SendProfileToDeviceAsync(profile);

        if (success)
        {
            // Notify plugins of willAppear for each plugin-action button.
            // Fire-and-forget with a small delay so the device can settle and
            // the plugin can react to the new button layout without blocking the response.
            var buttonsSnapshot = profile.Buttons.ToList();
            _ = Task.Run(async () =>
            {
                await Task.Delay(800);
                foreach (var button in buttonsSnapshot)
                {
                    if (button.Action is not PluginActionConfig pa) continue;
                    try
                    {
                        await _pluginManager.NotifyWillAppearAsync(
                            pa.PluginId, pa.ActionId, pa.Settings, button.ButtonId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "willAppear dispatch failed for button {Id}", button.ButtonId);
                    }
                }
            });
        }

        return success
            ? IpcResponse.Ok(message, new { ProfileId = profile.ProfileId, ProfileName = profile.Name })
            : IpcResponse.Fail(message, "Failed to send profile to device");
    }

    private async Task<IpcResponse> HandleProfileLoadFromDevice(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
        {
            return IpcResponse.Fail(message, "Device not connected");
        }

        _logger.LogInformation("Loading profile from device (slot 0)");

        var profile = await _profileService.LoadProfileFromDeviceAsync(0);

        return profile != null
            ? IpcResponse.Ok(message, profile)
            : IpcResponse.Fail(message, "Failed to load profile from device");
    }

    private async Task<IpcResponse> HandleProfileGetInfo(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
        {
            return IpcResponse.Fail(message, "Device not connected");
        }

        var profileInfo = await _deviceService.GetProfileInfoAsync(0);

        return profileInfo != null
            ? IpcResponse.Ok(message, profileInfo)
            : IpcResponse.Fail(message, "Failed to get profile info from device");
    }

    private async Task<IpcResponse> HandleSetButtonAction(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
        {
            return IpcResponse.Fail(message, "Device not connected");
        }

        var data = message.GetDataAsDictionary();
        if (data == null)
        {
            return IpcResponse.Fail(message, "Invalid data");
        }

        var profileId = Convert.ToByte(data.GetValueOrDefault("profileId", (byte)0));
        var buttonId = Convert.ToByte(data.GetValueOrDefault("buttonId", (byte)0));
        
        // Parse action from JToken
        ActionConfig? action = null;
        if (data.ContainsKey("action") && data["action"] is JObject actionObj)
        {
            var actionType = actionObj.Value<int>("ActionType");
            action = (ActionType)actionType switch
            {
                ActionType.Keyboard => actionObj.ToObject<KeyboardAction>(),
                ActionType.ProfileSwitch => actionObj.ToObject<ProfileSwitchAction>(),
                ActionType.CustomHid => actionObj.ToObject<CustomHidAction>(),
                ActionType.Folder => actionObj.ToObject<FolderAction>(),
                ActionType.Delay => actionObj.ToObject<DelayAction>(),
                ActionType.Shell => actionObj.ToObject<ShellAction>(),
                ActionType.Sequence => actionObj.ToObject<SequenceAction>(),
                ActionType.Media => actionObj.ToObject<MediaAction>(),
                ActionType.LaunchApp => actionObj.ToObject<LaunchAppAction>(),
                ActionType.NightMode => new NightModeAction(),
                ActionType.Plugin => actionObj.ToObject<PluginActionConfig>(),
                _ => null
            };
        }

        if (action == null)
        {
            return IpcResponse.Fail(message, "Invalid action data");
        }

        // TEXT_ACTION_SHORT = 0x00; folderId 0xFF = root buttons
        var success = await _deviceService.SendActionAsync(profileId, 0xFF, buttonId, 0x00, action);

        return success
            ? IpcResponse.Ok(message)
            : IpcResponse.Fail(message, "Failed to set button action on device");
    }

    private async Task<IpcResponse> HandleSetButtonName(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
            return IpcResponse.Fail(message, "Device not connected");

        var data = message.GetDataAsDictionary();
        if (data == null)
            return IpcResponse.Fail(message, "Invalid data");

        var profileId = Convert.ToByte(data.GetValueOrDefault("profileId", (byte)0));
        var buttonId  = Convert.ToByte(data.GetValueOrDefault("buttonId",  (byte)0));
        var name      = data.GetValueOrDefault("name", null)?.ToString();

        var success = await _deviceService.SetButtonNameAsync(profileId, buttonId, name);
        return success
            ? IpcResponse.Ok(message)
            : IpcResponse.Fail(message, "Failed to set button name on device");
    }

    private async Task<IpcResponse> HandleSetButtonImage(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
            return IpcResponse.Fail(message, "Device not connected");

        var data = message.GetDataAsDictionary();
        if (data == null)
            return IpcResponse.Fail(message, "Invalid data");

        var profileId  = Convert.ToByte(data.GetValueOrDefault("profileId",  (byte)0));
        var buttonId   = Convert.ToByte(data.GetValueOrDefault("buttonId",   (byte)0));
        var base64Data = data.GetValueOrDefault("imageData", null)?.ToString();

        if (string.IsNullOrEmpty(base64Data))
            return IpcResponse.Fail(message, "No image data");

        byte[] imageBytes;
        try { imageBytes = Convert.FromBase64String(base64Data); }
        catch { return IpcResponse.Fail(message, "Invalid base64 image data"); }

        var success = await _deviceService.SendButtonImageAsync(profileId, buttonId, imageBytes, null);
        return success
            ? IpcResponse.Ok(message)
            : IpcResponse.Fail(message, "Failed to send button image to device");
    }

    private async Task<IpcResponse> HandleClearButtonImage(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
            return IpcResponse.Fail(message, "Device not connected");

        var data = message.GetDataAsDictionary();
        if (data == null)
            return IpcResponse.Fail(message, "Invalid data");

        var profileId = Convert.ToByte(data.GetValueOrDefault("profileId", (byte)0));
        var buttonId  = Convert.ToByte(data.GetValueOrDefault("buttonId",  (byte)0));

        var success = await _deviceService.ClearButtonImageAsync(profileId, buttonId);
        return success
            ? IpcResponse.Ok(message)
            : IpcResponse.Fail(message, "Failed to clear button image on device");
    }

    private async Task<IpcResponse> HandleGetButtonAction(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
        {
            return IpcResponse.Fail(message, "Device not connected");
        }

        var data = message.GetDataAsDictionary();
        if (data == null)
        {
            return IpcResponse.Fail(message, "Invalid data");
        }

        var profileId = Convert.ToByte(data.GetValueOrDefault("profileId", (byte)0));
        var buttonId = Convert.ToByte(data.GetValueOrDefault("buttonId", (byte)0));

        var action = await _deviceService.GetButtonActionAsync(profileId, buttonId);
        
        return IpcResponse.Ok(message, action);
    }

    private async Task<IpcResponse> HandleSetLedColor(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
        {
            return IpcResponse.Fail(message, "Device not connected");
        }

        var data = message.GetDataAsDictionary();
        if (data == null)
        {
            return IpcResponse.Fail(message, "Invalid data");
        }

        var profileId = Convert.ToByte(data.GetValueOrDefault("profileId", (byte)0));
        var buttonId = Convert.ToByte(data.GetValueOrDefault("buttonId", (byte)0));
        
        LedConfig? led = null;
        if (data.ContainsKey("led") && data["led"] is JObject ledObj)
        {
            led = ledObj.ToObject<LedConfig>();
        }

        if (led == null)
        {
            return IpcResponse.Fail(message, "Invalid LED data");
        }

        var success = await _deviceService.SetLedColorAsync(profileId, buttonId, led);
        
        return success 
            ? IpcResponse.Ok(message) 
            : IpcResponse.Fail(message, "Failed to set LED color on device");
    }

    private async Task<IpcResponse> HandleGetLedColor(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
        {
            return IpcResponse.Fail(message, "Device not connected");
        }

        var data = message.GetDataAsDictionary();
        if (data == null)
        {
            return IpcResponse.Fail(message, "Invalid data");
        }

        var profileId = Convert.ToByte(data.GetValueOrDefault("profileId", (byte)0));
        var buttonId = Convert.ToByte(data.GetValueOrDefault("buttonId", (byte)0));

        var led = await _deviceService.GetLedColorAsync(profileId, buttonId);
        
        return IpcResponse.Ok(message, led);
    }

    private async Task<IpcResponse> HandleSetDisplayBrightness(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
        {
            return IpcResponse.Fail(message, "Device not connected");
        }

        var data = message.GetDataAsDictionary();
        if (data == null || !data.ContainsKey("brightness"))
        {
            return IpcResponse.Fail(message, "Missing brightness value");
        }

        var brightness = Convert.ToByte(data["brightness"]);
        
        var actualBrightness = await _deviceService.SetDisplayBrightnessAsync(brightness);
        
        return actualBrightness.HasValue
            ? IpcResponse.Ok(message, new { Brightness = actualBrightness.Value })
            : IpcResponse.Fail(message, "Failed to set display brightness");
    }

    private async Task<IpcResponse> HandlePluginGetSettings(IpcMessage message)
    {
        var data = message.GetDataAsDictionary();
        if (data == null) return IpcResponse.Fail(message, "Invalid data");

        var pluginId    = data.GetValueOrDefault("pluginId",    null)?.ToString();
        var buttonIndex = Convert.ToInt32(data.GetValueOrDefault("buttonIndex", 0));

        if (string.IsNullOrEmpty(pluginId))
            return IpcResponse.Fail(message, "Missing pluginId");

        var settings = await _pluginManager.GetActionSettingsAsync(pluginId, buttonIndex);
        return IpcResponse.Ok(message, settings);
    }

    private IpcResponse HandlePluginList(IpcMessage message)
    {
        var actions = _pluginManager.GetLoadedActions()
            .Select(x =>
            {
                var dir = _pluginManager.GetPluginDirectory(x.PluginId);
                return new PluginActionInfo
                {
                    PluginId   = x.PluginId,
                    PluginName = x.PluginName,
                    ActionId   = x.Action.EffectiveId,
                    ActionName = x.Action.Name,
                    Icon       = x.Action.Icon,
                    Tooltip    = x.Action.Tooltip,
                    IconPath   = ResolveIconPath(dir, x.Action.Icon),
                    PropertyInspectorUrl = BuildPiUrl(x.PluginId,
                        x.Action.EffectivePropertyInspectorPath ?? x.ManifestPiPath)
                };
            })
            .ToList();

        return IpcResponse.Ok(message, actions);
    }

    private static string? BuildPiUrl(string pluginId, string? piPath)
    {
        if (string.IsNullOrEmpty(piPath)) return null;
        return $"http://localhost:8787/plugins/{pluginId}/{piPath.TrimStart('/')}";
    }

    private static string? ResolveIconPath(string? pluginDir, string? iconRelative)
    {
        if (string.IsNullOrEmpty(pluginDir) || string.IsNullOrEmpty(iconRelative)) return null;

        // SD manifests use paths without extension; try common variants
        foreach (var suffix in new[] { ".png", "@2x.png", ".svg", "" })
        {
            var candidate = Path.GetFullPath(Path.Combine(pluginDir, iconRelative + suffix));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private IpcResponse HandleSystemRestart(IpcMessage message)
    {
        _logger.LogInformation("Restart requested via IPC — shutting down in 500ms");
        _ = Task.Delay(500).ContinueWith(_ => _lifetime.StopApplication());
        return IpcResponse.Ok(message);
    }

    private async Task<IpcResponse> HandlePluginReload(IpcMessage message)
    {
        try
        {
            _logger.LogInformation("Plugin reload requested via IPC");
            await _pluginManager.ReloadPluginsAsync();
            var count = _pluginManager.GetPlugins().Count();
            _logger.LogInformation("Plugin reload complete: {Count} plugins", count);
            return IpcResponse.Ok(message, new { pluginCount = count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading plugins");
            return IpcResponse.Fail(message, ex.Message);
        }
    }

    private IpcResponse HandleUnknownCommand(IpcMessage message)
    {
        _logger.LogWarning("Unknown IPC command: {MessageType}", message.MessageType);
        return IpcResponse.Fail(message, $"Unknown command: {message.MessageType}");
    }
}
