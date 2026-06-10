using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Svg.Skia;
using SkiaSharp;

namespace MacroKeyboard.Infrastructure.Services;

/// <summary>
/// Сервис для обработки изображений для дисплеев кнопок.
/// Поддерживает форматы: JPEG, PNG, SVG, ICO, GIF.
/// Все изображения сжимаются до размера дисплея (160x160).
/// </summary>
public class ImageService
{
    private readonly ILogger<ImageService> _logger;
    
    /// <summary>
    /// Target display resolution (GC9A01 round LCD)
    /// </summary>
    public const int DisplaySize = 160;
    
    /// <summary>
    /// Maximum GIF frame count (to keep file size reasonable for device transfer)
    /// </summary>
    public const int MaxGifFrames = 16;
    
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
    /// Поддерживает: JPEG, PNG, SVG, ICO, GIF.
    /// Результат: 160x160 JPEG (или GIF для анимаций).
    /// </summary>
    public async Task<byte[]?> ProcessImageForButtonAsync(string imagePath)
    {
        try
        {
            var ext = Path.GetExtension(imagePath).ToLowerInvariant();
            
            return ext switch
            {
                ".svg" => await ProcessSvgAsync(imagePath),
                ".gif" => await ProcessGifAsync(imagePath),
                ".ico" => await ProcessIcoAsync(imagePath),
                _ => await ProcessRasterImageAsync(imagePath) // jpg, jpeg, png, bmp
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image {ImagePath}", imagePath);
            return null;
        }
    }
    
    /// <summary>
    /// Process raster images (JPEG, PNG, BMP) — resize to display size and convert to JPEG
    /// </summary>
    private async Task<byte[]> ProcessRasterImageAsync(string imagePath)
    {
        using var source = await Image.LoadAsync<Rgba32>(imagePath);
        using var image = PrepareForDisplay(source);

        // Apply circular mask for round display
        ApplyCircularMask(image);

        // Convert to JPEG
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85 });

        var result = ms.ToArray();
        _logger.LogInformation("Raster image processed: {Path} → {Size} bytes ({W}x{H})",
            imagePath, result.Length, DisplaySize, DisplaySize);
        return result;
    }
    
    /// <summary>
    /// Process SVG images — render to bitmap at display size, then convert to JPEG
    /// </summary>
    private async Task<byte[]> ProcessSvgAsync(string imagePath)
    {
        var svgContent = await File.ReadAllTextAsync(imagePath);
        
        using var svg = new SKSvg();
        svg.FromSvg(svgContent);
        
        if (svg.Picture == null)
            throw new InvalidOperationException($"Failed to parse SVG: {imagePath}");
        
        // Render SVG to bitmap at display size
        using var bitmap = new SKBitmap(DisplaySize, DisplaySize);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        
        // Scale SVG to fit display
        var bounds = svg.Picture.CullRect;
        var scaleX = DisplaySize / bounds.Width;
        var scaleY = DisplaySize / bounds.Height;
        var scale = Math.Min(scaleX, scaleY);
        
        var offsetX = (DisplaySize - bounds.Width * scale) / 2;
        var offsetY = (DisplaySize - bounds.Height * scale) / 2;
        
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale);
        canvas.DrawPicture(svg.Picture);
        
        // Convert SKBitmap to ImageSharp Image
        using var image = ConvertSkBitmapToImageSharp(bitmap);
        
        // Apply circular mask
        ApplyCircularMask(image);
        
        // Convert to JPEG
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85 });
        
        var result = ms.ToArray();
        _logger.LogInformation("SVG image processed: {Path} → {Size} bytes", imagePath, result.Length);
        return result;
    }
    
    /// <summary>
    /// Process ICO files — extract the largest icon and resize
    /// </summary>
    private async Task<byte[]> ProcessIcoAsync(string imagePath)
    {
        // ImageSharp can load ICO files directly (they contain embedded BMP/PNG)
        using var source = await Image.LoadAsync<Rgba32>(imagePath);
        using var image = PrepareForDisplay(source);

        // Apply circular mask
        ApplyCircularMask(image);

        // Convert to JPEG
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85 });

        var result = ms.ToArray();
        _logger.LogInformation("ICO image processed: {Path} → {Size} bytes", imagePath, result.Length);
        return result;
    }
    
    /// <summary>
    /// Process GIF files — resize all frames to display size, limit frame count.
    /// Short GIFs are kept as animated GIF; long ones use only the first frame.
    /// </summary>
    private async Task<byte[]> ProcessGifAsync(string imagePath)
    {
        using var image = await Image.LoadAsync<Rgba32>(imagePath);
        
        var frameCount = image.Frames.Count;
        _logger.LogInformation("GIF loaded: {Path}, {Frames} frames", imagePath, frameCount);
        
        if (frameCount <= 1)
        {
            // Static GIF — treat as regular raster image
            ResizeToDisplaySize(image);
            ApplyCircularMask(image);
            
            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85 });
            return ms.ToArray();
        }
        
        // Animated GIF — resize all frames, limit count
        image.Mutate(x => x.Resize(DisplaySize, DisplaySize));
        
        // Remove excess frames if too many
        while (image.Frames.Count > MaxGifFrames)
        {
            image.Frames.RemoveFrame(image.Frames.Count - 1);
        }
        
        // Apply circular mask to each frame
        for (int i = 0; i < image.Frames.Count; i++)
        {
            ApplyCircularMaskToFrame(image, i);
        }
        
        // Save as GIF (animated)
        using var ms2 = new MemoryStream();
        await image.SaveAsGifAsync(ms2, new GifEncoder
        {
            ColorTableMode = GifColorTableMode.Local
        });
        
        var result = ms2.ToArray();
        _logger.LogInformation("Animated GIF processed: {Path} → {Size} bytes, {Frames} frames", 
            imagePath, result.Length, image.Frames.Count);
        return result;
    }
    
    /// <summary>
    /// Resize a source image to display size and return the result as a new image.
    /// Small icons (< 64 px) are centered on a black canvas instead of stretched.
    /// Large images are cropped to fill 160×160.
    /// Caller owns the returned image and must dispose it.
    /// </summary>
    private static Image<Rgba32> PrepareForDisplay(Image<Rgba32> source)
    {
        if (source.Width < 64 || source.Height < 64)
        {
            // Small icon: scale to at most 60 % of display, then center on black canvas.
            // Avoids the ugly 5× upscale that ResizeMode.Crop would produce for a 32×32 icon.
            const int MaxIconSize = (int)(DisplaySize * 0.6); // 96 px
            float scale = Math.Min((float)MaxIconSize / source.Width, (float)MaxIconSize / source.Height);
            int scaledW = Math.Max(1, (int)(source.Width  * scale));
            int scaledH = Math.Max(1, (int)(source.Height * scale));

            var canvas = new Image<Rgba32>(DisplaySize, DisplaySize);
            canvas.Mutate(ctx => ctx.BackgroundColor(Color.Black));

            using var scaled = source.Clone(ctx => ctx.Resize(scaledW, scaledH, KnownResamplers.Lanczos3));
            int ox = (DisplaySize - scaledW) / 2;
            int oy = (DisplaySize - scaledH) / 2;
            canvas.Mutate(ctx => ctx.DrawImage(scaled, new Point(ox, oy), opacity: 1.0f));

            return canvas;
        }

        // Regular image: crop-fill to 160×160.
        return source.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(DisplaySize, DisplaySize),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));
    }

    /// <summary>
    /// Resize image to display size in-place (square, cover mode). Used for GIF frames.
    /// </summary>
    private static void ResizeToDisplaySize(Image<Rgba32> image)
    {
        // Resize maintaining aspect ratio to cover the display area, then crop center
        var resizeOptions = new ResizeOptions
        {
            Size = new Size(DisplaySize, DisplaySize),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        };
        image.Mutate(x => x.Resize(resizeOptions));
    }
    
    /// <summary>
    /// Apply circular mask to the entire image (for round display)
    /// </summary>
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
                    {
                        row[x].W = 0; // Make pixel transparent
                    }
                }
            });
        });
    }
    
    /// <summary>
    /// Apply circular mask to a specific frame of an animated image
    /// </summary>
    private static void ApplyCircularMaskToFrame(Image<Rgba32> image, int frameIndex)
    {
        var frame = image.Frames[frameIndex];
        var centerX = frame.Width / 2;
        var centerY = frame.Height / 2;
        var radius = Math.Min(centerX, centerY);
        
        frame.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var dx = x - centerX;
                    var dy = y - centerY;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    
                    if (distance > radius)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);
                    }
                }
            }
        });
    }
    
    /// <summary>
    /// Convert SkiaSharp SKBitmap to ImageSharp Image
    /// </summary>
    private static Image<Rgba32> ConvertSkBitmapToImageSharp(SKBitmap bitmap)
    {
        var image = new Image<Rgba32>(bitmap.Width, bitmap.Height);
        
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                image[x, y] = new Rgba32(pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha);
            }
        }
        
        return image;
    }
    
    /// <summary>
    /// Создать изображение с текстом
    /// </summary>
    public async Task<byte[]?> CreateTextImageAsync(string text, int fontSize = 24, Color? backgroundColor = null, Color? textColor = null)
    {
        try
        {
            using var image = new Image<Rgba32>(DisplaySize, DisplaySize);
            
            // Заполнить фоном
            var bgColor = backgroundColor ?? Color.Black;
            var fgColor = textColor ?? Color.White;
            
            image.Mutate(x => x.BackgroundColor(bgColor));
            
            // Загрузить системный шрифт
            FontFamily fontFamily;
            
            try
            {
                fontFamily = SystemFonts.Get("Arial");
            }
            catch
            {
                try
                {
                    fontFamily = SystemFonts.Get("DejaVu Sans");
                }
                catch
                {
                    if (!SystemFonts.Families.Any())
                    {
                        throw new InvalidOperationException("No system fonts available");
                    }
                    fontFamily = SystemFonts.Families.First();
                }
            }
            
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
            
            // Apply circular mask
            ApplyCircularMask(image);
            
            // Convert to JPEG
            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85 });
            
            var result = ms.ToArray();
            _logger.LogInformation("Text image created: {Text}, {Size} bytes", text, result.Length);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating text image");
            return null;
        }
    }
}
