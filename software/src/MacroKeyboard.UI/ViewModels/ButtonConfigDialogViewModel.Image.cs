using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

public partial class ButtonConfigDialogViewModel
{
    // ── Image fields ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _imagePath = string.Empty;

    private Bitmap? _imagePreviewBitmap;

    public Bitmap? ImagePreview
    {
        get => _imagePreviewBitmap;
        private set
        {
            if (SetProperty(ref _imagePreviewBitmap, value))
                OnPropertyChanged(nameof(HasImagePreview));
        }
    }

    public bool HasImagePreview => _imagePreviewBitmap != null;

    // ── Handlers & commands ───────────────────────────────────────────────────

    partial void OnImagePathChanged(string value) => LoadImagePreview(value);

    private void LoadImagePreview(string path)
    {
        try
        {
            ImagePreview?.Dispose();
            ImagePreview = null;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                OnPropertyChanged(nameof(HasImagePreview));
                return;
            }

            // Skip SVG files — Avalonia Bitmap doesn't support them directly
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".svg")
            {
                OnPropertyChanged(nameof(HasImagePreview));
                return;
            }

            using var stream = File.OpenRead(path);
            ImagePreview = new Bitmap(stream);
            OnPropertyChanged(nameof(HasImagePreview));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load image preview from {Path}", path);
            ImagePreview = null;
            OnPropertyChanged(nameof(HasImagePreview));
        }
    }

    [RelayCommand]
    private void ClearImage() => ImagePath = string.Empty;

    [RelayCommand]
    private async Task BrowseImage()
    {
        try
        {
            _logger.LogInformation("Browse image clicked");

            if (_storageProvider == null)
            {
                _logger.LogWarning("StorageProvider not set");
                return;
            }

            var fileTypes = new FilePickerFileType[]
            {
                new("Images")
                {
                    Patterns  = new[] { "*.jpg", "*.jpeg", "*.png", "*.svg", "*.ico", "*.gif" },
                    MimeTypes = new[] { "image/*" }
                }
            };

            var options = new FilePickerOpenOptions
            {
                Title = "Select Button Image",
                AllowMultiple = false,
                FileTypeFilter = fileTypes
            };

            var result = await _storageProvider.OpenFilePickerAsync(options);

            if (result != null && result.Count > 0)
            {
                ImagePath = result[0].Path.LocalPath;
                _logger.LogInformation("Image selected: {Path}", ImagePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error browsing for image");
        }
    }
}
