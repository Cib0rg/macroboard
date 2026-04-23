using MacroKeyboard.Core.Models;

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
            
            return $"{prefix}Button {Button.ButtonId}: {actionText}";
        }
    }
    
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

    public FlattenedButtonItem(ButtonConfig button, int nestingLevel, byte? parentFolderId = null)
    {
        Button = button;
        NestingLevel = nestingLevel;
        ParentFolderId = parentFolderId;
    }
}
