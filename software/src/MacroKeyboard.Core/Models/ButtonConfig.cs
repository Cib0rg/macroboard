namespace MacroKeyboard.Core.Models;

/// <summary>
/// Конфигурация кнопки
/// </summary>
public class ButtonConfig
{
    /// <summary>
    /// ID кнопки (0-9)
    /// </summary>
    public byte ButtonId { get; set; }
    
    /// <summary>
    /// Конфигурация действия
    /// </summary>
    public ActionConfig? Action { get; set; }
    
    /// <summary>
    /// Путь к изображению кнопки
    /// </summary>
    public string? ImagePath { get; set; }
    
    /// <summary>
    /// Конфигурация LED
    /// </summary>
    public LedConfig Led { get; set; } = new();
    
    /// <summary>
    /// ID папки (для ACTION_TYPE_FOLDER)
    /// </summary>
    public byte FolderId { get; set; }
    
    /// <summary>
    /// Метаданные изображения (для внутреннего использования)
    /// </summary>
    public uint ImageOffset { get; set; }
    
    /// <summary>
    /// Размер изображения в байтах
    /// </summary>
    public uint ImageSize { get; set; }
    
    /// <summary>
    /// Формат изображения (0 = JPEG)
    /// </summary>
    public byte ImageFormat { get; set; }
    
    /// <summary>
    /// Проверить, настроена ли кнопка
    /// </summary>
    public bool IsConfigured => Action != null && Action.ActionType != ActionType.None;
}
