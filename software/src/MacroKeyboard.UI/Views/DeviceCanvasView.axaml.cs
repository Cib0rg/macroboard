using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MacroKeyboard.UI.ViewModels;

namespace MacroKeyboard.UI.Views;

public partial class DeviceCanvasView : UserControl
{
    public DeviceCanvasView()
    {
        InitializeComponent();
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is ButtonTileViewModel tile
            && DataContext is DeviceCanvasViewModel vm
            && !tile.IsBackButton)
        {
            vm.SelectTile(tile);
        }
    }

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.Tag is ButtonTileViewModel tile
            && DataContext is DeviceCanvasViewModel vm
            && tile.IsFolder && !tile.IsBackButton)
        {
            vm.NavigateInto(tile);
        }
    }
}
