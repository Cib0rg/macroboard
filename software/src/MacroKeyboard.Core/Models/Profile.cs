using Newtonsoft.Json;

namespace MacroKeyboard.Core.Models;

/// <summary>
/// Профиль с конфигурацией кнопок
/// </summary>
public class Profile
{
    /// <summary>
    /// ID профиля (0-4)
    /// </summary>
    public byte ProfileId { get; set; }
    
    /// <summary>
    /// Название профиля
    /// </summary>
    public string Name { get; set; } = "New Profile";
    
    /// <summary>
    /// Конфигурация кнопок (10 кнопок)
    /// </summary>
    public List<ButtonConfig> Buttons { get; set; } = new();
    
    /// <summary>
    /// Папки
    /// </summary>
    public List<Folder> Folders { get; set; } = new();
    
    /// <summary>
    /// Конфигурация энкодера (поворот CW/CCW + нажатие)
    /// </summary>
    public EncoderConfig Encoder { get; set; } = new();
    
    /// <summary>
    /// Дата создания
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Дата последнего изменения
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// CRC32 для проверки целостности
    /// </summary>
    [JsonIgnore]
    public uint Crc32 { get; set; }
    
    /// <summary>
    /// Создать новый профиль с пустыми кнопками
    /// </summary>
    public static Profile CreateEmpty(byte profileId, string name)
    {
        var profile = new Profile
        {
            ProfileId = profileId,
            Name = name
        };
        
        // Создать 10 пустых кнопок
        for (byte i = 0; i < 10; i++)
        {
            profile.Buttons.Add(new ButtonConfig
            {
                ButtonId = i,
                Action = null,
                Led = LedConfig.FromRgb(100, 100, 100) // Серый по умолчанию
            });
        }
        
        return profile;
    }
    
    /// <summary>
    /// Обновить время модификации
    /// </summary>
    public void Touch()
    {
        ModifiedAt = DateTime.Now;
    }
}

/// <summary>
/// Папка с кнопками
/// </summary>
public class Folder
{
    public byte FolderId { get; set; }
    public string Name { get; set; } = "Folder";
    public List<ButtonConfig> Buttons { get; set; } = new();
}

/// <summary>
/// Конфигурация поворотного энкодера.
/// Каждое из трёх событий (CW, CCW, нажатие) может иметь своё действие.
/// По умолчанию: CW = следующий профиль, CCW = предыдущий профиль, нажатие = профиль 0.
/// </summary>
public class EncoderConfig
{
    /// <summary>
    /// Действие при повороте по часовой стрелке
    /// </summary>
    public ActionConfig? RotateCwAction { get; set; }
    
    /// <summary>
    /// Действие при повороте против часовой стрелки
    /// </summary>
    public ActionConfig? RotateCcwAction { get; set; }
    
    /// <summary>
    /// Действие при коротком нажатии кнопки энкодера
    /// </summary>
    public ActionConfig? PressAction { get; set; }

    /// <summary>
    /// Действие при долгом нажатии кнопки энкодера
    /// </summary>
    public ActionConfig? LongPressAction { get; set; }
}
