using System.Collections.Generic;
using Avalonia.Input;
using KeyModifiers = MacroKeyboard.Core.Models.KeyModifiers;

namespace MacroKeyboard.UI.Utilities;

/// <summary>
/// Single source of truth for USB HID keycode ↔ display name conversions.
/// Replaces four near-identical private implementations spread across ViewModels.
/// </summary>
internal static class HidKeyCodeHelper
{
    public static string GetKeyName(byte code) => code switch
    {
        // Letters A–Z
        >= 0x04 and <= 0x1D => ((char)('A' + code - 0x04)).ToString(),
        // Digits 1–9, 0
        >= 0x1E and <= 0x26 => ((char)('1' + code - 0x1E)).ToString(),
        0x27 => "0",
        // Common keys
        0x28 => "Enter",  0x29 => "Esc",  0x2A => "Backspace", 0x2B => "Tab",
        0x2C => "Space",
        0x2D => "-",  0x2E => "=",  0x2F => "[",  0x30 => "]",
        0x31 => "\\", 0x33 => ";",  0x34 => "'",  0x35 => "`",
        0x36 => ",",  0x37 => ".",  0x38 => "/",
        0x39 => "CapsLock",
        // F-keys F1–F12
        >= 0x3A and <= 0x45 => $"F{code - 0x3A + 1}",
        0x46 => "PrintScreen", 0x47 => "ScrollLock", 0x48 => "Pause",
        0x49 => "Insert",  0x4A => "Home",   0x4B => "PageUp",
        0x4C => "Delete",  0x4D => "End",    0x4E => "PageDown",
        0x4F => "→", 0x50 => "←", 0x51 => "↓", 0x52 => "↑",
        _ => $"0x{code:X2}"
    };

    public static byte FromAvaloniaKey(Key key) => key switch
    {
        Key.A => 0x04, Key.B => 0x05, Key.C => 0x06, Key.D => 0x07,
        Key.E => 0x08, Key.F => 0x09, Key.G => 0x0A, Key.H => 0x0B,
        Key.I => 0x0C, Key.J => 0x0D, Key.K => 0x0E, Key.L => 0x0F,
        Key.M => 0x10, Key.N => 0x11, Key.O => 0x12, Key.P => 0x13,
        Key.Q => 0x14, Key.R => 0x15, Key.S => 0x16, Key.T => 0x17,
        Key.U => 0x18, Key.V => 0x19, Key.W => 0x1A, Key.X => 0x1B,
        Key.Y => 0x1C, Key.Z => 0x1D,
        Key.D1 => 0x1E, Key.D2 => 0x1F, Key.D3 => 0x20, Key.D4 => 0x21,
        Key.D5 => 0x22, Key.D6 => 0x23, Key.D7 => 0x24, Key.D8 => 0x25,
        Key.D9 => 0x26, Key.D0 => 0x27,
        Key.Return => 0x28, Key.Escape => 0x29, Key.Back => 0x2A, Key.Tab => 0x2B,
        Key.Space => 0x2C,
        Key.OemMinus => 0x2D, Key.OemPlus => 0x2E,
        Key.OemOpenBrackets => 0x2F, Key.OemCloseBrackets => 0x30,
        Key.OemPipe => 0x31, Key.OemSemicolon => 0x33,
        Key.OemQuotes => 0x34, Key.OemTilde => 0x35,
        Key.OemComma => 0x36, Key.OemPeriod => 0x37, Key.OemQuestion => 0x38,
        Key.CapsLock => 0x39,
        Key.F1 => 0x3A, Key.F2 => 0x3B, Key.F3 => 0x3C, Key.F4 => 0x3D,
        Key.F5 => 0x3E, Key.F6 => 0x3F, Key.F7 => 0x40, Key.F8 => 0x41,
        Key.F9 => 0x42, Key.F10 => 0x43, Key.F11 => 0x44, Key.F12 => 0x45,
        Key.PrintScreen => 0x46, Key.Scroll => 0x47, Key.Pause => 0x48,
        Key.Insert => 0x49, Key.Home => 0x4A, Key.PageUp => 0x4B,
        Key.Delete => 0x4C, Key.End => 0x4D, Key.PageDown => 0x4E,
        Key.Right => 0x4F, Key.Left => 0x50, Key.Down => 0x51, Key.Up => 0x52,
        _ => 0
    };

    public static string FormatKey(byte hidKeyCode, KeyModifiers mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.LeftCtrl)  || mods.HasFlag(KeyModifiers.RightCtrl))  parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.LeftShift) || mods.HasFlag(KeyModifiers.RightShift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.LeftAlt)   || mods.HasFlag(KeyModifiers.RightAlt))   parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.LeftGui)   || mods.HasFlag(KeyModifiers.RightGui))   parts.Add("Win");
        if (hidKeyCode != 0) parts.Add(GetKeyName(hidKeyCode));
        return parts.Count > 0 ? string.Join("+", parts) : "";
    }

    public static string FormatKeyWithSymbol(string symbol, KeyModifiers mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.LeftCtrl)  || mods.HasFlag(KeyModifiers.RightCtrl))  parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.LeftShift) || mods.HasFlag(KeyModifiers.RightShift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.LeftAlt)   || mods.HasFlag(KeyModifiers.RightAlt))   parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.LeftGui)   || mods.HasFlag(KeyModifiers.RightGui))   parts.Add("Win");
        parts.Add(symbol);
        return string.Join("+", parts);
    }
}
