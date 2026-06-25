using Avalonia.Media.Imaging;
using MacroKeyboard.Core.Models;
using MacroKeyboard.UI.Utilities;
using System;
using System.IO;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// Wraps a ButtonConfig with nesting level info for the flat list display.
/// Root buttons have NestingLevel=0, folder buttons have NestingLevel=1+.
/// </summary>
public class FlattenedButtonItem
{
    /// <summary>
    /// The button configuration
    /// </summary>
    public ButtonConfig Button { get; set; }
    
    /// <summary>
    /// Nesting level (0 = root, 1 = inside folder, 2 = nested folder, etc.)
    /// </summary>
    public int NestingLevel { get; set; }
    
    /// <summary>
    /// Left margin for indentation (NestingLevel * 30px)
    /// </summary>
    public double LeftMargin => NestingLevel * 30.0;
    
    /// <summary>
    /// Margin as Avalonia Thickness string
    /// </summary>
    public Avalonia.Thickness Margin => new(LeftMargin, 2, 2, 2);
    
    /// <summary>
    /// Display label
    /// </summary>
    public string Label
    {
        get
        {
            var prefix = NestingLevel > 0 ? "  ↳ " : "";

            if (IsBackButton)
                return $"{prefix}Button {Button.ButtonId + 1}: ⬅ Back (reserved)";

            var actionText = Button.Action?.ActionType.ToString() ?? "Not configured";
            
            if (Button.Action is KeyboardAction ka)
            {
                if (!string.IsNullOrEmpty(ka.Text))
                {
                    actionText = $"Keyboard: \"{ka.Text}\"";
                }
                else if (ka.KeyCode != 0)
                {
                    actionText = $"Keyboard: {HidKeyCodeHelper.FormatKey(ka.KeyCode, ka.Modifiers)}";
                }
                else
                {
                    actionText = "Keyboard: (not set)";
                }
            }
            else if (Button.Action is ProfileSwitchAction ps)
            {
                actionText = $"Switch → Profile {ps.TargetProfileId}";
            }
            else if (Button.Action is FolderAction)
            {
                actionText = $"📁 {FolderDisplayName}";
            }
            else if (Button.Action is LaunchAppAction la)
            {
                var appName = !string.IsNullOrEmpty(la.ExecutablePath)
                    ? System.IO.Path.GetFileNameWithoutExtension(la.ExecutablePath)
                    : "App";
                actionText = $"🚀 {appName}";
            }
            else if (Button.Action is MediaAction ma)
            {
                actionText = $"🔊 {ma.Key}";
            }
            else if (Button.Action is ShellAction sh)
            {
                var cmd = sh.Command.Length > 20 ? sh.Command[..20] + "..." : sh.Command;
                actionText = $"💻 {cmd}";
            }
            else if (Button.Action is PluginActionConfig pa)
            {
                actionText = $"🔌 {pa.ActionId}";
            }

            return $"{prefix}Button {Button.ButtonId + 1}: {actionText}";
        }
    }

    /// <summary>
    /// Short display text for the long press action (shown in the right sub-column)
    /// </summary>
    public string LongPressLabel => ActionText(Button.LongPressAction);

    private static string ActionText(ActionConfig? action) => action switch
    {
        null => "—",
        NoneAction => "—",
        KeyboardAction ka when ka.KeyCode != 0 => $"Key: {HidKeyCodeHelper.GetKeyName(ka.KeyCode)}",
        KeyboardAction ka when !string.IsNullOrEmpty(ka.Text) => $"\"{ka.Text}\"",
        KeyboardAction => "Key: (not set)",
        MediaAction ma => $"🔊 {ma.Key}",
        LaunchAppAction la when !string.IsNullOrEmpty(la.ExecutablePath)
            => $"🚀 {System.IO.Path.GetFileNameWithoutExtension(la.ExecutablePath)}",
        ShellAction sh => $"💻 {(sh.Command.Length > 18 ? sh.Command[..18] + "…" : sh.Command)}",
        FolderAction => "📁 Folder",
        PluginActionConfig pa => $"🔌 {pa.ActionId}",
        _ => action.ActionType.ToString()
    };

    /// <summary>
    /// Display name for the folder (if this button opens a folder)
    /// </summary>
    public string FolderDisplayName { get; set; } = "Folder";
    
    /// <summary>
    /// Whether this is a folder header (the button that opens a folder)
    /// </summary>
    public bool IsFolderHeader => Button.Action?.ActionType == ActionType.Folder;
    
    /// <summary>
    /// Folder ID if this is a folder header
    /// </summary>
    public byte? FolderId => IsFolderHeader ? Button.FolderId : null;
    
    /// <summary>
    /// Parent folder ID (null for root buttons)
    /// </summary>
    public byte? ParentFolderId { get; set; }

    /// <summary>
    /// ButtonId of the root button that entered this folder (only set for folder sub-buttons).
    /// The sub-button with the same index is the implicit back button — firmware ignores its action.
    /// </summary>
    public byte? EntryButtonId { get; set; }

    /// <summary>
    /// True when this sub-button slot is reserved as the "Back" button by the firmware.
    /// Its action cannot be configured — pressing it always exits the folder.
    /// </summary>
    public bool IsBackButton => NestingLevel > 0 && EntryButtonId.HasValue && Button.ButtonId == EntryButtonId.Value;

    /// <summary>
    /// Whether this button has an image assigned
    /// </summary>
    public bool HasImage => !string.IsNullOrWhiteSpace(Button.ImagePath) && File.Exists(Button.ImagePath);

    /// <summary>
    /// Thumbnail bitmap for the button image (lazy-loaded)
    /// </summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded)
                return _thumbnail;

            _thumbnailLoaded = true;
            _thumbnail = LoadThumbnail();
            return _thumbnail;
        }
    }

    private Bitmap? _thumbnail;
    private bool _thumbnailLoaded;

    private Bitmap? LoadThumbnail()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Button.ImagePath) || !File.Exists(Button.ImagePath))
                return null;

            var ext = Path.GetExtension(Button.ImagePath).ToLowerInvariant();
            if (ext == ".svg")
                return null;

            using var stream = File.OpenRead(Button.ImagePath);
            // Decode at reduced size for thumbnail (32x32)
            return Bitmap.DecodeToWidth(stream, 32);
        }
        catch
        {
            return null;
        }
    }

    public FlattenedButtonItem(ButtonConfig button, int nestingLevel, byte? parentFolderId = null, byte? entryButtonId = null)
    {
        Button = button;
        NestingLevel = nestingLevel;
        ParentFolderId = parentFolderId;
        EntryButtonId = entryButtonId;
    }

}
