using MacroKeyboard.Backend;
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

    // Cache the last device event so we can send it to newly connected IPC clients
    private SharedEvents.DeviceEventArgs? _lastDeviceEvent;

    public EventRouter(
        DeviceManager deviceManager,
        IIpcServer ipcServer,
        ILogger<EventRouter> logger)
    {
        _deviceManager = deviceManager;
        _ipcServer = ipcServer;
        _logger = logger;

        // Subscribe to device events
        _deviceManager.DeviceConnected    += (s, e)  => OnDeviceConnected(s, e).FireAndForget(_logger);
        _deviceManager.DeviceDisconnected += (s, e)  => OnDeviceDisconnected(s, e).FireAndForget(_logger);
        _deviceManager.ButtonPressed      += (s, e)  => OnButtonPressed(s, e).FireAndForget(_logger);
        _deviceManager.ButtonReleased     += (s, e)  => OnButtonReleased(s, e).FireAndForget(_logger);
        _deviceManager.EncoderRotated     += (s, e)  => OnEncoderRotated(s, e).FireAndForget(_logger);
        _deviceManager.ProfileChanged     += (s, e)  => OnProfileChanged(s, e).FireAndForget(_logger);
        _deviceManager.FolderEntered      += (s, e)  => OnFolderEntered(s, e).FireAndForget(_logger);
        _deviceManager.FolderExited       += (s, e)  => OnFolderExited(s, e).FireAndForget(_logger);

        // When a new IPC client connects, send current device status
        _ipcServer.ClientConnected += (s, id) => OnIpcClientConnected(s, id).FireAndForget(_logger);
    }

    private async Task OnIpcClientConnected(object? sender, string clientId) =>
        await _logger.TryCatchAsync(async () =>
        {
            if (_deviceManager.IsDeviceConnected && _lastDeviceEvent != null)
            {
                _logger.LogInformation("Sending cached device status to new IPC client {ClientId}", clientId);
                await _ipcServer.BroadcastAsync(new IpcMessage
                {
                    MessageType = IpcMessageTypes.DeviceConnected,
                    Data = _lastDeviceEvent
                });
            }
        }, "Error sending device status to new IPC client");

    private async Task OnDeviceConnected(object? sender, SharedEvents.DeviceEventArgs e) =>
        await _logger.TryCatchAsync(async () =>
        {
            _logger.LogInformation("Device connected: {DeviceName} (FW: {FirmwareVersion})",
                e.DeviceName, e.FirmwareVersion);
            _lastDeviceEvent = e;
            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.DeviceConnected,
                Data = e
            });
        }, "Error broadcasting device connected event");

    private async Task OnDeviceDisconnected(object? sender, SharedEvents.DeviceEventArgs e) =>
        await _logger.TryCatchAsync(async () =>
        {
            _logger.LogInformation("Device disconnected");
            _lastDeviceEvent = null;
            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.DeviceDisconnected,
                Data = e
            });
        }, "Error broadcasting device disconnected event");

    private async Task OnButtonPressed(object? sender, SharedEvents.ButtonEventArgs e) =>
        await _logger.TryCatchAsync(async () =>
        {
            _logger.LogDebug("Button pressed: {ButtonIndex}", e.ButtonIndex);
            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.ButtonPressed,
                Data = e
            });
        }, "Error broadcasting button pressed event");

    private async Task OnButtonReleased(object? sender, SharedEvents.ButtonEventArgs e) =>
        await _logger.TryCatchAsync(async () =>
        {
            _logger.LogDebug("Button released: {ButtonIndex}", e.ButtonIndex);
            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.ButtonReleased,
                Data = e
            });
        }, "Error broadcasting button released event");

    private async Task OnEncoderRotated(object? sender, SharedEvents.EncoderEventArgs e) =>
        await _logger.TryCatchAsync(async () =>
        {
            _logger.LogDebug("Encoder rotated: {Delta}", e.Delta);
            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.EncoderRotated,
                Data = e
            });
        }, "Error broadcasting encoder rotated event");

    private async Task OnProfileChanged(object? sender, SharedEvents.ProfileChangedEventArgs e) =>
        await _logger.TryCatchAsync(async () =>
        {
            _logger.LogInformation("Profile changed: {ProfileIndex} - {ProfileName}",
                e.ProfileIndex, e.ProfileName);
            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.ProfileChanged,
                Data = e
            });
        }, "Error broadcasting profile changed event");

    private async Task OnFolderEntered(object? sender, SharedEvents.FolderEventArgs e) =>
        await _logger.TryCatchAsync(async () =>
        {
            _logger.LogInformation("Folder entered: {FolderId}, depth: {Depth}",
                e.FolderId, e.FolderDepth);
            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.FolderEntered,
                Data = e
            });
        }, "Error broadcasting folder entered event");

    private async Task OnFolderExited(object? sender, SharedEvents.FolderEventArgs e) =>
        await _logger.TryCatchAsync(async () =>
        {
            _logger.LogInformation("Folder exited: {FolderId}, new depth: {Depth}",
                e.FolderId, e.FolderDepth);
            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.FolderExited,
                Data = e
            });
        }, "Error broadcasting folder exited event");
}
