using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
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
    private readonly SetLedColorCommand _setLedColorCommand;
    
    public event EventHandler<DeviceEventArgs>? DeviceConnected;
    public event EventHandler<DeviceEventArgs>? DeviceDisconnected;
    public event EventHandler<ButtonEventArgs>? ButtonPressed;
    public event EventHandler<ButtonEventArgs>? ButtonReleased;
    public event EventHandler<EncoderEventArgs>? EncoderRotated;
    public event EventHandler<ProfileChangedEventArgs>? ProfileChanged;
    
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
        _setLedColorCommand = new SetLedColorCommand(_protocol, loggerFactory.CreateLogger<SetLedColorCommand>());
        
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
        return await _imageTransferCommand.ExecuteAsync(profileId, buttonId, imageData, progress, cancellationToken);
    }
    
    public async Task<bool> SetButtonActionAsync(
        byte profileId, 
        byte buttonId, 
        ActionConfig action,
        CancellationToken cancellationToken = default)
    {
        return await _setButtonActionCommand.ExecuteAsync(profileId, buttonId, action, cancellationToken);
    }
    
    public async Task<bool> SetLedColorAsync(
        byte profileId, 
        byte buttonId, 
        LedConfig led,
        CancellationToken cancellationToken = default)
    {
        return await _setLedColorCommand.ExecuteAsync(profileId, buttonId, led, cancellationToken);
    }
    
    public async Task<bool> SaveProfileAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        var payload = new byte[] { profileId };
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_SAVE_PROFILE,
            payload,
            cancellationToken: cancellationToken);
        
        return response != null && response.Payload[0] == ProtocolConstants.STATUS_OK;
    }
    
    private void OnDeviceConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Device connected event");
        DeviceConnected?.Invoke(this, new DeviceEventArgs());
    }
    
    private void OnDeviceDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("Device disconnected event");
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
}
