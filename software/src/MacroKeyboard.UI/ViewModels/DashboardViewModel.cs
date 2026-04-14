using CommunityToolkit.Mvvm.ComponentModel;
using MacroKeyboard.Shared.Events;
using MacroKeyboard.Shared.IPC;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;

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
    private bool _isDeviceConnected;

    public ObservableCollection<string> RecentEvents { get; } = new();

    public DashboardViewModel(IpcClient ipcClient, ILogger<DashboardViewModel> logger)
    {
        _ipcClient = ipcClient;
        _logger = logger;

        // Subscribe to IPC events
        _ipcClient.MessageReceived += OnIpcMessageReceived;
    }

    private void OnIpcMessageReceived(object? sender, IpcMessage message)
    {
        switch (message.MessageType)
        {
            case IpcMessageTypes.DeviceConnected:
                if (message.Data is DeviceEventArgs deviceEvent)
                {
                    DeviceName = deviceEvent.DeviceName;
                    FirmwareVersion = deviceEvent.FirmwareVersion;
                    IsDeviceConnected = true;
                    AddEvent($"Device connected: {deviceEvent.DeviceName}");
                }
                break;

            case IpcMessageTypes.DeviceDisconnected:
                DeviceName = "No device";
                FirmwareVersion = "N/A";
                IsDeviceConnected = false;
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
                    CurrentProfile = profileEvent.ProfileName;
                    AddEvent($"Profile changed to: {profileEvent.ProfileName}");
                }
                break;
        }
    }

    private void AddEvent(string eventText)
    {
        RecentEvents.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {eventText}");

        // Keep only last 20 events
        while (RecentEvents.Count > 20)
        {
            RecentEvents.RemoveAt(RecentEvents.Count - 1);
        }
    }
}
