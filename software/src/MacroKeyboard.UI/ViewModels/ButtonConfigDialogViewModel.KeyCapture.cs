using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Core.Models;
using MacroKeyboard.UI.Utilities;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace MacroKeyboard.UI.ViewModels;

public partial class ButtonConfigDialogViewModel
{
    // ── Key capture fields ────────────────────────────────────────────────────

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

    private readonly List<CapturedKey> _capturedKeys = new();

    // ── Computed properties ───────────────────────────────────────────────────

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

    public IBrush KeyCaptureBackground => IsCapturingKeys
        ? new SolidColorBrush(Color.FromRgb(60, 60, 80))
        : new SolidColorBrush(Color.FromRgb(45, 45, 48));

    public IBrush KeyCaptureBorderBrush => IsCapturingKeys
        ? new SolidColorBrush(Color.FromRgb(0, 122, 204))
        : new SolidColorBrush(Color.FromRgb(85, 85, 85));

    public string KeyCaptureButtonText => IsCapturingKeys ? "Stop Recording" : "Start Recording";

    public bool HasCapturedKeys => _capturedKeys.Count > 0;

    public string CapturedModifiersText
    {
        get
        {
            var mods = new List<string>();
            if (CapturedModifiers.HasFlag(KeyModifiers.LeftCtrl)  || CapturedModifiers.HasFlag(KeyModifiers.RightCtrl))  mods.Add("Ctrl");
            if (CapturedModifiers.HasFlag(KeyModifiers.LeftShift) || CapturedModifiers.HasFlag(KeyModifiers.RightShift)) mods.Add("Shift");
            if (CapturedModifiers.HasFlag(KeyModifiers.LeftAlt)   || CapturedModifiers.HasFlag(KeyModifiers.RightAlt))   mods.Add("Alt");
            if (CapturedModifiers.HasFlag(KeyModifiers.LeftGui)   || CapturedModifiers.HasFlag(KeyModifiers.RightGui))   mods.Add("Win");
            return mods.Count > 0 ? string.Join(" + ", mods) : "None";
        }
    }

    public string CapturedKeyText => CapturedKeyCode != 0 ? $"0x{CapturedKeyCode:X2}" : "None";

    // ── Commands & event handlers ─────────────────────────────────────────────

    [RelayCommand]
    private void ToggleKeyCapture()
    {
        IsCapturingKeys = !IsCapturingKeys;
        NotifyKeyCapturePropertiesChanged();
    }

    public void StartKeyCapture()
    {
        IsCapturingKeys = true;
        NotifyKeyCapturePropertiesChanged();
    }

    public void StopKeyCapture()
    {
        IsCapturingKeys = false;
        NotifyKeyCapturePropertiesChanged();
    }

    public void HandleKeyDown(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers, string? keySymbol = null)
    {
        if (!IsCapturingKeys) return;

        if (key == Avalonia.Input.Key.LeftCtrl  || key == Avalonia.Input.Key.RightCtrl  ||
            key == Avalonia.Input.Key.LeftShift || key == Avalonia.Input.Key.RightShift ||
            key == Avalonia.Input.Key.LeftAlt   || key == Avalonia.Input.Key.RightAlt   ||
            key == Avalonia.Input.Key.LWin      || key == Avalonia.Input.Key.RWin)
            return;

        var keyMods = KeyModifiers.None;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) keyMods |= KeyModifiers.LeftCtrl;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))   keyMods |= KeyModifiers.LeftShift;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))     keyMods |= KeyModifiers.LeftAlt;
        if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta))    keyMods |= KeyModifiers.LeftGui;

        var hidKeyCode = HidKeyCodeHelper.FromAvaloniaKey(key);

        string displayName;
        if (!string.IsNullOrEmpty(keySymbol) && keySymbol.Length == 1 && !char.IsControl(keySymbol[0]))
            displayName = HidKeyCodeHelper.FormatKeyWithSymbol(keySymbol.ToUpper(), keyMods);
        else
            displayName = HidKeyCodeHelper.FormatKey(hidKeyCode, keyMods);

        _capturedKeys.Add(new CapturedKey(hidKeyCode, keyMods, displayName));
        CapturedKeyCode = hidKeyCode;
        CapturedModifiers = keyMods;

        NotifyKeyCapturePropertiesChanged();
        _logger.LogDebug("Key captured: {Key} (symbol: {Symbol}), Modifiers: {Modifiers}, HID: 0x{HidCode:X2}, Total keys: {Count}",
            key, keySymbol ?? "none", modifiers, hidKeyCode, _capturedKeys.Count);
    }

    public void HandleTextInput(string? text)
    {
        if (!IsCapturingKeys || string.IsNullOrEmpty(text)) return;

        if (_capturedKeys.Count > 0)
        {
            var lastKey = _capturedKeys[^1];
            var keyName = HidKeyCodeHelper.GetKeyName(lastKey.KeyCode);

            if (text.Length == 1 && !char.IsControl(text[0]))
            {
                var symbol = text.ToUpper();
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

    [RelayCommand]
    public void ClearCapturedKeys()
    {
        _capturedKeys.Clear();
        CapturedKeyCode = 0;
        CapturedModifiers = KeyModifiers.None;
        NotifyKeyCapturePropertiesChanged();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
        if (_capturedKeys.Count == 0) return "No keys captured";
        return string.Join(", ", _capturedKeys.Select(k => k.DisplayName));
    }

    // Priority: captured keys > TextToType > KeySequence (legacy).
    // Single captured key → KeyboardAction; multiple → SequenceAction (max 4 steps per HID packet).
    private ActionConfig CreateKeyboardAction()
    {
        if (_capturedKeys.Count == 0)
        {
            var raw  = !string.IsNullOrEmpty(TextToType) ? TextToType : KeySequence;
            var text = raw.Replace("\\n", "\n").Replace("\\t", "\t");
            return new KeyboardAction { Text = text, KeyCode = 0, Modifiers = KeyModifiers.None };
        }

        if (_capturedKeys.Count == 1)
        {
            var key = _capturedKeys[0];
            return new KeyboardAction { Text = null, KeyCode = key.KeyCode, Modifiers = key.Modifiers };
        }

        const int MaxKeysAsSequence = 4;
        if (_capturedKeys.Count > MaxKeysAsSequence)
            _logger.LogWarning(
                "Key capture has {Count} keys but only {Max} fit in one HID packet. Use TextToType for long text sequences.",
                _capturedKeys.Count, MaxKeysAsSequence);

        var steps = _capturedKeys.Take(MaxKeysAsSequence).Select(k => new SequenceStep
        {
            Action = new KeyboardAction { KeyCode = k.KeyCode, Modifiers = k.Modifiers, Text = null },
            DelayBeforeMs = 0
        }).ToList();

        return new SequenceAction { Steps = steps };
    }
}
