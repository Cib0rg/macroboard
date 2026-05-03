using Avalonia.Controls;
using Avalonia.Input;
using MacroKeyboard.UI.ViewModels;

namespace MacroKeyboard.UI.Views;

public partial class ProfileEditorView : UserControl
{
    private bool _profilesLoaded;

    public ProfileEditorView()
    {
        InitializeComponent();
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
                vm.ButtonConfigViewModel.HandleKeyDown(e.Key, e.KeyModifiers);
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
}
