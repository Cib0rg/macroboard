using System;
using Avalonia.Controls;
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
}
