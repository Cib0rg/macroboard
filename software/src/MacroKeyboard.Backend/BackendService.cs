using MacroKeyboard.Backend.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    public BackendService(
        ILogger<BackendService> logger,
        DeviceManager deviceManager,
        IpcServer ipcServer,
        EventRouter eventRouter,
        IpcCommandHandler commandHandler)
    {
        _logger = logger;
        _deviceManager = deviceManager;
        _ipcServer = ipcServer;
        _eventRouter = eventRouter;
        _commandHandler = commandHandler; // Resolved to activate event subscriptions
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MacroKeyboard Backend Service starting...");

        try
        {
            // Start IPC server
            await _ipcServer.StartAsync(stoppingToken);

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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MacroKeyboard Backend Service stopping...");

        await _deviceManager.StopAsync(cancellationToken);
        await _ipcServer.StopAsync(cancellationToken);

        await base.StopAsync(cancellationToken);

        _logger.LogInformation("MacroKeyboard Backend Service stopped");
    }
}
