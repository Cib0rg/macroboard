using MacroKeyboard.Backend.Plugin;
using MacroKeyboard.Backend.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedEvents = MacroKeyboard.Shared.Events;

namespace MacroKeyboard.Backend;

/// <summary>
/// Main backend service (Windows Service / Linux daemon)
/// </summary>
public class BackendService : BackgroundService
{
    private readonly ILogger<BackendService> _logger;
    private readonly DeviceManager _deviceManager;
    private readonly IpcServer _ipcServer;
    private readonly EventRouter _eventRouter;
    private readonly IpcCommandHandler _commandHandler;
    private readonly ActionExecutorService _actionExecutor;
    private readonly WebSocketServer _webSocketServer;
    private readonly PropertyInspectorServer _piServer;
    private readonly PluginManager _pluginManager;

    public BackendService(
        ILogger<BackendService> logger,
        DeviceManager deviceManager,
        IpcServer ipcServer,
        EventRouter eventRouter,
        IpcCommandHandler commandHandler,
        ActionExecutorService actionExecutor,
        WebSocketServer webSocketServer,
        PropertyInspectorServer piServer,
        PluginManager pluginManager)
    {
        _logger = logger;
        _deviceManager = deviceManager;
        _ipcServer = ipcServer;
        _eventRouter = eventRouter;
        _commandHandler = commandHandler;
        _actionExecutor = actionExecutor;
        _webSocketServer = webSocketServer;
        _piServer = piServer;
        _pluginManager = pluginManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MacroKeyboard Backend Service starting...");

        try
        {
            // Start IPC server
            await _ipcServer.StartAsync(stoppingToken);

            // Start WebSocket server for plugins
            await _webSocketServer.StartAsync(stoppingToken);

            // Start Property Inspector HTTP file server
            await _piServer.StartAsync(stoppingToken);

            // Load and start all plugins
            await _pluginManager.LoadPluginsAsync(stoppingToken);
            foreach (var manifest in _pluginManager.GetPlugins())
            {
                try
                {
                    await _pluginManager.StartPluginAsync(manifest.Id, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start plugin {PluginId}", manifest.Id);
                }
            }

            // Wire device connect/disconnect → plugin notifications
            _deviceManager.DeviceConnected    += (s, e) => OnDeviceConnected(s, e).FireAndForget(_logger);
            _deviceManager.DeviceDisconnected += (s, e) => OnDeviceDisconnected(s, e).FireAndForget(_logger);

            // Start device manager
            await _deviceManager.StartAsync(stoppingToken);

            _logger.LogInformation("MacroKeyboard Backend Service started successfully");

            // Keep running until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MacroKeyboard Backend Service is stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in MacroKeyboard Backend Service");
            throw;
        }
    }

    private async Task OnDeviceConnected(object? sender, SharedEvents.DeviceEventArgs e)
    {
        try { await _pluginManager.NotifyDeviceConnectedAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error notifying plugins of device connect"); }
    }

    private async Task OnDeviceDisconnected(object? sender, SharedEvents.DeviceEventArgs e)
    {
        try { await _pluginManager.NotifyDeviceDisconnectedAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error notifying plugins of device disconnect"); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MacroKeyboard Backend Service stopping...");

        foreach (var manifest in _pluginManager.GetPlugins())
        {
            try { await _pluginManager.StopPluginAsync(manifest.Id, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to stop plugin {PluginId}", manifest.Id); }
        }

        await _webSocketServer.StopAsync(cancellationToken);
        await _piServer.StopAsync();
        await _deviceManager.StopAsync(cancellationToken);
        await _ipcServer.StopAsync(cancellationToken);

        await base.StopAsync(cancellationToken);

        _logger.LogInformation("MacroKeyboard Backend Service stopped");
    }
}
