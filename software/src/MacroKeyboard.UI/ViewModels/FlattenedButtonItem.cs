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
                actionText = !string.IsNullOrEmpty(ka.Text)
                    ? $"Keyboard: \"{ka.Text}\""
                    : $"Keyboard: key 0x{ka.KeyCode:X2}";
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
}
