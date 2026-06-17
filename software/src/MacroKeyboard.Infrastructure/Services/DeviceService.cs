using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Core.Utilities;
using MacroKeyboard.Communication.HidDevice;
using MacroKeyboard.Communication.Commands;
using MacroKeyboard.Communication.Protocol;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Infrastructure.Services;

/// <summary>
/// Реализация сервиса для работы с устройством
/// </summary>
public class DeviceService : IDeviceService
{
    private readonly HidDeviceManager _deviceManager;
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<DeviceService> _logger;
    
    // Команды
    private readonly PingCommand _pingCommand;
    private readonly GetDeviceInfoCommand _getDeviceInfoCommand;
    private readonly SetProfileCommand _setProfileCommand;
    private readonly ImageTransferCommand _imageTransferCommand;
    private readonly SetButtonActionCommand _setButtonActionCommand;
    private readonly SetButtonNameCommand _setButtonNameCommand;
    private readonly SetLedColorCommand _setLedColorCommand;
    private readonly GetButtonActionCommand _getButtonActionCommand;
    private readonly GetLedColorCommand _getLedColorCommand;
    private readonly SetDisplayBrightnessCommand _setDisplayBrightnessCommand;
    private readonly SetFolderButtonActionCommand _setFolderButtonActionCommand;
    private readonly SetFolderButtonNameCommand _setFolderButtonNameCommand;
    private readonly SetFolderButtonLedCommand _setFolderButtonLedCommand;
    private readonly SetEncoderActionCommand _setEncoderActionCommand;
    private readonly SetButtonLongPressActionCommand _setButtonLongPressActionCommand;
    private readonly SetButtonLongPressNameCommand _setButtonLongPressNameCommand;
    private readonly GetImageHashesCommand _getImageHashesCommand;

    // CRC32 cache: (profileId, buttonId) → last-confirmed CRC on device
    private readonly Dictionary<(byte, byte), uint> _imageHashCache = new();

    public event EventHandler<DeviceEventArgs>? DeviceConnected;
    public event EventHandler<DeviceEventArgs>? DeviceDisconnected;
    public event EventHandler<ButtonEventArgs>? ButtonPressed;
    public event EventHandler<ButtonEventArgs>? ButtonReleased;
    public event EventHandler<EncoderEventArgs>? EncoderRotated;
    public event EventHandler<ProfileChangedEventArgs>? ProfileChanged;
    public event EventHandler<FolderEventArgs>? FolderEntered;
    public event EventHandler<FolderEventArgs>? FolderExited;
    
    public bool IsConnected => _deviceManager.IsConnected;
    
    public DeviceService(
        HidDeviceManager deviceManager,
        ILogger<DeviceService> logger,
        ILoggerFactory loggerFactory)
    {
        _deviceManager = deviceManager;
        _logger = logger;
        
        _protocol = new ProtocolHandler(deviceManager, loggerFactory.CreateLogger<ProtocolHandler>());
        
        // Создать команды
        _pingCommand = new PingCommand(_protocol, loggerFactory.CreateLogger<PingCommand>());
        _getDeviceInfoCommand = new GetDeviceInfoCommand(_protocol, loggerFactory.CreateLogger<GetDeviceInfoCommand>());
        _setProfileCommand = new SetProfileCommand(_protocol, loggerFactory.CreateLogger<SetProfileCommand>());
        _imageTransferCommand = new ImageTransferCommand(_protocol, loggerFactory.CreateLogger<ImageTransferCommand>());
        _setButtonActionCommand = new SetButtonActionCommand(_protocol, loggerFactory.CreateLogger<SetButtonActionCommand>());
        _setButtonNameCommand = new SetButtonNameCommand(_protocol, loggerFactory.CreateLogger<SetButtonNameCommand>());
        _setLedColorCommand = new SetLedColorCommand(_protocol, loggerFactory.CreateLogger<SetLedColorCommand>());
        _getButtonActionCommand = new GetButtonActionCommand(_protocol, loggerFactory.CreateLogger<GetButtonActionCommand>());
        _getLedColorCommand = new GetLedColorCommand(_protocol, loggerFactory.CreateLogger<GetLedColorCommand>());
        _setDisplayBrightnessCommand = new SetDisplayBrightnessCommand(_protocol, loggerFactory.CreateLogger<SetDisplayBrightnessCommand>());
        _setFolderButtonActionCommand = new SetFolderButtonActionCommand(_protocol, loggerFactory.CreateLogger<SetFolderButtonActionCommand>());
        _setFolderButtonNameCommand   = new SetFolderButtonNameCommand(_protocol, loggerFactory.CreateLogger<SetFolderButtonNameCommand>());
        _setFolderButtonLedCommand    = new SetFolderButtonLedCommand(_protocol, loggerFactory.CreateLogger<SetFolderButtonLedCommand>());
        _setEncoderActionCommand           = new SetEncoderActionCommand(_protocol, loggerFactory.CreateLogger<SetEncoderActionCommand>());
        _setButtonLongPressActionCommand   = new SetButtonLongPressActionCommand(_protocol, loggerFactory.CreateLogger<SetButtonLongPressActionCommand>());
        _setButtonLongPressNameCommand     = new SetButtonLongPressNameCommand(_protocol, loggerFactory.CreateLogger<SetButtonLongPressNameCommand>());
        _getImageHashesCommand             = new GetImageHashesCommand(_protocol, loggerFactory.CreateLogger<GetImageHashesCommand>());
        
        // Подписаться на события устройства
        _deviceManager.DeviceConnected += OnDeviceConnected;
        _deviceManager.DeviceDisconnected += OnDeviceDisconnected;
        _deviceManager.DataReceived += OnDataReceived;
    }
    
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to device...");
        return await _deviceManager.ConnectAsync();
    }
    
    public void Disconnect()
    {
        _logger.LogInformation("Disconnecting from device...");
        _deviceManager.Disconnect();
    }
    
    public async Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        var info = await _getDeviceInfoCommand.ExecuteAsync(cancellationToken);
        return info ?? new DeviceInfo { IsConnected = false };
    }
    
    public async Task<(byte FolderId, byte Depth)> GetFolderStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_GET_FOLDER_STATE,
                Array.Empty<byte>(),
                cancellationToken: cancellationToken);

            if (response != null && response.Payload.Length >= 3 && response.Payload[0] == ProtocolConstants.STATUS_OK)
                return (response.Payload[1], response.Payload[2]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetFolderState failed, assuming root");
        }
        return (0xFF, 0);
    }

    public async Task<ProfileInfoResult?> GetProfileInfoAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new byte[] { profileId };
            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_GET_PROFILE_INFO,
                payload,
                cancellationToken: cancellationToken);
            
            if (response == null)
                return null;
            
            // Parse response: byte 0 = profile_id, bytes 1-32 = name, byte 33 = is_configured
            var name = System.Text.Encoding.UTF8.GetString(response.Payload, 1, 32).TrimEnd('\0');
            var isConfigured = response.Payload[33] != 0;
            
            return new ProfileInfoResult
            {
                ProfileId = response.Payload[0],
                Name = name,
                IsConfigured = isConfigured
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting profile info for profile {ProfileId}", profileId);
            return null;
        }
    }
    
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        var response = await _pingCommand.ExecuteAsync(cancellationToken);
        return response != null;
    }
    
    public async Task<bool> SetProfileAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        return await _setProfileCommand.ExecuteAsync(profileId, cancellationToken);
    }
    
    public async Task<bool> SendButtonImageAsync(
        byte profileId,
        byte buttonId,
        byte[] imageData,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var crc = Crc32.Calculate(imageData);
        if (_imageHashCache.TryGetValue((profileId, buttonId), out var cached) && cached == crc)
        {
            _logger.LogDebug("Image for button {ButtonId} unchanged (CRC=0x{Crc:X8}), skipping transfer", buttonId, crc);
            progress?.Report(100);
            return true;
        }

        var result = await _imageTransferCommand.ExecuteAsync(profileId, buttonId, imageData, 0xFF, progress, cancellationToken);
        if (result)
            _imageHashCache[(profileId, buttonId)] = crc;
        return result;
    }

    public async Task<bool> SendFolderButtonImageAsync(
        byte profileId,
        byte folderId,
        byte buttonId,
        byte[] imageData,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _imageTransferCommand.ExecuteAsync(profileId, buttonId, imageData, folderId, progress, cancellationToken);
        return result;
    }

    public async Task LoadImageHashCacheAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        _imageHashCache.Clear();
        var hashes = await _getImageHashesCommand.ExecuteAsync(profileId, cancellationToken);
        if (hashes == null)
        {
            _logger.LogWarning("LoadImageHashCache: failed to load hashes for profile {ProfileId}", profileId);
            return;
        }
        foreach (var kvp in hashes)
            _imageHashCache[(profileId, kvp.Key)] = kvp.Value;
        _logger.LogInformation("Image hash cache: {Count} entries loaded for profile {ProfileId}", hashes.Count, profileId);
    }
    
    public async Task<bool> SetButtonActionAsync(
        byte profileId,
        byte buttonId,
        ActionConfig action,
        CancellationToken cancellationToken = default)
    {
        return await _setButtonActionCommand.ExecuteAsync(profileId, buttonId, action, cancellationToken);
    }

    public async Task<bool> SetButtonNameAsync(
        byte profileId,
        byte buttonId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        return await _setButtonNameCommand.ExecuteAsync(profileId, buttonId, name, cancellationToken);
    }
    
    public async Task<bool> SetLedColorAsync(
        byte profileId, 
        byte buttonId, 
        LedConfig led,
        CancellationToken cancellationToken = default)
    {
        return await _setLedColorCommand.ExecuteAsync(profileId, buttonId, led, cancellationToken);
    }
    
    public async Task<byte?> SetDisplayBrightnessAsync(byte brightness, CancellationToken cancellationToken = default)
    {
        return await _setDisplayBrightnessCommand.ExecuteAsync(brightness, cancellationToken);
    }
    
    public async Task<bool> SetFolderButtonActionAsync(byte profileId, byte folderId, byte buttonId,
        ActionConfig action, CancellationToken cancellationToken = default)
        => await _setFolderButtonActionCommand.ExecuteAsync(profileId, folderId, buttonId, action, cancellationToken);

    public async Task<bool> SetFolderButtonNameAsync(byte profileId, byte folderId, byte buttonId,
        string? name, CancellationToken cancellationToken = default)
        => await _setFolderButtonNameCommand.ExecuteAsync(profileId, folderId, buttonId, name, cancellationToken);

    public async Task<bool> SetFolderButtonLedAsync(byte profileId, byte folderId, byte buttonId,
        LedConfig led, CancellationToken cancellationToken = default)
        => await _setFolderButtonLedCommand.ExecuteAsync(profileId, folderId, buttonId, led, cancellationToken);


    public async Task<bool> SaveProfileAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        var payload = new byte[] { profileId };
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_SAVE_PROFILE,
            payload,
            cancellationToken: cancellationToken);

        return response != null && response.Payload[0] == ProtocolConstants.STATUS_OK;
    }

    public async Task<bool> SetEncoderActionAsync(byte slot, ActionConfig? action, CancellationToken cancellationToken = default)
        => await _setEncoderActionCommand.ExecuteAsync(slot, action, cancellationToken);

    public async Task<bool> SetButtonLongPressActionAsync(byte profileId, byte buttonId, ActionConfig? action,
        byte folderId = 0xFF, CancellationToken cancellationToken = default)
        => await _setButtonLongPressActionCommand.ExecuteAsync(profileId, buttonId, action, folderId, cancellationToken);

    public async Task<bool> SetButtonLongPressNameAsync(byte profileId, byte buttonId, string? name,
        byte folderId = 0xFF, CancellationToken cancellationToken = default)
        => await _setButtonLongPressNameCommand.ExecuteAsync(profileId, buttonId, name, folderId, cancellationToken);

    public async Task<bool> SetButtonLongTextAsync(
        byte profileId, byte folderId, byte buttonId, string text,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var textBytes = System.Text.Encoding.UTF8.GetBytes(text);
            uint textSize = (uint)textBytes.Length;

            const int TextChunkSize = 54; // PayloadSize(56) - chunkNum(2)

            // START: [profileId][folderId][buttonId][textSize(4 LE)]
            var startPayload = new byte[7];
            startPayload[0] = profileId;
            startPayload[1] = folderId;
            startPayload[2] = buttonId;
            startPayload[3] = (byte)(textSize & 0xFF);
            startPayload[4] = (byte)((textSize >> 8) & 0xFF);
            startPayload[5] = (byte)((textSize >> 16) & 0xFF);
            startPayload[6] = (byte)((textSize >> 24) & 0xFF);

            var startResp = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_BUTTON_TEXT_START,
                startPayload,
                cancellationToken: cancellationToken);

            if (startResp == null || startResp.Payload.Length < 1 ||
                startResp.Payload[0] != ProtocolConstants.STATUS_OK)
            {
                _logger.LogError("Text transfer START failed for button {ButtonId}", buttonId);
                return false;
            }

            // CHUNKs
            int offset = 0;
            ushort chunkNum = 0;
            while (offset < textBytes.Length)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                int chunkLen = Math.Min(TextChunkSize, textBytes.Length - offset);
                var chunkPayload = new byte[2 + chunkLen];
                chunkPayload[0] = (byte)(chunkNum & 0xFF);
                chunkPayload[1] = (byte)((chunkNum >> 8) & 0xFF);
                Array.Copy(textBytes, offset, chunkPayload, 2, chunkLen);

                var chunkResp = await _protocol.SendCommandAsync(
                    ProtocolConstants.CMD_SET_BUTTON_TEXT_CHUNK,
                    chunkPayload,
                    cancellationToken: cancellationToken);

                if (chunkResp == null || chunkResp.Payload.Length < 1 ||
                    chunkResp.Payload[0] != ProtocolConstants.STATUS_OK)
                {
                    _logger.LogError("Text CHUNK {Num} failed for button {ButtonId}", chunkNum, buttonId);
                    return false;
                }

                offset += chunkLen;
                chunkNum++;
                progress?.Report((int)(100.0 * offset / textBytes.Length));
            }

            // END
            var endResp = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_BUTTON_TEXT_END,
                Array.Empty<byte>(),
                cancellationToken: cancellationToken);

            var ok = endResp != null && endResp.Payload.Length >= 1 &&
                     endResp.Payload[0] == ProtocolConstants.STATUS_OK;
            if (ok)
                _logger.LogInformation("Long text ({Bytes}B) → button {ButtonId}", textBytes.Length, buttonId);
            else
                _logger.LogError("Text transfer END failed for button {ButtonId}", buttonId);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetButtonLongTextAsync failed for button {ButtonId}", buttonId);
            return false;
        }
    }

    public async Task<bool> RefreshDisplaysAsync(CancellationToken cancellationToken = default)
    {
        // Refreshing all 10 displays can take a few seconds (JPEG decode + SPI per button)
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_REFRESH_DISPLAYS,
            Array.Empty<byte>(),
            timeoutMs: 15000,
            cancellationToken: cancellationToken);

        return response != null && response.Payload[0] == ProtocolConstants.STATUS_OK;
    }

    public Task<bool> DeleteProfileFromDeviceAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        // Device holds a single profile slot; deletion is handled locally only.
        return Task.FromResult(true);
    }

    private void OnDeviceConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Device connected event");
        DeviceConnected?.Invoke(this, new DeviceEventArgs());
    }
    
    private void OnDeviceDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("Device disconnected event");
        _imageHashCache.Clear();
        DeviceDisconnected?.Invoke(this, new DeviceEventArgs());
    }
    
    private void OnDataReceived(object? sender, byte[] data)
    {
        try
        {
            var packet = ProtocolPacket.FromBytes(data);
            if (packet == null)
                return;
            
            // Обработать события от устройства
            switch (packet.CommandId)
            {
                case ProtocolConstants.EVENT_BUTTON_PRESSED:
                    HandleButtonPressed(packet.Payload);
                    break;
                    
                case ProtocolConstants.EVENT_ENCODER_ROTATED:
                    HandleEncoderRotated(packet.Payload);
                    break;
                    
                case ProtocolConstants.EVENT_PROFILE_CHANGED:
                    HandleProfileChanged(packet.Payload);
                    break;
                    
                case ProtocolConstants.EVENT_FOLDER_ENTERED:
                    HandleFolderEntered(packet.Payload);
                    break;
                    
                case ProtocolConstants.EVENT_FOLDER_EXITED:
                    HandleFolderExited(packet.Payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing device event");
        }
    }
    
    private void HandleButtonPressed(byte[] payload)
    {
        var buttonId = payload[0];
        var profileId = payload[1];
        var actionType = (ActionType)payload[2];
        
        _logger.LogDebug("Button {ButtonId} pressed", buttonId);
        
        ButtonPressed?.Invoke(this, new ButtonEventArgs
        {
            ButtonId = buttonId,
            ProfileId = profileId,
            ActionType = actionType
        });
    }
    
    private void HandleEncoderRotated(byte[] payload)
    {
        var direction = (EncoderDirection)payload[0];
        var steps = payload[1];
        var newProfileId = payload[2];
        
        _logger.LogDebug("Encoder rotated {Direction}, steps: {Steps}", direction, steps);
        
        EncoderRotated?.Invoke(this, new EncoderEventArgs
        {
            Direction = direction,
            Steps = steps,
            NewProfileId = newProfileId
        });
    }
    
    private void HandleProfileChanged(byte[] payload)
    {
        var oldProfileId = payload[0];
        var newProfileId = payload[1];
        var reason = (ProfileChangeReason)payload[2];
        
        _logger.LogInformation("Profile changed from {Old} to {New}, reason: {Reason}",
            oldProfileId, newProfileId, reason);
        
        ProfileChanged?.Invoke(this, new ProfileChangedEventArgs
        {
            OldProfileId = oldProfileId,
            NewProfileId = newProfileId,
            Reason = reason
        });
    }
    
    private void HandleFolderEntered(byte[] payload)
    {
        var folderId = payload[0];
        var depth = payload[1];
        var profileId = payload[2];
        
        _logger.LogInformation("Folder entered: {FolderId}, depth: {Depth}, profile: {ProfileId}",
            folderId, depth, profileId);
        
        FolderEntered?.Invoke(this, new FolderEventArgs
        {
            FolderId = folderId,
            FolderDepth = depth,
            ProfileId = profileId
        });
    }
    
    private void HandleFolderExited(byte[] payload)
    {
        var exitedFolderId = payload[0];
        var depth = payload[1];
        var profileId = payload[2];
        var parentFolderId = payload[3];
        
        _logger.LogInformation("Folder exited: {FolderId}, new depth: {Depth}, parent: {ParentId}",
            exitedFolderId, depth, parentFolderId);
        
        FolderExited?.Invoke(this, new FolderEventArgs
        {
            FolderId = exitedFolderId,
            FolderDepth = depth,
            ProfileId = profileId,
            ParentFolderId = parentFolderId
        });
    }
    
    public async Task<ActionConfig?> GetButtonActionAsync(byte profileId, byte buttonId, CancellationToken cancellationToken = default)
    {
        return await _getButtonActionCommand.ExecuteAsync(profileId, buttonId, cancellationToken);
    }
    
    public async Task<LedConfig?> GetLedColorAsync(byte profileId, byte buttonId, CancellationToken cancellationToken = default)
    {
        return await _getLedColorCommand.ExecuteAsync(profileId, buttonId, cancellationToken);
    }
}
