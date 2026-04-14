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
    /// Папки (для будущего расширения)
    /// </summary>
    public List<Folder> Folders { get; set; } = new();
    
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
/// Папка с кнопками (для будущего расширения)
/// </summary>
public class Folder
{
    public byte FolderId { get; set; }
    public string Name { get; set; } = "Folder";
    public List<ButtonConfig> Buttons { get; set; } = new();
}
