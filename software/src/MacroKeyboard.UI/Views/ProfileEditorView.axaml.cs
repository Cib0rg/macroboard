using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MacroKeyboard.Shared.Plugin;
using MacroKeyboard.UI.ViewModels;
using System;

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
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
                vm.SetStorageProvider(topLevel.StorageProvider);

            if (!_profilesLoaded)
            {
                _profilesLoaded = true;
                await vm.LoadProfilesAsync();
            }
        }
    }

    // ── Key capture (short press & long press fields)
    // Border.Tag holds the ButtonConfigDialogViewModel that owns the field.

    private void OnKeyCaptureFieldPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is ButtonConfigDialogViewModel captureVm)
        {
            captureVm.StartKeyCapture();
            border.Focus();
        }
    }

    private void OnKeyCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Border border && border.Tag is ButtonConfigDialogViewModel captureVm
            && captureVm.IsCapturingKeys)
        {
            captureVm.HandleKeyDown(e.Key, e.KeyModifiers, e.KeySymbol);
            e.Handled = true;
        }
    }

    private void OnKeyCaptureKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is Border border && border.Tag is ButtonConfigDialogViewModel captureVm
            && captureVm.IsCapturingKeys && e.Key == Key.Escape)
        {
            captureVm.StopKeyCapture();
            e.Handled = true;
        }
    }

    private void OnKeyCaptureTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is Border border && border.Tag is ButtonConfigDialogViewModel captureVm
            && captureVm.IsCapturingKeys)
        {
            captureVm.HandleTextInput(e.Text);
            e.Handled = true;
        }
    }

    // ── Sequence step key capture handlers

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
        if (sender is Border border && border.Tag is SequenceStepViewModel step && step.IsCapturingKeys
            && e.Key == Key.Escape)
        {
            step.StopKeyCapture();
            e.Handled = true;
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

    private void OnSequenceStepToggleCapture(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is SequenceStepViewModel step)
        {
            step.ToggleKeyCapture();
            if (button.Parent is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Border border)
                border.Focus();
        }
    }

    // ── Plugin action list selection ──────────────────────────────────────────
    // Using code-behind because SelectedItem TwoWay binding across a DataContext
    // boundary (DataContext="{Binding ButtonConfigViewModel}") is unreliable with
    // Avalonia's compiled binding when the outer x:DataType is ProfileEditorViewModel.

    private void OnPluginListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: PluginActionInfo action, DataContext: ButtonConfigDialogViewModel vm })
        {
            vm.SelectedPluginAction = action;
        }
    }

    // ── Property Inspector WebView ────────────────────────────────────────────

    private void OnPropertyInspectorNavigated(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        // Connection is established via autoconnect script injected by PropertyInspectorServer
        // into the served HTML — no post-navigation JS injection needed here.
    }
}
