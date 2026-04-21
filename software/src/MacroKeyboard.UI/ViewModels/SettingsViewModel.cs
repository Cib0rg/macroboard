using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using System;
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
                PluginsDirectory = PluginsDirectory
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
            
            await SaveSettings();
            
            _logger.LogInformation("Settings reset successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting settings");
        }
    }
    
    private static string GetSettingsPath()
    {
        var appDataPath = AppDataManager.GetAppDataPath();
        return Path.Combine(appDataPath, SettingsFileName);
    }
}
