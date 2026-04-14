using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// ViewModel for button configuration dialog
/// </summary>
public partial class ButtonConfigDialogViewModel : ViewModelBase
{
    private readonly ILogger<ButtonConfigDialogViewModel> _logger;

    [ObservableProperty]
    private ButtonConfig _buttonConfig;

    [ObservableProperty]
    private ActionType _selectedActionType;

    [ObservableProperty]
    private string _keySequence = string.Empty;

    [ObservableProperty]
    private string _imagePath = string.Empty;

    [ObservableProperty]
    private uint _ledColor = 0xFFFFFF;

    public ObservableCollection<ActionType> AvailableActionTypes { get; } = new()
    {
        ActionType.None,
        ActionType.Keyboard,
        ActionType.CustomHid,
        ActionType.ProfileSwitch,
        ActionType.Folder
    };

    public bool DialogResult { get; private set; }

    public ButtonConfigDialogViewModel(ILogger<ButtonConfigDialogViewModel> logger, ButtonConfig buttonConfig)
    {
        _logger = logger;
        _buttonConfig = buttonConfig;
        
        // Load existing configuration
        if (buttonConfig.Action != null)
        {
            SelectedActionType = buttonConfig.Action.ActionType;
            
            if (buttonConfig.Action is KeyboardAction keyAction)
            {
                KeySequence = keyAction.Text ?? $"KeyCode: {keyAction.KeyCode}";
            }
        }

        ImagePath = buttonConfig.ImagePath ?? string.Empty;
        LedColor = ((uint)buttonConfig.Led.R << 16) | ((uint)buttonConfig.Led.G << 8) | buttonConfig.Led.B;
    }

    [RelayCommand]
    private async Task BrowseImage()
    {
        // TODO: Open file dialog
        _logger.LogInformation("Browse image clicked");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            // Update button configuration
            ButtonConfig.Action = SelectedActionType switch
            {
                ActionType.Keyboard => new KeyboardAction
                {
                    Text = KeySequence,
                    KeyCode = 0, // Will be parsed from text
                    Modifiers = KeyModifiers.None
                },
                ActionType.ProfileSwitch => new ProfileSwitchAction
                {
                    TargetProfileId = byte.TryParse(KeySequence, out var profileId) ? profileId : (byte)0
                },
                ActionType.CustomHid => new CustomHidAction
                {
                    Data = Array.Empty<byte>()
                },
                _ => null
            };

            ButtonConfig.ImagePath = string.IsNullOrWhiteSpace(ImagePath) ? null : ImagePath;
            
            // Update LED color
            ButtonConfig.Led.R = (byte)((LedColor >> 16) & 0xFF);
            ButtonConfig.Led.G = (byte)((LedColor >> 8) & 0xFF);
            ButtonConfig.Led.B = (byte)(LedColor & 0xFF);

            DialogResult = true;
            _logger.LogInformation("Button configuration saved");
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
