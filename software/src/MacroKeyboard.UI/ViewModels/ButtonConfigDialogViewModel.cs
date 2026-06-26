using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Shared.Plugin;
using MacroKeyboard.UI.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
    private string _buttonName = string.Empty;

    [ObservableProperty]
    private string _imagePath = string.Empty;

    private Bitmap? _imagePreviewBitmap;

    /// <summary>
    /// Preview bitmap for the selected button image
    /// </summary>
    public Bitmap? ImagePreview
    {
        get => _imagePreviewBitmap;
        private set
        {
            if (SetProperty(ref _imagePreviewBitmap, value))
            {
                OnPropertyChanged(nameof(HasImagePreview));
            }
        }
    }

    /// <summary>
    /// Whether an image preview is available
    /// </summary>
    public bool HasImagePreview => _imagePreviewBitmap != null;

    [ObservableProperty]
    private string _ledColorHex = "#FFFFFF";

    [ObservableProperty]
    private double _colorR = 255;

    [ObservableProperty]
    private double _colorG = 255;

    [ObservableProperty]
    private double _colorB = 255;

    [ObservableProperty]
    private double _brightness = 80;

    [ObservableProperty]
    private bool _isColorPickerVisible = false;

    /// <summary>
    /// True when this dialog is editing a long press action — LED section is hidden
    /// because LED color belongs to the button, not the action.
    /// </summary>
    [ObservableProperty]
    private bool _isLongPress = false;

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
    // Custom HID action properties (Phase 7)
    // ============================================

    [ObservableProperty]
    private string _customHidData = string.Empty;

    // ============================================
    // Plugin action properties
    // ============================================

    [ObservableProperty]
    private string _pluginId = string.Empty;

    [ObservableProperty]
    private string _pluginActionId = string.Empty;

    [ObservableProperty]
    private string _pluginSettings = string.Empty;

    [ObservableProperty]
    private string _pluginSearchText = string.Empty;

    [ObservableProperty]
    private PluginActionInfo? _selectedPluginAction;

    /// <summary>Base HTTP URL of the Property Inspector HTML (no query params). Null when action has no PI.</summary>
    [ObservableProperty]
    private string? _propertyInspectorUrl;

    /// <summary>True when the selected plugin action has a Property Inspector page.</summary>
    public bool HasPropertyInspector => !string.IsNullOrEmpty(PropertyInspectorUrl);

    /// <summary>Full URL with SD connection params embedded as query string, for loading in NativeWebView.</summary>
    public string? PropertyInspectorSourceUrl
    {
        get
        {
            if (string.IsNullOrEmpty(PropertyInspectorUrl)) return null;
            var ctx        = $"{PluginId}:{ButtonConfig.ButtonId}";
            var info       = BuildPiInfoJson();
            var actionInfo = BuildPiActionInfoJson(ctx);
            var query = string.Concat(
                "port=28196",
                "&propertyInspectorUUID=", Uri.EscapeDataString(ctx),
                "&registerEvent=registerPropertyInspector",
                "&info=", Uri.EscapeDataString(info),
                "&actionInfo=", Uri.EscapeDataString(actionInfo));
            var ub = new UriBuilder(PropertyInspectorUrl) { Query = query };
            return ub.Uri.ToString();
        }
    }

    partial void OnPropertyInspectorUrlChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPropertyInspector));
        OnPropertyChanged(nameof(PropertyInspectorSourceUrl));
    }

    /// <summary>Returns the JS call to inject into the PI page after navigation.</summary>
    public string GetPropertyInspectorConnectScript()
    {
        var ctx        = $"{PluginId}:{ButtonConfig.ButtonId}";
        var info       = BuildPiInfoJson();
        var actionInfo = BuildPiActionInfoJson(ctx);
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
        return $"if(typeof connectElgatoStreamDeckSocket==='function'){{connectElgatoStreamDeckSocket('28196','{ctx}','registerPropertyInspector','{Esc(info)}','{Esc(actionInfo)}')}}";
    }

    private string BuildPiInfoJson() => JsonConvert.SerializeObject(new
    {
        application     = new { font = "Arial", language = "en", platform = "windows", platformVersion = "10.0.0", version = "1.0.0" },
        plugin          = new { uuid = PluginId, version = "1.0.0" },
        devicePixelRatio = 1,
        colors           = new { },
        devices          = new[] { new { id = "MK_DEVICE_0", name = "MacroKeyboard", size = new { columns = 5, rows = 2 }, type = 0 } }
    });

    private string BuildPiActionInfoJson(string context)
    {
        object? settingsObj = null;
        if (!string.IsNullOrEmpty(PluginSettings))
            try { settingsObj = JsonConvert.DeserializeObject(PluginSettings); } catch { }
        return JsonConvert.SerializeObject(new
        {
            action  = PluginActionId,
            context = context,
            device  = "MK_DEVICE_0",
            payload = new { settings = settingsObj ?? (object)new { }, coordinates = new { column = ButtonConfig.ButtonId % 5, row = ButtonConfig.ButtonId / 5 } }
        });
    }

    public ObservableCollection<PluginActionInfo> AvailablePluginActions { get; } = new();
    public ObservableCollection<PluginActionInfo> FilteredPluginActions  { get; } = new();

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
    // Media action properties
    // ============================================
    
    [ObservableProperty]
    private MediaKey _selectedMediaKey = MediaKey.Mute;
    
    public ObservableCollection<MediaKey> AvailableMediaKeys { get; } = new()
    {
        MediaKey.VolumeUp,
        MediaKey.VolumeDown,
        MediaKey.Mute,
        MediaKey.PlayPause,
        MediaKey.NextTrack,
        MediaKey.PreviousTrack,
        MediaKey.Stop,
    };
    
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

    public IReadOnlyList<ActionType> AvailableActionTypes { get; } = ActionTypeHelpers.AllActionTypes;

    public IReadOnlyList<ActionType> AvailableStepActionTypes { get; } = ActionTypeHelpers.SequenceStepTypes;

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
    /// Show plugin action fields
    /// </summary>
    public bool IsPluginAction => SelectedActionType == ActionType.Plugin;

    /// <summary>
    /// Show shell command fields
    /// </summary>
    public bool IsShellAction => SelectedActionType == ActionType.Shell;

    /// <summary>
    /// Show media key fields
    /// </summary>
    public bool IsMediaAction => SelectedActionType == ActionType.Media;

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
        ActionType.Media => "🔊",
        ActionType.Shell => "💻",
        ActionType.LaunchApp => "🚀",
        ActionType.Sequence => "📋",
        ActionType.ProfileSwitch => "🔄",
        ActionType.Folder => "📁",
        ActionType.CustomHid => "🎛",
        ActionType.NightMode => "🌙",
        ActionType.Plugin => "🔌",
        _ => "⊘"
    };

    /// <summary>
    /// Display name for the currently selected action type
    /// </summary>
    public string CurrentActionDisplayName => SelectedActionType switch
    {
        ActionType.Keyboard => "Keyboard",
        ActionType.Media => "Media",
        ActionType.Shell => "Shell",
        ActionType.LaunchApp => "Launch App",
        ActionType.Sequence => "Sequence",
        ActionType.ProfileSwitch => "Profile Switch",
        ActionType.Folder => "Folder",
        ActionType.CustomHid => "Custom HID",
        ActionType.NightMode => "Night Mode",
        ActionType.None => "None",
        ActionType.Plugin when SelectedPluginAction != null => SelectedPluginAction.ActionName,
        ActionType.Plugin => string.IsNullOrEmpty(PluginActionId) ? "Plugin" : $"Plugin: {PluginActionId}",
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
        IEnumerable<FolderSwitchItem>? availableFolders = null,
        IEnumerable<PluginActionInfo>? availablePluginActions = null)
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

        // Populate plugin action palette
        if (availablePluginActions != null)
        {
            foreach (var pa in availablePluginActions)
            {
                AvailablePluginActions.Add(pa);
                FilteredPluginActions.Add(pa);
            }
        }
        
        // Load existing configuration
        if (buttonConfig.Action != null)
        {
            SelectedActionType = buttonConfig.Action.ActionType;
            
            if (buttonConfig.Action is KeyboardAction keyAction)
            {
                if (keyAction.KeyCode != 0)
                {
                    // Restore captured key from stored HID keycode + modifiers
                    var displayName = HidKeyCodeHelper.FormatKey(keyAction.KeyCode, keyAction.Modifiers);
                    _capturedKeys.Add(new CapturedKey(keyAction.KeyCode, keyAction.Modifiers, displayName));
                    CapturedKeyCode = keyAction.KeyCode;
                    CapturedModifiers = keyAction.Modifiers;
                    KeySequence = displayName;
                }
                else
                {
                    // Text-typing mode
                    TextToType = keyAction.Text ?? string.Empty;
                    KeySequence = keyAction.Text ?? string.Empty;
                }
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
            else if (buttonConfig.Action is MediaAction mediaAction)
            {
                SelectedMediaKey = mediaAction.Key;
            }
            else if (buttonConfig.Action is PluginActionConfig pluginAction)
            {
                PluginId = pluginAction.PluginId;
                PluginActionId = pluginAction.ActionId;
                PluginSettings = pluginAction.Settings ?? string.Empty;
                // Restore the palette selection so the selected-mode panel shows correctly
                SelectedPluginAction = AvailablePluginActions
                    .FirstOrDefault(a => a.PluginId == pluginAction.PluginId && a.ActionId == pluginAction.ActionId);
            }
            else if (buttonConfig.Action is CustomHidAction customHidAction)
            {
                CustomHidData = FormatBytesAsHex(customHidAction.Data);
            }
        }

        FolderId = buttonConfig.FolderId;
        // Load folder name from available folders list
        var existingFolder = AvailableFolders.FirstOrDefault(f => f.FolderId == buttonConfig.FolderId);
        SelectedTargetFolder = existingFolder;
        FolderName = existingFolder?.Name ?? $"Folder {buttonConfig.FolderId}";
        ButtonName = buttonConfig.Name ?? string.Empty;
        ImagePath = buttonConfig.ImagePath ?? string.Empty;
        LoadImagePreview(ImagePath);
        
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
        var hidKeyCode = HidKeyCodeHelper.FromAvaloniaKey(key);

        // Build display name: prefer the actual character from the current layout
        string displayName;
        if (!string.IsNullOrEmpty(keySymbol) && keySymbol.Length == 1 && !char.IsControl(keySymbol[0]))
            displayName = HidKeyCodeHelper.FormatKeyWithSymbol(keySymbol.ToUpper(), keyMods);
        else
            displayName = HidKeyCodeHelper.FormatKey(hidKeyCode, keyMods);
        
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
            var keyName = HidKeyCodeHelper.GetKeyName(lastKey.KeyCode);

            // If the display name is just a single Latin letter but the text input is different,
            // it means the user is typing in a non-Latin layout
            if (text.Length == 1 && !char.IsControl(text[0]))
            {
                var symbol = text.ToUpper();
                // Update the display name if it differs from what was shown
                if (keyName.Length == 1 && keyName != symbol)
                {
                    var newDisplayName = HidKeyCodeHelper.FormatKeyWithSymbol(symbol, lastKey.Modifiers);
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
    [RelayCommand]
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
    
    partial void OnSelectedActionTypeChanged(ActionType value)
    {
        // Clear all action-specific fields so data from the previous action type
        // doesn't bleed into the newly selected one.
        KeySequence = string.Empty;
        TextToType = string.Empty;
        CapturedKeyCode = 0;
        CapturedModifiers = KeyModifiers.None;
        IsCapturingKeys = false;
        _capturedKeys.Clear();

        TargetProfileId = 0;
        SelectedTargetProfile = null;

        FolderId = 0;
        FolderName = string.Empty;
        SelectedTargetFolder = null;

        ShellCommand = string.Empty;
        ShellWorkingDirectory = null;
        ShellWaitForExit = true;

        SelectedMediaKey = MediaKey.Mute;

        LaunchAppPath = string.Empty;
        LaunchAppArguments = null;
        LaunchAppWorkingDirectory = null;
        LaunchAppIconPath = null;

        SequenceSteps.Clear();

        PluginId = string.Empty;
        PluginActionId = string.Empty;
        PluginSettings = string.Empty;
        CustomHidData = string.Empty;

        // Notify UI to show/hide action-specific fields
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

        // Reset plugin search when switching away/to Plugin type
        if (value == ActionType.Plugin)
        {
            PluginSearchText = string.Empty;
            SelectedPluginAction = null;
            ApplyPluginFilter();
        }

        // None action means the button is disabled — clear any image so the
        // device display isn't left showing a stale icon.
        if (value == ActionType.None)
            ImagePath = string.Empty;
    }

    [RelayCommand]
    private void ClearPluginAction()
    {
        SelectedPluginAction = null;
        PluginId             = string.Empty;
        PluginActionId       = string.Empty;
        PropertyInspectorUrl = null;
        PluginSearchText     = string.Empty;
        ApplyPluginFilter();
        OnPropertyChanged(nameof(CurrentActionDisplayName));
    }

    partial void OnPluginSearchTextChanged(string value) => ApplyPluginFilter();

    partial void OnPluginIdChanged(string value)
        => OnPropertyChanged(nameof(PropertyInspectorSourceUrl));

    partial void OnSelectedPluginActionChanged(PluginActionInfo? value)
    {
        if (value == null)
        {
            PropertyInspectorUrl = null;
            return;
        }
        // PluginId must be set BEFORE PropertyInspectorUrl so that PropertyInspectorSourceUrl
        // computes the correct context ("pluginId:buttonIndex") when the binding refreshes.
        PluginId       = value.PluginId;
        PluginActionId = value.ActionId;
        PropertyInspectorUrl = value.PropertyInspectorUrl;
        // Auto-set button image from plugin icon if not already customised
        if (string.IsNullOrEmpty(ImagePath) && !string.IsNullOrEmpty(value.IconPath))
            ImagePath = value.IconPath;
        OnPropertyChanged(nameof(CurrentActionDisplayName));
    }

    private void ApplyPluginFilter()
    {
        FilteredPluginActions.Clear();
        var q = PluginSearchText?.Trim() ?? string.Empty;
        foreach (var pa in AvailablePluginActions)
        {
            if (q.Length == 0
                || pa.ActionName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || pa.PluginName.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredPluginActions.Add(pa);
            }
        }
    }

    /// <summary>
    /// Parse a hex string like "FF 00 A0" or "FF00A0" into bytes.
    /// Returns empty array on invalid input.
    /// </summary>
    private static byte[] ParseHexString(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Array.Empty<byte>();

        var tokens = hex.Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<byte>();
        foreach (var token in tokens)
        {
            if (byte.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out var b))
                result.Add(b);
        }
        return result.ToArray();
    }

    private static string FormatBytesAsHex(byte[] data) =>
        data.Length == 0 ? string.Empty : string.Join(" ", data.Select(b => b.ToString("X2")));

    /// <summary>
    /// Create a KeyboardAction from captured keys or text input.
    /// Priority: captured keys > TextToType > KeySequence (legacy)
    ///
    /// When a single key is captured, we store the HID keycode + modifiers (KeyboardAction).
    /// When multiple keys are captured, we build a SequenceAction (max 4 steps fit in one HID packet).
    /// When TextToType is used, we store Text and set KeyCode = 0 (firmware types char by char).
    /// </summary>
    private ActionConfig CreateKeyboardAction()
    {
        if (_capturedKeys.Count == 0)
        {
            // No keys captured - use TextToType field.
            // Process escape sequences so the user can type \n for Enter, \t for Tab.
            var raw = !string.IsNullOrEmpty(TextToType) ? TextToType : KeySequence;
            var text = raw.Replace("\\n", "\n").Replace("\\t", "\t");
            return new KeyboardAction
            {
                Text = text,
                KeyCode = 0,
                Modifiers = KeyModifiers.None
            };
        }

        if (_capturedKeys.Count == 1)
        {
            var key = _capturedKeys[0];
            return new KeyboardAction
            {
                Text = null,
                KeyCode = key.KeyCode,
                Modifiers = key.Modifiers
            };
        }

        // Multiple captured keys → SequenceAction.
        // Each KeyboardAction step serializes to 7 bytes; with 5-byte step header that's 12 bytes/step.
        // HID packet allows 51 bytes of action data → max 4 steps: (1 + 4*12 = 49 bytes).
        const int MaxKeysAsSequence = 4;
        if (_capturedKeys.Count > MaxKeysAsSequence)
        {
            _logger.LogWarning(
                "Key capture has {Count} keys but only {Max} fit in one HID packet. " +
                "Use TextToType for long text sequences.",
                _capturedKeys.Count, MaxKeysAsSequence);
        }

        var steps = _capturedKeys.Take(MaxKeysAsSequence).Select(k => new SequenceStep
        {
            Action = new KeyboardAction { KeyCode = k.KeyCode, Modifiers = k.Modifiers, Text = null },
            DelayBeforeMs = 0
        }).ToList();

        return new SequenceAction { Steps = steps };
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
#pragma warning disable CA1416
                await Task.Run(() => ExtractWindowsAppIcon(executablePath, iconOutputPath));
#pragma warning restore CA1416
                if (File.Exists(iconOutputPath))
                {
                    LaunchAppIconPath = executablePath;
                    ImagePath = iconOutputPath;
                    _logger.LogInformation("App icon extracted to: {Path}", iconOutputPath);
                }
                else
                {
                    LaunchAppIconPath = executablePath;
                    _logger.LogWarning("Icon extraction produced no output for: {Path}", executablePath);
                }
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

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ExtractWindowsAppIcon(string executablePath, string outputPath)
    {
        // Method 1: JUMBO (256×256) icon via SHGetImageList — scaling DOWN to 160×160 looks great
        var hJumbo = TryGetJumboIcon(executablePath);
        if (hJumbo != IntPtr.Zero)
        {
            try
            {
                using var bmp = System.Drawing.Bitmap.FromHicon(hJumbo);
                bmp.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                return;
            }
            catch { }
            finally { DestroyIcon(hJumbo); }
        }

        // Method 2: Shell32 ExtractIconEx (32×32 fallback)
        var largeIcons = new IntPtr[1];
        var smallIcons = new IntPtr[1];
        try
        {
            ExtractIconEx(executablePath, 0, largeIcons, smallIcons, 1);
            var hIcon = largeIcons[0] != IntPtr.Zero ? largeIcons[0] : smallIcons[0];
            if (hIcon != IntPtr.Zero)
            {
                using var icon = System.Drawing.Icon.FromHandle(hIcon);
                using var bitmap = icon.ToBitmap();
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                return;
            }
        }
        finally
        {
            if (largeIcons[0] != IntPtr.Zero) DestroyIcon(largeIcons[0]);
            if (smallIcons[0] != IntPtr.Zero) DestroyIcon(smallIcons[0]);
        }

        // Method 3: ExtractAssociatedIcon last resort
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon != null)
            {
                using var bitmap = icon.ToBitmap();
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch { }
    }

    /// <summary>Returns an HICON for the JUMBO (256×256) shell icon, or Zero on failure. Caller must DestroyIcon.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IntPtr TryGetJumboIcon(string executablePath)
    {
        try
        {
            var shfi = default(SHFILEINFO);
            var res = SHGetFileInfo(executablePath, 0, ref shfi,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_SYSICONINDEX);
            if (res == IntPtr.Zero) return IntPtr.Zero;

            var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IID_IImageList
            if (SHGetImageList(SHIL_JUMBO, ref iid, out var imageList) != 0 || imageList is null)
                return IntPtr.Zero;

            imageList.GetIcon(shfi.iIcon, ILD_TRANSPARENT, out var hIcon);
            return hIcon;
        }
        catch { return IntPtr.Zero; }
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("Shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [System.Runtime.InteropServices.DllImport("Shell32.dll")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Interface)]
        out IShellImageList? ppv);

    [System.Runtime.InteropServices.DllImport("Shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
        IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

    [System.Runtime.InteropServices.DllImport("User32.dll")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const int  SHIL_JUMBO         = 4;
    private const int  ILD_TRANSPARENT    = 0x1;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    // Minimal IImageList COM interface — vtable slots 1-8 must be declared in order; GetIcon is slot 8.
    [System.Runtime.InteropServices.ComImport]
    [System.Runtime.InteropServices.Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellImageList
    {
        [System.Runtime.InteropServices.PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
        [System.Runtime.InteropServices.PreserveSig] int ReplaceIcon(int i, IntPtr hicon, out int pi);
        [System.Runtime.InteropServices.PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [System.Runtime.InteropServices.PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [System.Runtime.InteropServices.PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, out int pi);
        [System.Runtime.InteropServices.PreserveSig] int Draw(IntPtr pimldp);
        [System.Runtime.InteropServices.PreserveSig] int Remove(int i);
        [System.Runtime.InteropServices.PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
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

    partial void OnImagePathChanged(string value)
    {
        LoadImagePreview(value);
    }

    /// <summary>
    /// Load image preview bitmap from the given path
    /// </summary>
    private void LoadImagePreview(string path)
    {
        try
        {
            ImagePreview?.Dispose();
            ImagePreview = null;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                OnPropertyChanged(nameof(HasImagePreview));
                return;
            }

            // Skip SVG files — Avalonia Bitmap doesn't support them directly
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".svg")
            {
                OnPropertyChanged(nameof(HasImagePreview));
                return;
            }

            using var stream = File.OpenRead(path);
            ImagePreview = new Bitmap(stream);
            OnPropertyChanged(nameof(HasImagePreview));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load image preview from {Path}", path);
            ImagePreview = null;
            OnPropertyChanged(nameof(HasImagePreview));
        }
    }

    [RelayCommand]
    private void ClearImage()
    {
        ImagePath = string.Empty;
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

    /// <summary>
    /// Ensures a LaunchApp button has its icon extracted before the config is persisted.
    /// Called from ProfileEditorViewModel before SaveToButtonConfig() so that ImagePath
    /// is populated even when the user typed the exe path manually instead of using Browse.
    /// </summary>
    public async Task EnsureIconExtractedAsync()
    {
        if (SelectedActionType != ActionType.LaunchApp) return;
        if (!string.IsNullOrWhiteSpace(ImagePath)) return;
        if (string.IsNullOrWhiteSpace(LaunchAppPath)) return;
        if (!File.Exists(LaunchAppPath)) return;

        _logger.LogInformation("Extracting icon for manually-entered exe: {Path}", LaunchAppPath);
        await ExtractAndSetAppIconAsync(LaunchAppPath);
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
                ActionType.Folder => null, // handled by explicit if-block below (also sets ButtonConfig.FolderId)
                ActionType.CustomHid => new CustomHidAction
                {
                    Data = ParseHexString(CustomHidData)
                },
                ActionType.LaunchApp => new LaunchAppAction
                {
                    ExecutablePath = LaunchAppPath,
                    Arguments = string.IsNullOrWhiteSpace(LaunchAppArguments) ? null : LaunchAppArguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(LaunchAppWorkingDirectory) ? null : LaunchAppWorkingDirectory,
                    IconPath = LaunchAppIconPath
                },
                ActionType.Media => new MediaAction
                {
                    Key = SelectedMediaKey
                },
                ActionType.Shell => new ShellAction
                {
                    Command = ShellCommand ?? string.Empty,
                    WorkingDirectory = string.IsNullOrWhiteSpace(ShellWorkingDirectory) ? null : ShellWorkingDirectory,
                    WaitForExit = ShellWaitForExit
                },
                ActionType.Sequence => new SequenceAction
                {
                    Steps = SequenceSteps.Select(s => s.ToModel()).ToList()
                },
                ActionType.NightMode => new NightModeAction(),
                ActionType.Plugin => new PluginActionConfig
                {
                    PluginId   = PluginId,
                    ActionId   = PluginActionId,
                    ActionName = SelectedPluginAction?.ActionName,
                    Settings   = string.IsNullOrWhiteSpace(PluginSettings) ? null : PluginSettings
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

            ButtonConfig.Name = string.IsNullOrWhiteSpace(ButtonName) ? null : ButtonName.Trim();
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
