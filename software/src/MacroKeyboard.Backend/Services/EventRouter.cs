using MacroKeyboard.Shared.IPC;
using Microsoft.Extensions.Logging;
using SharedEvents = MacroKeyboard.Shared.Events;

namespace MacroKeyboard.Backend.Services;

/// <summary>
/// Routes device events to IPC clients and plugins
/// </summary>
public class EventRouter
{
    private readonly DeviceManager _deviceManager;
    private readonly IIpcServer _ipcServer;
    private readonly ILogger<EventRouter> _logger;

    public EventRouter(
        DeviceManager deviceManager,
        IIpcServer ipcServer,
        ILogger<EventRouter> logger)
    {
        _deviceManager = deviceManager;
        _ipcServer = ipcServer;
        _logger = logger;

        // Subscribe to device events
        _deviceManager.DeviceConnected += OnDeviceConnected;
        _deviceManager.DeviceDisconnected += OnDeviceDisconnected;
        _deviceManager.ButtonPressed += OnButtonPressed;
        _deviceManager.ButtonReleased += OnButtonReleased;
        _deviceManager.EncoderRotated += OnEncoderRotated;
        _deviceManager.ProfileChanged += OnProfileChanged;
    }

    private async void OnDeviceConnected(object? sender, SharedEvents.DeviceEventArgs e)
    {
        _logger.LogInformation("Device connected: {DeviceName} (FW: {FirmwareVersion})", 
            e.DeviceName, e.FirmwareVersion);

        await _ipcServer.BroadcastAsync(new IpcMessage
        {
            MessageType = IpcMessageTypes.DeviceConnected,
            Data = e
        });
    }

    private async void OnDeviceDisconnected(object? sender, SharedEvents.DeviceEventArgs e)
    {
        _logger.LogInformation("Device disconnected");

        await _ipcServer.BroadcastAsync(new IpcMessage
        {
            MessageType = IpcMessageTypes.DeviceDisconnected,
            Data = e
        });
    }

    private async void OnButtonPressed(object? sender, SharedEvents.ButtonEventArgs e)
    {
        _logger.LogDebug("Button pressed: {ButtonIndex}", e.ButtonIndex);

        await _ipcServer.BroadcastAsync(new IpcMessage
        {
            MessageType = IpcMessageTypes.ButtonPressed,
            Data = e
        });
    }

    private async void OnButtonReleased(object? sender, SharedEvents.ButtonEventArgs e)
    {
        _logger.LogDebug("Button released: {ButtonIndex}", e.ButtonIndex);

        await _ipcServer.BroadcastAsync(new IpcMessage
        {
            MessageType = IpcMessageTypes.ButtonReleased,
            Data = e
        });
    }

    private async void OnEncoderRotated(object? sender, SharedEvents.EncoderEventArgs e)
    {
        _logger.LogDebug("Encoder rotated: {Delta}", e.Delta);

        await _ipcServer.BroadcastAsync(new IpcMessage
        {
            MessageType = IpcMessageTypes.EncoderRotated,
            Data = e
        });
    }

    private async void OnProfileChanged(object? sender, SharedEvents.ProfileChangedEventArgs e)
    {
        _logger.LogInformation("Profile changed: {ProfileIndex} - {ProfileName}", 
            e.ProfileIndex, e.ProfileName);

        await _ipcServer.BroadcastAsync(new IpcMessage
        {
            MessageType = IpcMessageTypes.ProfileChanged,
            Data = e
        });
    }
}
