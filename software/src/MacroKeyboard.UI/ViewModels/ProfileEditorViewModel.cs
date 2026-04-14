using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.UI.Views;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// Profile Editor ViewModel
/// </summary>
public partial class ProfileEditorViewModel : ViewModelBase
{
    private readonly IProfileService _profileService;
    private readonly ILogger<ProfileEditorViewModel> _logger;
    private readonly ILogger<ButtonConfigDialogViewModel> _dialogLogger;

    [ObservableProperty]
    private Profile? _selectedProfile;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<Profile> Profiles { get; } = new();

    public ProfileEditorViewModel(
        IProfileService profileService,
        ILogger<ProfileEditorViewModel> logger,
        ILogger<ButtonConfigDialogViewModel> dialogLogger)
    {
        _profileService = profileService;
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading profiles");
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating profile");
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving profile");
        }
    }

    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (SelectedProfile == null)
            return;

        try
        {
            _logger.LogInformation("Deleting profile: {ProfileName}", SelectedProfile.Name);
            await _profileService.DeleteProfileAsync(SelectedProfile.ProfileId);
            Profiles.Remove(SelectedProfile);
            SelectedProfile = Profiles.FirstOrDefault();
            _logger.LogInformation("Profile deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile");
        }
    }

    [RelayCommand]
    private async Task ConfigureButton(ButtonConfig? button)
    {
        if (button == null)
            return;

        _logger.LogInformation("🔘 BUTTON CLICKED! Configuring button {ButtonId}", button.ButtonId);
        
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
                
                // Save profile
                if (SelectedProfile != null)
                {
                    await _profileService.UpdateProfileAsync(SelectedProfile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening button configuration dialog");
        }
    }
}
