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

    public event EventHandler<SharedEvents.DeviceEventArgs>? DeviceConnected;
    public event EventHandler<SharedEvents.DeviceEventArgs>? DeviceDisconnected;
    public event EventHandler<SharedEvents.ButtonEventArgs>? ButtonPressed;
    public event EventHandler<SharedEvents.ButtonEventArgs>? ButtonReleased;
    public event EventHandler<SharedEvents.EncoderEventArgs>? EncoderRotated;
    public event EventHandler<SharedEvents.ProfileChangedEventArgs>? ProfileChanged;

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

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_deviceService.IsConnected)
                {
                    _logger.LogDebug("Attempting to connect to device...");
                    
                    if (await _deviceService.ConnectAsync(cancellationToken))
                    {
                        _logger.LogInformation("Device connected successfully");
                        
                        var deviceInfo = await _deviceService.GetDeviceInfoAsync(cancellationToken);
                        
                        DeviceConnected?.Invoke(this, new SharedEvents.DeviceEventArgs
                        {
                            DeviceId = deviceInfo.DeviceId,
                            DeviceName = "MacroKeyboard",
                            FirmwareVersion = deviceInfo.FirmwareVersion.ToString()
                        });
                    }
                }

                // Check every 5 seconds
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in device monitoring loop");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }

        _logger.LogInformation("Device monitoring stopped");
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

    private void OnDeviceDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("Device disconnected");
        
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
        _deviceService.DeviceDisconnected -= OnDeviceDisconnected;
    }
}
