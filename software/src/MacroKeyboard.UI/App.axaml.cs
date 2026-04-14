using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MacroKeyboard.Communication.HidDevice;
using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Persistence;
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
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;

            // Initialize
            var viewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
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

        // Core services
        services.AddSingleton<HidDeviceManager>();
        services.AddSingleton<ProtocolHandler>();
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<ImageService>();

        // Infrastructure
        services.AddSingleton<ProfileRepository>();

        // IPC Client
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
