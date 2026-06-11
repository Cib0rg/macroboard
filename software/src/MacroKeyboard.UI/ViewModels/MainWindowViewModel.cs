using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Shared.IPC;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// Main Window ViewModel
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IpcClient _ipcClient;
    private readonly ILogger<MainWindowViewModel> _logger;
    private CancellationTokenSource? _reconnectCts;

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

        _ipcClient.Connected += OnIpcConnected;
        _ipcClient.Disconnected += OnIpcDisconnected;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Connecting to backend...");
        StatusText = "Connecting...";
        await TryConnectWithReconnectAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void NavigateToDashboard() => CurrentPage = DashboardViewModel;

    [RelayCommand]
    private void NavigateToProfileEditor() => CurrentPage = ProfileEditorViewModel;

    [RelayCommand]
    private void NavigateToSettings() => CurrentPage = SettingsViewModel;

    private void OnIpcConnected(object? sender, EventArgs e)
    {
        _reconnectCts?.Cancel();
        _logger.LogInformation("Connected to backend");
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsConnected = true;
            StatusText = "Connected";
        });
    }

    private void OnIpcDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("Disconnected from backend");

        // Start reconnect loop BEFORE touching any UI properties — a PropertyChanged
        // subscriber in App.axaml.cs tries to update native tray-icon objects which
        // require the UI thread; setting properties first would throw and abort this
        // handler before Task.Run is ever reached.
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _reconnectCts, cts)?.Cancel();
        _ = Task.Run(() => ReconnectLoopAsync(cts.Token));

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsConnected = false;
            StatusText = "Reconnecting...";
        });
    }

    /// <summary>
    /// Try to connect once. If it fails, start a reconnect loop.
    /// Used for initial connection at startup.
    /// </summary>
    private async Task TryConnectWithReconnectAsync(CancellationToken appToken)
    {
        try
        {
            await _ipcClient.ConnectAsync(appToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial connection to backend failed — starting reconnect loop");

            var cts = new CancellationTokenSource();
            Interlocked.Exchange(ref _reconnectCts, cts)?.Cancel();
            _ = Task.Run(() => ReconnectLoopAsync(cts.Token));

            Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = "Reconnecting...");
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            _logger.LogInformation("Reconnecting to backend (attempt {Attempt}, next in {Delay}s)...",
                attempt, delay.TotalSeconds);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                StatusText = $"Reconnecting (attempt {attempt})...");

            try
            {
                await _ipcClient.ConnectAsync(cancellationToken);
                // Success — OnIpcConnected will cancel this loop
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Reconnect attempt {Attempt} failed", attempt);
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }
}
