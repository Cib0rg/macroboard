using MacroKeyboard.Core.Models;

namespace MacroKeyboard.UI.ViewModels;

/// <summary>
/// Represents a draggable action item in the Actions Palette panel.
/// Each item corresponds to an ActionType with an icon and display name.
/// </summary>
public class ActionPaletteItem
{
    /// <summary>
    /// The action type this palette item represents
    /// </summary>
    public ActionType ActionType { get; set; }

    /// <summary>
    /// Human-readable display name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Emoji/text icon for display
    /// </summary>
    public string IconText { get; set; } = "⚡";

    /// <summary>
    /// Short description shown as tooltip
    /// </summary>
    public string Description { get; set; } = string.Empty;

    public ActionPaletteItem()
    {
    }

    public ActionPaletteItem(ActionType actionType, string displayName, string iconText, string description = "")
    {
        ActionType = actionType;
        DisplayName = displayName;
        IconText = iconText;
        Description = description;
    }
}
