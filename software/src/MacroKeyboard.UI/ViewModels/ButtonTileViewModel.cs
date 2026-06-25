using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MacroKeyboard.Core.Models;
using MacroKeyboard.UI.Utilities;
using System.IO;

namespace MacroKeyboard.UI.ViewModels;

public partial class ButtonTileViewModel : ViewModelBase
{
    public ButtonConfig Button { get; }
    public bool IsBackButton { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isDragOver;

    public bool IsFolder       => Button.Action is FolderAction;
    public bool IsPluginAction => Button.Action is PluginActionConfig;

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
                KeyboardAction ka when ka.KeyCode != 0 => HidKeyCodeHelper.GetKeyName(ka.KeyCode),
                KeyboardAction ka when !string.IsNullOrEmpty(ka.Text)
                    => ka.Text.Length > 8 ? ka.Text[..8] + "…" : ka.Text,
                KeyboardAction      => $"B{Button.ButtonId + 1}",
                MediaAction ma      => ma.Key.ToString(),
                FolderAction        => "Folder",
                ShellAction         => "Shell",
                LaunchAppAction la  => Path.GetFileNameWithoutExtension(la.ExecutablePath ?? "App"),
                NightModeAction     => "Night",
                PluginActionConfig pa when !string.IsNullOrEmpty(pa.ActionName) => pa.ActionName,
                PluginActionConfig pa when !string.IsNullOrEmpty(pa.ActionId)   => pa.ActionId,
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

}
