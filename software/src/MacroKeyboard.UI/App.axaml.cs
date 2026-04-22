using System;
using System.Linq;
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

            // Handle window close: hide to tray instead of exiting
            _mainWindow.Closing += (_, e) =>
            {
                e.Cancel = true;
                _mainWindow.Hide();
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

        // Set icon
        try
        {
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "avalonia-logo.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _trayIcon.Icon = new WindowIcon(iconPath);
            }
        }
        catch
        {
            // Icon not found, will use default
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
        exitItem.Click += (_, _) =>
        {
            _trayIcon.IsVisible = false;
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
                    statusItem.Header = $"Status: {viewModel.StatusText}";
                    _trayIcon.ToolTipText = $"MacroKeyboard - {viewModel.StatusText}";
                }
            };
        }

        _trayIcon.IsVisible = true;
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
