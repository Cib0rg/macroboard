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
    Folder = 0x04,
    
    /// <summary>
    /// Задержка (используется в последовательностях)
    /// </summary>
    Delay = 0x05,
    
    /// <summary>
    /// Shell-команда (выполняется на PC через Backend)
    /// </summary>
    Shell = 0x06,
    
    /// <summary>
    /// Последовательность действий (макс. 16 шагов)
    /// </summary>
    Sequence = 0x07,
    
    /// <summary>
    /// Запуск приложения (выполняется на PC через Backend)
    /// </summary>
    LaunchApp = 0x08,
    
    /// <summary>
    /// Медиа-клавиши (Consumer Control: громкость, mute и т.д.)
    /// </summary>
    Media = 0x09,

    /// <summary>
    /// Ночной режим — отключает LEDs и уменьшает яркость дисплеев до 0.
    /// Повторное нажатие восстанавливает предыдущие настройки.
    /// </summary>
    NightMode = 0x0A,

    /// <summary>
    /// Действие плагина — backend маршрутизирует нажатие в плагин по PluginId+ActionId.
    /// Firmware хранит конфиг как непрозрачный blob.
    /// </summary>
    Plugin = 0x0B
}
