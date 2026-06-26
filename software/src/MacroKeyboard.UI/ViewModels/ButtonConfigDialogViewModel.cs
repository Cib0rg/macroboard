using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Shared.Plugin;
using MacroKeyboard.UI.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

public partial class ButtonConfigDialogViewModel : ViewModelBase
{
    private readonly ILogger<ButtonConfigDialogViewModel> _logger;
    private IStorageProvider? _storageProvider;

    [ObservableProperty]
    private ButtonConfig _buttonConfig;

    [ObservableProperty]
    private ActionType _selectedActionType;

    [ObservableProperty]
    private string _buttonName = string.Empty;

    /// <summary>True when editing a long press action — LED section is hidden (color belongs to the button).</summary>
    [ObservableProperty]
    private bool _isLongPress = false;

    [ObservableProperty]
    private byte _targetProfileId;

    [ObservableProperty]
    private byte _folderId;

    [ObservableProperty]
    private string _folderName = string.Empty;

    [ObservableProperty]
    private string _customHidData = string.Empty;

    [ObservableProperty]
    private string _shellCommand = string.Empty;

    [ObservableProperty]
    private string? _shellWorkingDirectory;

    [ObservableProperty]
    private bool _shellWaitForExit = true;

    [ObservableProperty]
    private MediaKey _selectedMediaKey = MediaKey.Mute;

    [ObservableProperty]
    private ProfileSwitchItem? _selectedTargetProfile;

    [ObservableProperty]
    private FolderSwitchItem? _selectedTargetFolder;

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<MediaKey> AvailableMediaKeys { get; } = new()
    {
        MediaKey.VolumeUp, MediaKey.VolumeDown, MediaKey.Mute,
        MediaKey.PlayPause, MediaKey.NextTrack, MediaKey.PreviousTrack, MediaKey.Stop,
    };

    public ObservableCollection<ProfileSwitchItem> AvailableProfiles { get; } = new();
    public ObservableCollection<FolderSwitchItem>  AvailableFolders  { get; } = new();

    public IReadOnlyList<ActionType> AvailableActionTypes    { get; } = ActionTypeHelpers.AllActionTypes;
    public IReadOnlyList<ActionType> AvailableStepActionTypes { get; } = ActionTypeHelpers.SequenceStepTypes;

    public bool DialogResult { get; private set; }

    [ObservableProperty]
    private string _saveError = string.Empty;

    // ── Action-type visibility ────────────────────────────────────────────────

    public bool IsKeyboardAction      => SelectedActionType == ActionType.Keyboard;
    public bool IsProfileSwitchAction => SelectedActionType == ActionType.ProfileSwitch;
    public bool IsFolderAction        => SelectedActionType == ActionType.Folder;
    public bool IsCustomHidAction     => SelectedActionType == ActionType.CustomHid;
    public bool IsPluginAction        => SelectedActionType == ActionType.Plugin;
    public bool IsShellAction         => SelectedActionType == ActionType.Shell;
    public bool IsMediaAction         => SelectedActionType == ActionType.Media;
    public bool IsLaunchAppAction     => SelectedActionType == ActionType.LaunchApp;
    public bool IsSequenceAction      => SelectedActionType == ActionType.Sequence;

    public bool CanAddMoreSteps => SequenceSteps.Count < SequenceAction.MaxSteps;

    public string CurrentActionIcon => SelectedActionType switch
    {
        ActionType.Keyboard      => "⌨",
        ActionType.Media         => "🔊",
        ActionType.Shell         => "💻",
        ActionType.LaunchApp     => "🚀",
        ActionType.Sequence      => "📋",
        ActionType.ProfileSwitch => "🔄",
        ActionType.Folder        => "📁",
        ActionType.CustomHid     => "🎛",
        ActionType.NightMode     => "🌙",
        ActionType.Plugin        => "🔌",
        _                        => "⊘"
    };

    public string CurrentActionDisplayName => SelectedActionType switch
    {
        ActionType.Keyboard                             => "Keyboard",
        ActionType.Media                                => "Media",
        ActionType.Shell                                => "Shell",
        ActionType.LaunchApp                            => "Launch App",
        ActionType.Sequence                             => "Sequence",
        ActionType.ProfileSwitch                        => "Profile Switch",
        ActionType.Folder                               => "Folder",
        ActionType.CustomHid                            => "Custom HID",
        ActionType.NightMode                            => "Night Mode",
        ActionType.None                                 => "None",
        ActionType.Plugin when SelectedPluginAction != null => SelectedPluginAction.ActionName,
        ActionType.Plugin                               => string.IsNullOrEmpty(PluginActionId) ? "Plugin" : $"Plugin: {PluginActionId}",
        _                                               => "Not Set"
    };

    // ── Constructor ───────────────────────────────────────────────────────────

    public ButtonConfigDialogViewModel(
        ILogger<ButtonConfigDialogViewModel> logger,
        ButtonConfig buttonConfig,
        IEnumerable<ProfileSwitchItem>? availableProfiles = null,
        IEnumerable<FolderSwitchItem>?  availableFolders  = null,
        IEnumerable<PluginActionInfo>?  availablePluginActions = null)
    {
        _logger       = logger;
        _buttonConfig = buttonConfig;

        if (availableProfiles != null)
            foreach (var p in availableProfiles) AvailableProfiles.Add(p);

        if (availableFolders != null)
            foreach (var f in availableFolders) AvailableFolders.Add(f);

        if (availablePluginActions != null)
        {
            foreach (var pa in availablePluginActions)
            {
                AvailablePluginActions.Add(pa);
                FilteredPluginActions.Add(pa);
            }
        }

        if (buttonConfig.Action != null)
        {
            SelectedActionType = buttonConfig.Action.ActionType;

            if (buttonConfig.Action is KeyboardAction keyAction)
            {
                if (keyAction.KeyCode != 0)
                {
                    var displayName = HidKeyCodeHelper.FormatKey(keyAction.KeyCode, keyAction.Modifiers);
                    _capturedKeys.Add(new CapturedKey(keyAction.KeyCode, keyAction.Modifiers, displayName));
                    CapturedKeyCode   = keyAction.KeyCode;
                    CapturedModifiers = keyAction.Modifiers;
                    KeySequence       = displayName;
                }
                else
                {
                    TextToType  = keyAction.Text ?? string.Empty;
                    KeySequence = keyAction.Text ?? string.Empty;
                }
            }
            else if (buttonConfig.Action is ProfileSwitchAction psAction)
            {
                TargetProfileId       = psAction.TargetProfileId;
                SelectedTargetProfile = AvailableProfiles.FirstOrDefault(p => p.ProfileId == psAction.TargetProfileId);
            }
            else if (buttonConfig.Action is ShellAction shellAction)
            {
                ShellCommand          = shellAction.Command;
                ShellWorkingDirectory = shellAction.WorkingDirectory;
                ShellWaitForExit      = shellAction.WaitForExit;
            }
            else if (buttonConfig.Action is LaunchAppAction launchAction)
            {
                LaunchAppPath             = launchAction.ExecutablePath;
                LaunchAppArguments        = launchAction.Arguments;
                LaunchAppWorkingDirectory = launchAction.WorkingDirectory;
                LaunchAppIconPath         = launchAction.IconPath;
            }
            else if (buttonConfig.Action is MediaAction mediaAction)
            {
                SelectedMediaKey = mediaAction.Key;
            }
            else if (buttonConfig.Action is PluginActionConfig pluginAction)
            {
                PluginId       = pluginAction.PluginId;
                PluginActionId = pluginAction.ActionId;
                PluginSettings = pluginAction.Settings ?? string.Empty;
                SelectedPluginAction = AvailablePluginActions
                    .FirstOrDefault(a => a.PluginId == pluginAction.PluginId && a.ActionId == pluginAction.ActionId);
            }
            else if (buttonConfig.Action is CustomHidAction customHidAction)
            {
                CustomHidData = FormatBytesAsHex(customHidAction.Data);
            }
        }

        FolderId = buttonConfig.FolderId;
        var existingFolder   = AvailableFolders.FirstOrDefault(f => f.FolderId == buttonConfig.FolderId);
        SelectedTargetFolder = existingFolder;
        FolderName           = existingFolder?.Name ?? $"Folder {buttonConfig.FolderId}";
        ButtonName           = buttonConfig.Name ?? string.Empty;
        ImagePath            = buttonConfig.ImagePath ?? string.Empty;
        LoadImagePreview(ImagePath);

        _isUpdatingColor = true;
        ColorR     = buttonConfig.Led.R;
        ColorG     = buttonConfig.Led.G;
        ColorB     = buttonConfig.Led.B;
        Brightness = buttonConfig.Led.Brightness;
        LedColor   = Color.FromRgb(buttonConfig.Led.R, buttonConfig.Led.G, buttonConfig.Led.B);
        _isUpdatingColor = false;
        UpdateHexFromRgb();
    }

    // ── Action type change ────────────────────────────────────────────────────

    partial void OnSelectedActionTypeChanged(ActionType value)
    {
        // Reset ALL action-specific fields so data from the previous type doesn't bleed through.
        KeySequence = string.Empty;
        TextToType  = string.Empty;
        CapturedKeyCode   = 0;
        CapturedModifiers = KeyModifiers.None;
        IsCapturingKeys   = false;
        _capturedKeys.Clear();

        TargetProfileId       = 0;
        SelectedTargetProfile = null;

        FolderId             = 0;
        FolderName           = string.Empty;
        SelectedTargetFolder = null;

        ShellCommand          = string.Empty;
        ShellWorkingDirectory = null;
        ShellWaitForExit      = true;

        SelectedMediaKey = MediaKey.Mute;

        LaunchAppPath             = string.Empty;
        LaunchAppArguments        = null;
        LaunchAppWorkingDirectory = null;
        LaunchAppIconPath         = null;

        SequenceSteps.Clear();

        PluginId       = string.Empty;
        PluginActionId = string.Empty;
        PluginSettings = string.Empty;
        CustomHidData  = string.Empty;

        OnPropertyChanged(nameof(IsKeyboardAction));
        OnPropertyChanged(nameof(IsProfileSwitchAction));
        OnPropertyChanged(nameof(IsFolderAction));
        OnPropertyChanged(nameof(IsCustomHidAction));
        OnPropertyChanged(nameof(IsMediaAction));
        OnPropertyChanged(nameof(IsShellAction));
        OnPropertyChanged(nameof(IsLaunchAppAction));
        OnPropertyChanged(nameof(IsSequenceAction));
        OnPropertyChanged(nameof(IsPluginAction));
        OnPropertyChanged(nameof(CanAddMoreSteps));
        OnPropertyChanged(nameof(CurrentActionIcon));
        OnPropertyChanged(nameof(CurrentActionDisplayName));
        OnPropertyChanged(nameof(KeySequenceDisplay));
        OnPropertyChanged(nameof(HasCapturedKeys));

        if (value == ActionType.Plugin)
        {
            PluginSearchText     = string.Empty;
            SelectedPluginAction = null;
            ApplyPluginFilter();
        }

        // None action — clear image so the device display isn't left showing a stale icon.
        if (value == ActionType.None)
            ImagePath = string.Empty;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    public void SetStorageProvider(IStorageProvider storageProvider) => _storageProvider = storageProvider;

    private static byte[] ParseHexString(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Array.Empty<byte>();
        var tokens = hex.Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<byte>();
        foreach (var token in tokens)
            if (byte.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out var b))
                result.Add(b);
        return result.ToArray();
    }

    private static string FormatBytesAsHex(byte[] data) =>
        data.Length == 0 ? string.Empty : string.Join(" ", data.Select(b => b.ToString("X2")));

    // ── Public API for ProfileEditorViewModel ─────────────────────────────────

    public void SaveToButtonConfig() => Save();

    public async Task EnsureIconExtractedAsync()
    {
        if (SelectedActionType != ActionType.LaunchApp) return;
        if (!string.IsNullOrWhiteSpace(ImagePath)) return;
        if (string.IsNullOrWhiteSpace(LaunchAppPath)) return;
        if (!File.Exists(LaunchAppPath)) return;

        _logger.LogInformation("Extracting icon for manually-entered exe: {Path}", LaunchAppPath);
        await ExtractAndSetAppIconAsync(LaunchAppPath);
    }

    // ── Save / Cancel ─────────────────────────────────────────────────────────

    // Firmware ACTION_DATA_MAX_LEN = 51 bytes (ProtocolConstants.TextInlineMaxBytes).
    // ShellAction.ToBytes() = 1 byte flags + command UTF-8 + 1 byte null → max command = 49 bytes.
    private const int CustomHidMaxBytes   = 51;
    private const int ShellCommandMaxBytes = 49;

    private bool ValidateBeforeSave()
    {
        SaveError = string.Empty;

        if (SelectedActionType == ActionType.CustomHid)
        {
            var bytes = ParseHexString(CustomHidData);
            if (bytes.Length > CustomHidMaxBytes)
            {
                SaveError = $"Custom HID data too long: {bytes.Length} bytes (max {CustomHidMaxBytes}).";
                return false;
            }
        }

        if (SelectedActionType == ActionType.Shell)
        {
            var len = System.Text.Encoding.UTF8.GetByteCount(ShellCommand ?? string.Empty);
            if (len > ShellCommandMaxBytes)
            {
                SaveError = $"Shell command too long: {len} bytes UTF-8 (max {ShellCommandMaxBytes}). Long commands are silently truncated by firmware.";
                return false;
            }
        }

        return true;
    }

    [RelayCommand]
    private void Save()
    {
        if (!ValidateBeforeSave()) return;
        try
        {
            ButtonConfig.Action = SelectedActionType switch
            {
                ActionType.Keyboard      => CreateKeyboardAction(),
                ActionType.ProfileSwitch => new ProfileSwitchAction
                {
                    TargetProfileId = SelectedTargetProfile?.ProfileId ?? TargetProfileId
                },
                ActionType.Folder    => null, // handled by explicit if-block below (also sets ButtonConfig.FolderId)
                ActionType.CustomHid => new CustomHidAction
                {
                    Data = ParseHexString(CustomHidData)
                },
                ActionType.LaunchApp => new LaunchAppAction
                {
                    ExecutablePath   = LaunchAppPath,
                    Arguments        = string.IsNullOrWhiteSpace(LaunchAppArguments) ? null : LaunchAppArguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(LaunchAppWorkingDirectory) ? null : LaunchAppWorkingDirectory,
                    IconPath         = LaunchAppIconPath
                },
                ActionType.Media => new MediaAction { Key = SelectedMediaKey },
                ActionType.Shell => new ShellAction
                {
                    Command          = ShellCommand ?? string.Empty,
                    WorkingDirectory = string.IsNullOrWhiteSpace(ShellWorkingDirectory) ? null : ShellWorkingDirectory,
                    WaitForExit      = ShellWaitForExit
                },
                ActionType.Sequence => new SequenceAction
                {
                    Steps = SequenceSteps.Select(s => s.ToModel()).ToList()
                },
                ActionType.NightMode => new NightModeAction(),
                ActionType.Plugin    => new PluginActionConfig
                {
                    PluginId   = PluginId,
                    ActionId   = PluginActionId,
                    ActionName = SelectedPluginAction?.ActionName,
                    Settings   = string.IsNullOrWhiteSpace(PluginSettings) ? null : PluginSettings
                },
                ActionType.None => null,
                _               => null
            };

            if (SelectedActionType == ActionType.Folder)
            {
                ButtonConfig.Action   = new FolderAction { FolderId = FolderId };
                ButtonConfig.FolderId = FolderId;
            }

            ButtonConfig.Name      = string.IsNullOrWhiteSpace(ButtonName) ? null : ButtonName.Trim();
            ButtonConfig.ImagePath = string.IsNullOrWhiteSpace(ImagePath)  ? null : ImagePath;

            ButtonConfig.Led.R          = (byte)ColorR;
            ButtonConfig.Led.G          = (byte)ColorG;
            ButtonConfig.Led.B          = (byte)ColorB;
            ButtonConfig.Led.Brightness = (byte)Brightness;

            DialogResult = true;
            _logger.LogInformation("Button configuration saved: ActionType={ActionType}", SelectedActionType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving button configuration");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        _logger.LogInformation("Button configuration cancelled");
    }
}

public record CapturedKey(byte KeyCode, KeyModifiers Modifiers, string DisplayName);

public class ProfileSwitchItem
{
    public byte   ProfileId { get; set; }
    public string Name      { get; set; } = string.Empty;
    public override string ToString() => Name;
}

public class FolderSwitchItem
{
    public byte   FolderId { get; set; }
    public string Name     { get; set; } = string.Empty;
    public override string ToString() => Name;
}
