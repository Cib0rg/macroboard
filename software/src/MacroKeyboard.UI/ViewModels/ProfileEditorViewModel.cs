using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Shared.IPC;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// Profile Editor ViewModel — manages profiles, sends/loads to/from device via IPC
/// </summary>
public partial class ProfileEditorViewModel : ViewModelBase
{
    private readonly IProfileService _profileService;
    private readonly IpcClient _ipcClient;
    private readonly ILogger<ProfileEditorViewModel> _logger;
    private readonly ILogger<ButtonConfigDialogViewModel> _dialogLogger;
    private IStorageProvider? _storageProvider;

    /// <summary>
    /// Default profiles directory (relative to app working directory)
    /// </summary>
    private static readonly string DefaultProfilesDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Profiles");

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

    // ============================================
    // Encoder configuration
    // ============================================

    private int _encoderEditingSlot = -1;
    private readonly ButtonConfig _encoderCwConfig    = new() { ButtonId = 200 };
    private readonly ButtonConfig _encoderCcwConfig   = new() { ButtonId = 201 };
    private readonly ButtonConfig _encoderPressConfig = new() { ButtonId = 202 };
    private readonly ButtonConfig _encoderLongConfig  = new() { ButtonId = 203 };

    public string EncoderCwActionDisplay   => GetActionDisplayName(_encoderCwConfig.Action);
    public string EncoderCcwActionDisplay  => GetActionDisplayName(_encoderCcwConfig.Action);
    public string EncoderPressActionDisplay => GetActionDisplayName(_encoderPressConfig.Action);
    public string EncoderLongPressActionDisplay => GetActionDisplayName(_encoderLongConfig.Action);

    private static string GetActionDisplayName(ActionConfig? action) => action switch
    {
        null or NoneAction => "None",
        KeyboardAction ka when ka.KeyCode != 0 => $"Key: 0x{ka.KeyCode:X2}",
        KeyboardAction => "Type text",
        MediaAction ma => $"Media: {ma.Key}",
        ShellAction sa => $"Shell: {sa.Command?[..Math.Min(sa.Command?.Length ?? 0, 20)] ?? "..."}",
        LaunchAppAction la => $"Launch: {System.IO.Path.GetFileNameWithoutExtension(la.ExecutablePath ?? "App")}",
        FolderAction => "Folder",
        SequenceAction => "Sequence",
        CustomHidAction => "Custom HID",
        NightModeAction => "Night Mode",
        DelayAction da => $"Delay {da.DelayMs}ms",
        _ => action.ActionType.ToString()
    };

    [RelayCommand]
    private void ConfigureEncoderCw() { _encoderEditingSlot = 0; OpenButtonConfigInline(_encoderCwConfig); }

    [RelayCommand]
    private void ConfigureEncoderCcw() { _encoderEditingSlot = 1; OpenButtonConfigInline(_encoderCcwConfig); }

    [RelayCommand]
    private void ConfigureEncoderPress() { _encoderEditingSlot = 2; OpenButtonConfigInline(_encoderPressConfig); }

    [RelayCommand]
    private void ConfigureEncoderLongPress() { _encoderEditingSlot = 3; OpenButtonConfigInline(_encoderLongConfig); }

    [ObservableProperty]
    private ButtonConfigDialogViewModel? _buttonConfigViewModel;

    [ObservableProperty]
    private bool _isButtonConfigVisible;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// Reference to the ButtonConfig currently being edited (used for inline expansion matching)
    /// </summary>
    [ObservableProperty]
    private ButtonConfig? _configuredButtonConfig;

    public ObservableCollection<Profile> Profiles { get; } = new();
    
    /// <summary>
    /// Flattened list of buttons including folder contents, for the nested tree view
    /// </summary>
    public ObservableCollection<FlattenedButtonItem> FlattenedButtons { get; } = new();

    /// <summary>
    /// Actions palette items for drag-n-drop assignment
    /// </summary>
    public ObservableCollection<ActionPaletteItem> ActionPaletteItems { get; } = new()
    {
        new ActionPaletteItem(ActionType.Keyboard,  "Keyboard",   "⌨",  "Emulate keyboard key press or text input"),

        // Media — group header + pre-configured sub-items
        new ActionPaletteItem(ActionType.Media, "Media", "🔊", "Media keys — choose specific key below or drag to configure"),
        new ActionPaletteItem(ActionType.Media, "Volume Up",    "🔊", "Increase system volume")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.VolumeUp } },
        new ActionPaletteItem(ActionType.Media, "Volume Down",  "🔉", "Decrease system volume")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.VolumeDown } },
        new ActionPaletteItem(ActionType.Media, "Mute",         "🔇", "Toggle mute")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.Mute } },
        new ActionPaletteItem(ActionType.Media, "Play / Pause", "⏯",  "Play or pause media")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.PlayPause } },
        new ActionPaletteItem(ActionType.Media, "Next Track",   "⏭",  "Skip to next track")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.NextTrack } },
        new ActionPaletteItem(ActionType.Media, "Prev Track",   "⏮",  "Go to previous track")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.PreviousTrack } },
        new ActionPaletteItem(ActionType.Media, "Stop",         "⏹",  "Stop playback")
            { IndentLevel = 1, PreConfiguredAction = new MediaAction { Key = MediaKey.Stop } },

        new ActionPaletteItem(ActionType.LaunchApp,     "Launch App",  "🚀", "Launch an application with optional arguments"),
        new ActionPaletteItem(ActionType.Shell,         "Shell",       "💻", "Execute a shell command on the PC"),
        new ActionPaletteItem(ActionType.Sequence,      "Sequence",    "📋", "Execute multiple actions in sequence"),
        new ActionPaletteItem(ActionType.Folder,        "Folder",      "📁", "Open a folder of sub-buttons"),
        new ActionPaletteItem(ActionType.CustomHid,     "Custom HID",  "🔌", "Send custom HID report"),
        new ActionPaletteItem(ActionType.NightMode,     "Night Mode",  "🌙", "Toggle all LEDs and display brightness off; press again to restore"),
        new ActionPaletteItem(ActionType.None,          "None",        "⊘",  "No action assigned"),
    };

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
    }

    /// <summary>
    /// Called by source generator when SelectedProfile changes
    /// </summary>
    partial void OnSelectedProfileChanged(Profile? value)
    {
        BuildFlattenedButtons();
        SyncEncoderFromProfile();
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

    /// <summary>
    /// Build the flattened button list from the selected profile.
    /// Root buttons at level 0, folder contents at level 1+.
    /// </summary>
    public void BuildFlattenedButtons()
    {
        FlattenedButtons.Clear();
        
        if (SelectedProfile == null)
            return;

        foreach (var button in SelectedProfile.Buttons)
        {
            // Add root button with folder name if applicable
            var item = new FlattenedButtonItem(button, 0);
            
            // If this button opens a folder, set the folder display name
            if (button.Action?.ActionType == ActionType.Folder)
            {
                var folderId = button.FolderId;
                var folder = SelectedProfile.Folders.FirstOrDefault(f => f.FolderId == folderId);
                item.FolderDisplayName = folder?.Name ?? $"Folder {folderId}";
            }
            
            FlattenedButtons.Add(item);
            
            // If this button opens a folder, add the folder's buttons indented
            if (button.Action?.ActionType == ActionType.Folder)
            {
                var folderId = button.FolderId;
                var folder = SelectedProfile.Folders.FirstOrDefault(f => f.FolderId == folderId);
                
                if (folder == null)
                {
                    // Auto-create folder if it doesn't exist
                    folder = new Folder
                    {
                        FolderId = folderId,
                        Name = $"Folder {folderId}"
                    };
                    
                    // Initialize 10 empty buttons
                    for (byte i = 0; i < 10; i++)
                    {
                        folder.Buttons.Add(new ButtonConfig
                        {
                            ButtonId = i,
                            Action = null,
                            Led = LedConfig.FromRgb(80, 80, 80)
                        });
                    }
                    
                    SelectedProfile.Folders.Add(folder);
                }
                
                foreach (var folderButton in folder.Buttons)
                {
                    FlattenedButtons.Add(new FlattenedButtonItem(folderButton, 1, folderId, button.ButtonId));
                }
            }
        }
    }

    public async Task LoadProfilesAsync()
    {
        try
        {
            IsLoading = true;
            _logger.LogInformation("Loading profiles...");

            var profiles = await _profileService.GetAllProfilesAsync();
            
            Profiles.Clear();
            foreach (var profile in profiles)
            {
                Profiles.Add(profile);
            }

            if (Profiles.Any())
            {
                SelectedProfile = Profiles.First();
            }

            _logger.LogInformation("Loaded {Count} profiles", Profiles.Count);
            StatusMessage = $"Loaded {Profiles.Count} profiles";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading profiles");
            StatusMessage = $"Error loading profiles: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CreateNewProfile()
    {
        try
        {
            _logger.LogInformation("Creating new profile...");

            var newProfile = await _profileService.CreateProfileAsync($"Profile {Profiles.Count + 1}");
            Profiles.Add(newProfile);
            SelectedProfile = newProfile;

            _logger.LogInformation("Created new profile: {ProfileName}", newProfile.Name);
            StatusMessage = $"Created: {newProfile.Name}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating profile");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Set the storage provider for file dialogs (called from View code-behind)
    /// </summary>
    public void SetStorageProvider(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
    }

    /// <summary>
    /// Save the current profile to internal storage without showing a file dialog.
    /// Used when the application exits with unsaved changes.
    /// </summary>
    public async Task SaveCurrentProfileAsync()
    {
        if (SelectedProfile == null) return;
        await _profileService.UpdateProfileAsync(SelectedProfile);
        HasUnsavedChanges = false;
    }

    [RelayCommand]
    private async Task SaveProfile()
    {
        if (SelectedProfile == null)
            return;

        try
        {
            await _profileService.UpdateProfileAsync(SelectedProfile);
            HasUnsavedChanges = false;
            StatusMessage = $"Saved: {SelectedProfile.Name}";
            _logger.LogInformation("Profile '{Name}' saved", SelectedProfile.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving profile");
            StatusMessage = $"Error saving: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportProfile()
    {
        if (SelectedProfile == null || _storageProvider == null)
            return;

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
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting profile");
            StatusMessage = $"Error exporting: {ex.Message}";
        }
    }

    /// <summary>
    /// Load a profile from a JSON file via file dialog
    /// </summary>
    [RelayCommand]
    private async Task LoadProfile()
    {
        try
        {
            if (_storageProvider == null)
            {
                _logger.LogWarning("StorageProvider not set");
                StatusMessage = "Cannot open file dialog";
                return;
            }

            // Ensure default directory exists
            Directory.CreateDirectory(DefaultProfilesDir);

            var options = new FilePickerOpenOptions
            {
                Title = "Load Profile",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Profile JSON") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                },
                SuggestedStartLocation = await _storageProvider.TryGetFolderFromPathAsync(DefaultProfilesDir)
            };

            var files = await _storageProvider.OpenFilePickerAsync(options);
            if (files == null || files.Count == 0)
            {
                StatusMessage = "Load cancelled";
                return;
            }

            var filePath = files[0].Path.LocalPath;
            _logger.LogInformation("Loading profile from: {Path}", filePath);

            var json = await File.ReadAllTextAsync(filePath);
            var profile = JsonConvert.DeserializeObject<Profile>(json);

            if (profile == null)
            {
                StatusMessage = "Invalid profile file";
                return;
            }

            // Check if profile with same ID already exists
            var existing = Profiles.FirstOrDefault(p => p.ProfileId == profile.ProfileId);
            if (existing != null)
            {
                Profiles.Remove(existing);
            }

            // Save to internal storage and add to list
            await _profileService.UpdateProfileAsync(profile);
            Profiles.Add(profile);
            SelectedProfile = profile;

            StatusMessage = $"Loaded: {profile.Name} from {Path.GetFileName(filePath)}";
            _logger.LogInformation("Profile loaded: {Name}", profile.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading profile");
            StatusMessage = $"Error loading: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (SelectedProfile == null)
            return;

        var name = SelectedProfile.Name;
        var profileId = SelectedProfile.ProfileId;

        try
        {
            _logger.LogInformation("Deleting profile: {ProfileName}", name);

            if (_ipcClient.IsConnected)
            {
                // Backend handles both local JSON and device flash deletion
                var message = new IpcMessage
                {
                    MessageType = IpcMessageTypes.ProfileDelete,
                    Data = new { profileId }
                };
                var response = await _ipcClient.SendAndWaitAsync(message, TimeSpan.FromSeconds(10));
                if (!response.Success)
                {
                    StatusMessage = $"❌ Failed to delete: {response.Error}";
                    return;
                }
            }
            else
            {
                // Backend offline — delete local file only
                await _profileService.DeleteProfileAsync(profileId);
            }

            Profiles.Remove(SelectedProfile);
            SelectedProfile = Profiles.FirstOrDefault();
            _logger.LogInformation("Profile deleted: {ProfileName}", name);
            StatusMessage = $"Deleted: {name}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile");
            StatusMessage = $"Error deleting: {ex.Message}";
        }
    }

    /// <summary>
    /// Send the selected profile to the device via IPC → Backend → USB
    /// </summary>
    [RelayCommand]
    private async Task SendToDevice()
    {
        if (SelectedProfile == null)
            return;

        if (!_ipcClient.IsConnected)
        {
            StatusMessage = "Not connected to Backend";
            return;
        }

        try
        {
            IsSyncing = true;
            SyncProgress = 0;
            StatusMessage = $"Sending {SelectedProfile.Name} to device...";
            
            _logger.LogInformation("Sending profile {ProfileId} to device via IPC", SelectedProfile.ProfileId);

            // First save locally
            await _profileService.UpdateProfileAsync(SelectedProfile);

            // Then send to device via IPC (send full profile object so backend has all button data)
            var message = new IpcMessage
            {
                MessageType = IpcMessageTypes.ProfileSendToDevice,
                Data = SelectedProfile
            };

            var response = await _ipcClient.SendAndWaitAsync(message, TimeSpan.FromSeconds(30));
            
            if (response.Success)
            {
                StatusMessage = $"✅ {SelectedProfile.Name} sent to device";
                _logger.LogInformation("Profile sent to device successfully");
            }
            else
            {
                StatusMessage = $"❌ Failed: {response.Error}";
                _logger.LogError("Failed to send profile to device: {Error}", response.Error);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⏱ Send timed out";
            _logger.LogWarning("Send to device timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending profile to device");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
            SyncProgress = 0;
        }
    }

    /// <summary>
    /// Load profile from device via IPC → Backend → USB
    /// </summary>
    [RelayCommand]
    private async Task LoadFromDevice()
    {
        if (SelectedProfile == null)
            return;

        if (!_ipcClient.IsConnected)
        {
            StatusMessage = "Not connected to Backend";
            return;
        }

        try
        {
            IsSyncing = true;
            StatusMessage = $"Loading profile {SelectedProfile.ProfileId} from device...";
            
            _logger.LogInformation("Loading profile {ProfileId} from device via IPC", SelectedProfile.ProfileId);

            var message = new IpcMessage
            {
                MessageType = IpcMessageTypes.ProfileLoadFromDevice,
                Data = new { profileId = SelectedProfile.ProfileId }
            };

            var response = await _ipcClient.SendAndWaitAsync(message, TimeSpan.FromSeconds(30));
            
            if (response.Success)
            {
                var loadedProfile = response.GetData<Profile>();
                if (loadedProfile != null)
                {
                    // Update the profile in the list
                    var index = Profiles.IndexOf(SelectedProfile);
                    if (index >= 0)
                    {
                        Profiles[index] = loadedProfile;
                        SelectedProfile = loadedProfile;
                    }
                    else
                    {
                        Profiles.Add(loadedProfile);
                        SelectedProfile = loadedProfile;
                    }

                    StatusMessage = $"✅ Loaded {loadedProfile.Name} from device";
                    _logger.LogInformation("Profile loaded from device successfully");
                }
                else
                {
                    StatusMessage = "❌ Failed to parse loaded profile";
                }
            }
            else
            {
                StatusMessage = $"❌ Failed: {response.Error}";
                _logger.LogError("Failed to load profile from device: {Error}", response.Error);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⏱ Load timed out";
            _logger.LogWarning("Load from device timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading profile from device");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private void ConfigureButton(ButtonConfig? button)
    {
        if (button == null)
            return;

        OpenButtonConfigInline(button);
    }

    /// <summary>
    /// Configure a button from the flattened list (used by the nested tree view)
    /// </summary>
    [RelayCommand]
    private void ConfigureFlattenedButton(FlattenedButtonItem? item)
    {
        if (item == null || item.IsBackButton)
            return;

        OpenButtonConfigInline(item.Button);
    }

    /// <summary>
    /// Configure the long press action for a button.
    /// Uses a synthetic ButtonConfig with ButtonId = actual + 100 so SaveButtonConfig
    /// can detect the long press path, while ConfiguredButtonConfig stays as the real
    /// button so the inline panel opens in the correct row.
    /// </summary>
    [RelayCommand]
    private void ConfigureButtonLongPress(FlattenedButtonItem? item)
    {
        if (item == null || item.IsBackButton) return;

        var synthetic = new ButtonConfig
        {
            ButtonId = (byte)(item.Button.ButtonId + 100),
            Action = item.Button.LongPressAction,
            Name = item.Button.LongPressName
        };

        var profileItems = GetAvailableProfileItems();
        var folderItems = GetAvailableFolderItems();
        ButtonConfigViewModel = new ButtonConfigDialogViewModel(_dialogLogger, synthetic, profileItems, folderItems);
        ButtonConfigViewModel.IsLongPress = true;
        if (_storageProvider != null)
            ButtonConfigViewModel.SetStorageProvider(_storageProvider);
        ConfiguredButtonConfig = item.Button;   // panel appears under the real row
        IsButtonConfigVisible = true;
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// Open the inline button configuration panel
    /// </summary>
    private void OpenButtonConfigInline(ButtonConfig button)
    {
        _logger.LogInformation("🔘 Configuring button {ButtonId} inline", button.ButtonId);
        
        // Close any previously open config panel (without saving)
        if (ConfiguredButtonConfig != null && !ReferenceEquals(ConfiguredButtonConfig, button))
        {
            ConfiguredButtonConfig = null;
            ButtonConfigViewModel = null;
            IsButtonConfigVisible = false;
        }
        
        // Create or update the inline config ViewModel with available profiles and folders
        var profileItems = GetAvailableProfileItems();
        var folderItems = GetAvailableFolderItems();
        ButtonConfigViewModel = new ButtonConfigDialogViewModel(_dialogLogger, button, profileItems, folderItems);
        if (_storageProvider != null)
            ButtonConfigViewModel.SetStorageProvider(_storageProvider);
        ConfiguredButtonConfig = button;
        IsButtonConfigVisible = true;
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// Handle dropping an action type onto a button — opens inline config with the action pre-selected
    /// </summary>
    public void HandleActionDropOnButton(FlattenedButtonItem buttonItem, ActionType actionType)
    {
        if (buttonItem.IsFolderHeader || buttonItem.IsBackButton)
        {
            _logger.LogInformation("⛔ Drag blocked: button {ButtonId} is locked (folder entry or back slot)", buttonItem.Button.ButtonId);
            return;
        }

        _logger.LogInformation("🎯 Action {ActionType} dropped on button {ButtonId}", actionType, buttonItem.Button.ButtonId);

        // Open inline config for this button with available profiles and folders
        var profileItems = GetAvailableProfileItems();
        var folderItems = GetAvailableFolderItems();
        ButtonConfigViewModel = new ButtonConfigDialogViewModel(_dialogLogger, buttonItem.Button, profileItems, folderItems);
        if (_storageProvider != null)
            ButtonConfigViewModel.SetStorageProvider(_storageProvider);
        ButtonConfigViewModel.SelectedActionType = actionType;
        ConfiguredButtonConfig = buttonItem.Button;
        IsButtonConfigVisible = true;
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// Handle dropping a pre-configured action sub-item — applies the action directly without
    /// opening the config editor (the action is fully specified, no further input needed).
    /// </summary>
    public async Task HandlePreConfiguredActionDrop(FlattenedButtonItem buttonItem, ActionConfig action)
    {
        if (buttonItem.IsFolderHeader || buttonItem.IsBackButton)
            return;

        _logger.LogInformation("⚡ Pre-configured {ActionType} dropped on button {ButtonId}",
            action.ActionType, buttonItem.Button.ButtonId);

        buttonItem.Button.Action = action;
        HasUnsavedChanges = true;

        if (SelectedProfile != null)
        {
            await _profileService.UpdateProfileAsync(SelectedProfile);
            HasUnsavedChanges = false;
            StatusMessage = $"Button {buttonItem.Button.ButtonId + 1}: {action.ActionType}";
        }

        BuildFlattenedButtons();

        if (_ipcClient.IsConnected && SelectedProfile != null)
            await SendButtonConfigToDeviceAsync(SelectedProfile.ProfileId, buttonItem.Button);
    }

    /// <summary>
    /// Get the list of available profiles as ProfileSwitchItems for the ComboBox.
    /// Uses the Profiles collection which already contains all loaded profiles (local + device).
    /// </summary>
    private IEnumerable<ProfileSwitchItem> GetAvailableProfileItems()
    {
        return Profiles.Select(p => new ProfileSwitchItem
        {
            ProfileId = p.ProfileId,
            Name = p.Name
        }).OrderBy(p => p.ProfileId);
    }

    /// <summary>
    /// Get the list of available folders as FolderSwitchItems for the ComboBox.
    /// Folders exist only within the current profile.
    /// </summary>
    private IEnumerable<FolderSwitchItem> GetAvailableFolderItems()
    {
        if (SelectedProfile == null)
            return Enumerable.Empty<FolderSwitchItem>();

        // Get folders from the currently selected profile
        var items = SelectedProfile.Folders.Select(f => new FolderSwitchItem
        {
            FolderId = f.FolderId,
            Name = f.Name
        }).ToList();
        
        // If no folders exist yet, provide default options so user can create one
        if (items.Count == 0)
        {
            for (byte i = 0; i < 4; i++)
            {
                items.Add(new FolderSwitchItem
                {
                    FolderId = i,
                    Name = $"Folder {i}"
                });
            }
        }
        
        return items.OrderBy(f => f.FolderId);
    }

    /// <summary>
    /// Save the current button configuration and close the inline editor
    /// </summary>
    [RelayCommand]
    private async Task SaveButtonConfig()
    {
        if (ButtonConfigViewModel == null)
            return;

        try
        {
            // Ensure LaunchApp icon is extracted even when path was typed manually
            await ButtonConfigViewModel.EnsureIconExtractedAsync();

            ButtonConfigViewModel.SaveToButtonConfig();

            var button = ButtonConfigViewModel.ButtonConfig;

            // Button long press editing (synthetic ButtonId = actual + 100) — save to LongPressAction
            if (button.ButtonId >= 100 && button.ButtonId < 200 && SelectedProfile != null)
            {
                var actualId = (byte)(button.ButtonId - 100);
                var actualButton = SelectedProfile.Buttons.FirstOrDefault(b => b.ButtonId == actualId);
                if (actualButton != null)
                {
                    actualButton.LongPressAction = button.Action;
                    actualButton.LongPressName = string.IsNullOrWhiteSpace(button.Name) ? null : button.Name.Trim();
                    await _profileService.UpdateProfileAsync(SelectedProfile);
                    HasUnsavedChanges = false;
                    StatusMessage = $"Button {actualId + 1} long press configured";
                    BuildFlattenedButtons();
                    if (_ipcClient.IsConnected)
                        await SendFullProfileToDeviceAsync();
                }
                IsButtonConfigVisible = false;
                ButtonConfigViewModel = null;
                ConfiguredButtonConfig = null;
                return;
            }

            // Encoder slot editing — save action back to EncoderConfig and return early
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
                IsButtonConfigVisible = false;
                ButtonConfigViewModel = null;
                ConfiguredButtonConfig = null;
                if (_ipcClient.IsConnected)
                    await SendFullProfileToDeviceAsync();
                return;
            }

            // Handle Folder action: find or create folder by name, assign ID
            if (button.Action is FolderAction && SelectedProfile != null)
            {
                var folderName = ButtonConfigViewModel.FolderName;
                if (string.IsNullOrWhiteSpace(folderName))
                    folderName = "New Folder";
                
                // Find existing folder by name or create a new one
                var existingFolder = SelectedProfile.Folders.FirstOrDefault(
                    f => f.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase));
                
                if (existingFolder != null)
                {
                    // Use existing folder
                    button.FolderId = existingFolder.FolderId;
                    if (button.Action is FolderAction fa)
                        fa.FolderId = existingFolder.FolderId;
                }
                else
                {
                    // Create new folder with next available ID
                    byte newFolderId = 0;
                    if (SelectedProfile.Folders.Count > 0)
                        newFolderId = (byte)(SelectedProfile.Folders.Max(f => f.FolderId) + 1);
                    
                    var newFolder = new Folder
                    {
                        FolderId = newFolderId,
                        Name = folderName
                    };
                    
                    // Initialize with 10 empty buttons
                    for (byte i = 0; i < 10; i++)
                    {
                        newFolder.Buttons.Add(new ButtonConfig
                        {
                            ButtonId = i,
                            Action = null,
                            Led = LedConfig.FromRgb(80, 80, 80)
                        });
                    }
                    
                    SelectedProfile.Folders.Add(newFolder);
                    button.FolderId = newFolderId;
                    if (button.Action is FolderAction fa)
                        fa.FolderId = newFolderId;
                    
                    _logger.LogInformation("Created new folder '{Name}' with ID {Id}", folderName, newFolderId);
                }
            }
            
            _logger.LogInformation("Button {ButtonId} configured successfully", button.ButtonId);

            // Save profile locally
            if (SelectedProfile != null)
            {
                await _profileService.UpdateProfileAsync(SelectedProfile);
                HasUnsavedChanges = false;
                StatusMessage = $"Button {button.ButtonId + 1} configured";
            }

            // Rebuild flattened list to update button labels
            BuildFlattenedButtons();

            // If connected, sync to device
            if (_ipcClient.IsConnected && SelectedProfile != null)
            {
                bool isFolderButton = !SelectedProfile.Buttons.Contains(button);
                bool isFolderAction = button.Action?.ActionType == ActionType.Folder;
                bool hasImage = !string.IsNullOrEmpty(button.ImagePath);

                if (isFolderButton || isFolderAction || hasImage)
                {
                    // Folder buttons have no profile-level address in the IPC protocol —
                    // individual button update would land on the wrong slot.
                    // Full profile sync also ensures firmware has initialized folder data
                    // before the user presses the folder button (prevents reboot on folder enter).
                    // Buttons with images also require full sync — there is no per-button
                    // image transfer IPC message, only the full ProfileSendToDevice path.
                    await SendFullProfileToDeviceAsync();
                }
                else
                {
                    await SendButtonConfigToDeviceAsync(SelectedProfile.ProfileId, button);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving button configuration");
            StatusMessage = $"Error: {ex.Message}";
        }
    }


    /// <summary>
    /// Close the inline button config editor without saving
    /// </summary>
    [RelayCommand]
    private void CloseButtonConfig()
    {
        _encoderEditingSlot = -1;
        IsButtonConfigVisible = false;
        ButtonConfigViewModel = null;
        ConfiguredButtonConfig = null;
        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Send the full profile to the device — used when folder structure changes.
    /// </summary>
    private async Task SendFullProfileToDeviceAsync()
    {
        if (SelectedProfile == null || !_ipcClient.IsConnected) return;
        IsSyncing = true;
        try
        {
            var message = new IpcMessage
            {
                MessageType = IpcMessageTypes.ProfileSendToDevice,
                Data = SelectedProfile
            };
            var response = await _ipcClient.SendAndWaitAsync(message, TimeSpan.FromSeconds(30));
            StatusMessage = response.Success
                ? $"✅ {SelectedProfile.Name} sent to device"
                : $"❌ Failed: {response.Error}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending full profile to device");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    /// <summary>
    /// Send individual button config (action + LED) to device via IPC
    /// </summary>
    private async Task SendButtonConfigToDeviceAsync(byte profileId, ButtonConfig button)
    {
        try
        {
            // Send button action
            if (button.Action != null)
            {
                var actionMsg = new IpcMessage
                {
                    MessageType = IpcMessageTypes.SetButtonAction,
                    Data = new
                    {
                        profileId = profileId,
                        buttonId = button.ButtonId,
                        action = button.Action
                    }
                };

                var actionResponse = await _ipcClient.SendAndWaitAsync(actionMsg, TimeSpan.FromSeconds(5));
                if (!actionResponse.Success)
                {
                    _logger.LogWarning("Failed to set button action on device: {Error}", actionResponse.Error);
                }
            }

            // Send button name (empty string clears it; firmware falls back to auto-label from action)
            var nameMsg = new IpcMessage
            {
                MessageType = IpcMessageTypes.SetButtonName,
                Data = new
                {
                    profileId = profileId,
                    buttonId = button.ButtonId,
                    name = button.Name ?? string.Empty
                }
            };

            var nameResponse = await _ipcClient.SendAndWaitAsync(nameMsg, TimeSpan.FromSeconds(5));
            if (!nameResponse.Success)
            {
                _logger.LogWarning("Failed to set button name on device: {Error}", nameResponse.Error);
            }

            // Send LED color
            var ledMsg = new IpcMessage
            {
                MessageType = IpcMessageTypes.SetLedColor,
                Data = new
                {
                    profileId = profileId,
                    buttonId = button.ButtonId,
                    led = button.Led
                }
            };

            var ledResponse = await _ipcClient.SendAndWaitAsync(ledMsg, TimeSpan.FromSeconds(5));
            if (!ledResponse.Success)
            {
                _logger.LogWarning("Failed to set LED color on device: {Error}", ledResponse.Error);
            }

            StatusMessage = $"Button {button.ButtonId + 1} synced to device";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error sending button config to device (non-critical)");
        }
    }
}

