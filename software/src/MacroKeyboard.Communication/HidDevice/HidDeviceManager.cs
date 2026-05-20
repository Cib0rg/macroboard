using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Communication.Usb;

namespace MacroKeyboard.Communication.HidDevice;

/// <summary>
/// Менеджер для работы с USB Vendor-class устройством (кроссплатформенный).
/// Использует LibUsbDotNet (NuGet) для связи с Vendor-интерфейсом (Interface 1)
/// составного USB-устройства (HID Keyboard + Vendor).
///
/// Architecture:
///   - Single reader thread (MonitorDeviceAsync) reads all incoming USB data
///   - Command responses are routed to pending TaskCompletionSource via response queue
///   - Unsolicited events (button presses, etc.) are fired via DataReceived event
///   - On disconnect: cleans up USB resources, fires DeviceDisconnected event
///   - DeviceManager handles reconnection by calling ConnectAsync() again
///
/// Требования:
///   - Linux: libusb-1.0-0 (apt install libusb-1.0-0) + udev rules для доступа без root
///   - Windows: WinUSB драйвер (устанавливается через Zadig или INF-файл).
///              libusb-1.0.dll поставляется вместе с LibUsbDotNet NuGet пакетом.
///   - macOS: libusb поставляется вместе с LibUsbDotNet NuGet пакетом
/// </summary>
public class HidDeviceManager : IDisposable
{
    private readonly ILogger<HidDeviceManager> _logger;
    private UsbDeviceWrapper? _usb;
    private volatile bool _isMonitoring;
    private CancellationTokenSource? _monitoringCts;
    private bool _disposed;
    private readonly object _writeLock = new();
    private readonly object _connectLock = new();

    // Flag set by ReadFromUsb when device loss is detected
    private volatile bool _deviceLost;

    // Pending response queue: the monitor thread routes responses here
    private readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]?>> _pendingResponses = new();
    private int _responseIdCounter;

    // Vendor interface endpoints (must match firmware usb_descriptors.c)
    private const int VENDOR_INTERFACE_NUMBER = 1;
    private const byte EP_VENDOR_OUT = 0x02;  // Bulk OUT
    private const byte EP_VENDOR_IN = 0x82;   // Bulk IN

    // Max consecutive USB errors before treating as disconnect
    private const int MAX_CONSECUTIVE_ERRORS = 5;

    public event EventHandler? DeviceConnected;
    public event EventHandler? DeviceDisconnected;
    public event EventHandler<byte[]>? DataReceived;

    public bool IsConnected => _usb != null && _usb.IsOpen && !_deviceLost;

    public HidDeviceManager(ILogger<HidDeviceManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Подключиться к устройству
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        lock (_connectLock)
        {
            if (IsConnected)
            {
                _logger.LogDebug("Already connected to device");
                return true;
            }

            // If there's a stale connection, clean it up first
            if (_usb != null)
            {
                _logger.LogInformation("Cleaning up stale USB connection before reconnect...");
                StopMonitoring();
                CancelAllPendingResponses();
                CleanupDevice();
            }
        }

        try
        {
            _logger.LogInformation("Searching for device (VID: 0x{VID:X4}, PID: 0x{PID:X4})...",
                ProtocolConstants.VendorId, ProtocolConstants.ProductId);

            // Reset device-lost flag
            _deviceLost = false;

            // Create USB wrapper and open device
            var usb = new UsbDeviceWrapper();

            if (!usb.Open(ProtocolConstants.VendorId, ProtocolConstants.ProductId))
            {
                _logger.LogDebug("Device not found (VID: 0x{VID:X4}, PID: 0x{PID:X4})",
                    ProtocolConstants.VendorId, ProtocolConstants.ProductId);
                usb.Dispose();
                return false;
            }

            _logger.LogInformation("USB device opened");

            // Claim the vendor interface and open endpoints
            int rc = usb.ClaimInterface(VENDOR_INTERFACE_NUMBER, EP_VENDOR_OUT, EP_VENDOR_IN);
            if (rc != UsbDeviceWrapper.SUCCESS)
            {
                _logger.LogError("Failed to claim interface {Interface}: {Error}",
                    VENDOR_INTERFACE_NUMBER, UsbDeviceWrapper.GetErrorString(rc));
                usb.Dispose();
                return false;
            }

            _usb = usb;

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
            _logger.LogError(ex, "libusb-1.0 native library not found. This should not happen " +
                "as LibUsbDotNet bundles it. Check your deployment.\n" +
                "  Linux:  sudo apt install libusb-1.0-0\n" +
                "  macOS:  Bundled with LibUsbDotNet\n" +
                "  Windows: Bundled with LibUsbDotNet (WinUSB driver required via Zadig)");
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
    /// Отключиться от устройства (explicit disconnect)
    /// </summary>
    public void Disconnect()
    {
        _logger.LogInformation("Explicit device disconnect requested");
        HandleDisconnect(fireEvent: true);
    }

    /// <summary>
    /// Internal disconnect handler — cleans up and optionally fires event.
    /// Thread-safe: can be called from monitor thread or externally.
    /// </summary>
    private void HandleDisconnect(bool fireEvent)
    {
        lock (_connectLock)
        {
            if (_usb == null)
            {
                // Already disconnected
                return;
            }

            _logger.LogInformation("Handling device disconnect (fireEvent={FireEvent})", fireEvent);

            _deviceLost = true;
            StopMonitoring();
            CancelAllPendingResponses();
            CleanupDevice();
        }

        if (fireEvent)
        {
            _logger.LogInformation("Device disconnected — ready for reconnection");
            DeviceDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Отправить данные на устройство через Vendor Bulk OUT endpoint
    /// </summary>
    public async Task<bool> WriteAsync(byte[] data)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot write: device not connected");
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
                if (_usb == null || !_usb.IsOpen || _deviceLost)
                    return false;

                rc = _usb.BulkWrite(
                    packet,
                    packet.Length,
                    out int bytesWritten,
                    1000); // 1 second timeout
            }

            if (rc == UsbDeviceWrapper.ERROR_NO_DEVICE)
            {
                _logger.LogWarning("Device lost during write");
                _deviceLost = true;
                return false;
            }

            if (rc != UsbDeviceWrapper.SUCCESS)
            {
                _logger.LogError("USB write error: {Error}", UsbDeviceWrapper.GetErrorString(rc));
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
    /// Returns the data, or null on timeout/error.
    /// Sets _deviceLost = true on ERROR_NO_DEVICE.
    /// </summary>
    private byte[]? ReadFromUsb(int timeout = 100)
    {
        if (_usb == null || !_usb.IsOpen || _deviceLost) return null;

        try
        {
            var buffer = new byte[ProtocolConstants.PacketSize];

            int rc = _usb.BulkRead(
                buffer,
                buffer.Length,
                out int bytesRead,
                timeout);

            if (rc == UsbDeviceWrapper.SUCCESS && bytesRead > 0)
            {
                var data = new byte[bytesRead];
                Array.Copy(buffer, 0, data, 0, bytesRead);
                return data;
            }

            if (rc == UsbDeviceWrapper.ERROR_NO_DEVICE)
            {
                _logger.LogWarning("USB device lost (ERROR_NO_DEVICE)");
                _deviceLost = true;
                return null;
            }

            // ERROR_TIMEOUT is normal — no data available
            // Other errors are logged but not treated as disconnect (yet)
            if (rc != UsbDeviceWrapper.SUCCESS && rc != UsbDeviceWrapper.ERROR_TIMEOUT)
            {
                _logger.LogDebug("USB read returned: {Error}", UsbDeviceWrapper.GetErrorString(rc));
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception reading from USB");
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
        
        // Don't dispose here — the monitor thread may still be using it
        // It will be replaced on next StartMonitoring()
        _monitoringCts = null;

        _logger.LogDebug("Device monitoring stop requested");
    }

    /// <summary>
    /// Single reader thread: reads all incoming USB data and routes it.
    /// - If there are pending response waiters → complete the oldest one
    /// - Otherwise → fire DataReceived event (unsolicited device events)
    /// - On device loss → clean up and fire DeviceDisconnected
    /// </summary>
    private async Task MonitorDeviceAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Monitor thread started");
        int consecutiveErrors = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested && !_deviceLost)
            {
                try
                {
                    var data = ReadFromUsb(100);

                    // Check if device was lost during read
                    if (_deviceLost)
                    {
                        _logger.LogWarning("Device lost detected in monitor loop — initiating disconnect");
                        break;
                    }

                    if (data != null && data.Length > 0)
                    {
                        consecutiveErrors = 0; // Reset error counter on successful read

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
                    else
                    {
                        // null data is normal (timeout) — don't count as error
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    consecutiveErrors++;
                    _logger.LogError(ex, "Error in monitor loop (consecutive: {Count})", consecutiveErrors);

                    if (consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                    {
                        _logger.LogError("Too many consecutive errors ({Count}) — treating as device disconnect",
                            consecutiveErrors);
                        _deviceLost = true;
                        break;
                    }

                    // Brief delay before retrying
                    try { await Task.Delay(200, cancellationToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        finally
        {
            _isMonitoring = false;
            _logger.LogDebug("Monitor thread ended (deviceLost={DeviceLost}, cancelled={Cancelled})",
                _deviceLost, cancellationToken.IsCancellationRequested);

            // If device was lost (not an explicit disconnect/cancel), handle cleanup
            if (_deviceLost && !cancellationToken.IsCancellationRequested)
            {
                // Run cleanup on a separate task to avoid deadlocks
                _ = Task.Run(() => HandleDisconnect(fireEvent: true));
            }
        }
    }

    /// <summary>
    /// Cancel all pending response waiters (e.g., on disconnect)
    /// </summary>
    private void CancelAllPendingResponses()
    {
        int count = 0;
        foreach (var kvp in _pendingResponses)
        {
            if (_pendingResponses.TryRemove(kvp.Key, out var tcs))
            {
                tcs.TrySetResult(null);
                count++;
            }
        }
        if (count > 0)
        {
            _logger.LogDebug("Cancelled {Count} pending response waiters", count);
        }
    }

    /// <summary>
    /// Clean up USB device resources.
    /// After this, _usb is null, IsConnected returns false.
    /// </summary>
    private void CleanupDevice()
    {
        try
        {
            if (_usb != null)
            {
                try
                {
                    _usb.ReleaseInterface(VENDOR_INTERFACE_NUMBER);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error releasing interface: {Message}", ex.Message);
                }

                try
                {
                    _usb.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error closing device: {Message}", ex.Message);
                }

                try
                {
                    _usb.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Error disposing USB wrapper: {Message}", ex.Message);
                }

                _usb = null;
            }

            _logger.LogDebug("USB resources cleaned up");
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error during device cleanup: {Message}", ex.Message);
            // Force null even on error
            _usb = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        HandleDisconnect(fireEvent: false);
    }
}
