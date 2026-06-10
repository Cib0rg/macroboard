using MacroKeyboard.Core.Services;
using Microsoft.Extensions.Logging;
using SharedEvents = MacroKeyboard.Shared.Events;
using CoreEvents = MacroKeyboard.Core.Services;

namespace MacroKeyboard.Backend.Services;

/// <summary>
/// Manages device connection and events
/// </summary>
public class DeviceManager : IDisposable
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<DeviceManager> _logger;
    private bool _isRunning;
    private CancellationTokenSource? _cts;
    // Cancelled whenever device disconnects, so the monitoring loop wakes up immediately
    private volatile CancellationTokenSource? _sleepCts;

    public event EventHandler<SharedEvents.DeviceEventArgs>? DeviceConnected;
    public event EventHandler<SharedEvents.DeviceEventArgs>? DeviceDisconnected;
    public event EventHandler<SharedEvents.ButtonEventArgs>? ButtonPressed;
    public event EventHandler<SharedEvents.ButtonEventArgs>? ButtonReleased;
    public event EventHandler<SharedEvents.EncoderEventArgs>? EncoderRotated;
    public event EventHandler<SharedEvents.ProfileChangedEventArgs>? ProfileChanged;
    public event EventHandler<SharedEvents.FolderEventArgs>? FolderEntered;
    public event EventHandler<SharedEvents.FolderEventArgs>? FolderExited;

    public bool IsDeviceConnected => _deviceService.IsConnected;

    public DeviceManager(IDeviceService deviceService, ILogger<DeviceManager> logger)
    {
        _deviceService = deviceService;
        _logger = logger;

        // Subscribe to device events
        _deviceService.ButtonPressed += OnButtonPressed;
        _deviceService.ButtonReleased += OnButtonReleased;
        _deviceService.EncoderRotated += OnEncoderRotated;
        _deviceService.ProfileChanged += OnProfileChanged;
        _deviceService.FolderEntered += OnFolderEntered;
        _deviceService.FolderExited += OnFolderExited;
        _deviceService.DeviceDisconnected += OnDeviceDisconnected;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("DeviceManager is already running");
            return;
        }

        _logger.LogInformation("Starting DeviceManager...");
        _isRunning = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Start monitoring for device connection
        _ = Task.Run(() => MonitorDeviceAsync(_cts.Token), _cts.Token);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Stopping DeviceManager...");
        _isRunning = false;
        _cts?.Cancel();

        // Device will disconnect automatically when disposed
        return Task.CompletedTask;
    }

    private async Task MonitorDeviceAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Device monitoring started");
        bool wasConnected = false;
        // Tracks whether the firmware has responded to GetDeviceInfo (i.e., is fully booted).
        // The ESP32 enumerates USB during Phase 3 of startup but doesn't create protocol tasks
        // until Phase 6 (after image loading, which can take several seconds). Without this flag,
        // DeviceConnected fires with a fake DeviceInfo while the firmware is still initialising,
        // causing every subsequent command to time out.
        bool firmwareReady = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_deviceService.IsConnected)
                {
                    if (wasConnected)
                    {
                        _logger.LogInformation("Device was connected, now disconnected — will attempt reconnection");
                    }
                    wasConnected = false;
                    firmwareReady = false;

                    _logger.LogDebug("Attempting to connect to device...");

                    if (await _deviceService.ConnectAsync(cancellationToken))
                    {
                        _logger.LogInformation("USB connection established — waiting for firmware...");
                        wasConnected = true;
                        // firmwareReady stays false; the block below will poll until the firmware responds
                    }
                    else
                    {
                        // Device not found — wait before retrying
                        await InterruptibleDelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                        continue;
                    }
                }

                // USB is up but the firmware has not yet confirmed it is ready.
                // Retry GetDeviceInfoAsync (each call has a 3 s internal timeout) until
                // the firmware actually responds. This loop handles:
                //   • Fresh boot: firmware takes 1–5 s to load images before starting tasks.
                //   • Backend restart while device is already running: firmware responds immediately.
                if (wasConnected && !firmwareReady)
                {
                    // Brief settle delay on every attempt (avoids log spam; also gives the firmware
                    // a moment to initialise the USB vendor interface after enumeration).
                    await Task.Delay(500, cancellationToken);

                    var deviceInfo = await _deviceService.GetDeviceInfoAsync(cancellationToken);
                    if (deviceInfo.IsConnected)
                    {
                        firmwareReady = true;
                        _logger.LogInformation("Firmware ready — firing DeviceConnected");
                        DeviceConnected?.Invoke(this, new SharedEvents.DeviceEventArgs
                        {
                            DeviceId = deviceInfo.DeviceId,
                            DeviceName = "MacroKeyboard",
                            FirmwareVersion = deviceInfo.FirmwareVersion.ToString()
                        });
                    }
                    else
                    {
                        _logger.LogDebug("Firmware not ready yet (still booting) — retrying...");
                        await InterruptibleDelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                    continue;
                }

                // Firmware is ready — sleep until disconnect wakes us, or 5 s max
                await InterruptibleDelayAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in device monitoring loop");
                wasConnected = false;
                firmwareReady = false;
                await InterruptibleDelayAsync(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }

        _logger.LogInformation("Device monitoring stopped");
    }

    /// <summary>
    /// Sleep for <paramref name="duration"/> but wake up immediately if the device disconnects.
    /// </summary>
    private async Task InterruptibleDelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sleepCts = cts;
        try
        {
            await Task.Delay(duration, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Woken by disconnect signal — not a real cancellation
        }
        finally
        {
            _sleepCts = null;
        }
    }

    private void OnButtonPressed(object? sender, CoreEvents.ButtonEventArgs e)
    {
        ButtonPressed?.Invoke(this, new SharedEvents.ButtonEventArgs
        {
            ButtonIndex = e.ButtonId,
            EventType = SharedEvents.ButtonEventType.Pressed,
            Timestamp = DateTime.UtcNow
        });
    }

    private void OnButtonReleased(object? sender, CoreEvents.ButtonEventArgs e)
    {
        ButtonReleased?.Invoke(this, new SharedEvents.ButtonEventArgs
        {
            ButtonIndex = e.ButtonId,
            EventType = SharedEvents.ButtonEventType.Released,
            Timestamp = DateTime.UtcNow
        });
    }

    private void OnEncoderRotated(object? sender, CoreEvents.EncoderEventArgs e)
    {
        sbyte delta = e.Direction == CoreEvents.EncoderDirection.Clockwise 
            ? (sbyte)e.Steps 
            : (sbyte)-e.Steps;
            
        EncoderRotated?.Invoke(this, new SharedEvents.EncoderEventArgs
        {
            Delta = delta,
            Timestamp = DateTime.UtcNow
        });
    }

    private void OnProfileChanged(object? sender, CoreEvents.ProfileChangedEventArgs e)
    {
        ProfileChanged?.Invoke(this, new SharedEvents.ProfileChangedEventArgs
        {
            ProfileIndex = e.NewProfileId,
            ProfileName = $"Profile {e.NewProfileId}",
            Timestamp = DateTime.UtcNow
        });
    }

    private void OnFolderEntered(object? sender, CoreEvents.FolderEventArgs e)
    {
        FolderEntered?.Invoke(this, new SharedEvents.FolderEventArgs
        {
            FolderId = e.FolderId,
            FolderDepth = e.FolderDepth,
            ProfileId = e.ProfileId,
            IsEntering = true,
            Timestamp = DateTime.UtcNow
        });
    }

    private void OnFolderExited(object? sender, CoreEvents.FolderEventArgs e)
    {
        FolderExited?.Invoke(this, new SharedEvents.FolderEventArgs
        {
            FolderId = e.FolderId,
            FolderDepth = e.FolderDepth,
            ProfileId = e.ProfileId,
            ParentFolderId = e.ParentFolderId,
            IsEntering = false,
            Timestamp = DateTime.UtcNow
        });
    }

    private void OnDeviceDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("Device disconnected");

        // Wake the monitoring loop so reconnection starts immediately
        _sleepCts?.Cancel();

        DeviceDisconnected?.Invoke(this, new SharedEvents.DeviceEventArgs
        {
            DeviceId = string.Empty,
            DeviceName = string.Empty,
            FirmwareVersion = string.Empty
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        
        _deviceService.ButtonPressed -= OnButtonPressed;
        _deviceService.ButtonReleased -= OnButtonReleased;
        _deviceService.EncoderRotated -= OnEncoderRotated;
        _deviceService.ProfileChanged -= OnProfileChanged;
        _deviceService.FolderEntered -= OnFolderEntered;
        _deviceService.FolderExited -= OnFolderExited;
        _deviceService.DeviceDisconnected -= OnDeviceDisconnected;
    }
}
