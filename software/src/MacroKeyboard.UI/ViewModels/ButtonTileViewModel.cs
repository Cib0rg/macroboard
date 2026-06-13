using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MacroKeyboard.Core.Models;
using System.IO;

namespace MacroKeyboard.UI.ViewModels;

public partial class ButtonTileViewModel : ViewModelBase
{
    public ButtonConfig Button { get; }
    public bool IsBackButton { get; }

    [ObservableProperty]
    private bool _isSelected;

    public bool IsFolder => Button.Action is FolderAction;

    public bool HasLongPress =>
        Button.LongPressAction != null &&
        Button.LongPressAction.ActionType != ActionType.None;

    public bool HasImage => !string.IsNullOrWhiteSpace(Button.ImagePath) && File.Exists(Button.ImagePath);

    public Bitmap? TileImage => HasImage ? LoadThumbnail() : null;

    public string ActionIcon => IsBackButton ? "←" : Button.Action switch
    {
        null or NoneAction  => "",
        KeyboardAction      => "⌨",
        MediaAction         => "🔊",
        ShellAction         => "💻",
        LaunchAppAction     => "🚀",
        FolderAction        => "📁",
        SequenceAction      => "📋",
        ProfileSwitchAction => "🔄",
        CustomHidAction     => "🎛",
        NightModeAction     => "🌙",
        PluginActionConfig  => "🔌",
        _                   => "?"
    };

    public string DisplayName
    {
        get
        {
            if (IsBackButton) return "← Back";
            if (!string.IsNullOrWhiteSpace(Button.Name)) return Button.Name;
            return Button.Action switch
            {
                null or NoneAction  => $"B{Button.ButtonId + 1}",
                KeyboardAction ka when ka.KeyCode != 0 => HidKeyName(ka.KeyCode),
                KeyboardAction ka when !string.IsNullOrEmpty(ka.Text)
                    => ka.Text.Length > 8 ? ka.Text[..8] + "…" : ka.Text,
                KeyboardAction      => $"B{Button.ButtonId + 1}",
                MediaAction ma      => ma.Key.ToString(),
                FolderAction        => "Folder",
                ShellAction         => "Shell",
                LaunchAppAction la  => Path.GetFileNameWithoutExtension(la.ExecutablePath ?? "App"),
                NightModeAction     => "Night",
                PluginActionConfig pa when !string.IsNullOrEmpty(pa.ActionId) => pa.ActionId,
                _                   => $"B{Button.ButtonId + 1}"
            };
        }
    }

    public ButtonTileViewModel(ButtonConfig button, bool isBackButton = false)
    {
        Button = button;
        IsBackButton = isBackButton;
    }

    private Bitmap? LoadThumbnail()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Button.ImagePath) || !File.Exists(Button.ImagePath))
                return null;
            if (Path.GetExtension(Button.ImagePath).ToLowerInvariant() == ".svg")
                return null;
            using var stream = File.OpenRead(Button.ImagePath);
            return Bitmap.DecodeToWidth(stream, 64);
        }
        catch { return null; }
    }

    private static string HidKeyName(byte k) => k switch
    {
        >= 0x04 and <= 0x1D => ((char)('A' + k - 0x04)).ToString(),
        >= 0x1E and <= 0x26 => ((char)('1' + k - 0x1E)).ToString(),
        0x27 => "0", 0x28 => "Enter", 0x29 => "Esc",
        0x2C => "Space",
        >= 0x3A and <= 0x45 => $"F{k - 0x3A + 1}",
        _ => $"0x{k:X2}"
    };
}
