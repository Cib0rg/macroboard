using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MacroKeyboard.UI.ViewModels;

namespace MacroKeyboard.UI.Views;

public partial class ButtonConfigDialog : Window
{
    public ButtonConfigDialog()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Provide the StorageProvider to the ViewModel so file dialogs work
        if (DataContext is ButtonConfigDialogViewModel vm)
        {
            vm.SetStorageProvider(StorageProvider);
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ButtonConfigDialogViewModel vm)
        {
            vm.SaveCommand.Execute(null);
            if (vm.DialogResult)
            {
                Close(true);
            }
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ButtonConfigDialogViewModel vm)
        {
            vm.CancelCommand.Execute(null);
        }
        Close(false);
    }

    private void OnKeyCaptureFieldPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ButtonConfigDialogViewModel vm)
        {
            vm.StartKeyCapture();
            
            // Focus the field to receive keyboard events
            if (sender is Border border)
            {
                border.Focus();
            }
        }
    }

    private void OnKeyCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is ButtonConfigDialogViewModel vm && vm.IsCapturingKeys)
        {
            // Don't process modifier-only keys as the main key
            if (e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl &&
                e.Key != Key.LeftShift && e.Key != Key.RightShift &&
                e.Key != Key.LeftAlt && e.Key != Key.RightAlt &&
                e.Key != Key.LWin && e.Key != Key.RWin)
            {
                vm.HandleKeyDown(e.Key, e.KeyModifiers);
            }
            else
            {
                // Still update modifiers display even for modifier-only keys
                vm.HandleKeyDown(e.Key, e.KeyModifiers);
            }
            
            e.Handled = true; // Prevent default handling
        }
    }

    private void OnKeyCaptureKeyUp(object? sender, KeyEventArgs e)
    {
        // We don't need to do anything special on key up for now
        if (DataContext is ButtonConfigDialogViewModel vm && vm.IsCapturingKeys)
        {
            e.Handled = true;
        }
    }
}
