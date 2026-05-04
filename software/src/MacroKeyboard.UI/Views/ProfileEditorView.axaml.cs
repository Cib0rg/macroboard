using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MacroKeyboard.Core.Models;
using MacroKeyboard.UI.ViewModels;

namespace MacroKeyboard.UI.Views;

public partial class ProfileEditorView : UserControl
{
    private bool _profilesLoaded;

    /// <summary>
    /// Custom in-process string data format for passing ActionType name via drag-n-drop
    /// </summary>
    private static readonly DataFormat<string> ActionTypeFormat = 
        DataFormat.CreateInProcessFormat<string>("MacroKeyboard.ActionType");

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
            // Create DataTransfer with the ActionType as string
            var data = new DataTransfer();
            var transferItem = DataTransferItem.Create(ActionTypeFormat, item.ActionType.ToString());
            data.Add(transferItem);

            // Start the drag operation
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        }
    }

    /// <summary>
    /// Handle drag over - show visual feedback on valid drop targets
    /// </summary>
    private void OnActionDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(ActionTypeFormat))
        {
            // Check if we're over a button item (has Tag with FlattenedButtonItem)
            var target = FindButtonDropTarget(e.Source as Interactive);
            if (target != null)
            {
                e.DragEffects = DragDropEffects.Copy;
                return;
            }

            // Also accept drops on the config panel action type zone
            if (DataContext is ProfileEditorViewModel vm && vm.IsButtonConfigVisible)
            {
                e.DragEffects = DragDropEffects.Copy;
                return;
            }
        }
        
        e.DragEffects = DragDropEffects.None;
    }

    /// <summary>
    /// Handle drop of an action type - either onto a button or onto the config panel
    /// </summary>
    private void OnActionDropped(object? sender, DragEventArgs e)
    {
        var actionTypeName = e.DataTransfer.TryGetValue(ActionTypeFormat);
        if (actionTypeName == null || !Enum.TryParse<ActionType>(actionTypeName, out var actionType))
            return;

        if (DataContext is not ProfileEditorViewModel vm)
            return;

        // Try to find a button drop target
        var buttonTarget = FindButtonDropTarget(e.Source as Interactive);
        if (buttonTarget != null)
        {
            // Drop on a button in the list → open config with this action type
            vm.HandleActionDropOnButton(buttonTarget, actionType);
            e.Handled = true;
            return;
        }

        // Drop on the config panel (if already open) → change action type
        if (vm.ButtonConfigViewModel != null && vm.IsButtonConfigVisible)
        {
            vm.ButtonConfigViewModel.SelectedActionType = actionType;
            e.Handled = true;
        }
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
            {
                return item;
            }
            current = current.GetVisualParent();
        }
        return null;
    }
}
