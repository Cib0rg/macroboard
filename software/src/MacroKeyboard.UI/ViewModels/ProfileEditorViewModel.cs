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

    [ObservableProperty]
    private ButtonConfigDialogViewModel? _buttonConfigViewModel;

    [ObservableProperty]
    private bool _isButtonConfigVisible;

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
        new ActionPaletteItem(ActionType.Keyboard, "Keyboard", "⌨", "Emulate keyboard key press or text input"),
        new ActionPaletteItem(ActionType.Shell, "Shell", "💻", "Execute a shell command on the PC"),
        new ActionPaletteItem(ActionType.Sequence, "Sequence", "📋", "Execute multiple actions in sequence"),
        new ActionPaletteItem(ActionType.ProfileSwitch, "Profile", "🔄", "Switch to another profile"),
        new ActionPaletteItem(ActionType.Folder, "Folder", "📁", "Open a folder of sub-buttons"),
        new ActionPaletteItem(ActionType.CustomHid, "Custom HID", "🔌", "Send custom HID report"),
        new ActionPaletteItem(ActionType.None, "None", "⊘", "No action assigned"),
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
            // Add root button
            FlattenedButtons.Add(new FlattenedButtonItem(button, 0));
            
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
                    FlattenedButtons.Add(new FlattenedButtonItem(folderButton, 1, folderId));
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

    [RelayCommand]
    private async Task SaveProfile()
    {
        if (SelectedProfile == null)
            return;

        try
        {
            if (_storageProvider == null)
            {
                _logger.LogWarning("StorageProvider not set, saving to default location");
                await _profileService.UpdateProfileAsync(SelectedProfile);
                StatusMessage = $"Saved: {SelectedProfile.Name}";
                return;
            }

            // Ensure default directory exists
            Directory.CreateDirectory(DefaultProfilesDir);

            var suggestedName = $"{SelectedProfile.Name.Replace(' ', '_')}.json";

            var options = new FilePickerSaveOptions
            {
                Title = "Save Profile",
                SuggestedFileName = suggestedName,
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Profile JSON") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                },
                SuggestedStartLocation = await _storageProvider.TryGetFolderFromPathAsync(DefaultProfilesDir)
            };

            var file = await _storageProvider.SaveFilePickerAsync(options);
            if (file == null)
            {
                StatusMessage = "Save cancelled";
                return;
            }

            var filePath = file.Path.LocalPath;
            _logger.LogInformation("Saving profile to: {Path}", filePath);

            var json = JsonConvert.SerializeObject(SelectedProfile, Formatting.Indented);
            await File.WriteAllTextAsync(filePath, json);

            // Also save to internal storage
            await _profileService.UpdateProfileAsync(SelectedProfile);

            StatusMessage = $"Saved: {Path.GetFileName(filePath)}";
            _logger.LogInformation("Profile saved to {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving profile");
            StatusMessage = $"Error saving: {ex.Message}";
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

        try
        {
            var name = SelectedProfile.Name;
            _logger.LogInformation("Deleting profile: {ProfileName}", name);
            await _profileService.DeleteProfileAsync(SelectedProfile.ProfileId);
            Profiles.Remove(SelectedProfile);
            SelectedProfile = Profiles.FirstOrDefault();
            _logger.LogInformation("Profile deleted successfully");
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

            // Then send to device via IPC
            var message = new IpcMessage
            {
                MessageType = IpcMessageTypes.ProfileSendToDevice,
                Data = new { profileId = SelectedProfile.ProfileId }
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
        if (item == null)
            return;

        OpenButtonConfigInline(item.Button);
    }

    /// <summary>
    /// Open the inline button configuration panel
    /// </summary>
    private void OpenButtonConfigInline(ButtonConfig button)
    {
        _logger.LogInformation("🔘 Configuring button {ButtonId} inline", button.ButtonId);
        
        // Create or update the inline config ViewModel
        ButtonConfigViewModel = new ButtonConfigDialogViewModel(_dialogLogger, button);
        IsButtonConfigVisible = true;
    }

    /// <summary>
    /// Handle dropping an action type onto a button — opens inline config with the action pre-selected
    /// </summary>
    public void HandleActionDropOnButton(FlattenedButtonItem buttonItem, ActionType actionType)
    {
        _logger.LogInformation("🎯 Action {ActionType} dropped on button {ButtonId}", actionType, buttonItem.Button.ButtonId);
        
        // Open inline config for this button
        ButtonConfigViewModel = new ButtonConfigDialogViewModel(_dialogLogger, buttonItem.Button);
        ButtonConfigViewModel.SelectedActionType = actionType;
        IsButtonConfigVisible = true;
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
            // The ButtonConfigDialogViewModel already modifies the ButtonConfig directly
            // via its Save logic, so we just need to persist
            ButtonConfigViewModel.SaveToButtonConfig();
            
            var button = ButtonConfigViewModel.ButtonConfig;
            _logger.LogInformation("Button {ButtonId} configured successfully", button.ButtonId);
            
            // Save profile locally
            if (SelectedProfile != null)
            {
                await _profileService.UpdateProfileAsync(SelectedProfile);
                StatusMessage = $"Button {button.ButtonId} configured";
            }

            // Rebuild flattened list to update button labels
            BuildFlattenedButtons();

            // If connected, also send button action and LED to device
            if (_ipcClient.IsConnected && SelectedProfile != null)
            {
                await SendButtonConfigToDeviceAsync(SelectedProfile.ProfileId, button);
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
        IsButtonConfigVisible = false;
        ButtonConfigViewModel = null;
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

            StatusMessage = $"Button {button.ButtonId} synced to device";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error sending button config to device (non-critical)");
        }
    }
}
