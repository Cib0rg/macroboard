using System.Diagnostics;
using MacroKeyboard.Backend;
using MacroKeyboard.Backend.Plugin;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using SharedEvents = MacroKeyboard.Shared.Events;

namespace MacroKeyboard.Backend.Services;

/// <summary>
/// Executes PC-side actions (LaunchApp, Shell, Plugin) triggered by device button presses.
/// Subscribes directly to IDeviceService.ButtonPressed so it receives ActionType and ProfileId.
/// </summary>
public class ActionExecutorService : IActionExecutorService
{
    private readonly IDeviceService _deviceService;
    private readonly ProfileService _profileService;
    private readonly ShellCommandExecutor _shellExecutor;
    private readonly PluginManager _pluginManager;
    private readonly ILogger<ActionExecutorService> _logger;

    // Tracks which folder the device is currently in (0xFF = root).
    private volatile byte _currentFolderId = 0xFF;

    public ActionExecutorService(
        IDeviceService deviceService,
        DeviceManager deviceManager,
        ProfileService profileService,
        ShellCommandExecutor shellExecutor,
        PluginManager pluginManager,
        ILogger<ActionExecutorService> logger)
    {
        _deviceService = deviceService;
        _profileService = profileService;
        _shellExecutor = shellExecutor;
        _pluginManager = pluginManager;
        _logger = logger;

        _deviceService.ButtonPressed += (s, e) => OnButtonPressed(s, e).FireAndForget(_logger);
        _deviceService.FolderEntered += (_, e) => OnFolderEnteredAsync(e).FireAndForget(_logger);
        _deviceService.FolderExited  += (_, e) => OnFolderExitedAsync(e).FireAndForget(_logger);
        // Subscribe to DeviceManager.DeviceConnected (fires only after firmware is confirmed ready),
        // not DeviceService.DeviceConnected (fires on raw USB connect, firmware may still be booting).
        // The premature CMD 0x12 that was sent via the raw event would arrive late and poison the
        // CMD 0x02 FIFO slot in HidDeviceManager, causing GetDeviceInfo to fail and triggering
        // the boot retry counter even when the device was perfectly healthy.
        deviceManager.DeviceConnected += (s, e) => OnDeviceConnected(s, e).FireAndForget(_logger);
    }

    private async Task OnDeviceConnected(object? sender, SharedEvents.DeviceEventArgs e)
    {
        try
        {
            var (folderId, depth) = await _deviceService.GetFolderStateAsync();
            _currentFolderId = depth > 0 ? folderId : (byte)0xFF;
            _logger.LogInformation("Device connected: folder state restored — folderId={FolderId}, depth={Depth}",
                folderId, depth);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore folder state on connect, assuming root");
            _currentFolderId = 0xFF;
        }
    }

    private ButtonConfig? FindButton(Profile? profile, byte buttonId)
    {
        if (profile == null) return null;
        if (_currentFolderId != 0xFF)
        {
            var folder = profile.Folders.FirstOrDefault(f => f.FolderId == _currentFolderId);
            var fb = folder?.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);
            if (fb != null) return fb;
        }
        return profile.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);
    }

    private async Task OnButtonPressed(object? sender, ButtonEventArgs e)
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
                case ActionType.Plugin:
                    await ExecutePluginActionAsync(e.ProfileId, e.ButtonId);
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
        var dbProfileId = _profileService.ActiveProfileId;
        var profile = await _profileService.GetProfileAsync(dbProfileId);
        var button = FindButton(profile, buttonId);

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
        var dbProfileId = _profileService.ActiveProfileId;
        var profile = await _profileService.GetProfileAsync(dbProfileId);
        var button = FindButton(profile, buttonId);

        if (button?.Action is not ShellAction action || string.IsNullOrWhiteSpace(action.Command))
        {
            _logger.LogWarning("Shell: no valid action for button {ButtonId} in profile {ProfileId}",
                buttonId, profileId);
            return;
        }

        _logger.LogInformation("Executing shell command: {Command}", action.Command);
        await _shellExecutor.ExecuteAsync(action);
    }

    private async Task ExecutePluginActionAsync(byte profileId, byte buttonId)
    {
        // Device always uses slot 0; map back to the database profile ID we last sent.
        var dbProfileId = _profileService.ActiveProfileId;
        var profile = await _profileService.GetProfileAsync(dbProfileId);
        var button  = FindButton(profile, buttonId);

        if (button?.Action is not PluginActionConfig action)
        {
            _logger.LogWarning(
                "Plugin: no valid action for button {ButtonId} in profile dbId={DbId} (device slot={Slot})",
                buttonId, dbProfileId, profileId);
            return;
        }

        // Prefer sidecar settings (written by setSettings from plugin/PI) over profile snapshot.
        var liveSettings = await _pluginManager.GetActionSettingsAsync(action.PluginId, buttonId, _currentFolderId);

        _logger.LogInformation(
            "Dispatching keyDown: plugin={Plugin} action={Action} button={Btn} folder={Folder} settingsSource={Src}",
            action.PluginId, action.ActionId, buttonId,
            _currentFolderId == 0xFF ? "root" : $"F{_currentFolderId}",
            liveSettings != null ? "sidecar" : "profile");

        await _pluginManager.DispatchButtonPressAsync(
            action.PluginId, action.ActionId, liveSettings ?? action.Settings, buttonId, _currentFolderId);
    }

    // ── Folder navigation — dispatch willAppear/willDisappear for plugin buttons ───

    private async Task OnFolderEnteredAsync(FolderEventArgs e)
    {
        // Capture the previous folder BEFORE updating _currentFolderId so we can
        // send willDisappear for whichever buttons were visible before.
        var previousFolderId = _currentFolderId;
        _currentFolderId = e.FolderId;

        var dbProfileId = _profileService.ActiveProfileId;
        var profile     = await _profileService.GetProfileAsync(dbProfileId);
        if (profile == null) return;

        // willDisappear for whichever buttons were visible before (root or parent folder)
        if (previousFolderId == 0xFF)
        {
            foreach (var btn in profile.Buttons)
            {
                if (btn.Action is not PluginActionConfig pa) continue;
                await _pluginManager.NotifyWillDisappearAsync(pa.PluginId, pa.ActionId, pa.Settings, btn.ButtonId);
            }
        }
        else
        {
            var prevFolder = profile.Folders.FirstOrDefault(f => f.FolderId == previousFolderId);
            if (prevFolder != null)
            {
                foreach (var btn in prevFolder.Buttons)
                {
                    if (btn.Action is not PluginActionConfig pa) continue;
                    await _pluginManager.NotifyWillDisappearAsync(pa.PluginId, pa.ActionId, pa.Settings, btn.ButtonId, previousFolderId);
                }
            }
        }

        // willAppear for newly visible folder plugin buttons
        var newFolder = profile.Folders.FirstOrDefault(f => f.FolderId == e.FolderId);
        if (newFolder == null) return;
        foreach (var btn in newFolder.Buttons)
        {
            if (btn.Action is not PluginActionConfig pa) continue;
            await _pluginManager.NotifyWillAppearAsync(pa.PluginId, pa.ActionId, pa.Settings, btn.ButtonId, e.FolderId);
        }
    }

    private async Task OnFolderExitedAsync(FolderEventArgs e)
    {
        var exitedFolderId = _currentFolderId;
        _currentFolderId = e.FolderDepth == 0 ? (byte)0xFF : e.ParentFolderId;

        var dbProfileId = _profileService.ActiveProfileId;
        var profile     = await _profileService.GetProfileAsync(dbProfileId);
        if (profile == null) return;

        // willDisappear for the folder we just left
        var exitedFolder = profile.Folders.FirstOrDefault(f => f.FolderId == exitedFolderId);
        if (exitedFolder != null)
        {
            foreach (var btn in exitedFolder.Buttons)
            {
                if (btn.Action is not PluginActionConfig pa) continue;
                await _pluginManager.NotifyWillDisappearAsync(pa.PluginId, pa.ActionId, pa.Settings, btn.ButtonId, exitedFolderId);
            }
        }

        // willAppear for whichever buttons are now visible (root or parent folder)
        if (e.FolderDepth == 0)
        {
            foreach (var btn in profile.Buttons)
            {
                if (btn.Action is not PluginActionConfig pa) continue;
                await _pluginManager.NotifyWillAppearAsync(pa.PluginId, pa.ActionId, pa.Settings, btn.ButtonId);
            }
        }
        else
        {
            var parentFolder = profile.Folders.FirstOrDefault(f => f.FolderId == e.ParentFolderId);
            if (parentFolder != null)
            {
                foreach (var btn in parentFolder.Buttons)
                {
                    if (btn.Action is not PluginActionConfig pa) continue;
                    await _pluginManager.NotifyWillAppearAsync(pa.PluginId, pa.ActionId, pa.Settings, btn.ButtonId, e.ParentFolderId);
                }
            }
        }
    }
}
