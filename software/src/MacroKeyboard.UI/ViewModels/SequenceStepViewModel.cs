using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MacroKeyboard.Core.Models;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// ViewModel для одного шага в последовательности действий
/// </summary>
public class SequenceStepViewModel : ViewModelBase
{
    public SequenceStepViewModel()
    {
        // Initialize available action types (excluding Sequence to prevent recursion)
        AvailableActionTypes = new ObservableCollection<ActionType>
        {
            ActionType.Keyboard,
            ActionType.CustomHid,
            ActionType.ProfileSwitch,
            ActionType.Folder,
            ActionType.Delay,
            ActionType.Shell
        };
        
        // Initialize available folder IDs (0-15)
        AvailableFolderIds = new ObservableCollection<byte>();
        for (byte i = 0; i <= 15; i++)
        {
            AvailableFolderIds.Add(i);
        }
        
        // Initialize available profile IDs (0-7)
        AvailableProfileIds = new ObservableCollection<byte>();
        for (byte i = 0; i <= 7; i++)
        {
            AvailableProfileIds.Add(i);
        }
    }
    
    /// <summary>
    /// Available action types for sequence steps (excludes Sequence to prevent recursion)
    /// </summary>
    public ObservableCollection<ActionType> AvailableActionTypes { get; }
    
    /// <summary>
    /// Available folder IDs (0-15)
    /// </summary>
    public ObservableCollection<byte> AvailableFolderIds { get; }
    
    /// <summary>
    /// Available profile IDs (0-7)
    /// </summary>
    public ObservableCollection<byte> AvailableProfileIds { get; }
    
    private int _stepNumber;
    public int StepNumber
    {
        get => _stepNumber;
        set => SetProperty(ref _stepNumber, value);
    }
    
    private ActionType _actionType = ActionType.Keyboard;
    
    /// <summary>
    /// Action type for this step (alias for SelectedActionType for XAML binding)
    /// </summary>
    public ActionType ActionType
    {
        get => _actionType;
        set
        {
            if (SetProperty(ref _actionType, value))
            {
                OnPropertyChanged(nameof(SelectedActionType));
                OnPropertyChanged(nameof(IsKeyboardStep));
                OnPropertyChanged(nameof(IsShellStep));
                OnPropertyChanged(nameof(IsCustomHidStep));
                OnPropertyChanged(nameof(IsProfileSwitchStep));
                OnPropertyChanged(nameof(IsFolderStep));
                OnPropertyChanged(nameof(IsDelayStep));
            }
        }
    }
    
    /// <summary>
    /// Alias for ActionType for backward compatibility
    /// </summary>
    public ActionType SelectedActionType
    {
        get => _actionType;
        set => ActionType = value;
    }
    
    private ushort _delayBeforeMs;
    public ushort DelayBeforeMs
    {
        get => _delayBeforeMs;
        set => SetProperty(ref _delayBeforeMs, value);
    }
    
    // ============================================
    // Keyboard action properties (with key capture)
    // ============================================
    
    private string _keyboardText = string.Empty;
    public string KeyboardText
    {
        get => _keyboardText;
        set => SetProperty(ref _keyboardText, value);
    }
    
    private byte _keyCode;
    public byte KeyCode
    {
        get => _keyCode;
        set => SetProperty(ref _keyCode, value);
    }
    
    private KeyModifiers _modifiers = KeyModifiers.None;
    public KeyModifiers Modifiers
    {
        get => _modifiers;
        set => SetProperty(ref _modifiers, value);
    }

    private bool _isCapturingKeys;
    public bool IsCapturingKeys
    {
        get => _isCapturingKeys;
        set
        {
            if (SetProperty(ref _isCapturingKeys, value))
            {
                OnPropertyChanged(nameof(KeySequenceDisplay));
                OnPropertyChanged(nameof(KeyCaptureButtonText));
            }
        }
    }

    private readonly List<CapturedKey> _capturedKeys = new();

    public string KeySequenceDisplay
    {
        get
        {
            if (IsCapturingKeys)
            {
                return _capturedKeys.Count == 0
                    ? "Press keys..."
                    : FormatKeySequence() + " ...";
            }
            if (_capturedKeys.Count == 0)
            {
                return string.IsNullOrEmpty(KeyboardText)
                    ? "Click to capture"
                    : $"Text: {KeyboardText}";
            }
            return FormatKeySequence();
        }
    }

    public string KeyCaptureButtonText => IsCapturingKeys ? "Stop" : "Capture";

    public bool HasCapturedKeys => _capturedKeys.Count > 0;

    public void StartKeyCapture()
    {
        _capturedKeys.Clear();
        IsCapturingKeys = true;
    }

    public void StopKeyCapture()
    {
        IsCapturingKeys = false;
        OnPropertyChanged(nameof(HasCapturedKeys));
        OnPropertyChanged(nameof(KeySequenceDisplay));
    }

    public void ToggleKeyCapture()
    {
        if (IsCapturingKeys)
            StopKeyCapture();
        else
            StartKeyCapture();
    }

    public void HandleKeyDown(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers, string? keySymbol = null)
    {
        if (!IsCapturingKeys)
            return;

        // Skip modifier-only keys
        if (key == Avalonia.Input.Key.LeftCtrl || key == Avalonia.Input.Key.RightCtrl ||
            key == Avalonia.Input.Key.LeftShift || key == Avalonia.Input.Key.RightShift ||
            key == Avalonia.Input.Key.LeftAlt || key == Avalonia.Input.Key.RightAlt ||
            key == Avalonia.Input.Key.LWin || key == Avalonia.Input.Key.RWin)
            return;

        var keyMods = KeyModifiers.None;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) keyMods |= KeyModifiers.LeftCtrl;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)) keyMods |= KeyModifiers.LeftShift;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt)) keyMods |= KeyModifiers.LeftAlt;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)) keyMods |= KeyModifiers.LeftGui;

        var hidKeyCode = ConvertToHidKeyCode(key);
        
        string displayName;
        if (!string.IsNullOrEmpty(keySymbol) && keySymbol.Length == 1 && !char.IsControl(keySymbol[0]))
            displayName = FormatKeyWithSymbol(keySymbol.ToUpper(), keyMods);
        else
            displayName = FormatKey(hidKeyCode, keyMods);

        _capturedKeys.Add(new CapturedKey(hidKeyCode, keyMods, displayName));
        KeyCode = hidKeyCode;
        Modifiers = keyMods;

        OnPropertyChanged(nameof(KeySequenceDisplay));
        OnPropertyChanged(nameof(HasCapturedKeys));
    }

    public void HandleTextInput(string? text)
    {
        if (!IsCapturingKeys || string.IsNullOrEmpty(text)) return;
        if (_capturedKeys.Count > 0 && text.Length == 1 && !char.IsControl(text[0]))
        {
            var lastKey = _capturedKeys[^1];
            var keyName = GetKeyName(lastKey.KeyCode);
            if (keyName.Length == 1 && keyName != text.ToUpper())
            {
                _capturedKeys[^1] = new CapturedKey(lastKey.KeyCode, lastKey.Modifiers,
                    FormatKeyWithSymbol(text.ToUpper(), lastKey.Modifiers));
                OnPropertyChanged(nameof(KeySequenceDisplay));
            }
        }
    }

    /// <summary>
    /// Get the captured key data for saving
    /// </summary>
    public (byte keyCode, KeyModifiers modifiers, string text) GetCapturedKeyData()
    {
        if (_capturedKeys.Count > 0)
            return (_capturedKeys[0].KeyCode, _capturedKeys[0].Modifiers, FormatKeySequence());
        return (KeyCode, Modifiers, KeyboardText);
    }

    private string FormatKeySequence()
    {
        return string.Join(", ", _capturedKeys.ConvertAll(k => k.DisplayName));
    }

    private static string FormatKey(byte hidKeyCode, KeyModifiers mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.LeftCtrl)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.LeftShift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.LeftAlt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.LeftGui)) parts.Add("Win");
        if (hidKeyCode != 0) parts.Add(GetKeyName(hidKeyCode));
        return parts.Count > 0 ? string.Join("+", parts) : "";
    }

    private static string FormatKeyWithSymbol(string symbol, KeyModifiers mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.LeftCtrl)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.LeftShift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.LeftAlt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.LeftGui)) parts.Add("Win");
        parts.Add(symbol);
        return string.Join("+", parts);
    }

    private static string GetKeyName(byte hidKeyCode) => hidKeyCode switch
    {
        0x04 => "A", 0x05 => "B", 0x06 => "C", 0x07 => "D", 0x08 => "E",
        0x09 => "F", 0x0A => "G", 0x0B => "H", 0x0C => "I", 0x0D => "J",
        0x0E => "K", 0x0F => "L", 0x10 => "M", 0x11 => "N", 0x12 => "O",
        0x13 => "P", 0x14 => "Q", 0x15 => "R", 0x16 => "S", 0x17 => "T",
        0x18 => "U", 0x19 => "V", 0x1A => "W", 0x1B => "X", 0x1C => "Y", 0x1D => "Z",
        0x1E => "1", 0x1F => "2", 0x20 => "3", 0x21 => "4", 0x22 => "5",
        0x23 => "6", 0x24 => "7", 0x25 => "8", 0x26 => "9", 0x27 => "0",
        0x28 => "Enter", 0x29 => "Esc", 0x2A => "Backspace", 0x2B => "Tab",
        0x2C => "Space", 0x39 => "CapsLock",
        0x3A => "F1", 0x3B => "F2", 0x3C => "F3", 0x3D => "F4",
        0x3E => "F5", 0x3F => "F6", 0x40 => "F7", 0x41 => "F8",
        0x42 => "F9", 0x43 => "F10", 0x44 => "F11", 0x45 => "F12",
        0x49 => "Insert", 0x4A => "Home", 0x4B => "PgUp",
        0x4C => "Delete", 0x4D => "End", 0x4E => "PgDn",
        0x4F => "→", 0x50 => "←", 0x51 => "↓", 0x52 => "↑",
        _ => $"0x{hidKeyCode:X2}"
    };

    private static byte ConvertToHidKeyCode(Avalonia.Input.Key key) => key switch
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
        Avalonia.Input.Key.Space => 0x2C, Avalonia.Input.Key.CapsLock => 0x39,
        Avalonia.Input.Key.F1 => 0x3A, Avalonia.Input.Key.F2 => 0x3B,
        Avalonia.Input.Key.F3 => 0x3C, Avalonia.Input.Key.F4 => 0x3D,
        Avalonia.Input.Key.F5 => 0x3E, Avalonia.Input.Key.F6 => 0x3F,
        Avalonia.Input.Key.F7 => 0x40, Avalonia.Input.Key.F8 => 0x41,
        Avalonia.Input.Key.F9 => 0x42, Avalonia.Input.Key.F10 => 0x43,
        Avalonia.Input.Key.F11 => 0x44, Avalonia.Input.Key.F12 => 0x45,
        Avalonia.Input.Key.Insert => 0x49, Avalonia.Input.Key.Home => 0x4A,
        Avalonia.Input.Key.PageUp => 0x4B, Avalonia.Input.Key.Delete => 0x4C,
        Avalonia.Input.Key.End => 0x4D, Avalonia.Input.Key.PageDown => 0x4E,
        Avalonia.Input.Key.Right => 0x4F, Avalonia.Input.Key.Left => 0x50,
        Avalonia.Input.Key.Down => 0x51, Avalonia.Input.Key.Up => 0x52,
        _ => 0
    };
    
    // ============================================
    // Shell action properties
    // ============================================
    
    private string _shellCommand = string.Empty;
    public string ShellCommand
    {
        get => _shellCommand;
        set => SetProperty(ref _shellCommand, value);
    }
    
    private string? _workingDirectory;
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set => SetProperty(ref _workingDirectory, value);
    }
    
    private bool _waitForExit = true;
    public bool WaitForExit
    {
        get => _waitForExit;
        set => SetProperty(ref _waitForExit, value);
    }
    
    private int _shellTimeoutMs = 30000;
    public int ShellTimeoutMs
    {
        get => _shellTimeoutMs;
        set => SetProperty(ref _shellTimeoutMs, value);
    }
    
    // ============================================
    // CustomHID action properties
    // ============================================
    
    private string _customHidData = string.Empty;
    public string CustomHidData
    {
        get => _customHidData;
        set => SetProperty(ref _customHidData, value);
    }
    
    // ============================================
    // ProfileSwitch action properties
    // ============================================
    
    private byte _targetProfileId;
    public byte TargetProfileId
    {
        get => _targetProfileId;
        set => SetProperty(ref _targetProfileId, value);
    }
    
    // ============================================
    // Folder action properties
    // ============================================
    
    private byte _folderId;
    public byte FolderId
    {
        get => _folderId;
        set => SetProperty(ref _folderId, value);
    }
    
    // ============================================
    // Delay action properties
    // ============================================
    
    private ushort _delayMs;
    public ushort DelayMs
    {
        get => _delayMs;
        set => SetProperty(ref _delayMs, value);
    }
    
    // ============================================
    // Visibility helpers
    // ============================================
    
    public bool IsKeyboardStep => SelectedActionType == ActionType.Keyboard;
    public bool IsShellStep => SelectedActionType == ActionType.Shell;
    public bool IsCustomHidStep => SelectedActionType == ActionType.CustomHid;
    public bool IsProfileSwitchStep => SelectedActionType == ActionType.ProfileSwitch;
    public bool IsFolderStep => SelectedActionType == ActionType.Folder;
    public bool IsDelayStep => SelectedActionType == ActionType.Delay;
    
    /// <summary>
    /// Конвертировать ViewModel в модель SequenceStep
    /// </summary>
    public SequenceStep ToModel()
    {
        ActionConfig action = SelectedActionType switch
        {
            ActionType.Keyboard => new KeyboardAction
            {
                KeyCode = KeyCode,
                Modifiers = Modifiers,
                Text = string.IsNullOrEmpty(KeyboardText) ? null : KeyboardText
            },
            ActionType.Shell => new ShellAction
            {
                Command = ShellCommand,
                WorkingDirectory = WorkingDirectory,
                WaitForExit = WaitForExit,
                TimeoutMs = ShellTimeoutMs
            },
            ActionType.CustomHid => new CustomHidAction
            {
                Data = ParseHexString(CustomHidData)
            },
            ActionType.ProfileSwitch => new ProfileSwitchAction
            {
                TargetProfileId = TargetProfileId
            },
            ActionType.Folder => new FolderAction
            {
                FolderId = FolderId
            },
            ActionType.Delay => new DelayAction
            {
                DelayMs = DelayMs
            },
            _ => new KeyboardAction()
        };
        
        return new SequenceStep
        {
            Action = action,
            DelayBeforeMs = DelayBeforeMs
        };
    }
    
    /// <summary>
    /// Загрузить данные из модели SequenceStep
    /// </summary>
    public void LoadFromModel(SequenceStep step)
    {
        SelectedActionType = step.Action.ActionType;
        DelayBeforeMs = step.DelayBeforeMs;
        
        switch (step.Action)
        {
            case KeyboardAction keyboard:
                KeyCode = keyboard.KeyCode;
                Modifiers = keyboard.Modifiers;
                KeyboardText = keyboard.Text ?? string.Empty;
                break;
                
            case ShellAction shell:
                ShellCommand = shell.Command;
                WorkingDirectory = shell.WorkingDirectory;
                WaitForExit = shell.WaitForExit;
                ShellTimeoutMs = shell.TimeoutMs;
                break;
                
            case CustomHidAction customHid:
                CustomHidData = BitConverter.ToString(customHid.Data).Replace("-", " ");
                break;
                
            case ProfileSwitchAction profileSwitch:
                TargetProfileId = profileSwitch.TargetProfileId;
                break;
                
            case FolderAction folder:
                FolderId = folder.FolderId;
                break;
                
            case DelayAction delay:
                DelayMs = delay.DelayMs;
                break;
        }
    }
    
    /// <summary>
    /// Парсинг hex-строки в массив байтов
    /// </summary>
    private static byte[] ParseHexString(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Array.Empty<byte>();
            
        // Remove spaces and common separators
        hex = hex.Replace(" ", "").Replace("-", "").Replace(":", "");
        
        if (hex.Length % 2 != 0)
            return Array.Empty<byte>();
            
        try
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
}
