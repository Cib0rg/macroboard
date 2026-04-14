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
