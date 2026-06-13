using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Shared.IPC;
using MacroKeyboard.Shared.Plugin;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

public partial class ProfileEditorViewModel : ViewModelBase
{
    private readonly IProfileService _profileService;
    private readonly IpcClient _ipcClient;
    private readonly ILogger<ProfileEditorViewModel> _logger;
    private readonly ILogger<ButtonConfigDialogViewModel> _dialogLogger;
    private IStorageProvider? _storageProvider;

    private static readonly string DefaultProfilesDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Profiles");

    // ── Canvas ────────────────────────────────────────────────────────────────

    public DeviceCanvasViewModel DeviceCanvas { get; } = new();

    // ── Profile selection ─────────────────────────────────────────────────────

    [ObservableProperty]
    private Profile? _selectedProfile;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _syncProgress;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    // ── Config panel ──────────────────────────────────────────────────────────

    /// <summary>Short press (and shared button metadata) config VM shown in right panel.</summary>
    [ObservableProperty]
    private ButtonConfigDialogViewModel? _buttonConfigViewModel;

    /// <summary>Long press config VM — null for encoder slots (they have no long press).</summary>
    [ObservableProperty]
    private ButtonConfigDialogViewModel? _longPressConfigViewModel;

    /// <summary>Temporary ButtonConfig that holds long-press state while editing.</summary>
    private ButtonConfig? _longPressTempConfig;

    /// <summary>Header label for the config panel ("Button 3", "Encoder: CW", …).</summary>
    [ObservableProperty]
    private string _selectedButtonHeader = string.Empty;

    // ── Encoder ───────────────────────────────────────────────────────────────

    private int _encoderEditingSlot = -1;
    private readonly ButtonConfig _encoderCwConfig    = new() { ButtonId = 200 };
    private readonly ButtonConfig _encoderCcwConfig   = new() { ButtonId = 201 };
    private readonly ButtonConfig _encoderPressConfig = new() { ButtonId = 202 };
    private readonly ButtonConfig _encoderLongConfig  = new() { ButtonId = 203 };

    public string EncoderCwActionDisplay    => GetActionDisplayName(_encoderCwConfig.Action);
    public string EncoderCcwActionDisplay   => GetActionDisplayName(_encoderCcwConfig.Action);
    public string EncoderPressActionDisplay => GetActionDisplayName(_encoderPressConfig.Action);
    public string EncoderLongPressActionDisplay => GetActionDisplayName(_encoderLongConfig.Action);

    private static string GetActionDisplayName(ActionConfig? action) => action switch
    {
        null or NoneAction  => "None",
        KeyboardAction ka when ka.KeyCode != 0
            => $"Key: 0x{ka.KeyCode:X2}",
        KeyboardAction      => "Type text",
        MediaAction ma      => $"Media: {ma.Key}",
        ShellAction sa      => $"Shell: {sa.Command?[..Math.Min(sa.Command?.Length ?? 0, 20)] ?? "..."}",
        LaunchAppAction la  => $"Launch: {Path.GetFileNameWithoutExtension(la.ExecutablePath ?? "App")}",
        FolderAction        => "Folder",
        SequenceAction      => "Sequence",
        CustomHidAction     => "Custom HID",
        NightModeAction     => "Night Mode",
        DelayAction da      => $"Delay {da.DelayMs}ms",
        PluginActionConfig pa => $"Plugin: {pa.ActionId}",
        _ => action.ActionType.ToString()
    };

    // ── Profiles collection ───────────────────────────────────────────────────

    public ObservableCollection<Profile> Profiles { get; } = new();

    /// <summary>Action palette items kept for future Action Picker (Phase 2).</summary>
    public ObservableCollection<ActionPaletteItem> ActionPaletteItems { get; } = new()
    {
        new ActionPaletteItem(ActionType.Keyboard,  "Keyboard",   "⌨",  "Emulate keyboard key press or text input"),
        new ActionPaletteItem(ActionType.Media,     "Media",      "🔊", "Media keys"),
        new ActionPaletteItem(ActionType.Media, "Volume Up",   "🔊", "Increase system volume")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.VolumeUp } },
        new ActionPaletteItem(ActionType.Media, "Volume Down", "🔉", "Decrease system volume")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.VolumeDown } },
        new ActionPaletteItem(ActionType.Media, "Mute",        "🔇", "Toggle mute")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.Mute } },
        new ActionPaletteItem(ActionType.Media, "Play/Pause",  "⏯",  "Play or pause media")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.PlayPause } },
        new ActionPaletteItem(ActionType.Media, "Next Track",  "⏭",  "Skip to next track")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.NextTrack } },
        new ActionPaletteItem(ActionType.Media, "Prev Track",  "⏮",  "Go to previous track")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.PreviousTrack } },
        new ActionPaletteItem(ActionType.LaunchApp, "Launch App", "🚀", "Launch an application"),
        new ActionPaletteItem(ActionType.Shell,     "Shell",      "💻", "Execute a shell command"),
        new ActionPaletteItem(ActionType.Sequence,  "Sequence",   "📋", "Execute multiple actions"),
        new ActionPaletteItem(ActionType.Folder,    "Folder",     "📁", "Open a folder of sub-buttons"),
        new ActionPaletteItem(ActionType.CustomHid, "Custom HID", "🎛", "Send custom HID report"),
        new ActionPaletteItem(ActionType.NightMode, "Night Mode", "🌙", "Toggle all LEDs off"),
        new ActionPaletteItem(ActionType.None,      "None",       "⊘",  "No action assigned"),
    };

    // ── Constructor ───────────────────────────────────────────────────────────

    public ProfileEditorViewModel(
        IProfileService profileService,
        IpcClient ipcClient,
        ILogger<ProfileEditorViewModel> logger,
        ILogger<ButtonConfigDialogViewModel> dialogLogger)
    {
        _profileService = profileService;
        _ipcClient = ipcClient;
        _logger = logger;
        _dialogLogger = dialogLogger;

        DeviceCanvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeviceCanvasViewModel.SelectedTile))
                OnCanvasTileSelected(DeviceCanvas.SelectedTile);
        };
    }

    // ── Canvas selection ──────────────────────────────────────────────────────

    private void OnCanvasTileSelected(ButtonTileViewModel? tile)
    {
        if (tile == null || tile.IsBackButton)
        {
            ButtonConfigViewModel = null;
            LongPressConfigViewModel = null;
            _longPressTempConfig = null;
            SelectedButtonHeader = string.Empty;
            return;
        }

        _encoderEditingSlot = -1;
        OpenButtonConfigPanel(tile.Button);
    }

    private void OpenButtonConfigPanel(ButtonConfig button)
    {
        var profileItems = GetAvailableProfileItems();
        var folderItems  = GetAvailableFolderItems();

        ButtonConfigViewModel = new ButtonConfigDialogViewModel(_dialogLogger, button, profileItems, folderItems);
        if (_storageProvider != null)
            ButtonConfigViewModel.SetStorageProvider(_storageProvider);

        _longPressTempConfig = new ButtonConfig
        {
            ButtonId = button.ButtonId,
            Action   = button.LongPressAction,
            Name     = button.LongPressName
        };
        LongPressConfigViewModel = new ButtonConfigDialogViewModel(_dialogLogger, _longPressTempConfig, profileItems, folderItems);
        LongPressConfigViewModel.IsLongPress = true;

        var label = string.IsNullOrWhiteSpace(button.Name)
            ? $"Button {button.ButtonId + 1}"
            : $"{button.Name}  (Button {button.ButtonId + 1})";
        SelectedButtonHeader = label;
        HasUnsavedChanges = true;
    }

    // ── Encoder config ────────────────────────────────────────────────────────

    [RelayCommand] private void ConfigureEncoderCw()        => OpenEncoderConfig(_encoderCwConfig,    0, "Encoder: Clockwise");
    [RelayCommand] private void ConfigureEncoderCcw()       => OpenEncoderConfig(_encoderCcwConfig,   1, "Encoder: Counter-CW");
    [RelayCommand] private void ConfigureEncoderPress()     => OpenEncoderConfig(_encoderPressConfig, 2, "Encoder: Press");
    [RelayCommand] private void ConfigureEncoderLongPress() => OpenEncoderConfig(_encoderLongConfig,  3, "Encoder: Long Press");

    private void OpenEncoderConfig(ButtonConfig config, int slot, string header)
    {
        _encoderEditingSlot = slot;
        DeviceCanvas.DeselectAll();

        var profileItems = GetAvailableProfileItems();
        var folderItems  = GetAvailableFolderItems();

        ButtonConfigViewModel = new ButtonConfigDialogViewModel(_dialogLogger, config, profileItems, folderItems);
        ButtonConfigViewModel.IsLongPress = true; // hides LED + long press fields
        if (_storageProvider != null)
            ButtonConfigViewModel.SetStorageProvider(_storageProvider);

        LongPressConfigViewModel = null;
        _longPressTempConfig = null;
        SelectedButtonHeader = header;
        HasUnsavedChanges = true;
    }

    // ── SelectedProfile changed ───────────────────────────────────────────────

    partial void OnSelectedProfileChanged(Profile? value)
    {
        DeviceCanvas.LoadProfile(value);
        SyncEncoderFromProfile();
        ButtonConfigViewModel = null;
        LongPressConfigViewModel = null;
        _longPressTempConfig = null;
        _encoderEditingSlot = -1;
        SelectedButtonHeader = string.Empty;
        HasUnsavedChanges = false;
    }

    private void SyncEncoderFromProfile()
    {
        _encoderCwConfig.Action    = SelectedProfile?.Encoder?.RotateCwAction;
        _encoderCcwConfig.Action   = SelectedProfile?.Encoder?.RotateCcwAction;
        _encoderPressConfig.Action = SelectedProfile?.Encoder?.PressAction;
        _encoderLongConfig.Action  = SelectedProfile?.Encoder?.LongPressAction;
        OnPropertyChanged(nameof(EncoderCwActionDisplay));
        OnPropertyChanged(nameof(EncoderCcwActionDisplay));
        OnPropertyChanged(nameof(EncoderPressActionDisplay));
        OnPropertyChanged(nameof(EncoderLongPressActionDisplay));
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void NavigateUp()
    {
        DeviceCanvas.NavigateUp();
        ButtonConfigViewModel = null;
        LongPressConfigViewModel = null;
        SelectedButtonHeader = string.Empty;
    }

    // ── Save button config ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveButtonConfig()
    {
        if (ButtonConfigViewModel == null) return;

        try
        {
            await ButtonConfigViewModel.EnsureIconExtractedAsync();
            ButtonConfigViewModel.SaveToButtonConfig();
            var button = ButtonConfigViewModel.ButtonConfig;

            // Encoder slot
            if (_encoderEditingSlot >= 0 && SelectedProfile != null)
            {
                SelectedProfile.Encoder ??= new EncoderConfig();
                switch (_encoderEditingSlot)
                {
                    case 0: SelectedProfile.Encoder.RotateCwAction  = button.Action; break;
                    case 1: SelectedProfile.Encoder.RotateCcwAction = button.Action; break;
                    case 2: SelectedProfile.Encoder.PressAction     = button.Action; break;
                    case 3: SelectedProfile.Encoder.LongPressAction = button.Action; break;
                }
                _encoderEditingSlot = -1;
                SyncEncoderFromProfile();
                await _profileService.UpdateProfileAsync(SelectedProfile);
                HasUnsavedChanges = false;
                StatusMessage = "Encoder action configured";
                if (_ipcClient.IsConnected) await SendFullProfileToDeviceAsync();
                return;
            }

            // Long press
            if (LongPressConfigViewModel != null && _longPressTempConfig != null)
            {
                LongPressConfigViewModel.SaveToButtonConfig();
                button.LongPressAction = _longPressTempConfig.Action;
                button.LongPressName   = string.IsNullOrWhiteSpace(_longPressTempConfig.Name)
                    ? null : _longPressTempConfig.Name.Trim();
            }

            // Folder action — find or create folder
            if (button.Action is FolderAction && SelectedProfile != null)
            {
                var folderName = ButtonConfigViewModel.FolderName;
                if (string.IsNullOrWhiteSpace(folderName)) folderName = "New Folder";

                var existing = SelectedProfile.Folders.FirstOrDefault(
                    f => f.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    button.FolderId = existing.FolderId;
                    if (button.Action is FolderAction fa1) fa1.FolderId = existing.FolderId;
                }
                else
                {
                    byte newId = SelectedProfile.Folders.Count > 0
                        ? (byte)(SelectedProfile.Folders.Max(f => f.FolderId) + 1)
                        : (byte)0;
                    var newFolder = new Folder { FolderId = newId, Name = folderName };
                    for (byte i = 0; i < 10; i++)
                        newFolder.Buttons.Add(new ButtonConfig { ButtonId = i, Action = null, Led = LedConfig.FromRgb(80, 80, 80) });
                    SelectedProfile.Folders.Add(newFolder);
                    button.FolderId = newId;
                    if (button.Action is FolderAction fa2) fa2.FolderId = newId;
                    _logger.LogInformation("Created folder '{Name}' id={Id}", folderName, newId);
                }
            }

            _logger.LogInformation("Button {ButtonId} configured", button.ButtonId);

            if (SelectedProfile != null)
            {
                await _profileService.UpdateProfileAsync(SelectedProfile);
                HasUnsavedChanges = false;
                StatusMessage = $"Button {button.ButtonId + 1} configured";
            }

            DeviceCanvas.Refresh();

            if (_ipcClient.IsConnected && SelectedProfile != null)
            {
                bool isFolderButton = !SelectedProfile.Buttons.Contains(button);
                bool isFolderAction = button.Action?.ActionType == ActionType.Folder;
                bool hasImage       = !string.IsNullOrEmpty(button.ImagePath);

                if (isFolderButton || isFolderAction || hasImage)
                    await SendFullProfileToDeviceAsync();
                else
                    await SendButtonConfigToDeviceAsync(SelectedProfile.ProfileId, button);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving button configuration");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseButtonConfig()
    {
        DeviceCanvas.DeselectAll();
        ButtonConfigViewModel = null;
        LongPressConfigViewModel = null;
        _longPressTempConfig = null;
        _encoderEditingSlot = -1;
        SelectedButtonHeader = string.Empty;
        HasUnsavedChanges = false;
    }

    // ── Storage provider ──────────────────────────────────────────────────────

    public void SetStorageProvider(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
    }

    // ── Profile CRUD ──────────────────────────────────────────────────────────

    public async Task LoadProfilesAsync()
    {
        try
        {
            IsLoading = true;
            var profiles = await _profileService.GetAllProfilesAsync();
            Profiles.Clear();
            foreach (var p in profiles) Profiles.Add(p);
            if (Profiles.Any()) SelectedProfile = Profiles.First();
            StatusMessage = $"Loaded {Profiles.Count} profiles";
            await LoadPluginActionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading profiles");
            StatusMessage = $"Error loading profiles: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private async Task LoadPluginActionsAsync()
    {
        if (!_ipcClient.IsConnected) return;
        try
        {
            var message  = new IpcMessage { MessageType = IpcMessageTypes.PluginList };
            var response = await _ipcClient.SendAndWaitAsync(message, TimeSpan.FromSeconds(5));
            if (!response.Success) return;

            var pluginActions = response.GetData<List<PluginActionInfo>>();
            if (pluginActions == null || pluginActions.Count == 0) return;

            var existing = ActionPaletteItems.Where(i => i.ActionType == ActionType.Plugin).ToList();
            foreach (var item in existing) ActionPaletteItems.Remove(item);

            string? lastPluginId = null;
            foreach (var pa in pluginActions)
            {
                if (pa.PluginId != lastPluginId)
                {
                    ActionPaletteItems.Add(new ActionPaletteItem(ActionType.Plugin, pa.PluginName, "🔌", pa.PluginName));
                    lastPluginId = pa.PluginId;
                }
                ActionPaletteItems.Add(new ActionPaletteItem(ActionType.Plugin, pa.ActionName,
                    string.IsNullOrEmpty(pa.Icon) ? "🔌" : pa.Icon, pa.Tooltip)
                {
                    IndentLevel = 1,
                    PreConfiguredAction = new PluginActionConfig { PluginId = pa.PluginId, ActionId = pa.ActionId }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load plugin actions");
        }
    }

    public async Task SaveCurrentProfileAsync()
    {
        if (SelectedProfile == null) return;
        await _profileService.UpdateProfileAsync(SelectedProfile);
        HasUnsavedChanges = false;
    }

    [RelayCommand]
    private async Task CreateNewProfile()
    {
        try
        {
            var newProfile = await _profileService.CreateProfileAsync($"Profile {Profiles.Count + 1}");
            Profiles.Add(newProfile);
            SelectedProfile = newProfile;
            StatusMessage = $"Created: {newProfile.Name}";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SaveProfile()
    {
        if (SelectedProfile == null) return;
        try
        {
            await _profileService.UpdateProfileAsync(SelectedProfile);
            HasUnsavedChanges = false;
            StatusMessage = $"Saved: {SelectedProfile.Name}";
        }
        catch (Exception ex) { StatusMessage = $"Error saving: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task ExportProfile()
    {
        if (SelectedProfile == null || _storageProvider == null) return;
        try
        {
            Directory.CreateDirectory(DefaultProfilesDir);
            var options = new FilePickerSaveOptions
            {
                Title = "Export Profile",
                SuggestedFileName = $"{SelectedProfile.Name.Replace(' ', '_')}.json",
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Profile JSON") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All Files")    { Patterns = new[] { "*" } }
                },
                SuggestedStartLocation = await _storageProvider.TryGetFolderFromPathAsync(DefaultProfilesDir)
            };
            var file = await _storageProvider.SaveFilePickerAsync(options);
            if (file == null) { StatusMessage = "Export cancelled"; return; }

            var json = JsonConvert.SerializeObject(SelectedProfile, Formatting.Indented);
            await File.WriteAllTextAsync(file.Path.LocalPath, json);
            await _profileService.UpdateProfileAsync(SelectedProfile);
            HasUnsavedChanges = false;
            StatusMessage = $"Exported: {Path.GetFileName(file.Path.LocalPath)}";
        }
        catch (Exception ex) { StatusMessage = $"Error exporting: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task LoadProfile()
    {
        if (_storageProvider == null) { StatusMessage = "Cannot open file dialog"; return; }
        try
        {
            Directory.CreateDirectory(DefaultProfilesDir);
            var options = new FilePickerOpenOptions
            {
                Title = "Load Profile",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Profile JSON") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All Files")    { Patterns = new[] { "*" } }
                },
                SuggestedStartLocation = await _storageProvider.TryGetFolderFromPathAsync(DefaultProfilesDir)
            };
            var files = await _storageProvider.OpenFilePickerAsync(options);
            if (files == null || files.Count == 0) { StatusMessage = "Load cancelled"; return; }

            var json    = await File.ReadAllTextAsync(files[0].Path.LocalPath);
            var profile = JsonConvert.DeserializeObject<Profile>(json);
            if (profile == null) { StatusMessage = "Invalid profile file"; return; }

            var existing = Profiles.FirstOrDefault(p => p.ProfileId == profile.ProfileId);
            if (existing != null) Profiles.Remove(existing);

            await _profileService.UpdateProfileAsync(profile);
            Profiles.Add(profile);
            SelectedProfile = profile;
            StatusMessage = $"Loaded: {profile.Name}";
        }
        catch (Exception ex) { StatusMessage = $"Error loading: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (SelectedProfile == null) return;
        var name      = SelectedProfile.Name;
        var profileId = SelectedProfile.ProfileId;
        try
        {
            if (_ipcClient.IsConnected)
            {
                var msg  = new IpcMessage { MessageType = IpcMessageTypes.ProfileDelete, Data = new { profileId } };
                var resp = await _ipcClient.SendAndWaitAsync(msg, TimeSpan.FromSeconds(10));
                if (!resp.Success) { StatusMessage = $"Failed to delete: {resp.Error}"; return; }
            }
            else
            {
                await _profileService.DeleteProfileAsync(profileId);
            }
            Profiles.Remove(SelectedProfile);
            SelectedProfile = Profiles.FirstOrDefault();
            StatusMessage = $"Deleted: {name}";
        }
        catch (Exception ex) { StatusMessage = $"Error deleting: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SendToDevice()
    {
        if (SelectedProfile == null || !_ipcClient.IsConnected)
        {
            StatusMessage = _ipcClient.IsConnected ? "" : "Not connected to Backend";
            return;
        }
        try
        {
            IsSyncing = true;
            StatusMessage = $"Sending {SelectedProfile.Name} to device...";
            await _profileService.UpdateProfileAsync(SelectedProfile);
            var msg  = new IpcMessage { MessageType = IpcMessageTypes.ProfileSendToDevice, Data = SelectedProfile };
            var resp = await _ipcClient.SendAndWaitAsync(msg, TimeSpan.FromSeconds(30));
            StatusMessage = resp.Success ? $"✅ {SelectedProfile.Name} sent" : $"❌ Failed: {resp.Error}";
        }
        catch (OperationCanceledException) { StatusMessage = "⏱ Send timed out"; }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsSyncing = false; }
    }

    [RelayCommand]
    private async Task LoadFromDevice()
    {
        if (SelectedProfile == null || !_ipcClient.IsConnected) return;
        try
        {
            IsSyncing = true;
            StatusMessage = $"Loading from device...";
            var msg  = new IpcMessage { MessageType = IpcMessageTypes.ProfileLoadFromDevice, Data = new { profileId = SelectedProfile.ProfileId } };
            var resp = await _ipcClient.SendAndWaitAsync(msg, TimeSpan.FromSeconds(30));
            if (resp.Success)
            {
                var loaded = resp.GetData<Profile>();
                if (loaded != null)
                {
                    var idx = Profiles.IndexOf(SelectedProfile);
                    if (idx >= 0) Profiles[idx] = loaded; else Profiles.Add(loaded);
                    SelectedProfile = loaded;
                    StatusMessage = $"✅ Loaded {loaded.Name} from device";
                }
            }
            else { StatusMessage = $"❌ Failed: {resp.Error}"; }
        }
        catch (OperationCanceledException) { StatusMessage = "⏱ Load timed out"; }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsSyncing = false; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IEnumerable<ProfileSwitchItem> GetAvailableProfileItems() =>
        Profiles.Select(p => new ProfileSwitchItem { ProfileId = p.ProfileId, Name = p.Name })
                .OrderBy(p => p.ProfileId);

    private IEnumerable<FolderSwitchItem> GetAvailableFolderItems()
    {
        if (SelectedProfile == null) return Enumerable.Empty<FolderSwitchItem>();
        var items = SelectedProfile.Folders
            .Select(f => new FolderSwitchItem { FolderId = f.FolderId, Name = f.Name })
            .ToList();
        if (items.Count == 0)
            for (byte i = 0; i < 4; i++)
                items.Add(new FolderSwitchItem { FolderId = i, Name = $"Folder {i}" });
        return items.OrderBy(f => f.FolderId);
    }

    private async Task SendFullProfileToDeviceAsync()
    {
        if (SelectedProfile == null || !_ipcClient.IsConnected) return;
        IsSyncing = true;
        try
        {
            var msg  = new IpcMessage { MessageType = IpcMessageTypes.ProfileSendToDevice, Data = SelectedProfile };
            var resp = await _ipcClient.SendAndWaitAsync(msg, TimeSpan.FromSeconds(30));
            StatusMessage = resp.Success ? $"✅ {SelectedProfile.Name} sent" : $"❌ {resp.Error}";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsSyncing = false; }
    }

    private async Task SendButtonConfigToDeviceAsync(byte profileId, ButtonConfig button)
    {
        try
        {
            if (button.Action != null)
            {
                var actionMsg  = new IpcMessage { MessageType = IpcMessageTypes.SetButtonAction,
                    Data = new { profileId, buttonId = button.ButtonId, action = button.Action } };
                await _ipcClient.SendAndWaitAsync(actionMsg, TimeSpan.FromSeconds(5));
            }
            var nameMsg = new IpcMessage { MessageType = IpcMessageTypes.SetButtonName,
                Data = new { profileId, buttonId = button.ButtonId, name = button.Name ?? string.Empty } };
            await _ipcClient.SendAndWaitAsync(nameMsg, TimeSpan.FromSeconds(5));

            var ledMsg = new IpcMessage { MessageType = IpcMessageTypes.SetLedColor,
                Data = new { profileId, buttonId = button.ButtonId, led = button.Led } };
            await _ipcClient.SendAndWaitAsync(ledMsg, TimeSpan.FromSeconds(5));

            StatusMessage = $"Button {button.ButtonId + 1} synced";
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Error sending button config to device"); }
    }
}
