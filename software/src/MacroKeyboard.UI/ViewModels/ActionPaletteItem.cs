using Avalonia;
using MacroKeyboard.Core.Models;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// Represents a draggable action item in the Actions Palette panel.
/// Items with IndentLevel > 0 are sub-items under a group header and carry a fully
/// pre-configured action that is applied directly on drop (no editor opened).
/// </summary>
public class ActionPaletteItem
{
    public ActionType ActionType { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string IconText { get; set; } = "⚡";
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// When set, dropping this item applies the action directly without opening the config editor.
    /// </summary>
    public ActionConfig? PreConfiguredAction { get; init; }

    /// <summary>
    /// 0 = top-level item / group header, 1 = sub-item (indented)
    /// </summary>
    public int IndentLevel { get; init; }

    // ── XAML-friendly visual helpers ────────────────────────────────────────
    public bool      IsSubItem     => IndentLevel > 0;
    public Thickness ItemMargin   => IsSubItem ? new Thickness(14, 0, 0, 3)  : new Thickness(0, 0, 0, 6);
    public Thickness ItemPadding  => IsSubItem ? new Thickness(8, 4)          : new Thickness(10, 8);
    public double    IconFontSize  => IsSubItem ? 15.0 : 22.0;
    public double    LabelFontSize => IsSubItem ? 10.5 : 12.0;
    public double    ItemOpacity   => IsSubItem ? 0.75 : 1.0;

    public ActionPaletteItem() { }

    public ActionPaletteItem(ActionType actionType, string displayName, string iconText, string description = "")
    {
        ActionType  = actionType;
        DisplayName = displayName;
        IconText    = iconText;
        Description = description;
    }
}
