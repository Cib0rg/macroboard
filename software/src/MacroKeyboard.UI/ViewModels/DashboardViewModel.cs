using CommunityToolkit.Mvvm.ComponentModel;
using MacroKeyboard.Shared.Events;
using MacroKeyboard.Shared.IPC;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
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
        try
        {
            switch (message.MessageType)
            {
                case IpcMessageTypes.DeviceConnected:
                {
                    var deviceEvent = TryConvertData<DeviceEventArgs>(message.Data);
                    if (deviceEvent != null)
                    {
                        DeviceName = deviceEvent.DeviceName;
                        FirmwareVersion = deviceEvent.FirmwareVersion;
                        IsDeviceConnected = true;
                        AddEvent($"Device connected: {deviceEvent.DeviceName}");
                    }
                    break;
                }

                case IpcMessageTypes.DeviceDisconnected:
                    DeviceName = "No device";
                    FirmwareVersion = "N/A";
                    IsDeviceConnected = false;
                    AddEvent("Device disconnected");
                    break;

                case IpcMessageTypes.ButtonPressed:
                {
                    var buttonEvent = TryConvertData<ButtonEventArgs>(message.Data);
                    if (buttonEvent != null)
                    {
                        AddEvent($"Button {buttonEvent.ButtonIndex} pressed");
                    }
                    break;
                }

                case IpcMessageTypes.ProfileChanged:
                {
                    var profileEvent = TryConvertData<ProfileChangedEventArgs>(message.Data);
                    if (profileEvent != null)
                    {
                        CurrentProfile = profileEvent.ProfileName;
                        AddEvent($"Profile changed to: {profileEvent.ProfileName}");
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

    /// <summary>
    /// Safely convert IPC message Data to the expected type.
    /// Handles both direct type match and JObject deserialization (from JSON).
    /// </summary>
    private T? TryConvertData<T>(object? data) where T : class
    {
        if (data is T typed)
            return typed;

        if (data is JObject jObject)
        {
            try
            {
                return jObject.ToObject<T>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to convert JObject to {Type}", typeof(T).Name);
            }
        }

        _logger.LogWarning("Cannot convert IPC data to {Type}, actual type: {ActualType}",
            typeof(T).Name, data?.GetType().Name ?? "null");
        return null;
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
