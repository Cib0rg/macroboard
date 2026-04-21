using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Communication.Usb;

namespace MacroKeyboard.Communication.HidDevice;

/// <summary>
/// Менеджер для работы с USB Vendor-class устройством (кроссплатформенный).
/// Использует libusb-1.0 через P/Invoke для связи с Vendor-интерфейсом (Interface 1)
/// составного USB-устройства (HID Keyboard + Vendor).
///
/// Architecture:
///   - Single reader thread (MonitorDeviceAsync) reads all incoming USB data
///   - Command responses are routed to pending TaskCompletionSource via response queue
///   - Unsolicited events (button presses, etc.) are fired via DataReceived event
///
/// Требования:
///   - Linux: libusb-1.0-0 (apt install libusb-1.0-0) + udev rules для доступа без root
///   - Windows: WinUSB драйвер (устанавливается через Zadig или INF-файл)
///   - macOS: libusb (brew install libusb)
/// </summary>
public class HidDeviceManager : IDisposable
{
    private readonly ILogger<HidDeviceManager> _logger;
    private nint _ctx;
    private nint _devHandle;
    private bool _interfaceClaimed;
    private bool _isMonitoring;
    private CancellationTokenSource? _monitoringCts;
    private bool _disposed;
    private readonly object _writeLock = new();

    // Pending response queue: the monitor thread routes responses here
    private readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]?>> _pendingResponses = new();
    private int _responseIdCounter;

    // Vendor interface endpoints (must match firmware usb_descriptors.c)
    private const int VENDOR_INTERFACE_NUMBER = 1;
    private const byte EP_VENDOR_OUT = 0x02;  // Bulk OUT
    private const byte EP_VENDOR_IN = 0x82;   // Bulk IN

    public event EventHandler? DeviceConnected;
    public event EventHandler? DeviceDisconnected;
    public event EventHandler<byte[]>? DataReceived;

    public bool IsConnected => _devHandle != 0;

    public HidDeviceManager(ILogger<HidDeviceManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Подключиться к устройству
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            _logger.LogInformation("Searching for device (VID: 0x{VID:X4}, PID: 0x{PID:X4})...",
                ProtocolConstants.VendorId, ProtocolConstants.ProductId);

            // Initialize libusb
            int rc = LibUsb.libusb_init(out _ctx);
            if (rc != LibUsb.LIBUSB_SUCCESS)
            {
                _logger.LogError("Failed to initialize libusb: {Error}", LibUsb.GetErrorString(rc));
                return false;
            }

            // Open device by VID/PID
            _devHandle = LibUsb.libusb_open_device_with_vid_pid(
                _ctx,
                (ushort)ProtocolConstants.VendorId,
                (ushort)ProtocolConstants.ProductId);

            if (_devHandle == 0)
            {
                _logger.LogWarning("Device not found (VID: 0x{VID:X4}, PID: 0x{PID:X4}). " +
                    "Check: 1) Device is plugged in, 2) On Linux: udev rule installed " +
                    "(sudo cp scripts/99-macrokeyboard.rules /etc/udev/rules.d/ && sudo udevadm control --reload-rules && sudo udevadm trigger)",
                    ProtocolConstants.VendorId, ProtocolConstants.ProductId);
                LibUsb.libusb_exit(_ctx);
                _ctx = 0;
                return false;
            }

            _logger.LogInformation("USB device opened");

            // Auto-detach kernel driver (works on Linux, no-op on Windows/macOS)
            LibUsb.libusb_set_auto_detach_kernel_driver(_devHandle, 1);

            // Claim the vendor interface
            rc = LibUsb.libusb_claim_interface(_devHandle, VENDOR_INTERFACE_NUMBER);
            if (rc != LibUsb.LIBUSB_SUCCESS)
            {
                _logger.LogError("Failed to claim interface {Interface}: {Error}",
                    VENDOR_INTERFACE_NUMBER, LibUsb.GetErrorString(rc));
                CleanupDevice();
                return false;
            }

            _interfaceClaimed = true;
            _logger.LogInformation("Claimed vendor interface {Interface}", VENDOR_INTERFACE_NUMBER);
            _logger.LogInformation("Vendor endpoints: OUT=0x{Out:X2}, IN=0x{In:X2}",
                EP_VENDOR_OUT, EP_VENDOR_IN);

            // Start the single reader thread
            StartMonitoring();

            DeviceConnected?.Invoke(this, EventArgs.Empty);

            _logger.LogInformation("Device connected successfully");
            return true;
        }
        catch (DllNotFoundException ex)
        {
            _logger.LogError(ex, "libusb-1.0 not found. Install it:\n" +
                "  Linux:  sudo apt install libusb-1.0-0\n" +
                "  macOS:  brew install libusb\n" +
                "  Windows: Install WinUSB driver via Zadig");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to device");
            CleanupDevice();
            return false;
        }
    }

    /// <summary>
    /// Отключиться от устройства
    /// </summary>
    public void Disconnect()
    {
        StopMonitoring();
        CancelAllPendingResponses();
        CleanupDevice();

        _logger.LogInformation("Device disconnected");
        DeviceDisconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Отправить данные на устройство через Vendor Bulk OUT endpoint
    /// </summary>
    public async Task<bool> WriteAsync(byte[] data)
    {
        if (_devHandle == 0)
        {
            _logger.LogWarning("Device not connected or cannot write");
            return false;
        }

        try
        {
            _logger.LogDebug("WriteAsync: Sending {Length} bytes via Vendor Bulk OUT", data.Length);

            // Pad to 64 bytes if needed (firmware expects fixed-size packets)
            var packet = new byte[ProtocolConstants.PacketSize];
            Array.Copy(data, 0, packet, 0, Math.Min(data.Length, ProtocolConstants.PacketSize));

            int rc;
            lock (_writeLock)
            {
                rc = LibUsb.libusb_bulk_transfer(
                    _devHandle,
                    EP_VENDOR_OUT,
                    packet,
                    packet.Length,
                    out int bytesWritten,
                    1000); // 1 second timeout
            }

            if (rc != LibUsb.LIBUSB_SUCCESS)
            {
                _logger.LogError("USB write error: {Error}", LibUsb.GetErrorString(rc));
                return false;
            }

            _logger.LogDebug("Sent {Length} bytes to device successfully", data.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to device");
            return false;
        }
    }

    /// <summary>
    /// Register a response waiter and return a Task that will complete when data arrives.
    /// The monitor thread will route the next incoming packet to this waiter.
    ///
    /// MUST be called BEFORE WriteAsync to avoid race conditions where the
    /// monitor thread reads the response before the waiter is registered.
    /// </summary>
    public Task<byte[]?> WaitForResponseAsync(int timeoutMs = 3000)
    {
        var tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Interlocked.Increment(ref _responseIdCounter);

        _pendingResponses[id] = tcs;

        // Set up timeout cancellation
        var cts = new CancellationTokenSource(timeoutMs);
        cts.Token.Register(() =>
        {
            if (_pendingResponses.TryRemove(id, out _))
            {
                tcs.TrySetResult(null);
            }
            cts.Dispose();
        });

        // Clean up on completion
        tcs.Task.ContinueWith(task =>
        {
            _pendingResponses.TryRemove(id, out var removed);
        }, TaskContinuationOptions.ExecuteSynchronously);

        return tcs.Task;
    }

    /// <summary>
    /// Cancel a pending response waiter (e.g., if WriteAsync fails).
    /// </summary>
    public void CancelPendingResponse(Task<byte[]?> responseTask)
    {
        // Find and remove the TCS associated with this task
        foreach (var kvp in _pendingResponses)
        {
            if (kvp.Value.Task == responseTask)
            {
                if (_pendingResponses.TryRemove(kvp.Key, out var tcs))
                {
                    tcs.TrySetResult(null);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Read raw data from USB (used only by the monitor thread).
    /// </summary>
    private byte[]? ReadFromUsb(int timeout = 100)
    {
        if (_devHandle == 0) return null;

        try
        {
            var buffer = new byte[ProtocolConstants.PacketSize];

            int rc = LibUsb.libusb_bulk_transfer(
                _devHandle,
                EP_VENDOR_IN,
                buffer,
                buffer.Length,
                out int bytesRead,
                (uint)timeout);

            if (rc == LibUsb.LIBUSB_SUCCESS && bytesRead > 0)
            {
                var data = new byte[bytesRead];
                Array.Copy(buffer, 0, data, 0, bytesRead);
                return data;
            }

            if (rc == LibUsb.LIBUSB_ERROR_NO_DEVICE)
            {
                _logger.LogWarning("Device disconnected during read");
                return null;
            }

            // LIBUSB_ERROR_TIMEOUT is normal — no data available
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from USB");
            return null;
        }
    }

    /// <summary>
    /// Backward-compatible ReadAsync — registers a pending response and waits.
    /// Used by ProtocolHandler.SendCommandAsync after WriteAsync.
    /// </summary>
    public async Task<byte[]?> ReadAsync(int timeout = 1000)
    {
        return await WaitForResponseAsync(timeout);
    }

    /// <summary>
    /// Запустить мониторинг — единственный поток чтения из USB
    /// </summary>
    private void StartMonitoring()
    {
        if (_isMonitoring) return;

        _isMonitoring = true;
        _monitoringCts = new CancellationTokenSource();

        _ = Task.Run(() => MonitorDeviceAsync(_monitoringCts.Token));

        _logger.LogDebug("Device monitoring started");
    }

    /// <summary>
    /// Остановить мониторинг
    /// </summary>
    private void StopMonitoring()
    {
        if (!_isMonitoring) return;

        _isMonitoring = false;
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _monitoringCts = null;

        _logger.LogDebug("Device monitoring stopped");
    }

    /// <summary>
    /// Single reader thread: reads all incoming USB data and routes it.
    /// - If there are pending response waiters → complete the oldest one
    /// - Otherwise → fire DataReceived event (unsolicited device events)
    /// </summary>
    private async Task MonitorDeviceAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Monitoring device events (single reader thread)...");

        while (!cancellationToken.IsCancellationRequested && _devHandle != 0)
        {
            try
            {
                var data = ReadFromUsb(100);

                if (data != null && data.Length > 0)
                {
                    // Try to route to a pending response waiter
                    bool routed = false;

                    // Find the oldest pending response and complete it
                    foreach (var kvp in _pendingResponses)
                    {
                        if (_pendingResponses.TryRemove(kvp.Key, out var tcs))
                        {
                            tcs.TrySetResult(data);
                            routed = true;
                            _logger.LogDebug("Routed {Length} bytes to pending response (id={Id})",
                                data.Length, kvp.Key);
                            break;
                        }
                    }

                    if (!routed)
                    {
                        // No pending response — this is an unsolicited event from device
                        _logger.LogDebug("Received unsolicited {Length} bytes, firing DataReceived", data.Length);
                        DataReceived?.Invoke(this, data);
                    }
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) break;

                _logger.LogError(ex, "Error in device monitoring loop");

                if (_devHandle == 0)
                {
                    _logger.LogWarning("Device disconnected during monitoring");
                    DeviceDisconnected?.Invoke(this, EventArgs.Empty);
                    break;
                }

                await Task.Delay(1000, cancellationToken);
            }
        }

        _logger.LogDebug("Device monitoring loop ended");
    }

    /// <summary>
    /// Cancel all pending response waiters (e.g., on disconnect)
    /// </summary>
    private void CancelAllPendingResponses()
    {
        foreach (var kvp in _pendingResponses)
        {
            if (_pendingResponses.TryRemove(kvp.Key, out var tcs))
            {
                tcs.TrySetResult(null);
            }
        }
    }

    /// <summary>
    /// Clean up USB device resources
    /// </summary>
    private void CleanupDevice()
    {
        try
        {
            if (_devHandle != 0)
            {
                if (_interfaceClaimed)
                {
                    LibUsb.libusb_release_interface(_devHandle, VENDOR_INTERFACE_NUMBER);
                    _interfaceClaimed = false;
                }

                LibUsb.libusb_close(_devHandle);
                _devHandle = 0;
            }

            if (_ctx != 0)
            {
                LibUsb.libusb_exit(_ctx);
                _ctx = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error during device cleanup: {Message}", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Disconnect();
    }
}
