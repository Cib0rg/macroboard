using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MacroKeyboard.Shared.IPC;
using MacroKeyboard.TrayApp.ViewModels;
using MacroKeyboard.TrayApp.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MacroKeyboard.TrayApp;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

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
            .WriteTo.File("logs/trayapp-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create tray icon
            var trayIcon = _serviceProvider.GetRequiredService<TrayIconView>();
            
            // Initialize
            var viewModel = _serviceProvider.GetRequiredService<TrayIconViewModel>();
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // Services
        services.AddSingleton<IpcClient>();
        
        // ViewModels
        services.AddSingleton<TrayIconViewModel>();
        
        // Views
        services.AddSingleton<TrayIconView>();
    }
}
