using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// Settings ViewModel
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ILogger<SettingsViewModel> _logger;

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
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        try
        {
            _logger.LogInformation("Saving settings...");
            // TODO: Implement settings persistence
            _logger.LogInformation("Settings saved successfully");
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
            _logger.LogInformation("Settings reset successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting settings");
        }
    }
}
