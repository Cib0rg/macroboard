using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Shared.IPC;
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
    private readonly IProfileService _profileService;
    private readonly IIpcServer _ipcServer;
    private readonly ILogger<IpcCommandHandler> _logger;

    public IpcCommandHandler(
        IDeviceService deviceService,
        IProfileService profileService,
        IIpcServer ipcServer,
        ILogger<IpcCommandHandler> logger)
    {
        _deviceService = deviceService;
        _profileService = profileService;
        _ipcServer = ipcServer;
        _logger = logger;

        // Subscribe to incoming IPC messages
        _ipcServer.MessageReceived += OnMessageReceived;
    }

    private async void OnMessageReceived(object? sender, IpcMessage message)
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
                IpcMessageTypes.SetLedColor => await HandleSetLedColor(message),
                IpcMessageTypes.GetLedColor => await HandleGetLedColor(message),
                IpcMessageTypes.SetDisplayBrightness => await HandleSetDisplayBrightness(message),
                _ => HandleUnknownCommand(message)
            };

            // Broadcast response to all clients (the RequestId will match the sender's request)
            await _ipcServer.BroadcastAsync(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling IPC command: {MessageType}", message.MessageType);
            
            try
            {
                var errorResponse = IpcResponse.Fail(message, ex.Message);
                await _ipcServer.BroadcastAsync(errorResponse);
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
        var success = await _profileService.DeleteProfileAsync(profileId);
        
        return success 
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

        var data = message.GetDataAsDictionary();
        if (data == null || !data.ContainsKey("profileId"))
        {
            return IpcResponse.Fail(message, "Missing profileId");
        }

        var profileId = Convert.ToByte(data["profileId"]);
        
        _logger.LogInformation("Loading profile {ProfileId} from device", profileId);
        
        var profile = await _profileService.LoadProfileFromDeviceAsync(profileId);
        
        return profile != null 
            ? IpcResponse.Ok(message, profile) 
            : IpcResponse.Fail(message, $"Failed to load profile {profileId} from device");
    }

    private async Task<IpcResponse> HandleProfileGetInfo(IpcMessage message)
    {
        if (!_deviceService.IsConnected)
        {
            return IpcResponse.Fail(message, "Device not connected");
        }

        var data = message.GetDataAsDictionary();
        if (data == null || !data.ContainsKey("profileId"))
        {
            return IpcResponse.Fail(message, "Missing profileId");
        }

        var profileId = Convert.ToByte(data["profileId"]);
        
        var profileInfo = await _deviceService.GetProfileInfoAsync(profileId);
        
        return profileInfo != null
            ? IpcResponse.Ok(message, profileInfo)
            : IpcResponse.Fail(message, $"Failed to get info for profile {profileId}");
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
                _ => null
            };
        }

        if (action == null)
        {
            return IpcResponse.Fail(message, "Invalid action data");
        }

        var success = await _deviceService.SetButtonActionAsync(profileId, buttonId, action);
        
        return success 
            ? IpcResponse.Ok(message) 
            : IpcResponse.Fail(message, "Failed to set button action on device");
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

    private IpcResponse HandleUnknownCommand(IpcMessage message)
    {
        _logger.LogWarning("Unknown IPC command: {MessageType}", message.MessageType);
        return IpcResponse.Fail(message, $"Unknown command: {message.MessageType}");
    }
}
