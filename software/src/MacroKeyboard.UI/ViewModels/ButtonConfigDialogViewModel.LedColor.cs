using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Globalization;

namespace MacroKeyboard.UI.ViewModels;

public partial class ButtonConfigDialogViewModel
{
    // ── LED color fields ──────────────────────────────────────────────────────

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

    [ObservableProperty]
    private Color _ledColor = Color.FromRgb(255, 255, 255);

    private bool _isUpdatingColor = false;

    public Color LedColorPreview => Color.FromRgb((byte)ColorR, (byte)ColorG, (byte)ColorB);

    // ── Sync handlers ─────────────────────────────────────────────────────────

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

    partial void OnLedColorHexChanged(string value)
    {
        if (TryParseHexColor(value, out byte r, out byte g, out byte b))
        {
            if ((byte)ColorR != r || (byte)ColorG != g || (byte)ColorB != b)
            {
                ColorR = r;
                ColorG = g;
                ColorB = b;
                OnPropertyChanged(nameof(LedColorPreview));
            }
        }
    }

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

    private void UpdateHexFromRgb()
    {
        var newHex = $"#{(byte)ColorR:X2}{(byte)ColorG:X2}{(byte)ColorB:X2}";
        if (LedColorHex != newHex)
        {
            _ledColorHex = newHex; // Direct field access avoids triggering OnLedColorHexChanged
            OnPropertyChanged(nameof(LedColorHex));
        }
    }

    private static bool TryParseHexColor(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var clean = hex.TrimStart('#');
        if (clean.Length != 6) return false;
        if (!uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;
        r = (byte)((value >> 16) & 0xFF);
        g = (byte)((value >> 8)  & 0xFF);
        b = (byte)(value         & 0xFF);
        return true;
    }

    [RelayCommand]
    private void ToggleColorPicker() => IsColorPickerVisible = !IsColorPickerVisible;
}
