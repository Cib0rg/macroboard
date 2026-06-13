using MacroKeyboard.Backend;
using MacroKeyboard.Backend.Plugin;
using MacroKeyboard.Backend.Services;
using MacroKeyboard.Communication.HidDevice;
using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Persistence;
using MacroKeyboard.Infrastructure.Repositories;
using MacroKeyboard.Infrastructure.Services;
using MacroKeyboard.Shared.IPC;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

// When running as a Windows Service the default working directory is System32.
// Pin it to the binary directory so that relative paths (logs, data) resolve correctly.
Environment.CurrentDirectory = AppContext.BaseDirectory;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/backend-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting MacroKeyboard Backend Service");

    var builder = Host.CreateApplicationBuilder(args);

    // Configure to run as Windows Service or Linux daemon
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "MacroKeyboard Backend";
    });
    builder.Services.AddSystemd();

    // Add Serilog
    builder.Services.AddSerilog();

    // Register Core services
    builder.Services.AddSingleton<HidDeviceManager>();
    builder.Services.AddSingleton<ProtocolHandler>();
    builder.Services.AddSingleton<IDeviceService, DeviceService>();
    builder.Services.AddSingleton<IProfileService, ProfileService>();
    builder.Services.AddSingleton<ImageService>();

    // Register Infrastructure
    builder.Services.AddSingleton<ProfileRepository>();

    // Register Backend services
    builder.Services.AddSingleton<DeviceManager>();
    builder.Services.AddSingleton<IpcServer>();
    builder.Services.AddSingleton<IIpcServer>(sp => sp.GetRequiredService<IpcServer>());
    builder.Services.AddSingleton<EventRouter>();
    builder.Services.AddSingleton<IpcCommandHandler>();
    builder.Services.AddSingleton<IShellCommandExecutor, ShellCommandExecutor>();
    builder.Services.AddSingleton<ActionExecutorService>();

    // Register Plugin services
    builder.Services.AddSingleton<WebSocketServer>();
    builder.Services.AddSingleton<PluginManager>(sp => new PluginManager(
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PluginManager>>(),
        sp.GetRequiredService<MacroKeyboard.Core.Services.IDeviceService>(),
        sp.GetRequiredService<WebSocketServer>(),
        Path.Combine(AppContext.BaseDirectory, "plugins")));

    // Register the main service
    builder.Services.AddHostedService<BackendService>();

    var host = builder.Build();
    await host.RunAsync();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "MacroKeyboard Backend Service terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
