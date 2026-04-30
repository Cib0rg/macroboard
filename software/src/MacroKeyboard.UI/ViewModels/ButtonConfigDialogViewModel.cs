using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// ViewModel for button configuration dialog
/// </summary>
public partial class ButtonConfigDialogViewModel : ViewModelBase
{
    private readonly ILogger<ButtonConfigDialogViewModel> _logger;
    private IStorageProvider? _storageProvider;

    [ObservableProperty]
    private ButtonConfig _buttonConfig;

    [ObservableProperty]
    private ActionType _selectedActionType;

    [ObservableProperty]
    private string _keySequence = string.Empty;

    [ObservableProperty]
    private string _imagePath = string.Empty;

    [ObservableProperty]
    private string _ledColorHex = "#FFFFFF";

    [ObservableProperty]
    private double _colorR = 255;

    [ObservableProperty]
    private double _colorG = 255;

    [ObservableProperty]
    private double _colorB = 255;

    [ObservableProperty]
    private bool _isColorPickerVisible = false;

    [ObservableProperty]
    private byte _targetProfileId;

    [ObservableProperty]
    private byte _folderId;

    public ObservableCollection<ActionType> AvailableActionTypes { get; } = new()
    {
        ActionType.None,
        ActionType.Keyboard,
        ActionType.ProfileSwitch,
        ActionType.Folder,
        ActionType.CustomHid,
    };

    /// <summary>
    /// Available profile IDs for ProfileSwitch action
    /// </summary>
    public ObservableCollection<byte> AvailableProfileIds { get; } = new()
    {
        0, 1, 2, 3, 4
    };

    /// <summary>
    /// Available folder IDs for Folder action
    /// </summary>
    public ObservableCollection<byte> AvailableFolderIds { get; } = new()
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
    };

    public bool DialogResult { get; private set; }

    /// <summary>
    /// Show keyboard-specific fields
    /// </summary>
    public bool IsKeyboardAction => SelectedActionType == ActionType.Keyboard;

    /// <summary>
    /// Show profile switch fields
    /// </summary>
    public bool IsProfileSwitchAction => SelectedActionType == ActionType.ProfileSwitch;

    /// <summary>
    /// Show folder fields
    /// </summary>
    public bool IsFolderAction => SelectedActionType == ActionType.Folder;

    /// <summary>
    /// Show custom HID fields
    /// </summary>
    public bool IsCustomHidAction => SelectedActionType == ActionType.CustomHid;

    /// <summary>
    /// Color preview for the LED color picker
    /// </summary>
    public Color LedColorPreview => Color.FromRgb((byte)ColorR, (byte)ColorG, (byte)ColorB);

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
            else if (buttonConfig.Action is ProfileSwitchAction psAction)
            {
                TargetProfileId = psAction.TargetProfileId;
            }
        }

        FolderId = buttonConfig.FolderId;
        ImagePath = buttonConfig.ImagePath ?? string.Empty;
        
        // Initialize LED color from button config
        ColorR = buttonConfig.Led.R;
        ColorG = buttonConfig.Led.G;
        ColorB = buttonConfig.Led.B;
        UpdateHexFromRgb();
    }

    partial void OnColorRChanged(double value)
    {
        UpdateHexFromRgb();
        OnPropertyChanged(nameof(LedColorPreview));
    }

    partial void OnColorGChanged(double value)
    {
        UpdateHexFromRgb();
        OnPropertyChanged(nameof(LedColorPreview));
    }

    partial void OnColorBChanged(double value)
    {
        UpdateHexFromRgb();
        OnPropertyChanged(nameof(LedColorPreview));
    }

    partial void OnLedColorHexChanged(string value)
    {
        // Try to parse hex string and update RGB values
        if (TryParseHexColor(value, out byte r, out byte g, out byte b))
        {
            // Avoid infinite loop by checking if values are different
            if ((byte)ColorR != r || (byte)ColorG != g || (byte)ColorB != b)
            {
                ColorR = r;
                ColorG = g;
                ColorB = b;
                OnPropertyChanged(nameof(LedColorPreview));
            }
        }
    }

    private void UpdateHexFromRgb()
    {
        var newHex = $"#{(byte)ColorR:X2}{(byte)ColorG:X2}{(byte)ColorB:X2}";
        if (LedColorHex != newHex)
        {
            _ledColorHex = newHex; // Direct field access to avoid triggering OnLedColorHexChanged
            OnPropertyChanged(nameof(LedColorHex));
        }
    }

    private static bool TryParseHexColor(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 255;
        
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        // Remove # prefix if present
        hex = hex.TrimStart('#');
        
        // Also handle 0x prefix
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex.Substring(2);

        if (hex.Length != 6)
            return false;

        if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            r = (byte)((value >> 16) & 0xFF);
            g = (byte)((value >> 8) & 0xFF);
            b = (byte)(value & 0xFF);
            return true;
        }

        return false;
    }

    [RelayCommand]
    private void ToggleColorPicker()
    {
        IsColorPickerVisible = !IsColorPickerVisible;
    }

    partial void OnSelectedActionTypeChanged(ActionType value)
    {
        // Notify UI to show/hide action-specific fields
        OnPropertyChanged(nameof(IsKeyboardAction));
        OnPropertyChanged(nameof(IsProfileSwitchAction));
        OnPropertyChanged(nameof(IsFolderAction));
        OnPropertyChanged(nameof(IsCustomHidAction));
    }

    /// <summary>
    /// Set the storage provider for file dialogs (called from View)
    /// </summary>
    public void SetStorageProvider(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
    }

    [RelayCommand]
    private async Task BrowseImage()
    {
        try
        {
            _logger.LogInformation("Browse image clicked");
            
            if (_storageProvider == null)
            {
                _logger.LogWarning("StorageProvider not set");
                return;
            }
            
            var fileTypes = new FilePickerFileType[]
            {
                new("Images")
                {
                    Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif" },
                    MimeTypes = new[] { "image/*" }
                }
            };
            
            var options = new FilePickerOpenOptions
            {
                Title = "Select Button Image",
                AllowMultiple = false,
                FileTypeFilter = fileTypes
            };
            
            var result = await _storageProvider.OpenFilePickerAsync(options);
            
            if (result != null && result.Count > 0)
            {
                var file = result[0];
                ImagePath = file.Path.LocalPath;
                _logger.LogInformation("Image selected: {Path}", ImagePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error browsing for image");
        }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            // Update button configuration based on selected action type
            ButtonConfig.Action = SelectedActionType switch
            {
                ActionType.Keyboard => new KeyboardAction
                {
                    Text = KeySequence,
                    KeyCode = 0, // Text mode: keycode=0, text in data bytes 7+
                    Modifiers = KeyModifiers.None
                },
                ActionType.ProfileSwitch => new ProfileSwitchAction
                {
                    TargetProfileId = TargetProfileId
                },
                ActionType.Folder => new ProfileSwitchAction
                {
                    // Folder uses a special "marker" action — ActionType.Folder
                    // but we store it as a ProfileSwitchAction-like object for serialization.
                    // The actual folder navigation is handled by the firmware.
                    TargetProfileId = 0
                },
                ActionType.CustomHid => new CustomHidAction
                {
                    Data = Array.Empty<byte>()
                },
                ActionType.None => null,
                _ => null
            };

            // For Folder action, we need a special FolderAction class
            // Since we don't have one, use a workaround: set the action type via a wrapper
            if (SelectedActionType == ActionType.Folder)
            {
                ButtonConfig.Action = new FolderAction { FolderId = FolderId };
                ButtonConfig.FolderId = FolderId;
            }

            ButtonConfig.ImagePath = string.IsNullOrWhiteSpace(ImagePath) ? null : ImagePath;
            
            // Update LED color from RGB sliders
            ButtonConfig.Led.R = (byte)ColorR;
            ButtonConfig.Led.G = (byte)ColorG;
            ButtonConfig.Led.B = (byte)ColorB;

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
