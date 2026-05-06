using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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
    private string _textToType = string.Empty;

    [ObservableProperty]
    private bool _isCapturingKeys = false;

    [ObservableProperty]
    private byte _capturedKeyCode = 0;

    [ObservableProperty]
    private KeyModifiers _capturedModifiers = KeyModifiers.None;

    /// <summary>
    /// List of captured key combinations (for sequence recording)
    /// </summary>
    private readonly List<CapturedKey> _capturedKeys = new();

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
    private double _brightness = 200;

    [ObservableProperty]
    private bool _isColorPickerVisible = false;

    /// <summary>
    /// Color property for binding to ColorPicker control.
    /// Syncs with ColorR/ColorG/ColorB.
    /// </summary>
    [ObservableProperty]
    private Color _ledColor = Color.FromRgb(255, 255, 255);
    
    private bool _isUpdatingColor = false;

    [ObservableProperty]
    private byte _targetProfileId;

    [ObservableProperty]
    private byte _folderId;

    [ObservableProperty]
    private string _folderName = string.Empty;

    // ============================================
    // Shell action properties
    // ============================================
    
    [ObservableProperty]
    private string _shellCommand = string.Empty;
    
    [ObservableProperty]
    private string? _shellWorkingDirectory;
    
    [ObservableProperty]
    private bool _shellWaitForExit = true;
    
    // ============================================
    // LaunchApp action properties
    // ============================================
    
    [ObservableProperty]
    private string _launchAppPath = string.Empty;
    
    [ObservableProperty]
    private string? _launchAppArguments;
    
    [ObservableProperty]
    private string? _launchAppWorkingDirectory;
    
    [ObservableProperty]
    private string? _launchAppIconPath;
    
    // ============================================
    // Sequence action properties
    // ============================================
    
    /// <summary>
    /// Steps in the sequence action
    /// </summary>
    public ObservableCollection<SequenceStepViewModel> SequenceSteps { get; } = new();

    public ObservableCollection<ActionType> AvailableActionTypes { get; } = new()
    {
        ActionType.None,
        ActionType.Keyboard,
        ActionType.LaunchApp,
        ActionType.Shell,
        ActionType.Sequence,
        ActionType.ProfileSwitch,
        ActionType.Folder,
        ActionType.CustomHid,
    };
    
    /// <summary>
    /// Available action types for sequence steps (excludes Sequence to prevent recursion)
    /// </summary>
    public ObservableCollection<ActionType> AvailableStepActionTypes { get; } = new()
    {
        ActionType.Keyboard,
        ActionType.Shell,
        ActionType.CustomHid,
        ActionType.ProfileSwitch,
        ActionType.Folder,
        ActionType.Delay,
    };

    /// <summary>
    /// Available profiles for ProfileSwitch action (populated from existing profiles)
    /// </summary>
    public ObservableCollection<ProfileSwitchItem> AvailableProfiles { get; } = new();

    /// <summary>
    /// Selected target profile for ProfileSwitch action
    /// </summary>
    [ObservableProperty]
    private ProfileSwitchItem? _selectedTargetProfile;

    /// <summary>
    /// Available folders for Folder action (populated from existing profile folders)
    /// </summary>
    public ObservableCollection<FolderSwitchItem> AvailableFolders { get; } = new();

    /// <summary>
    /// Selected target folder for Folder action
    /// </summary>
    [ObservableProperty]
    private FolderSwitchItem? _selectedTargetFolder;

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
    /// Show shell command fields
    /// </summary>
    public bool IsShellAction => SelectedActionType == ActionType.Shell;

    /// <summary>
    /// Show launch app fields
    /// </summary>
    public bool IsLaunchAppAction => SelectedActionType == ActionType.LaunchApp;

    /// <summary>
    /// Show sequence editor fields
    /// </summary>
    public bool IsSequenceAction => SelectedActionType == ActionType.Sequence;

    /// <summary>
    /// Whether more steps can be added to the sequence
    /// </summary>
    public bool CanAddMoreSteps => SequenceSteps.Count < SequenceAction.MaxSteps;

    /// <summary>
    /// Emoji icon for the currently selected action type
    /// </summary>
    public string CurrentActionIcon => SelectedActionType switch
    {
        ActionType.Keyboard => "⌨",
        ActionType.Shell => "💻",
        ActionType.LaunchApp => "🚀",
        ActionType.Sequence => "📋",
        ActionType.ProfileSwitch => "🔄",
        ActionType.Folder => "📁",
        ActionType.CustomHid => "🔌",
        _ => "⊘"
    };

    /// <summary>
    /// Display name for the currently selected action type
    /// </summary>
    public string CurrentActionDisplayName => SelectedActionType switch
    {
        ActionType.Keyboard => "Keyboard",
        ActionType.Shell => "Shell",
        ActionType.LaunchApp => "Launch App",
        ActionType.Sequence => "Sequence",
        ActionType.ProfileSwitch => "Profile Switch",
        ActionType.Folder => "Folder",
        ActionType.CustomHid => "Custom HID",
        ActionType.None => "None",
        _ => "Not Set"
    };

    /// <summary>
    /// Color preview for the LED color picker
    /// </summary>
    public Color LedColorPreview => Color.FromRgb((byte)ColorR, (byte)ColorG, (byte)ColorB);

    /// <summary>
    /// Display text for the key capture field
    /// </summary>
    public string KeySequenceDisplay
    {
        get
        {
            if (IsCapturingKeys)
            {
                if (_capturedKeys.Count == 0)
                    return "Press keys... (click 'Stop' when done)";
                return FormatKeySequence() + " ...";
            }
            
            if (_capturedKeys.Count == 0)
                return "Click here to capture keys";
            
            return FormatKeySequence();
        }
    }

    /// <summary>
    /// Background color for key capture field
    /// </summary>
    public IBrush KeyCaptureBackground => IsCapturingKeys
        ? new SolidColorBrush(Color.FromRgb(60, 60, 80))
        : new SolidColorBrush(Color.FromRgb(45, 45, 48));

    /// <summary>
    /// Border color for key capture field
    /// </summary>
    public IBrush KeyCaptureBorderBrush => IsCapturingKeys
        ? new SolidColorBrush(Color.FromRgb(0, 122, 204))
        : new SolidColorBrush(Color.FromRgb(85, 85, 85));

    /// <summary>
    /// Button text for key capture toggle
    /// </summary>
    public string KeyCaptureButtonText => IsCapturingKeys ? "Stop Recording" : "Start Recording";

    /// <summary>
    /// Whether any keys have been captured
    /// </summary>
    public bool HasCapturedKeys => _capturedKeys.Count > 0;

    /// <summary>
    /// Display text for captured modifiers
    /// </summary>
    public string CapturedModifiersText
    {
        get
        {
            var mods = new List<string>();
            if (CapturedModifiers.HasFlag(KeyModifiers.LeftCtrl) || CapturedModifiers.HasFlag(KeyModifiers.RightCtrl))
                mods.Add("Ctrl");
            if (CapturedModifiers.HasFlag(KeyModifiers.LeftShift) || CapturedModifiers.HasFlag(KeyModifiers.RightShift))
                mods.Add("Shift");
            if (CapturedModifiers.HasFlag(KeyModifiers.LeftAlt) || CapturedModifiers.HasFlag(KeyModifiers.RightAlt))
                mods.Add("Alt");
            if (CapturedModifiers.HasFlag(KeyModifiers.LeftGui) || CapturedModifiers.HasFlag(KeyModifiers.RightGui))
                mods.Add("Win");
            return mods.Count > 0 ? string.Join(" + ", mods) : "None";
        }
    }

    /// <summary>
    /// Display text for captured key
    /// </summary>
    public string CapturedKeyText => CapturedKeyCode != 0 ? $"0x{CapturedKeyCode:X2}" : "None";

    public ButtonConfigDialogViewModel(ILogger<ButtonConfigDialogViewModel> logger, ButtonConfig buttonConfig,
        IEnumerable<ProfileSwitchItem>? availableProfiles = null,
        IEnumerable<FolderSwitchItem>? availableFolders = null)
    {
        _logger = logger;
        _buttonConfig = buttonConfig;
        
        // Populate available profiles for ProfileSwitch
        if (availableProfiles != null)
        {
            foreach (var profile in availableProfiles)
                AvailableProfiles.Add(profile);
        }
        
        // Populate available folders for Folder action
        if (availableFolders != null)
        {
            foreach (var folder in availableFolders)
                AvailableFolders.Add(folder);
        }
        
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
                // Select the matching profile by ID
                SelectedTargetProfile = AvailableProfiles.FirstOrDefault(p => p.ProfileId == psAction.TargetProfileId);
            }
            else if (buttonConfig.Action is ShellAction shellAction)
            {
                ShellCommand = shellAction.Command;
                ShellWorkingDirectory = shellAction.WorkingDirectory;
                ShellWaitForExit = shellAction.WaitForExit;
            }
            else if (buttonConfig.Action is LaunchAppAction launchAction)
            {
                LaunchAppPath = launchAction.ExecutablePath;
                LaunchAppArguments = launchAction.Arguments;
                LaunchAppWorkingDirectory = launchAction.WorkingDirectory;
                LaunchAppIconPath = launchAction.IconPath;
            }
        }

        FolderId = buttonConfig.FolderId;
        // Load folder name from available folders list
        var existingFolder = AvailableFolders.FirstOrDefault(f => f.FolderId == buttonConfig.FolderId);
        SelectedTargetFolder = existingFolder;
        FolderName = existingFolder?.Name ?? $"Folder {buttonConfig.FolderId}";
        ImagePath = buttonConfig.ImagePath ?? string.Empty;
        
        // Initialize LED color and brightness from button config
        _isUpdatingColor = true;
        ColorR = buttonConfig.Led.R;
        ColorG = buttonConfig.Led.G;
        ColorB = buttonConfig.Led.B;
        Brightness = buttonConfig.Led.Brightness;
        LedColor = Color.FromRgb(buttonConfig.Led.R, buttonConfig.Led.G, buttonConfig.Led.B);
        _isUpdatingColor = false;
        UpdateHexFromRgb();
    }

    partial void OnColorRChanged(double value)
    {
        if (_isUpdatingColor) return;
        UpdateHexFromRgb();
        SyncLedColorFromRgb();
        OnPropertyChanged(nameof(LedColorPreview));
    }

    partial void OnColorGChanged(double value)
    {
        if (_isUpdatingColor) return;
        UpdateHexFromRgb();
        SyncLedColorFromRgb();
        OnPropertyChanged(nameof(LedColorPreview));
    }

    partial void OnColorBChanged(double value)
    {
        if (_isUpdatingColor) return;
        UpdateHexFromRgb();
        SyncLedColorFromRgb();
        OnPropertyChanged(nameof(LedColorPreview));
    }

    /// <summary>
    /// Called when the ColorPicker changes the LedColor property
    /// </summary>
    partial void OnLedColorChanged(Color value)
    {
        if (_isUpdatingColor) return;
        _isUpdatingColor = true;
        try
        {
            ColorR = value.R;
            ColorG = value.G;
            ColorB = value.B;
            UpdateHexFromRgb();
            OnPropertyChanged(nameof(LedColorPreview));
        }
        finally
        {
            _isUpdatingColor = false;
        }
    }

    /// <summary>
    /// Sync LedColor from individual R/G/B values (for ColorPicker binding)
    /// </summary>
    private void SyncLedColorFromRgb()
    {
        _isUpdatingColor = true;
        try
        {
            LedColor = Color.FromRgb((byte)ColorR, (byte)ColorG, (byte)ColorB);
        }
        finally
        {
            _isUpdatingColor = false;
        }
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

    [RelayCommand]
    private void ToggleKeyCapture()
    {
        IsCapturingKeys = !IsCapturingKeys;
        NotifyKeyCapturePropertiesChanged();
    }

    /// <summary>
    /// Start key capture mode (called from View when field is clicked)
    /// </summary>
    public void StartKeyCapture()
    {
        IsCapturingKeys = true;
        NotifyKeyCapturePropertiesChanged();
    }

    /// <summary>
    /// Stop key capture mode
    /// </summary>
    public void StopKeyCapture()
    {
        IsCapturingKeys = false;
        NotifyKeyCapturePropertiesChanged();
    }

    /// <summary>
    /// Handle key down event during capture.
    /// keySymbol is the character produced by the key in the current keyboard layout (e.g., "Ф" for Russian).
    /// </summary>
    public void HandleKeyDown(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers, string? keySymbol = null)
    {
        if (!IsCapturingKeys)
            return;

        // Skip modifier-only keys (they will be captured with the main key)
        if (key == Avalonia.Input.Key.LeftCtrl || key == Avalonia.Input.Key.RightCtrl ||
            key == Avalonia.Input.Key.LeftShift || key == Avalonia.Input.Key.RightShift ||
            key == Avalonia.Input.Key.LeftAlt || key == Avalonia.Input.Key.RightAlt ||
            key == Avalonia.Input.Key.LWin || key == Avalonia.Input.Key.RWin)
        {
            return;
        }

        // Convert modifiers
        var keyMods = KeyModifiers.None;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            keyMods |= KeyModifiers.LeftCtrl;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
            keyMods |= KeyModifiers.LeftShift;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
            keyMods |= KeyModifiers.LeftAlt;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta))
            keyMods |= KeyModifiers.LeftGui;

        // Convert Avalonia key to HID keycode (physical key, layout-independent)
        var hidKeyCode = ConvertToHidKeyCode(key);
        
        // Build display name: prefer the actual character from the current layout
        string displayName;
        if (!string.IsNullOrEmpty(keySymbol) && keySymbol.Length == 1 && !char.IsControl(keySymbol[0]))
        {
            // Use the actual character produced by the key in the current layout
            displayName = FormatSingleKeyWithSymbol(keySymbol.ToUpper(), keyMods);
        }
        else
        {
            // Fallback to HID keycode name (for special keys like Enter, F1, etc.)
            displayName = FormatSingleKey(hidKeyCode, keyMods);
        }
        
        // Add to the captured keys list
        _capturedKeys.Add(new CapturedKey(hidKeyCode, keyMods, displayName));
        
        // Also update the single-key properties for backward compatibility
        CapturedKeyCode = hidKeyCode;
        CapturedModifiers = keyMods;

        NotifyKeyCapturePropertiesChanged();
        _logger.LogDebug("Key captured: {Key} (symbol: {Symbol}), Modifiers: {Modifiers}, HID: 0x{HidCode:X2}, Total keys: {Count}",
            key, keySymbol ?? "none", modifiers, hidKeyCode, _capturedKeys.Count);
    }

    /// <summary>
    /// Handle text input during capture (provides the actual character for the current keyboard layout).
    /// This is needed because on Linux, KeyDown may not provide KeySymbol for non-Latin layouts.
    /// </summary>
    public void HandleTextInput(string? text)
    {
        if (!IsCapturingKeys || string.IsNullOrEmpty(text))
            return;

        // If the last captured key has a generic display name (like "Key(0x...)"),
        // update it with the actual character from TextInput
        if (_capturedKeys.Count > 0)
        {
            var lastKey = _capturedKeys[^1];
            var keyName = GetKeyName(lastKey.KeyCode);
            
            // If the display name is just a single Latin letter but the text input is different,
            // it means the user is typing in a non-Latin layout
            if (text.Length == 1 && !char.IsControl(text[0]))
            {
                var symbol = text.ToUpper();
                // Update the display name if it differs from what was shown
                if (keyName.Length == 1 && keyName != symbol)
                {
                    var newDisplayName = FormatSingleKeyWithSymbol(symbol, lastKey.Modifiers);
                    _capturedKeys[^1] = new CapturedKey(lastKey.KeyCode, lastKey.Modifiers, newDisplayName);
                    NotifyKeyCapturePropertiesChanged();
                    _logger.LogDebug("Updated last key display to: {Symbol} (from TextInput)", symbol);
                }
            }
        }
    }

    /// <summary>
    /// Clear captured keys
    /// </summary>
    public void ClearCapturedKeys()
    {
        _capturedKeys.Clear();
        CapturedKeyCode = 0;
        CapturedModifiers = KeyModifiers.None;
        NotifyKeyCapturePropertiesChanged();
    }

    // ============================================
    // Sequence step management
    // ============================================
    
    /// <summary>
    /// Add a new step to the sequence
    /// </summary>
    [RelayCommand]
    private void AddSequenceStep()
    {
        if (SequenceSteps.Count < SequenceAction.MaxSteps)
        {
            var step = new SequenceStepViewModel
            {
                StepNumber = SequenceSteps.Count + 1,
                SelectedActionType = ActionType.Keyboard,
                DelayBeforeMs = 0
            };
            SequenceSteps.Add(step);
            OnPropertyChanged(nameof(CanAddMoreSteps));
            _logger.LogDebug("Added sequence step {StepNumber}", step.StepNumber);
        }
    }
    
    /// <summary>
    /// Remove a step from the sequence
    /// </summary>
    [RelayCommand]
    private void RemoveSequenceStep(SequenceStepViewModel? step)
    {
        if (step != null && SequenceSteps.Contains(step))
        {
            SequenceSteps.Remove(step);
            // Renumber remaining steps
            for (int i = 0; i < SequenceSteps.Count; i++)
            {
                SequenceSteps[i].StepNumber = i + 1;
            }
            OnPropertyChanged(nameof(CanAddMoreSteps));
            _logger.LogDebug("Removed sequence step, {Count} steps remaining", SequenceSteps.Count);
        }
    }
    
    /// <summary>
    /// Move a step up in the sequence
    /// </summary>
    [RelayCommand]
    private void MoveStepUp(SequenceStepViewModel? step)
    {
        if (step == null) return;
        var index = SequenceSteps.IndexOf(step);
        if (index > 0)
        {
            SequenceSteps.Move(index, index - 1);
            // Renumber steps
            for (int i = 0; i < SequenceSteps.Count; i++)
            {
                SequenceSteps[i].StepNumber = i + 1;
            }
        }
    }
    
    /// <summary>
    /// Move a step down in the sequence
    /// </summary>
    [RelayCommand]
    private void MoveStepDown(SequenceStepViewModel? step)
    {
        if (step == null) return;
        var index = SequenceSteps.IndexOf(step);
        if (index >= 0 && index < SequenceSteps.Count - 1)
        {
            SequenceSteps.Move(index, index + 1);
            // Renumber steps
            for (int i = 0; i < SequenceSteps.Count; i++)
            {
                SequenceSteps[i].StepNumber = i + 1;
            }
        }
    }

    private void NotifyKeyCapturePropertiesChanged()
    {
        OnPropertyChanged(nameof(KeySequenceDisplay));
        OnPropertyChanged(nameof(KeyCaptureBackground));
        OnPropertyChanged(nameof(KeyCaptureBorderBrush));
        OnPropertyChanged(nameof(KeyCaptureButtonText));
        OnPropertyChanged(nameof(HasCapturedKeys));
        OnPropertyChanged(nameof(CapturedModifiersText));
        OnPropertyChanged(nameof(CapturedKeyText));
    }

    private string FormatKeySequence()
    {
        if (_capturedKeys.Count == 0)
            return "No keys captured";
        
        // Join all captured keys with ", " separator
        return string.Join(", ", _capturedKeys.Select(k => k.DisplayName));
    }
    
    /// <summary>
    /// Format a single key with its modifiers for display
    /// </summary>
    private string FormatSingleKey(byte hidKeyCode, KeyModifiers modifiers)
    {
        var parts = new List<string>();
        
        if (modifiers.HasFlag(KeyModifiers.LeftCtrl) || modifiers.HasFlag(KeyModifiers.RightCtrl))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.LeftShift) || modifiers.HasFlag(KeyModifiers.RightShift))
            parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.LeftAlt) || modifiers.HasFlag(KeyModifiers.RightAlt))
            parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.LeftGui) || modifiers.HasFlag(KeyModifiers.RightGui))
            parts.Add("Win");
        
        if (hidKeyCode != 0)
            parts.Add(GetKeyName(hidKeyCode));
        
        return parts.Count > 0 ? string.Join("+", parts) : "";
    }

    /// <summary>
    /// Format a key using the actual character symbol from the current keyboard layout
    /// </summary>
    private string FormatSingleKeyWithSymbol(string symbol, KeyModifiers modifiers)
    {
        var parts = new List<string>();
        
        if (modifiers.HasFlag(KeyModifiers.LeftCtrl) || modifiers.HasFlag(KeyModifiers.RightCtrl))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.LeftShift) || modifiers.HasFlag(KeyModifiers.RightShift))
            parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.LeftAlt) || modifiers.HasFlag(KeyModifiers.RightAlt))
            parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.LeftGui) || modifiers.HasFlag(KeyModifiers.RightGui))
            parts.Add("Win");
        
        parts.Add(symbol);
        
        return string.Join("+", parts);
    }

    private static string GetKeyName(byte hidKeyCode)
    {
        // Common HID keycodes to names
        return hidKeyCode switch
        {
            0x04 => "A", 0x05 => "B", 0x06 => "C", 0x07 => "D", 0x08 => "E",
            0x09 => "F", 0x0A => "G", 0x0B => "H", 0x0C => "I", 0x0D => "J",
            0x0E => "K", 0x0F => "L", 0x10 => "M", 0x11 => "N", 0x12 => "O",
            0x13 => "P", 0x14 => "Q", 0x15 => "R", 0x16 => "S", 0x17 => "T",
            0x18 => "U", 0x19 => "V", 0x1A => "W", 0x1B => "X", 0x1C => "Y",
            0x1D => "Z",
            0x1E => "1", 0x1F => "2", 0x20 => "3", 0x21 => "4", 0x22 => "5",
            0x23 => "6", 0x24 => "7", 0x25 => "8", 0x26 => "9", 0x27 => "0",
            0x28 => "Enter", 0x29 => "Escape", 0x2A => "Backspace", 0x2B => "Tab",
            0x2C => "Space", 0x2D => "-", 0x2E => "=", 0x2F => "[", 0x30 => "]",
            0x31 => "\\", 0x33 => ";", 0x34 => "'", 0x35 => "`", 0x36 => ",",
            0x37 => ".", 0x38 => "/",
            0x39 => "CapsLock", 0x3A => "F1", 0x3B => "F2", 0x3C => "F3",
            0x3D => "F4", 0x3E => "F5", 0x3F => "F6", 0x40 => "F7", 0x41 => "F8",
            0x42 => "F9", 0x43 => "F10", 0x44 => "F11", 0x45 => "F12",
            0x46 => "PrintScreen", 0x47 => "ScrollLock", 0x48 => "Pause",
            0x49 => "Insert", 0x4A => "Home", 0x4B => "PageUp", 0x4C => "Delete",
            0x4D => "End", 0x4E => "PageDown", 0x4F => "Right", 0x50 => "Left",
            0x51 => "Down", 0x52 => "Up",
            _ => $"Key(0x{hidKeyCode:X2})"
        };
    }

    private static byte ConvertToHidKeyCode(Avalonia.Input.Key key)
    {
        // Convert Avalonia Key to USB HID keycode
        return key switch
        {
            Avalonia.Input.Key.A => 0x04, Avalonia.Input.Key.B => 0x05,
            Avalonia.Input.Key.C => 0x06, Avalonia.Input.Key.D => 0x07,
            Avalonia.Input.Key.E => 0x08, Avalonia.Input.Key.F => 0x09,
            Avalonia.Input.Key.G => 0x0A, Avalonia.Input.Key.H => 0x0B,
            Avalonia.Input.Key.I => 0x0C, Avalonia.Input.Key.J => 0x0D,
            Avalonia.Input.Key.K => 0x0E, Avalonia.Input.Key.L => 0x0F,
            Avalonia.Input.Key.M => 0x10, Avalonia.Input.Key.N => 0x11,
            Avalonia.Input.Key.O => 0x12, Avalonia.Input.Key.P => 0x13,
            Avalonia.Input.Key.Q => 0x14, Avalonia.Input.Key.R => 0x15,
            Avalonia.Input.Key.S => 0x16, Avalonia.Input.Key.T => 0x17,
            Avalonia.Input.Key.U => 0x18, Avalonia.Input.Key.V => 0x19,
            Avalonia.Input.Key.W => 0x1A, Avalonia.Input.Key.X => 0x1B,
            Avalonia.Input.Key.Y => 0x1C, Avalonia.Input.Key.Z => 0x1D,
            Avalonia.Input.Key.D1 => 0x1E, Avalonia.Input.Key.D2 => 0x1F,
            Avalonia.Input.Key.D3 => 0x20, Avalonia.Input.Key.D4 => 0x21,
            Avalonia.Input.Key.D5 => 0x22, Avalonia.Input.Key.D6 => 0x23,
            Avalonia.Input.Key.D7 => 0x24, Avalonia.Input.Key.D8 => 0x25,
            Avalonia.Input.Key.D9 => 0x26, Avalonia.Input.Key.D0 => 0x27,
            Avalonia.Input.Key.Return => 0x28, Avalonia.Input.Key.Escape => 0x29,
            Avalonia.Input.Key.Back => 0x2A, Avalonia.Input.Key.Tab => 0x2B,
            Avalonia.Input.Key.Space => 0x2C,
            Avalonia.Input.Key.OemMinus => 0x2D, Avalonia.Input.Key.OemPlus => 0x2E,
            Avalonia.Input.Key.OemOpenBrackets => 0x2F, Avalonia.Input.Key.OemCloseBrackets => 0x30,
            Avalonia.Input.Key.OemPipe => 0x31, Avalonia.Input.Key.OemSemicolon => 0x33,
            Avalonia.Input.Key.OemQuotes => 0x34, Avalonia.Input.Key.OemTilde => 0x35,
            Avalonia.Input.Key.OemComma => 0x36, Avalonia.Input.Key.OemPeriod => 0x37,
            Avalonia.Input.Key.OemQuestion => 0x38,
            Avalonia.Input.Key.CapsLock => 0x39,
            Avalonia.Input.Key.F1 => 0x3A, Avalonia.Input.Key.F2 => 0x3B,
            Avalonia.Input.Key.F3 => 0x3C, Avalonia.Input.Key.F4 => 0x3D,
            Avalonia.Input.Key.F5 => 0x3E, Avalonia.Input.Key.F6 => 0x3F,
            Avalonia.Input.Key.F7 => 0x40, Avalonia.Input.Key.F8 => 0x41,
            Avalonia.Input.Key.F9 => 0x42, Avalonia.Input.Key.F10 => 0x43,
            Avalonia.Input.Key.F11 => 0x44, Avalonia.Input.Key.F12 => 0x45,
            Avalonia.Input.Key.PrintScreen => 0x46, Avalonia.Input.Key.Scroll => 0x47,
            Avalonia.Input.Key.Pause => 0x48, Avalonia.Input.Key.Insert => 0x49,
            Avalonia.Input.Key.Home => 0x4A, Avalonia.Input.Key.PageUp => 0x4B,
            Avalonia.Input.Key.Delete => 0x4C, Avalonia.Input.Key.End => 0x4D,
            Avalonia.Input.Key.PageDown => 0x4E, Avalonia.Input.Key.Right => 0x4F,
            Avalonia.Input.Key.Left => 0x50, Avalonia.Input.Key.Down => 0x51,
            Avalonia.Input.Key.Up => 0x52,
            _ => 0 // Unknown key
        };
    }

    partial void OnSelectedActionTypeChanged(ActionType value)
    {
        // Notify UI to show/hide action-specific fields
        OnPropertyChanged(nameof(IsKeyboardAction));
        OnPropertyChanged(nameof(IsProfileSwitchAction));
        OnPropertyChanged(nameof(IsFolderAction));
        OnPropertyChanged(nameof(IsCustomHidAction));
        OnPropertyChanged(nameof(IsShellAction));
        OnPropertyChanged(nameof(IsLaunchAppAction));
        OnPropertyChanged(nameof(IsSequenceAction));
        OnPropertyChanged(nameof(CanAddMoreSteps));
        OnPropertyChanged(nameof(CurrentActionIcon));
        OnPropertyChanged(nameof(CurrentActionDisplayName));
    }

    /// <summary>
    /// Create a KeyboardAction from captured keys or text input.
    /// Priority: captured keys > TextToType > KeySequence (legacy)
    /// </summary>
    private KeyboardAction CreateKeyboardAction()
    {
        if (_capturedKeys.Count == 0)
        {
            // No keys captured - use TextToType field (for typing text like "Привет" or "Hello")
            var text = !string.IsNullOrEmpty(TextToType) ? TextToType : KeySequence;
            return new KeyboardAction
            {
                Text = text,
                KeyCode = 0,
                Modifiers = KeyModifiers.None
            };
        }
        
        // Use captured keys - store the display text and first key's code/modifiers
        // For multiple keys, the Text field contains the full sequence for display
        var firstKey = _capturedKeys[0];
        return new KeyboardAction
        {
            Text = FormatKeySequence(), // Store the formatted sequence for display
            KeyCode = firstKey.KeyCode,
            Modifiers = firstKey.Modifiers
        };
    }

    /// <summary>
    /// Set the storage provider for file dialogs (called from View)
    /// </summary>
    public void SetStorageProvider(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
    }

    [RelayCommand]
    private async Task BrowseLaunchApp()
    {
        try
        {
            _logger.LogInformation("Browse launch app clicked");
            
            if (_storageProvider == null)
            {
                _logger.LogWarning("StorageProvider not set");
                return;
            }
            
            var fileTypes = new FilePickerFileType[]
            {
                new("Executables")
                {
                    Patterns = OperatingSystem.IsWindows()
                        ? new[] { "*.exe", "*.bat", "*.cmd", "*.lnk" }
                        : new[] { "*" },
                }
            };
            
            var options = new FilePickerOpenOptions
            {
                Title = "Select Application",
                AllowMultiple = false,
                FileTypeFilter = fileTypes
            };
            
            var result = await _storageProvider.OpenFilePickerAsync(options);
            
            if (result != null && result.Count > 0)
            {
                var file = result[0];
                LaunchAppPath = file.Path.LocalPath;
                _logger.LogInformation("App selected: {Path}", LaunchAppPath);
                
                // Auto-extract icon from the executable and set as button image
                await ExtractAndSetAppIconAsync(LaunchAppPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error browsing for application");
        }
    }

    /// <summary>
    /// Extract icon from an executable and save it as the button image
    /// </summary>
    private async Task ExtractAndSetAppIconAsync(string executablePath)
    {
        try
        {
            var appDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MacroKeyboard", "icons");
            Directory.CreateDirectory(appDataDir);
            
            var iconFileName = System.IO.Path.GetFileNameWithoutExtension(executablePath) + ".png";
            var iconOutputPath = System.IO.Path.Combine(appDataDir, iconFileName);
            
            if (OperatingSystem.IsWindows())
            {
                // On Windows, extract icon from exe using System.Drawing (via shell)
                // Use a simple approach: copy the exe path and let the backend handle icon extraction
                // For now, store the path — the backend will extract the icon when syncing
                LaunchAppIconPath = executablePath; // Will be resolved to actual icon by backend
                ImagePath = iconOutputPath; // Placeholder path for the extracted icon
                _logger.LogInformation("App icon will be extracted from: {Path}", executablePath);
            }
            else
            {
                // On Linux, try to find the app icon from .desktop files or freedesktop icon theme
                var desktopIconPath = TryFindLinuxAppIcon(executablePath);
                if (desktopIconPath != null)
                {
                    LaunchAppIconPath = desktopIconPath;
                    ImagePath = desktopIconPath;
                    _logger.LogInformation("Found Linux app icon: {Path}", desktopIconPath);
                }
                else
                {
                    LaunchAppIconPath = null;
                    _logger.LogInformation("No icon found for: {Path}", executablePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract app icon from {Path}", executablePath);
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Try to find an icon for a Linux application by searching .desktop files
    /// </summary>
    private static string? TryFindLinuxAppIcon(string executablePath)
    {
        try
        {
            var appName = System.IO.Path.GetFileNameWithoutExtension(executablePath).ToLower();
            
            // Search common .desktop file locations
            var desktopDirs = new[]
            {
                "/usr/share/applications",
                "/usr/local/share/applications",
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications")
            };
            
            foreach (var dir in desktopDirs)
            {
                if (!Directory.Exists(dir)) continue;
                
                foreach (var desktopFile in Directory.GetFiles(dir, "*.desktop"))
                {
                    var content = File.ReadAllText(desktopFile);
                    if (content.Contains(executablePath, StringComparison.OrdinalIgnoreCase) ||
                        content.Contains(appName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract Icon= line
                        foreach (var line in content.Split('\n'))
                        {
                            if (line.StartsWith("Icon=", StringComparison.OrdinalIgnoreCase))
                            {
                                var iconValue = line.Substring(5).Trim();
                                // If it's an absolute path, use it directly
                                if (System.IO.Path.IsPathRooted(iconValue) && File.Exists(iconValue))
                                    return iconValue;
                                
                                // Try to find in common icon directories
                                var iconPaths = new[]
                                {
                                    $"/usr/share/icons/hicolor/128x128/apps/{iconValue}.png",
                                    $"/usr/share/icons/hicolor/64x64/apps/{iconValue}.png",
                                    $"/usr/share/icons/hicolor/48x48/apps/{iconValue}.png",
                                    $"/usr/share/pixmaps/{iconValue}.png",
                                    $"/usr/share/pixmaps/{iconValue}.svg",
                                };
                                
                                foreach (var iconPath in iconPaths)
                                {
                                    if (File.Exists(iconPath))
                                        return iconPath;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { /* ignore errors in icon search */ }
        
        return null;
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
                    Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.svg", "*.ico", "*.gif" },
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

    /// <summary>
    /// Public method to save button config (used by inline editor in ProfileEditorView)
    /// </summary>
    public void SaveToButtonConfig()
    {
        Save();
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            // Update button configuration based on selected action type
            ButtonConfig.Action = SelectedActionType switch
            {
                ActionType.Keyboard => CreateKeyboardAction(),
                ActionType.ProfileSwitch => new ProfileSwitchAction
                {
                    TargetProfileId = SelectedTargetProfile?.ProfileId ?? TargetProfileId
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
                ActionType.LaunchApp => new LaunchAppAction
                {
                    ExecutablePath = LaunchAppPath,
                    Arguments = string.IsNullOrWhiteSpace(LaunchAppArguments) ? null : LaunchAppArguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(LaunchAppWorkingDirectory) ? null : LaunchAppWorkingDirectory,
                    IconPath = LaunchAppIconPath
                },
                ActionType.None => null,
                _ => null
            };

            // For Folder action: FolderId will be resolved by ProfileEditorViewModel
            // based on FolderName (creates folder if needed, assigns next free ID)
            if (SelectedActionType == ActionType.Folder)
            {
                ButtonConfig.Action = new FolderAction { FolderId = FolderId };
                ButtonConfig.FolderId = FolderId;
            }

            ButtonConfig.ImagePath = string.IsNullOrWhiteSpace(ImagePath) ? null : ImagePath;
            
            // Update LED color and brightness from sliders
            ButtonConfig.Led.R = (byte)ColorR;
            ButtonConfig.Led.G = (byte)ColorG;
            ButtonConfig.Led.B = (byte)ColorB;
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

/// <summary>
/// Represents a captured key with modifiers
/// </summary>
public record CapturedKey(byte KeyCode, KeyModifiers Modifiers, string DisplayName);

/// <summary>
/// Represents a profile available for ProfileSwitch action (shows name to user, stores ID internally)
/// </summary>
public class ProfileSwitchItem
{
    public byte ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    
    public override string ToString() => Name;
}

/// <summary>
/// Represents a folder available for Folder action (shows name to user, stores ID internally)
/// </summary>
public class FolderSwitchItem
{
    public byte FolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    
    public override string ToString() => Name;
}
