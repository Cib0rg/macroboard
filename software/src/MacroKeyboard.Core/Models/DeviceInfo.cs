namespace MacroKeyboard.Core.Models;

/// <summary>
/// Информация об устройстве
/// </summary>
public class DeviceInfo
{
    /// <summary>
    /// Уникальный ID устройства (UUID)
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Версия прошивки
    /// </summary>
    public Version FirmwareVersion { get; set; } = new Version(1, 0, 0);
    
    /// <summary>
    /// Количество кнопок
    /// </summary>
    public byte ButtonCount { get; set; } = 10;
    
    /// <summary>
    /// Количество профилей
    /// </summary>
    public byte ProfileCount { get; set; } = 5;
    
    /// <summary>
    /// Текущий активный профиль
    /// </summary>
    public byte CurrentProfile { get; set; }
    
    /// <summary>
    /// Свободное место в flash памяти (байты)
    /// </summary>
    public uint FreeSpace { get; set; }
    
    /// <summary>
    /// Время работы устройства (секунды)
    /// </summary>
    public uint Uptime { get; set; }
    
    /// <summary>
    /// Устройство подключено
    /// </summary>
    public bool IsConnected { get; set; }
}
