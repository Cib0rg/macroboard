using MacroKeyboard.Core.Models;
using System.Collections.Generic;

namespace MacroKeyboard.UI.Utilities;

internal static class ActionTypeHelpers
{
    public static IReadOnlyList<ActionType> AllActionTypes { get; } =
    [
        ActionType.None,
        ActionType.Keyboard,
        ActionType.Media,
        ActionType.LaunchApp,
        ActionType.Shell,
        ActionType.Sequence,
        ActionType.Folder,
        ActionType.NightMode,
        ActionType.CustomHid,
        ActionType.Plugin,
    ];

    // Sequence steps exclude Sequence itself to prevent recursion.
    public static IReadOnlyList<ActionType> SequenceStepTypes { get; } =
    [
        ActionType.Keyboard,
        ActionType.Shell,
        ActionType.CustomHid,
        ActionType.Folder,
        ActionType.Delay,
    ];
}
