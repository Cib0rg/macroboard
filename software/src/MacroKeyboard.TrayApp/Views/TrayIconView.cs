using Avalonia.Controls;
using Avalonia.Platform;
using MacroKeyboard.TrayApp.ViewModels;
using System;
using System.Runtime.InteropServices;

namespace MacroKeyboard.TrayApp.Views;

/// <summary>
/// Tray icon view
/// </summary>
public class TrayIconView
{
    private readonly TrayIcon _trayIcon;
    private readonly TrayIconViewModel _viewModel;

    public TrayIconView(TrayIconViewModel viewModel)
    {
        _viewModel = viewModel;
        
        _trayIcon = new TrayIcon();
        
        // Set icon (will use default if not found)
        try
        {
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _trayIcon.Icon = new WindowIcon(iconPath);
            }
        }
        catch
        {
            // Icon not found, will use default
        }

        _trayIcon.ToolTipText = "MacroKeyboard";
        
        // Create context menu
        var menu = new NativeMenu();
        
        var statusItem = new NativeMenuItem
        {
            Header = "Status: Disconnected",
            IsEnabled = false
        };
        menu.Add(statusItem);
        
        menu.Add(new NativeMenuItemSeparator());
        
        var configItem = new NativeMenuItem { Header = "Configuration..." };
        configItem.Click += (s, e) => _viewModel.ShowConfiguration();
        menu.Add(configItem);
        
        menu.Add(new NativeMenuItemSeparator());
        
        var exitItem = new NativeMenuItem { Header = "Exit" };
        exitItem.Click += (s, e) => _viewModel.Exit();
        menu.Add(exitItem);
        
        _trayIcon.Menu = menu;
        
        // Update status when connection changes
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TrayIconViewModel.StatusText))
            {
                statusItem.Header = $"Status: {_viewModel.StatusText}";
                _trayIcon.ToolTipText = $"MacroKeyboard - {_viewModel.StatusText}";
            }
        };
        
        // Show tray icon
        _trayIcon.IsVisible = true;
    }
}
