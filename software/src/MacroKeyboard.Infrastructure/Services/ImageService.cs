using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Svg.Skia;
using SkiaSharp;

namespace MacroKeyboard.Infrastructure.Services;

/// <summary>
/// Сервис для обработки изображений для дисплеев кнопок.
/// Поддерживает форматы: JPEG, PNG, SVG, ICO, GIF.
/// Все изображения масштабируются до 160x160 и конвертируются в raw RGB565 big-endian.
/// </summary>
public class ImageService
{
    private readonly ILogger<ImageService> _logger;

    /// <summary>
    /// Target display resolution (GC9A01 round LCD)
    /// </summary>
    public const int DisplaySize = 160;

    public ImageService(ILogger<ImageService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Supported image file extensions
    /// </summary>
    public static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".svg", ".ico", ".gif" };

    /// <summary>
    /// Check if a file extension is supported
    /// </summary>
    public static bool IsSupportedFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// Загрузить и обработать изображение для кнопки.
    /// Результат: JPEG bytes (decoded to RGB565 on device).
    /// </summary>
    public async Task<byte[]?> ProcessImageForButtonAsync(string imagePath, Color? ringColor = null)
    {
        try
        {
            var ext = Path.GetExtension(imagePath).ToLowerInvariant();

            return ext switch
            {
                ".svg" => await ProcessSvgAsync(imagePath, ringColor),
                ".gif" => await ProcessGifAsync(imagePath),
                ".ico" => await ProcessIcoAsync(imagePath, ringColor),
                _      => await ProcessRasterImageAsync(imagePath, ringColor)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image {ImagePath}", imagePath);
            return null;
        }
    }

    /// <summary>
    /// Process raw image bytes (e.g., from plugin setImage).
    /// </summary>
    public async Task<byte[]?> ProcessImageBytesForButtonAsync(byte[] rawBytes, Color? ringColor = null)
    {
        try
        {
            using var source = Image.Load<Rgba32>(rawBytes);
            using var image  = PrepareForDisplay(source);
            ApplyCircularMask(image);
            if (ringColor.HasValue) DrawRing(image, ringColor.Value);
            return await ToJpegBytesAsync(image);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image bytes");
            return null;
        }
    }

    /// <summary>
    /// Send this to the device to erase a previously-displayed image (None action).
    /// </summary>
    public Task<byte[]> CreateBlankImageAsync()
    {
        using var image = new Image<Rgba32>(DisplaySize, DisplaySize);
        image.Mutate(ctx => ctx.BackgroundColor(Color.Black));
        ApplyCircularMask(image);
        return ToJpegBytesAsync(image);
    }

    /// <summary>
    /// Create a 160×160 black circle with the ring drawn and an optional text label.
    /// </summary>
    public Task<byte[]> CreateRingPlaceholderAsync(Color ringColor, string? label = null)
    {
        using var image = new Image<Rgba32>(DisplaySize, DisplaySize);
        image.Mutate(ctx => ctx.BackgroundColor(Color.Black));

        if (!string.IsNullOrEmpty(label))
        {
            try
            {
                FontFamily fontFamily;
                try        { fontFamily = SystemFonts.Get("Arial"); }
                catch      { try { fontFamily = SystemFonts.Get("DejaVu Sans"); }
                             catch { fontFamily = SystemFonts.Families.First(); } }

                var font = fontFamily.CreateFont(28, FontStyle.Bold);
                var opts = new RichTextOptions(font)
                {
                    Origin              = new PointF(DisplaySize / 2f, DisplaySize / 2f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                    WrappingLength      = DisplaySize - 24,
                    TextAlignment       = TextAlignment.Center
                };
                image.Mutate(ctx => ctx.DrawText(opts, label, Color.White));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to render label on ring placeholder");
            }
        }

        ApplyCircularMask(image);
        DrawRing(image, ringColor);
        return ToJpegBytesAsync(image);
    }

    private static void DrawRing(Image<Rgba32> image, Color color)
    {
        var ring = new SixLabors.ImageSharp.Drawing.EllipsePolygon(
            DisplaySize / 2f, DisplaySize / 2f, (DisplaySize / 2f) - 4f);
        image.Mutate(ctx => ctx.Draw(Pens.Solid(color, 4f), ring));
    }

    private async Task<byte[]> ProcessRasterImageAsync(string imagePath, Color? ringColor = null)
    {
        using var source = await Image.LoadAsync<Rgba32>(imagePath);
        using var image = PrepareForDisplay(source);
        ApplyCircularMask(image);
        if (ringColor.HasValue) DrawRing(image, ringColor.Value);
        var result = await ToJpegBytesAsync(image);
        _logger.LogInformation("Raster image processed: {Path} → {Size} bytes JPEG", imagePath, result.Length);
        return result;
    }

    private async Task<byte[]> ProcessSvgAsync(string imagePath, Color? ringColor = null)
    {
        var svgContent = await File.ReadAllTextAsync(imagePath);

        using var svg = new SKSvg();
        svg.FromSvg(svgContent);

        if (svg.Picture == null)
            throw new InvalidOperationException($"Failed to parse SVG: {imagePath}");

        using var bitmap = new SKBitmap(DisplaySize, DisplaySize);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var bounds = svg.Picture.CullRect;
        var scaleX = DisplaySize / bounds.Width;
        var scaleY = DisplaySize / bounds.Height;
        var scale  = Math.Min(scaleX, scaleY);
        canvas.Translate((DisplaySize - bounds.Width * scale) / 2, (DisplaySize - bounds.Height * scale) / 2);
        canvas.Scale(scale);
        canvas.DrawPicture(svg.Picture);

        using var image = ConvertSkBitmapToImageSharp(bitmap);
        ApplyCircularMask(image);
        if (ringColor.HasValue) DrawRing(image, ringColor.Value);

        var result = await ToJpegBytesAsync(image);
        _logger.LogInformation("SVG image processed: {Path} → {Size} bytes JPEG", imagePath, result.Length);
        return result;
    }

    private async Task<byte[]> ProcessIcoAsync(string imagePath, Color? ringColor = null)
    {
        using var source = await Image.LoadAsync<Rgba32>(imagePath);
        using var image  = PrepareForDisplay(source);
        ApplyCircularMask(image);
        if (ringColor.HasValue) DrawRing(image, ringColor.Value);
        var result = await ToJpegBytesAsync(image);
        _logger.LogInformation("ICO image processed: {Path} → {Size} bytes JPEG", imagePath, result.Length);
        return result;
    }

    private async Task<byte[]> ProcessGifAsync(string imagePath)
    {
        using var image = await Image.LoadAsync<Rgba32>(imagePath);
        if (image.Frames.Count > 1)
            _logger.LogInformation("GIF has {FrameCount} frames, using first frame only", image.Frames.Count);
        // Keep only first frame
        while (image.Frames.Count > 1)
            image.Frames.RemoveFrame(image.Frames.Count - 1);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(DisplaySize, DisplaySize),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));
        ApplyCircularMask(image);
        var result = await ToJpegBytesAsync(image);
        _logger.LogInformation("GIF processed: {Path} → {Size} bytes JPEG (first frame)", imagePath, result.Length);
        return result;
    }

    private static async Task<byte[]> ToJpegBytesAsync(Image<Rgba32> image)
    {
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 90 });
        return ms.ToArray();
    }

    private static Image<Rgba32> PrepareForDisplay(Image<Rgba32> source)
    {
        bool isSquarish = Math.Abs(source.Width - source.Height) <=
                          Math.Max(source.Width, source.Height) * 0.1f;
        if (!isSquarish)
        {
            return source.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(DisplaySize, DisplaySize),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));
        }

        const int MaxFitSize = (int)(DisplaySize * 0.875);
        float scale = Math.Min((float)MaxFitSize / source.Width, (float)MaxFitSize / source.Height);
        if (source.Width < 48 && scale > 3.0f) scale = 3.0f;

        int scaledW = Math.Max(1, (int)(source.Width  * scale));
        int scaledH = Math.Max(1, (int)(source.Height * scale));

        var canvas = new Image<Rgba32>(DisplaySize, DisplaySize);
        canvas.Mutate(ctx => ctx.BackgroundColor(Color.Black));
        using var scaled = source.Clone(ctx => ctx.Resize(scaledW, scaledH, KnownResamplers.Lanczos3));
        canvas.Mutate(ctx => ctx.DrawImage(scaled,
            new Point((DisplaySize - scaledW) / 2, (DisplaySize - scaledH) / 2), opacity: 1.0f));
        return canvas;
    }

    private static void ApplyCircularMask(Image<Rgba32> image)
    {
        var centerX = image.Width / 2;
        var centerY = image.Height / 2;
        var radius = Math.Min(centerX, centerY);

        image.Mutate(ctx =>
        {
            ctx.ProcessPixelRowsAsVector4((row, point) =>
            {
                for (int x = 0; x < row.Length; x++)
                {
                    var dx = point.X + x - centerX;
                    var dy = point.Y - centerY;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance > radius)
                        row[x].W = 0;
                }
            });
        });
    }

    private static Image<Rgba32> ConvertSkBitmapToImageSharp(SKBitmap bitmap)
    {
        var image = new Image<Rgba32>(bitmap.Width, bitmap.Height);
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                image[x, y] = new Rgba32(pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha);
            }
        return image;
    }

    public async Task<byte[]?> CreateTextImageAsync(string text, int fontSize = 24,
        Color? backgroundColor = null, Color? textColor = null)
    {
        try
        {
            using var image = new Image<Rgba32>(DisplaySize, DisplaySize);
            var bgColor = backgroundColor ?? Color.Black;
            var fgColor = textColor ?? Color.White;
            image.Mutate(x => x.BackgroundColor(bgColor));

            FontFamily fontFamily;
            try        { fontFamily = SystemFonts.Get("Arial"); }
            catch      { try { fontFamily = SystemFonts.Get("DejaVu Sans"); }
                         catch { fontFamily = SystemFonts.Families.Any()
                             ? SystemFonts.Families.First()
                             : throw new InvalidOperationException("No system fonts available"); } }

            var font = fontFamily.CreateFont(fontSize, FontStyle.Bold);
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(DisplaySize / 2, DisplaySize / 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                WrappingLength = DisplaySize - 20,
                TextAlignment = TextAlignment.Center
            };
            image.Mutate(x => x.DrawText(textOptions, text, fgColor));
            ApplyCircularMask(image);
            var result = await ToJpegBytesAsync(image);
            _logger.LogInformation("Text image created: {Text}, {Size} bytes JPEG", text, result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating text image");
            return null;
        }
    }
}
