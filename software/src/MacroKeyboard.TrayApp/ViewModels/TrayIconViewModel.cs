using MacroKeyboard.Shared.Events;
using MacroKeyboard.Shared.IPC;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace MacroKeyboard.TrayApp.ViewModels;

/// <summary>
/// ViewModel для системного трея
/// </summary>
public class TrayIconViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IpcClient _ipcClient;
    private readonly ILogger<TrayIconViewModel> _logger;
    private string _statusText = "Disconnected";
    private bool _isConnected;
    private string _deviceName = "No device";
    private string _firmwareVersion = "N/A";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged();
            StatusText = value ? "Connected" : "Disconnected";
        }
    }

    public string DeviceName
    {
        get => _deviceName;
        set
        {
            _deviceName = value;
            OnPropertyChanged();
        }
    }

    public string FirmwareVersion
    {
        get => _firmwareVersion;
        set
        {
            _firmwareVersion = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> RecentEvents { get; } = new();

    public TrayIconViewModel(IpcClient ipcClient, ILogger<TrayIconViewModel> logger)
    {
        _ipcClient = ipcClient;
        _logger = logger;

        // Subscribe to IPC events
        _ipcClient.Connected += OnIpcConnected;
        _ipcClient.Disconnected += OnIpcDisconnected;
        _ipcClient.MessageReceived += OnIpcMessageReceived;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing TrayApp...");
            await _ipcClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to backend");
            StatusText = "Backend not running";
        }
    }

    private void OnIpcConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Connected to backend");
        IsConnected = true;

        // Request device info
        _ = Task.Run(async () =>
        {
            try
            {
                var message = new IpcMessage
                {
                    MessageType = IpcMessageTypes.DeviceInfo
                };
                await _ipcClient.SendAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting device info");
            }
        });
    }

    private void OnIpcDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("Disconnected from backend");
        IsConnected = false;
        DeviceName = "No device";
        FirmwareVersion = "N/A";
    }

    private void OnIpcMessageReceived(object? sender, IpcMessage message)
    {
        _logger.LogDebug("Received message: {MessageType}", message.MessageType);

        switch (message.MessageType)
        {
            case IpcMessageTypes.DeviceConnected:
                if (message.Data is DeviceEventArgs deviceEvent)
                {
                    DeviceName = deviceEvent.DeviceName;
                    FirmwareVersion = deviceEvent.FirmwareVersion;
                    AddEvent($"Device connected: {deviceEvent.DeviceName}");
                }
                break;

            case IpcMessageTypes.DeviceDisconnected:
                DeviceName = "No device";
                FirmwareVersion = "N/A";
                AddEvent("Device disconnected");
                break;

            case IpcMessageTypes.ButtonPressed:
                if (message.Data is ButtonEventArgs buttonEvent)
                {
                    AddEvent($"Button {buttonEvent.ButtonIndex} pressed");
                }
                break;

            case IpcMessageTypes.ProfileChanged:
                if (message.Data is ProfileChangedEventArgs profileEvent)
                {
                    AddEvent($"Profile changed to: {profileEvent.ProfileName}");
                }
                break;
        }
    }

    private void AddEvent(string eventText)
    {
        RecentEvents.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {eventText}");

        // Keep only last 10 events
        while (RecentEvents.Count > 10)
        {
            RecentEvents.RemoveAt(RecentEvents.Count - 1);
        }
    }

    public void ShowConfiguration()
    {
        try
        {
            _logger.LogInformation("Opening configuration UI...");
            
            // Check if UI is already running
            var uiProcessName = "MacroKeyboard.UI";
            var runningProcesses = Process.GetProcessesByName(uiProcessName);
            
            if (runningProcesses.Length > 0)
            {
                _logger.LogInformation("UI is already running, bringing to front");
                // UI is already running, just bring it to front
                // Note: Actual window activation would require platform-specific code
                return;
            }
            
            // Find UI executable
            var baseDirectory = AppContext.BaseDirectory;
            var uiExecutable = Path.Combine(baseDirectory, $"{uiProcessName}.exe");
            
            // On Linux/Mac, try without .exe extension
            if (!File.Exists(uiExecutable))
            {
                uiExecutable = Path.Combine(baseDirectory, uiProcessName);
            }
            
            // Try parent directory (common in development)
            if (!File.Exists(uiExecutable))
            {
                var parentDir = Directory.GetParent(baseDirectory)?.FullName;
                if (parentDir != null)
                {
                    uiExecutable = Path.Combine(parentDir, uiProcessName, $"{uiProcessName}.exe");
                    if (!File.Exists(uiExecutable))
                    {
                        uiExecutable = Path.Combine(parentDir, uiProcessName, uiProcessName);
                    }
                }
            }
            
            if (!File.Exists(uiExecutable))
            {
                _logger.LogError("UI executable not found. Searched: {BaseDirectory}", baseDirectory);
                return;
            }
            
            _logger.LogInformation("Launching UI from: {Path}", uiExecutable);
            
            var startInfo = new ProcessStartInfo
            {
                FileName = uiExecutable,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(uiExecutable)
            };
            
            Process.Start(startInfo);
            _logger.LogInformation("UI launched successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error launching configuration UI");
        }
    }

    public void Exit()
    {
        _logger.LogInformation("Exiting TrayApp...");
        Dispose();
        Environment.Exit(0);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _ipcClient.Connected -= OnIpcConnected;
        _ipcClient.Disconnected -= OnIpcDisconnected;
        _ipcClient.MessageReceived -= OnIpcMessageReceived;
        _ipcClient.Dispose();
    }
}
