using HidSharp;
using Microsoft.Extensions.Logging;
using MacroKeyboard.Communication.Protocol;

namespace MacroKeyboard.Communication.HidDevice;

/// <summary>
/// Менеджер для работы с USB HID устройством (кроссплатформенный)
/// </summary>
public class HidDeviceManager : IDisposable
{
    private readonly ILogger<HidDeviceManager> _logger;
    private HidSharp.HidDevice? _hidDevice;
    private HidSharp.HidStream? _stream;
    private bool _isMonitoring;
    private CancellationTokenSource? _monitoringCts;
    
    public event EventHandler? DeviceConnected;
    public event EventHandler? DeviceDisconnected;
    public event EventHandler<byte[]>? DataReceived;
    
    public bool IsConnected => _stream != null && _stream.CanRead && _stream.CanWrite;
    
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
            
            var deviceList = DeviceList.Local;
            var devices = deviceList.GetHidDevices(ProtocolConstants.VendorId, ProtocolConstants.ProductId).ToList();
            
            _logger.LogInformation("Found {Count} HID interface(s)", devices.Count);
            
            // For composite device with Keyboard + Raw interfaces:
            // - Interface 0: Keyboard (UsagePage=1, Usage=6)
            // - Interface 1: Raw HID (UsagePage=0xFF00 or Generic)
            // We want the Raw interface (usually the second one)
            _hidDevice = devices.Count > 1 ? devices[1] : devices.FirstOrDefault();
            
            if (_hidDevice == null)
            {
                _logger.LogWarning("Device not found");
                return false;
            }
            
            _logger.LogInformation("Found device: {ProductName} by {Manufacturer}", 
                _hidDevice.GetProductName(), _hidDevice.GetManufacturer());
            
            // Открыть устройство
            if (!_hidDevice.TryOpen(out _stream))
            {
                _logger.LogError("Failed to open device");
                return false;
            }
            
            _logger.LogInformation("Device connected successfully");
            
            // Запустить мониторинг событий
            StartMonitoring();
            
            DeviceConnected?.Invoke(this, EventArgs.Empty);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to device");
            return false;
        }
    }
    
    /// <summary>
    /// Отключиться от устройства
    /// </summary>
    public void Disconnect()
    {
        StopMonitoring();
        
        if (_stream != null)
        {
            _stream.Close();
            _stream.Dispose();
            _stream = null;
            
            _logger.LogInformation("Device disconnected");
            DeviceDisconnected?.Invoke(this, EventArgs.Empty);
        }
        
        _hidDevice = null;
    }
    
    /// <summary>
    /// Отправить данные на устройство
    /// </summary>
    public async Task<bool> WriteAsync(byte[] data)
    {
        if (_stream == null || !_stream.CanWrite)
        {
            _logger.LogWarning("Device not connected or cannot write");
            return false;
        }
        
        try
        {
            // HID report: Report ID (1 byte) + data (64 bytes) = 65 bytes total
            // But we send data directly without Report ID prefix since device expects 64 bytes
            var report = new byte[65];
            report[0] = 0; // Report ID (0 для устройств без report ID)
            Array.Copy(data, 0, report, 1, Math.Min(data.Length, 64));
            
            await _stream.WriteAsync(report, 0, report.Length);
            
            _logger.LogDebug("Sent {Length} bytes to device (with Report ID)", data.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to device");
            return false;
        }
    }
    
    /// <summary>
    /// Прочитать данные с устройства
    /// </summary>
    public async Task<byte[]?> ReadAsync(int timeout = 1000)
    {
        if (_stream == null || !_stream.CanRead)
        {
            _logger.LogWarning("Device not connected or cannot read");
            return null;
        }
        
        try
        {
            var buffer = new byte[64];
            
            using var cts = new CancellationTokenSource(timeout);
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            
            if (bytesRead > 0)
            {
                // Пропустить первый байт (Report ID)
                var data = new byte[bytesRead - 1];
                Array.Copy(buffer, 1, data, 0, data.Length);
                
                _logger.LogDebug("Received {Length} bytes from device", data.Length);
                return data;
            }
            
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Read timeout");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from device");
            return null;
        }
    }
    
    /// <summary>
    /// Запустить мониторинг событий от устройства
    /// </summary>
    private void StartMonitoring()
    {
        if (_isMonitoring)
        {
            return;
        }
        
        _isMonitoring = true;
        _monitoringCts = new CancellationTokenSource();
        
        _ = Task.Run(async () => await MonitorDeviceAsync(_monitoringCts.Token));
        
        _logger.LogDebug("Device monitoring started");
    }
    
    /// <summary>
    /// Остановить мониторинг событий
    /// </summary>
    private void StopMonitoring()
    {
        if (!_isMonitoring)
        {
            return;
        }
        
        _isMonitoring = false;
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _monitoringCts = null;
        
        _logger.LogDebug("Device monitoring stopped");
    }
    
    /// <summary>
    /// Мониторинг событий от устройства
    /// </summary>
    private async Task MonitorDeviceAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Monitoring device events...");
        
        while (!cancellationToken.IsCancellationRequested && _stream != null)
        {
            try
            {
                var data = await ReadAsync(100);
                
                if (data != null && data.Length > 0)
                {
                    DataReceived?.Invoke(this, data);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in device monitoring loop");
                
                // Если устройство отключено, выйти из цикла
                if (_stream == null || !_stream.CanRead)
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
    
    public void Dispose()
    {
        Disconnect();
    }
}
