using Avalonia.Controls;
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

        // Load profiles when the view is first shown
        if (!_profilesLoaded && DataContext is ProfileEditorViewModel vm)
        {
            _profilesLoaded = true;
            await vm.LoadProfilesAsync();
        }
    }
}
