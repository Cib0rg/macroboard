using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// Settings ViewModel
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ILogger<SettingsViewModel> _logger;
    private const string SettingsFileName = "settings.json";

    [ObservableProperty]
    private bool _autoStart = false;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private int _ipcPort = 28195;

    [ObservableProperty]
    private int _webSocketPort = 28196;

    [ObservableProperty]
    private string _pluginsDirectory = "Plugins";

    [ObservableProperty]
    private string _defaultLedColorHex = "#00FFFF";

    [ObservableProperty]
    private Color _defaultLedColor = Color.FromRgb(0, 255, 255);

    [ObservableProperty]
    private byte _defaultLedBrightness = 200;

    [ObservableProperty]
    private bool _isDefaultColorPickerVisible = false;

    private bool _isUpdatingDefaultColor = false;

    public SettingsViewModel(ILogger<SettingsViewModel> logger)
    {
        _logger = logger;
        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settingsPath = GetSettingsPath();
            
            if (!File.Exists(settingsPath))
            {
                _logger.LogInformation("Settings file not found, using defaults");
                return;
            }

            _logger.LogInformation("Loading settings from {Path}", settingsPath);
            
            var json = await File.ReadAllTextAsync(settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            
            if (settings != null)
            {
                AutoStart = settings.AutoStart;
                MinimizeToTray = settings.MinimizeToTray;
                ShowNotifications = settings.ShowNotifications;
                IpcPort = settings.IpcPort;
                WebSocketPort = settings.WebSocketPort;
                PluginsDirectory = settings.PluginsDirectory;
                DefaultLedColorHex = settings.DefaultLedColor;
                DefaultLedBrightness = settings.DefaultLedBrightness;
                UpdateDefaultColorFromHex();
                
                _logger.LogInformation("Settings loaded successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings");
        }
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        try
        {
            _logger.LogInformation("Saving settings...");
            
            var settings = new AppSettings
            {
                AutoStart = AutoStart,
                MinimizeToTray = MinimizeToTray,
                ShowNotifications = ShowNotifications,
                IpcPort = IpcPort,
                WebSocketPort = WebSocketPort,
                PluginsDirectory = PluginsDirectory,
                DefaultLedColor = DefaultLedColorHex,
                DefaultLedBrightness = DefaultLedBrightness
            };
            
            var settingsPath = GetSettingsPath();
            var directory = Path.GetDirectoryName(settingsPath);
            
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(settingsPath, json);
            
            _logger.LogInformation("Settings saved successfully to {Path}", settingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
        }
    }

    [RelayCommand]
    private async Task ResetSettings()
    {
        try
        {
            _logger.LogInformation("Resetting settings to defaults...");
            AutoStart = false;
            MinimizeToTray = true;
            ShowNotifications = true;
            IpcPort = 28195;
            WebSocketPort = 28196;
            PluginsDirectory = "Plugins";
            DefaultLedColorHex = "#00FFFF";
            DefaultLedBrightness = 200;
            UpdateDefaultColorFromHex();
            
            await SaveSettings();
            
            _logger.LogInformation("Settings reset successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting settings");
        }
    }
    
    [RelayCommand]
    private void ToggleDefaultColorPicker()
    {
        IsDefaultColorPickerVisible = !IsDefaultColorPickerVisible;
    }

    /// <summary>
    /// Called when ColorPicker changes the DefaultLedColor
    /// </summary>
    partial void OnDefaultLedColorChanged(Color value)
    {
        if (_isUpdatingDefaultColor) return;
        _isUpdatingDefaultColor = true;
        try
        {
            DefaultLedColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        }
        finally
        {
            _isUpdatingDefaultColor = false;
        }
    }

    partial void OnDefaultLedColorHexChanged(string value)
    {
        if (_isUpdatingDefaultColor) return;
        UpdateDefaultColorFromHex();
    }

    private void UpdateDefaultColorFromHex()
    {
        _isUpdatingDefaultColor = true;
        try
        {
            var hex = DefaultLedColorHex.TrimStart('#');
            if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var colorValue))
            {
                var r = (byte)((colorValue >> 16) & 0xFF);
                var g = (byte)((colorValue >> 8) & 0xFF);
                var b = (byte)(colorValue & 0xFF);
                DefaultLedColor = Color.FromRgb(r, g, b);
            }
        }
        finally
        {
            _isUpdatingDefaultColor = false;
        }
    }

    /// <summary>
    /// Get the current default LED color as RGB bytes (for use by other ViewModels)
    /// </summary>
    public (byte R, byte G, byte B, byte Brightness) GetDefaultLedColor()
    {
        return (DefaultLedColor.R, DefaultLedColor.G, DefaultLedColor.B, DefaultLedBrightness);
    }

    private static string GetSettingsPath()
    {
        var appDataPath = AppDataManager.GetAppDataPath();
        return Path.Combine(appDataPath, SettingsFileName);
    }
}
