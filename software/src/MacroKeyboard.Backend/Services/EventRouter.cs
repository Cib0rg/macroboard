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
        _deviceManager.DeviceConnected += OnDeviceConnected;
        _deviceManager.DeviceDisconnected += OnDeviceDisconnected;
        _deviceManager.ButtonPressed += OnButtonPressed;
        _deviceManager.ButtonReleased += OnButtonReleased;
        _deviceManager.EncoderRotated += OnEncoderRotated;
        _deviceManager.ProfileChanged += OnProfileChanged;
        _deviceManager.FolderEntered += OnFolderEntered;
        _deviceManager.FolderExited += OnFolderExited;

        // When a new IPC client connects, send current device status
        _ipcServer.ClientConnected += OnIpcClientConnected;
    }

    private async void OnIpcClientConnected(object? sender, string clientId)
    {
        try
        {
            // If device is already connected, send the cached event to the new client
            if (_deviceManager.IsDeviceConnected && _lastDeviceEvent != null)
            {
                _logger.LogInformation("Sending cached device status to new IPC client {ClientId}", clientId);
                await _ipcServer.BroadcastAsync(new IpcMessage
                {
                    MessageType = IpcMessageTypes.DeviceConnected,
                    Data = _lastDeviceEvent
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending device status to new IPC client");
        }
    }

    private async void OnDeviceConnected(object? sender, SharedEvents.DeviceEventArgs e)
    {
        try
        {
            _logger.LogInformation("Device connected: {DeviceName} (FW: {FirmwareVersion})",
                e.DeviceName, e.FirmwareVersion);

            // Cache for newly connecting IPC clients
            _lastDeviceEvent = e;

            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.DeviceConnected,
                Data = e
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting device connected event");
        }
    }

    private async void OnDeviceDisconnected(object? sender, SharedEvents.DeviceEventArgs e)
    {
        try
        {
            _logger.LogInformation("Device disconnected");

            // Clear cache
            _lastDeviceEvent = null;

            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.DeviceDisconnected,
                Data = e
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting device disconnected event");
        }
    }

    private async void OnButtonPressed(object? sender, SharedEvents.ButtonEventArgs e)
    {
        try
        {
            _logger.LogDebug("Button pressed: {ButtonIndex}", e.ButtonIndex);

            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.ButtonPressed,
                Data = e
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting button pressed event");
        }
    }

    private async void OnButtonReleased(object? sender, SharedEvents.ButtonEventArgs e)
    {
        try
        {
            _logger.LogDebug("Button released: {ButtonIndex}", e.ButtonIndex);

            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.ButtonReleased,
                Data = e
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting button released event");
        }
    }

    private async void OnEncoderRotated(object? sender, SharedEvents.EncoderEventArgs e)
    {
        try
        {
            _logger.LogDebug("Encoder rotated: {Delta}", e.Delta);

            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.EncoderRotated,
                Data = e
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting encoder rotated event");
        }
    }

    private async void OnProfileChanged(object? sender, SharedEvents.ProfileChangedEventArgs e)
    {
        try
        {
            _logger.LogInformation("Profile changed: {ProfileIndex} - {ProfileName}",
                e.ProfileIndex, e.ProfileName);

            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.ProfileChanged,
                Data = e
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting profile changed event");
        }
    }

    private async void OnFolderEntered(object? sender, SharedEvents.FolderEventArgs e)
    {
        try
        {
            _logger.LogInformation("Folder entered: {FolderId}, depth: {Depth}",
                e.FolderId, e.FolderDepth);

            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.FolderEntered,
                Data = e
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting folder entered event");
        }
    }

    private async void OnFolderExited(object? sender, SharedEvents.FolderEventArgs e)
    {
        try
        {
            _logger.LogInformation("Folder exited: {FolderId}, new depth: {Depth}",
                e.FolderId, e.FolderDepth);

            await _ipcServer.BroadcastAsync(new IpcMessage
            {
                MessageType = IpcMessageTypes.FolderExited,
                Data = e
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting folder exited event");
        }
    }
}
