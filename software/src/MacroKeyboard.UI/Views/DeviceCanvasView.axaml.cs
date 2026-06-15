using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MacroKeyboard.UI.ViewModels;
using System;

namespace MacroKeyboard.UI.Views;

public partial class DeviceCanvasView : UserControl
{
    private ButtonTileViewModel? _dragSource;
    private ButtonTileViewModel? _dropTarget;
    private bool _isDragging;
    private Avalonia.Point _dragStart;
    private const double DragThreshold = 6.0;

    public DeviceCanvasView()
    {
        InitializeComponent();
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not ButtonTileViewModel tile) return;
        if (DataContext is not DeviceCanvasViewModel vm) return;
        if (tile.IsBackButton) return;

        vm.SelectTile(tile);
        _dragSource = tile;
        _dragStart  = e.GetPosition(this);
        _isDragging = false;
    }

    private void OnTilePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource == null) return;

        var pos = e.GetPosition(this);

        if (!_isDragging)
        {
            var dx = pos.X - _dragStart.X;
            var dy = pos.Y - _dragStart.Y;
            if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;
            _isDragging = true;
        }

        var newTarget = FindTileAt(e.GetPosition(TilesControl));
        if (newTarget == _dragSource || (newTarget?.IsBackButton ?? false))
            newTarget = null;

        if (newTarget == _dropTarget) return;

        if (_dropTarget != null) _dropTarget.IsDragOver = false;
        _dropTarget = newTarget;
        if (_dropTarget != null) _dropTarget.IsDragOver = true;
    }

    private void OnTilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (_isDragging && _dragSource != null && _dropTarget != null
                && DataContext is DeviceCanvasViewModel vm)
            {
                vm.SwapButtons(_dragSource, _dropTarget);
            }
        }
        finally
        {
            if (_dropTarget != null) _dropTarget.IsDragOver = false;
            _dragSource = null;
            _dropTarget = null;
            _isDragging = false;
        }
    }

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_isDragging) return;
        if (sender is Border border && border.Tag is ButtonTileViewModel tile
            && DataContext is DeviceCanvasViewModel vm
            && tile.IsFolder && !tile.IsBackButton)
        {
            vm.NavigateInto(tile);
        }
    }

    private ButtonTileViewModel? FindTileAt(Avalonia.Point posInGrid)
    {
        if (DataContext is not DeviceCanvasViewModel vm) return null;

        var bounds = TilesControl.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        // Reject positions outside the grid
        if (posInGrid.X < 0 || posInGrid.Y < 0 ||
            posInGrid.X >= bounds.Width || posInGrid.Y >= bounds.Height)
            return null;

        const int cols = 5, rows = 2;
        int col   = Math.Clamp((int)(posInGrid.X / (bounds.Width  / cols)), 0, cols - 1);
        int row   = Math.Clamp((int)(posInGrid.Y / (bounds.Height / rows)), 0, rows - 1);
        int index = row * cols + col;

        return index < vm.CurrentTiles.Count ? vm.CurrentTiles[index] : null;
    }
}
