using CommunityToolkit.Mvvm.ComponentModel;
using MacroKeyboard.Core.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MacroKeyboard.UI.ViewModels;

public partial class DeviceCanvasViewModel : ViewModelBase
{
    private record NavLevel(string Name, IReadOnlyList<ButtonConfig> Buttons, byte? FolderId, byte? EntryButtonId);

    private readonly Stack<NavLevel> _navStack = new();
    private Profile? _profile;

    public ObservableCollection<ButtonTileViewModel> CurrentTiles { get; } = new();
    public ObservableCollection<string> BreadcrumbPath { get; } = new();

    [ObservableProperty]
    private ButtonTileViewModel? _selectedTile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigateUp))]
    private int _navDepth;

    public bool CanNavigateUp => _navDepth > 0;

    public void LoadProfile(Profile? profile)
    {
        _profile = profile;
        _navStack.Clear();
        BreadcrumbPath.Clear();
        NavDepth = 0;
        SelectedTile = null;

        if (profile == null) { CurrentTiles.Clear(); return; }

        _navStack.Push(new NavLevel("Root", profile.Buttons, null, null));
        RebuildTiles();
    }

    public void NavigateInto(ButtonTileViewModel tile)
    {
        if (_profile == null || !tile.IsFolder) return;
        if (tile.Button.Action is not FolderAction fa) return;

        var folder = _profile.Folders.FirstOrDefault(f => f.FolderId == fa.FolderId);
        if (folder == null) return;

        var folderName = !string.IsNullOrWhiteSpace(tile.Button.Name)
            ? tile.Button.Name
            : folder.Name ?? $"Folder {fa.FolderId}";

        BreadcrumbPath.Add(folderName);
        _navStack.Push(new NavLevel(folderName, folder.Buttons, fa.FolderId, tile.Button.ButtonId));
        NavDepth = _navStack.Count - 1;
        SelectedTile = null;
        RebuildTiles();
    }

    public void NavigateUp()
    {
        if (_navStack.Count <= 1) return;
        _navStack.Pop();
        if (BreadcrumbPath.Count > 0)
            BreadcrumbPath.RemoveAt(BreadcrumbPath.Count - 1);
        NavDepth = _navStack.Count - 1;
        SelectedTile = null;
        RebuildTiles();
    }

    public void NavigateTo(int targetDepth)
    {
        while (_navStack.Count > targetDepth + 1)
        {
            _navStack.Pop();
            if (BreadcrumbPath.Count > 0)
                BreadcrumbPath.RemoveAt(BreadcrumbPath.Count - 1);
        }
        NavDepth = _navStack.Count - 1;
        SelectedTile = null;
        RebuildTiles();
    }

    public void SelectTile(ButtonTileViewModel tile)
    {
        if (SelectedTile != null)
            SelectedTile.IsSelected = false;
        SelectedTile = tile;
        tile.IsSelected = true;
    }

    public void DeselectAll()
    {
        if (SelectedTile != null)
            SelectedTile.IsSelected = false;
        SelectedTile = null;
    }

    public void Refresh()
    {
        var prevId = SelectedTile?.Button.ButtonId;
        RebuildTiles();
        if (prevId.HasValue)
        {
            var tile = CurrentTiles.FirstOrDefault(t => t.Button.ButtonId == prevId.Value);
            if (tile != null) { tile.IsSelected = true; SelectedTile = tile; }
        }
    }

    private void RebuildTiles()
    {
        CurrentTiles.Clear();
        if (_navStack.Count == 0) return;

        var level = _navStack.Peek();
        foreach (var btn in level.Buttons.OrderBy(b => b.ButtonId))
        {
            bool isBack = level.EntryButtonId.HasValue && btn.ButtonId == level.EntryButtonId.Value;
            CurrentTiles.Add(new ButtonTileViewModel(btn, isBack));
        }
    }
}
