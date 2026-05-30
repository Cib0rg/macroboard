using Avalonia.Media.Imaging;
using MacroKeyboard.Core.Models;
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
            var actionText = Button.Action?.ActionType.ToString() ?? "Not configured";
            
            if (Button.Action is KeyboardAction ka)
            {
                if (!string.IsNullOrEmpty(ka.Text))
                {
                    actionText = $"Keyboard: \"{ka.Text}\"";
                }
                else if (ka.KeyCode != 0)
                {
                    var keyName = HidKeyName(ka.KeyCode);
                    var modText = FormatModifiers(ka.Modifiers);
                    actionText = modText.Length > 0
                        ? $"Keyboard: {modText}+{keyName}"
                        : $"Keyboard: {keyName}";
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
            
            return $"{prefix}Button {Button.ButtonId + 1}: {actionText}";
        }
    }

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

    public FlattenedButtonItem(ButtonConfig button, int nestingLevel, byte? parentFolderId = null)
    {
        Button = button;
        NestingLevel = nestingLevel;
        ParentFolderId = parentFolderId;
    }

    private static string HidKeyName(byte keyCode) => keyCode switch
    {
        >= 0x04 and <= 0x1D => ((char)('A' + keyCode - 0x04)).ToString(),
        >= 0x1E and <= 0x26 => ((char)('1' + keyCode - 0x1E)).ToString(),
        0x27 => "0",
        0x28 => "Enter",
        0x29 => "Esc",
        0x2A => "Backspace",
        0x2B => "Tab",
        0x2C => "Space",
        >= 0x3A and <= 0x45 => $"F{keyCode - 0x3A + 1}",
        0x46 => "PrintScreen",
        0x47 => "ScrollLock",
        0x48 => "Pause",
        0x49 => "Insert",
        0x4A => "Home",
        0x4B => "PageUp",
        0x4C => "Delete",
        0x4D => "End",
        0x4E => "PageDown",
        0x4F => "Right",
        0x50 => "Left",
        0x51 => "Down",
        0x52 => "Up",
        _ => $"0x{keyCode:X2}"
    };

    private static string FormatModifiers(KeyModifiers mods)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (mods.HasFlag(KeyModifiers.LeftCtrl) || mods.HasFlag(KeyModifiers.RightCtrl)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.LeftShift) || mods.HasFlag(KeyModifiers.RightShift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.LeftAlt) || mods.HasFlag(KeyModifiers.RightAlt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.LeftGui) || mods.HasFlag(KeyModifiers.RightGui)) parts.Add("Win");
        return string.Join("+", parts);
    }
}
