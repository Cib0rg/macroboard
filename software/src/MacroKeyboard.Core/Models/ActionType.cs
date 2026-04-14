namespace MacroKeyboard.Core.Models;

/// <summary>
/// Типы действий кнопок (совместимо с прошивкой)
/// </summary>
public enum ActionType : byte
{
    /// <summary>
    /// Нет действия
    /// </summary>
    None = 0x00,
    
    /// <summary>
    /// Эмуляция клавиатуры
    /// </summary>
    Keyboard = 0x01,
    
    /// <summary>
    /// Пользовательский HID report
    /// </summary>
    CustomHid = 0x02,
    
    /// <summary>
    /// Переключение профиля
    /// </summary>
    ProfileSwitch = 0x03,
    
    /// <summary>
    /// Открытие папки
    /// </summary>
    Folder = 0x04
}
