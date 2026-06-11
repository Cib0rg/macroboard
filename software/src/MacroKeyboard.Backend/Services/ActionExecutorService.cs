using System.Diagnostics;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Backend.Services;

/// <summary>
/// Executes PC-side actions (LaunchApp, Shell) triggered by device button presses.
/// Subscribes directly to IDeviceService.ButtonPressed so it receives ActionType and ProfileId.
/// </summary>
public class ActionExecutorService
{
    private readonly IDeviceService _deviceService;
    private readonly IProfileService _profileService;
    private readonly IShellCommandExecutor _shellExecutor;
    private readonly ILogger<ActionExecutorService> _logger;

    public ActionExecutorService(
        IDeviceService deviceService,
        IProfileService profileService,
        IShellCommandExecutor shellExecutor,
        ILogger<ActionExecutorService> logger)
    {
        _deviceService = deviceService;
        _profileService = profileService;
        _shellExecutor = shellExecutor;
        _logger = logger;

        _deviceService.ButtonPressed += OnButtonPressed;
    }

    private async void OnButtonPressed(object? sender, ButtonEventArgs e)
    {
        try
        {
            switch (e.ActionType)
            {
                case ActionType.LaunchApp:
                    await ExecuteLaunchAppAsync(e.ProfileId, e.ButtonId);
                    break;
                case ActionType.Shell:
                    await ExecuteShellAsync(e.ProfileId, e.ButtonId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action for button {ButtonId}", e.ButtonId);
        }
    }

    private async Task ExecuteLaunchAppAsync(byte profileId, byte buttonId)
    {
        var profile = await _profileService.GetProfileAsync(profileId);
        var button = profile?.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);

        if (button?.Action is not LaunchAppAction action || string.IsNullOrWhiteSpace(action.ExecutablePath))
        {
            _logger.LogWarning("LaunchApp: no valid action for button {ButtonId} in profile {ProfileId}",
                buttonId, profileId);
            return;
        }

        _logger.LogInformation("Launching: {Path} {Args}", action.ExecutablePath, action.Arguments);

        var psi = new ProcessStartInfo
        {
            FileName = action.ExecutablePath,
            Arguments = action.Arguments ?? string.Empty,
            WorkingDirectory = action.WorkingDirectory ?? Path.GetDirectoryName(action.ExecutablePath) ?? string.Empty,
            UseShellExecute = true
        };
        Process.Start(psi);
    }

    private async Task ExecuteShellAsync(byte profileId, byte buttonId)
    {
        var profile = await _profileService.GetProfileAsync(profileId);
        var button = profile?.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);

        if (button?.Action is not ShellAction action || string.IsNullOrWhiteSpace(action.Command))
        {
            _logger.LogWarning("Shell: no valid action for button {ButtonId} in profile {ProfileId}",
                buttonId, profileId);
            return;
        }

        _logger.LogInformation("Executing shell command: {Command}", action.Command);
        await _shellExecutor.ExecuteAsync(action);
    }
}
