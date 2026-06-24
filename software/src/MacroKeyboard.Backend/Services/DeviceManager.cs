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

    // Boot-wait state: how many consecutive GetDeviceInfo probes have failed for the
    // current connection attempt.  Reset to 0 on each new USB connection.
    private const int MaxBootRetries = 8;   // ~32 s at 3 s timeout + 1 s pause per attempt
    private int _bootRetryCount;

    // Set (TrySetResult) by OnDeviceReady when EVENT_DEVICE_READY (0xF4) arrives.
    // Replaced with a fresh TCS on every new USB connection so it can be awaited again.
    private volatile TaskCompletionSource<bool>? _firmwareReadyTcs;

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
        _deviceService.DeviceReady += OnDeviceReady;
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
                        _bootRetryCount = 0;
                        // Fresh TCS for this connection — EVENT_DEVICE_READY (0xF4) will complete it.
                        _firmwareReadyTcs = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    else
                    {
                        // Device not found — wait before retrying
                        await InterruptibleDelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                        continue;
                    }
                }

                // USB is up but the firmware has not yet confirmed it is ready.
                // Strategy:
                //   First probe: wait up to 4 s for EVENT_DEVICE_READY (0xF4) or
                //     EVENT_HEARTBEAT (0xF8, sent every 2 s by heartbeat_task), then probe CMD 0x02.
                //     - Fresh boot: 0xF4 arrives quickly → probe early.
                //     - Already-running firmware (backend restart): 0xF4 is gone but 0xF8 arrives
                //       within 2 s → drain window ends early → fast probe.
                //     - Any stale TX FIFO data from the old session is drained by the monitor thread
                //       as unsolicited DataReceived BEFORE the CMD 0x02 waiter is registered.
                //   Retries (bootRetryCount > 0): use short 500 ms settle.
                //   After MaxBootRetries failures → CMD_FACTORY_RESET + USB reconnect.
                if (wasConnected && !firmwareReady)
                {
                    if (_bootRetryCount == 0)
                    {
                        // First probe: drain window — wait for 0xF4/0xF8 or 4 s, whichever comes first.
                        _logger.LogDebug("Waiting up to 4s for EVENT_DEVICE_READY (0xF4) or EVENT_HEARTBEAT (0xF8)...");
                        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        await Task.WhenAny(_firmwareReadyTcs!.Task, Task.Delay(4000, drainCts.Token));
                        drainCts.Cancel();

                        if (_firmwareReadyTcs.Task.IsCompleted)
                            _logger.LogInformation("Firmware ready signal received (0xF4/0xF8) — probing");
                        else
                            _logger.LogInformation("No ready signal in 4s — probing anyway");
                    }
                    else
                    {
                        await Task.Delay(500, cancellationToken);
                    }

                    var deviceInfo = await _deviceService.GetDeviceInfoAsync(cancellationToken);
                    if (deviceInfo.IsConnected)
                    {
                        firmwareReady = true;
                        _bootRetryCount = 0;
                        _logger.LogInformation("Firmware ready — loading image hash cache");
                        await _deviceService.LoadImageHashCacheAsync(0, cancellationToken);
                        _logger.LogInformation("Firing DeviceConnected");
                        DeviceConnected?.Invoke(this, new SharedEvents.DeviceEventArgs
                        {
                            DeviceId = deviceInfo.DeviceId,
                            DeviceName = "MacroKeyboard",
                            FirmwareVersion = deviceInfo.FirmwareVersion.ToString()
                        });
                    }
                    else
                    {
                        _bootRetryCount++;
                        if (_bootRetryCount >= MaxBootRetries)
                        {
                            _logger.LogWarning(
                                "Firmware did not respond after {Max} attempts (~{Sec}s) — " +
                                "sending CMD_FACTORY_RESET then forcing USB reconnect",
                                MaxBootRetries, MaxBootRetries * 4);
                            _bootRetryCount = 0;
                            wasConnected = false;
                            firmwareReady = false;
                            // Best-effort reboot: if protocol_task was stuck (e.g. on a SPIFFS
                            // write from the previous session), the device reboots cleanly, sends
                            // 0xF4 on reconnect, and the next probe succeeds.  If the device cannot
                            // process this command either, RebootDeviceAsync times out and we fall
                            // through to the forced USB close below.
                            _logger.LogInformation("Requesting firmware reboot via CMD_FACTORY_RESET...");
                            try { await _deviceService.RebootDeviceAsync(cancellationToken); } catch { }
                            // Allow the device to reboot and disconnect naturally (100 ms firmware
                            // delay + USB re-enumeration) before we close the handle ourselves.
                            await Task.Delay(500, cancellationToken);
                            _deviceService.Disconnect();
                            await InterruptibleDelayAsync(TimeSpan.FromSeconds(2), cancellationToken);
                        }
                        else
                        {
                            _logger.LogDebug("Firmware not ready yet ({N}/{Max}) — retrying...",
                                _bootRetryCount, MaxBootRetries);
                            await InterruptibleDelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                        }
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

    private void OnDeviceReady(object? sender, EventArgs e)
    {
        var tcs = _firmwareReadyTcs;
        if (tcs != null && !tcs.Task.IsCompleted)
        {
            // First signal after connect — log at Info and wake the probe loop.
            _logger.LogInformation("Firmware signalled ready (0xF4 or 0xF8 heartbeat)");
            tcs.TrySetResult(true);
            _sleepCts?.Cancel();
        }
        // Subsequent heartbeats while already connected are silently ignored here;
        // DeviceService already logs them at Trace level.
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
        _deviceService.DeviceReady -= OnDeviceReady;
    }
}
