using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MacroKeyboard.Core.Models;
using MacroKeyboard.UI.ViewModels;
// PluginActionConfig is in MacroKeyboard.Core.Models

namespace MacroKeyboard.UI.Views;

public partial class ProfileEditorView : UserControl
{
    private bool _profilesLoaded;

    /// <summary>
    /// Custom in-process format for passing the ActionPaletteItem via drag-n-drop.
    /// Using the object directly lets the drop handler access PreConfiguredAction.
    /// </summary>
    private static readonly DataFormat<ActionPaletteItem> PaletteItemFormat =
        DataFormat.CreateInProcessFormat<ActionPaletteItem>("MacroKeyboard.PaletteItem");

    public ProfileEditorView()
    {
        InitializeComponent();

        // Set up drop handlers (bubbling, so they catch drops on child elements too)
        AddHandler(DragDrop.DropEvent, OnActionDropped);
        AddHandler(DragDrop.DragOverEvent, OnActionDragOver);
    }

    protected override async void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is ProfileEditorViewModel vm)
        {
            // Provide StorageProvider from the top-level window
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                vm.SetStorageProvider(topLevel.StorageProvider);
            }

            // Load profiles when the view is first shown
            if (!_profilesLoaded)
            {
                _profilesLoaded = true;
                await vm.LoadProfilesAsync();
            }
        }
    }

    /// <summary>
    /// Handle click on key capture field to start capturing
    /// </summary>
    private void OnKeyCaptureFieldPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ProfileEditorViewModel vm && vm.ButtonConfigViewModel != null)
        {
            vm.ButtonConfigViewModel.StartKeyCapture();
            // Focus the field so it receives key events
            if (sender is Border border)
            {
                border.Focus();
            }
        }
    }

    /// <summary>
    /// Handle key down during capture
    /// </summary>
    private void OnKeyCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is ProfileEditorViewModel vm && vm.ButtonConfigViewModel != null)
        {
            if (vm.ButtonConfigViewModel.IsCapturingKeys)
            {
                // Pass KeySymbol to display the correct character for the current keyboard layout
                vm.ButtonConfigViewModel.HandleKeyDown(e.Key, e.KeyModifiers, e.KeySymbol);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Handle key up during capture (stop on Escape)
    /// </summary>
    private void OnKeyCaptureKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is ProfileEditorViewModel vm && vm.ButtonConfigViewModel != null)
        {
            if (vm.ButtonConfigViewModel.IsCapturingKeys && e.Key == Key.Escape)
            {
                vm.ButtonConfigViewModel.StopKeyCapture();
                e.Handled = true;
            }
        }
    }

    // ============================================
    // Sequence Step Key Capture Handlers
    // ============================================

    private void OnSequenceStepKeyCapturePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is SequenceStepViewModel step)
        {
            step.StartKeyCapture();
            border.Focus();
        }
    }

    private void OnSequenceStepKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Border border && border.Tag is SequenceStepViewModel step && step.IsCapturingKeys)
        {
            step.HandleKeyDown(e.Key, e.KeyModifiers, e.KeySymbol);
            e.Handled = true;
        }
    }

    private void OnSequenceStepKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is Border border && border.Tag is SequenceStepViewModel step && step.IsCapturingKeys)
        {
            if (e.Key == Key.Escape)
            {
                step.StopKeyCapture();
                e.Handled = true;
            }
        }
    }

    private void OnSequenceStepTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is Border border && border.Tag is SequenceStepViewModel step && step.IsCapturingKeys)
        {
            step.HandleTextInput(e.Text);
            e.Handled = true;
        }
    }

    private void OnSequenceStepToggleCapture(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is SequenceStepViewModel step)
        {
            step.ToggleKeyCapture();
            // Focus the capture border (previous sibling in the grid)
            if (button.Parent is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Border border)
            {
                border.Focus();
            }
        }
    }

    /// <summary>
    /// Handle text input during capture - provides the actual character for non-Latin layouts (e.g., Russian)
    /// </summary>
    private void OnKeyCaptureTextInput(object? sender, TextInputEventArgs e)
    {
        if (DataContext is ProfileEditorViewModel vm && vm.ButtonConfigViewModel != null)
        {
            if (vm.ButtonConfigViewModel.IsCapturingKeys)
            {
                vm.ButtonConfigViewModel.HandleTextInput(e.Text);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Start drag operation when an action palette item is pressed
    /// </summary>
    private async void OnActionDragStarted(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is ActionPaletteItem item)
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(PaletteItemFormat, item));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        }
    }

    /// <summary>
    /// Handle drag over - show visual feedback on valid drop targets
    /// </summary>
    private void OnActionDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(PaletteItemFormat))
        {
            var target = FindButtonDropTarget(e.Source as Interactive);
            if (target != null) { e.DragEffects = DragDropEffects.Copy; return; }

            if (FindEncoderSlotTarget(e.Source as Interactive) >= 0) { e.DragEffects = DragDropEffects.Copy; return; }

            if (DataContext is ProfileEditorViewModel vm && (vm.IsButtonConfigVisible || vm.IsEncoderConfigVisible))
            {
                e.DragEffects = DragDropEffects.Copy;
                return;
            }
        }

        e.DragEffects = DragDropEffects.None;
    }

    /// <summary>
    /// Handle drop — pre-configured sub-items apply directly; group headers open the config editor.
    /// </summary>
    private async void OnActionDropped(object? sender, DragEventArgs e)
    {
        var paletteItem = e.DataTransfer.TryGetValue(PaletteItemFormat);
        if (paletteItem == null || DataContext is not ProfileEditorViewModel vm)
            return;

        var buttonTarget = FindButtonDropTarget(e.Source as Interactive);
        if (buttonTarget != null)
        {
            if (paletteItem.PreConfiguredAction != null)
                await vm.HandlePreConfiguredActionDrop(buttonTarget, paletteItem.PreConfiguredAction);
            else
                vm.HandleActionDropOnButton(buttonTarget, paletteItem.ActionType);
            e.Handled = true;
            return;
        }

        // Drop directly on an encoder slot button → open encoder config for that slot
        var encoderSlot = FindEncoderSlotTarget(e.Source as Interactive);
        if (encoderSlot >= 0)
        {
            vm.HandleActionDropOnEncoder(encoderSlot, paletteItem.ActionType);
            if (paletteItem.PreConfiguredAction is MediaAction slotMedia)
                vm.ButtonConfigViewModel!.SelectedMediaKey = slotMedia.Key;
            else if (paletteItem.PreConfiguredAction is PluginActionConfig slotPlugin)
                ApplyPluginAction(vm.ButtonConfigViewModel!, slotPlugin);
            e.Handled = true;
            return;
        }

        // Drop on the open config panel → change action type (and pre-fill key if applicable)
        if (vm.ButtonConfigViewModel != null && (vm.IsButtonConfigVisible || vm.IsEncoderConfigVisible))
        {
            vm.ButtonConfigViewModel.SelectedActionType = paletteItem.ActionType;
            if (paletteItem.PreConfiguredAction is MediaAction mediaAction)
                vm.ButtonConfigViewModel.SelectedMediaKey = mediaAction.Key;
            else if (paletteItem.PreConfiguredAction is PluginActionConfig pluginAction)
                ApplyPluginAction(vm.ButtonConfigViewModel, pluginAction);
            e.Handled = true;
        }
    }

    private static void ApplyPluginAction(ButtonConfigDialogViewModel vm, PluginActionConfig plugin)
    {
        vm.PluginId = plugin.PluginId;
        vm.PluginActionId = plugin.ActionId;
        vm.PluginSettings = plugin.Settings ?? string.Empty;
    }

    /// <summary>
    /// Walk up the visual tree from the drop source to find a Border with a FlattenedButtonItem Tag
    /// </summary>
    private FlattenedButtonItem? FindButtonDropTarget(Interactive? source)
    {
        var current = source as Visual;
        while (current != null)
        {
            if (current is Border border && border.Tag is FlattenedButtonItem item)
                return item;
            current = current.GetVisualParent();
        }
        return null;
    }

    /// <summary>
    /// Walk up the visual tree looking for a Tag="encoder:N" element; returns slot 0-3 or -1.
    /// </summary>
    private static int FindEncoderSlotTarget(Interactive? source)
    {
        var current = source as Visual;
        while (current != null)
        {
            if (current is Control el && el.Tag is string tag && tag.StartsWith("encoder:") &&
                int.TryParse(tag["encoder:".Length..], out int slot) && slot is >= 0 and <= 3)
                return slot;
            current = current.GetVisualParent();
        }
        return -1;
    }
}
