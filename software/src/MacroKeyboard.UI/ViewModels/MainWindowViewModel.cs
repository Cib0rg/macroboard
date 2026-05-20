using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Shared.IPC;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// Main Window ViewModel
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IpcClient _ipcClient;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _statusText = "Disconnected";

    [ObservableProperty]
    private bool _isConnected;

    public DashboardViewModel DashboardViewModel { get; }
    public ProfileEditorViewModel ProfileEditorViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    public MainWindowViewModel(
        IpcClient ipcClient,
        ILogger<MainWindowViewModel> logger,
        DashboardViewModel dashboardViewModel,
        ProfileEditorViewModel profileEditorViewModel,
        SettingsViewModel settingsViewModel)
    {
        _ipcClient = ipcClient;
        _logger = logger;
        DashboardViewModel = dashboardViewModel;
        ProfileEditorViewModel = profileEditorViewModel;
        SettingsViewModel = settingsViewModel;

        _currentPage = DashboardViewModel;

        // Subscribe to IPC events
        _ipcClient.Connected += OnIpcConnected;
        _ipcClient.Disconnected += OnIpcDisconnected;
        _ipcClient.Reconnecting += OnIpcReconnecting;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Connecting to backend...");
            StatusText = "Connecting...";
            await _ipcClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to backend");
            StatusText = "Reconnecting...";
            // ConnectAsync already enables auto-reconnect, so the IpcClient
            // will keep retrying in the background after the initial failure.
        }
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        CurrentPage = DashboardViewModel;
    }

    [RelayCommand]
    private void NavigateToProfileEditor()
    {
        CurrentPage = ProfileEditorViewModel;
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        CurrentPage = SettingsViewModel;
    }

    private void OnIpcConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Connected to backend");
        IsConnected = true;
        StatusText = "Connected";
    }

    private void OnIpcDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("Disconnected from backend");
        IsConnected = false;
        StatusText = "Disconnected — reconnecting...";
    }

    private void OnIpcReconnecting(object? sender, int attempt)
    {
        _logger.LogInformation("Reconnection attempt {Attempt}...", attempt);
        StatusText = $"Reconnecting (attempt {attempt})...";
    }
}
