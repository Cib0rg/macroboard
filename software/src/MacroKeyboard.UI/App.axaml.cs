using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MacroKeyboard.Communication.HidDevice;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Repositories;
using MacroKeyboard.Infrastructure.Services;
using MacroKeyboard.Shared.IPC;
using MacroKeyboard.UI.ViewModels;
using MacroKeyboard.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MacroKeyboard.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/ui-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Don't shutdown when main window closes — keep running in tray
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            desktop.MainWindow = _mainWindow;

            // Handle window close: hide to tray, but warn about unsaved changes first
            _mainWindow.Closing += async (_, e) =>
            {
                e.Cancel = true;
                var vm = _serviceProvider?.GetRequiredService<ProfileEditorViewModel>();
                if (vm?.HasUnsavedChanges == true)
                {
                    var save = await ShowUnsavedChangesDialog(_mainWindow);
                    if (save == null) return; // Cancel — keep window open
                    if (save == true) await vm.SaveCurrentProfileAsync();
                }
                _mainWindow?.Hide();
            };

            // Setup tray icon
            SetupTrayIcon(desktop);

            // Initialize backend connection
            var viewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            _ = viewModel.InitializeAsync();

            // Start with window visible (use --minimized arg to start hidden)
            var startMinimized = desktop.Args?.Contains("--minimized") == true;
            if (!startMinimized)
            {
                _mainWindow.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _trayIcon = new TrayIcon();

        // Set icon from embedded resource
        try
        {
            var uri = new Uri("avares://MacroKeyboard.UI/Assets/app-icon.ico");
            var stream = Avalonia.Platform.AssetLoader.Open(uri);
            _trayIcon.Icon = new WindowIcon(stream);
        }
        catch
        {
            // Fallback: try file path
            try
            {
                var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app-icon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    _trayIcon.Icon = new WindowIcon(iconPath);
                }
            }
            catch { /* Icon not found, will use default */ }
        }

        _trayIcon.ToolTipText = "MacroKeyboard";

        // Create context menu
        var menu = new NativeMenu();

        var statusItem = new NativeMenuItem
        {
            Header = "Status: Connecting...",
            IsEnabled = false
        };
        menu.Add(statusItem);

        menu.Add(new NativeMenuItemSeparator());

        var showItem = new NativeMenuItem { Header = "Configuration..." };
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem { Header = "Exit" };
        exitItem.Click += async (_, _) =>
        {
            try
            {
                var vm = _serviceProvider?.GetRequiredService<ProfileEditorViewModel>();
                if (vm?.HasUnsavedChanges == true)
                {
                    // ShowDialog requires a visible parent — bring window up first
                    ShowMainWindow();
                    var save = await ShowUnsavedChangesDialog(_mainWindow);
                    if (save == null) return; // Cancel — stay running
                    if (save == true) await vm.SaveCurrentProfileAsync();
                }
            }
            catch { /* ensure we can always exit */ }
            _trayIcon!.IsVisible = false;
            desktop.Shutdown();
        };
        menu.Add(exitItem);

        _trayIcon.Menu = menu;

        // Double-click on tray icon opens the window
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        // Update status text when connection state changes
        var viewModel = _serviceProvider?.GetRequiredService<MainWindowViewModel>();
        if (viewModel != null)
        {
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.StatusText))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        statusItem.Header = $"Status: {viewModel.StatusText}";
                        _trayIcon!.ToolTipText = $"MacroKeyboard - {viewModel.StatusText}";
                    });
                }
            };
        }

        _trayIcon.IsVisible = true;
    }

    /// <summary>
    /// Returns true = save, false = discard, null = cancel.
    /// </summary>
    private static async Task<bool?> ShowUnsavedChangesDialog(Window? parent)
    {
        bool? result = null;

        var saveBtn    = new Button { Content = "Save",    Width = 80, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        var discardBtn = new Button { Content = "Discard", Width = 80, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        var cancelBtn  = new Button { Content = "Cancel",  Width = 80 };

        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 20,
                Children =
                {
                    new TextBlock
                    {
                        Text = "You have unsaved changes. Save before closing?",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 14
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { saveBtn, discardBtn, cancelBtn }
                    }
                }
            }
        };

        saveBtn.Click    += (_, _) => { result = true;  dialog.Close(); };
        discardBtn.Click += (_, _) => { result = false; dialog.Close(); };
        cancelBtn.Click  += (_, _) => {                 dialog.Close(); };

        if (parent != null)
            await dialog.ShowDialog(parent);
        else
            dialog.Show();

        return result;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // Device services (needed by ProfileService dependency chain)
        // Note: In UI context, DeviceService won't actively connect to USB.
        // All device commands go through IPC to Backend.
        services.AddSingleton<HidDeviceManager>();
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<ImageService>();

        // Infrastructure
        services.AddSingleton<ProfileRepository>();

        // IPC Client (communication with Backend service)
        services.AddSingleton<IpcClient>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ProfileEditorViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }
}
