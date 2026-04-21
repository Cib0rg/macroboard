using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace MacroKeyboard.Infrastructure.Services;

/// <summary>
/// Сервис для обработки изображений
/// </summary>
public class ImageService
{
    private readonly ILogger<ImageService> _logger;
    private const int TargetSize = 160;
    
    public ImageService(ILogger<ImageService> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Загрузить и обработать изображение для кнопки
    /// </summary>
    public async Task<byte[]?> ProcessImageForButtonAsync(string imagePath)
    {
        try
        {
            using var image = await Image.LoadAsync(imagePath);
            
            // Изменить размер до 160x160
            image.Mutate(x => x.Resize(TargetSize, TargetSize));
            
            // Применить круглую маску
            ApplyCircularMask(image);
            
            // Конвертировать в JPEG
            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 90 });
            
            var result = ms.ToArray();
            _logger.LogInformation("Image processed: {Size} bytes", result.Length);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image {ImagePath}", imagePath);
            return null;
        }
    }
    
    /// <summary>
    /// Применить круглую маску к изображению
    /// </summary>
    private void ApplyCircularMask(Image image)
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
                        // Сделать пиксель прозрачным
                        row[x].W = 0;
                    }
                }
            });
        });
    }
    
    /// <summary>
    /// Создать изображение с текстом
    /// </summary>
    public async Task<byte[]?> CreateTextImageAsync(string text, int fontSize = 24, Color? backgroundColor = null, Color? textColor = null)
    {
        try
        {
            using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(TargetSize, TargetSize);
            
            // Заполнить фоном
            var bgColor = backgroundColor ?? Color.Black;
            var fgColor = textColor ?? Color.White;
            
            image.Mutate(x => x.BackgroundColor(bgColor));
            
            // Загрузить системный шрифт
            FontFamily fontFamily;
            
            try
            {
                // Попытаться загрузить системные шрифты
                fontFamily = SystemFonts.Get("Arial");
            }
            catch
            {
                try
                {
                    // Fallback на другие распространенные шрифты
                    fontFamily = SystemFonts.Get("DejaVu Sans");
                }
                catch
                {
                    // Использовать первый доступный шрифт
                    if (!SystemFonts.Families.Any())
                    {
                        throw new InvalidOperationException("No system fonts available");
                    }
                    fontFamily = SystemFonts.Families.First();
                }
            }
            
            var font = fontFamily.CreateFont(fontSize, FontStyle.Bold);
            
            // Настройки рендеринга текста
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(TargetSize / 2, TargetSize / 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                WrappingLength = TargetSize - 20, // Отступы по краям
                TextAlignment = TextAlignment.Center
            };
            
            // Нарисовать текст
            image.Mutate(x => x.DrawText(textOptions, text, fgColor));
            
            // Применить круглую маску
            ApplyCircularMask(image);
            
            // Конвертировать в JPEG
            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 90 });
            
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
