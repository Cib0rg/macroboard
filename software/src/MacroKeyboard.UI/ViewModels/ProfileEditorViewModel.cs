using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Shared.IPC;
using MacroKeyboard.UI.Views;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
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

    public ObservableCollection<Profile> Profiles { get; } = new();

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

    [RelayCommand]
    private async Task SaveProfile()
    {
        if (SelectedProfile == null)
            return;

        try
        {
            _logger.LogInformation("Saving profile: {ProfileName}", SelectedProfile.Name);
            await _profileService.UpdateProfileAsync(SelectedProfile);
            _logger.LogInformation("Profile saved successfully");
            StatusMessage = $"Saved: {SelectedProfile.Name}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving profile");
            StatusMessage = $"Error saving: {ex.Message}";
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
    private async Task ConfigureButton(ButtonConfig? button)
    {
        if (button == null)
            return;

        _logger.LogInformation("🔘 Configuring button {ButtonId}", button.ButtonId);
        
        try
        {
            // Get main window
            var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow == null)
            {
                _logger.LogWarning("Main window not found");
                return;
            }

            // Create dialog
            var dialogViewModel = new ButtonConfigDialogViewModel(_dialogLogger, button);

            var dialog = new ButtonConfigDialog
            {
                DataContext = dialogViewModel
            };

            // Show dialog
            var result = await dialog.ShowDialog<bool>(mainWindow);
            
            if (result)
            {
                _logger.LogInformation("Button {ButtonId} configured successfully", button.ButtonId);
                
                // Save profile locally
                if (SelectedProfile != null)
                {
                    await _profileService.UpdateProfileAsync(SelectedProfile);
                    StatusMessage = $"Button {button.ButtonId} configured";
                }

                // If connected, also send button action and LED to device
                if (_ipcClient.IsConnected && SelectedProfile != null)
                {
                    await SendButtonConfigToDeviceAsync(SelectedProfile.ProfileId, button);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening button configuration dialog");
            StatusMessage = $"Error: {ex.Message}";
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
