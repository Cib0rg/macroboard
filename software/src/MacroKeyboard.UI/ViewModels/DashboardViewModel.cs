using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Shared.Events;
using MacroKeyboard.Shared.IPC;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// Dashboard ViewModel - показывает статус устройства и последние события
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly IpcClient _ipcClient;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty]
    private string _deviceName = "No device";

    [ObservableProperty]
    private string _firmwareVersion = "N/A";

    [ObservableProperty]
    private string _currentProfile = "N/A";

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private bool _isDeviceConnected;

    [ObservableProperty]
    private byte _buttonCount;

    [ObservableProperty]
    private byte _profileCount;

    [ObservableProperty]
    private string _freeSpace = "N/A";

    public ObservableCollection<string> RecentEvents { get; } = new();

    public DashboardViewModel(IpcClient ipcClient, ILogger<DashboardViewModel> logger)
    {
        _ipcClient = ipcClient;
        _logger = logger;

        // Subscribe to IPC events
        _ipcClient.MessageReceived += OnIpcMessageReceived;
        _ipcClient.Connected += OnIpcConnected;
        _ipcClient.Disconnected += OnIpcDisconnected;
    }

    private async void OnIpcConnected(object? sender, EventArgs e)
    {
        ConnectionStatus = "Connected to Backend";
        AddEvent("Connected to Backend service");
        
        // Request device info when we connect to backend
        await RequestDeviceInfoAsync();
    }

    private void OnIpcDisconnected(object? sender, EventArgs e)
    {
        ConnectionStatus = "Disconnected from Backend";
        IsDeviceConnected = false;
        DeviceName = "No device";
        FirmwareVersion = "N/A";
        CurrentProfile = "N/A";
        AddEvent("Disconnected from Backend service");
    }

    /// <summary>
    /// Request device info from backend
    /// </summary>
    [RelayCommand]
    private async Task RequestDeviceInfoAsync()
    {
        try
        {
            if (!_ipcClient.IsConnected)
            {
                _logger.LogWarning("Cannot request device info: not connected to backend");
                return;
            }

            var message = new IpcMessage
            {
                MessageType = IpcMessageTypes.GetDeviceInfo
            };

            var response = await _ipcClient.SendAndWaitAsync(message, TimeSpan.FromSeconds(5));
            
            if (response.Success)
            {
                var deviceInfo = response.GetData<DeviceInfo>();
                if (deviceInfo != null)
                {
                    DeviceName = "MacroKeyboard";
                    FirmwareVersion = deviceInfo.FirmwareVersion.ToString();
                    ButtonCount = deviceInfo.ButtonCount;
                    ProfileCount = deviceInfo.ProfileCount;
                    CurrentProfile = $"Profile {deviceInfo.CurrentProfile}";
                    FreeSpace = FormatBytes(deviceInfo.FreeSpace);
                    IsDeviceConnected = deviceInfo.IsConnected;
                    ConnectionStatus = deviceInfo.IsConnected ? "Device Connected" : "Device Not Found";
                    AddEvent($"Device info received: FW {deviceInfo.FirmwareVersion}");
                }
            }
            else
            {
                _logger.LogWarning("Failed to get device info: {Error}", response.Error);
                ConnectionStatus = response.Error ?? "Device not available";
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Device info request timed out");
            ConnectionStatus = "Request timed out";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting device info");
        }
    }

    /// <summary>
    /// Ping the backend to check connectivity
    /// </summary>
    [RelayCommand]
    private async Task PingBackendAsync()
    {
        try
        {
            if (!_ipcClient.IsConnected)
            {
                AddEvent("Cannot ping: not connected to backend");
                return;
            }

            var message = new IpcMessage
            {
                MessageType = IpcMessageTypes.Ping
            };

            var response = await _ipcClient.SendAndWaitAsync(message, TimeSpan.FromSeconds(3));
            
            if (response.Success)
            {
                AddEvent("Ping OK — Backend is responsive");
            }
            else
            {
                AddEvent($"Ping failed: {response.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            AddEvent("Ping timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pinging backend");
            AddEvent($"Ping error: {ex.Message}");
        }
    }

    private void OnIpcMessageReceived(object? sender, IpcMessage message)
    {
        try
        {
            switch (message.MessageType)
            {
                case IpcMessageTypes.DeviceConnected:
                {
                    var deviceEvent = message.GetData<DeviceEventArgs>();
                    if (deviceEvent != null)
                    {
                        DeviceName = deviceEvent.DeviceName;
                        FirmwareVersion = deviceEvent.FirmwareVersion;
                        IsDeviceConnected = true;
                        ConnectionStatus = "Device Connected";
                        AddEvent($"Device connected: {deviceEvent.DeviceName}");
                    }
                    else
                    {
                        IsDeviceConnected = true;
                        ConnectionStatus = "Device Connected";
                        AddEvent("Device connected");
                    }
                    break;
                }

                case IpcMessageTypes.DeviceDisconnected:
                    DeviceName = "No device";
                    FirmwareVersion = "N/A";
                    CurrentProfile = "N/A";
                    IsDeviceConnected = false;
                    ConnectionStatus = "Device Disconnected";
                    AddEvent("Device disconnected");
                    break;

                case IpcMessageTypes.ButtonPressed:
                {
                    var buttonEvent = message.GetData<ButtonEventArgs>();
                    if (buttonEvent != null)
                    {
                        AddEvent($"🔘 Button {buttonEvent.ButtonIndex} pressed");
                    }
                    break;
                }

                case IpcMessageTypes.ButtonReleased:
                {
                    var buttonEvent = message.GetData<ButtonEventArgs>();
                    if (buttonEvent != null)
                    {
                        AddEvent($"⚪ Button {buttonEvent.ButtonIndex} released");
                    }
                    break;
                }

                case IpcMessageTypes.EncoderRotated:
                {
                    var encoderEvent = message.GetData<EncoderEventArgs>();
                    if (encoderEvent != null)
                    {
                        var direction = encoderEvent.Delta > 0 ? "→ CW" : "← CCW";
                        AddEvent($"🔄 Encoder rotated {direction} (delta: {encoderEvent.Delta})");
                    }
                    break;
                }

                case IpcMessageTypes.ProfileChanged:
                {
                    var profileEvent = message.GetData<ProfileChangedEventArgs>();
                    if (profileEvent != null)
                    {
                        CurrentProfile = profileEvent.ProfileName;
                        AddEvent($"📋 Profile changed to: {profileEvent.ProfileName}");
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing IPC message: {MessageType}", message.MessageType);
        }
    }

    private void AddEvent(string eventText)
    {
        // Ensure we're on the UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RecentEvents.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {eventText}");

            // Keep only last 50 events
            while (RecentEvents.Count > 50)
            {
                RecentEvents.RemoveAt(RecentEvents.Count - 1);
            }
        });
    }

    private static string FormatBytes(uint bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
