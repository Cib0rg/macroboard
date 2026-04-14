namespace MacroKeyboard.Core.Models;

/// <summary>
/// Базовый класс для конфигурации действия кнопки
/// </summary>
public abstract class ActionConfig
{
    /// <summary>
    /// Тип действия
    /// </summary>
    public abstract ActionType ActionType { get; }
    
    /// <summary>
    /// Конвертировать в байтовый массив для отправки на устройство
    /// </summary>
    public abstract byte[] ToBytes();
}

/// <summary>
/// Конфигурация действия клавиатуры
/// </summary>
public class KeyboardAction : ActionConfig
{
    public override ActionType ActionType => ActionType.Keyboard;
    
    /// <summary>
    /// Модификаторы (Ctrl, Shift, Alt, GUI)
    /// </summary>
    public KeyModifiers Modifiers { get; set; }
    
    /// <summary>
    /// HID keycode основной клавиши
    /// </summary>
    public byte KeyCode { get; set; }
    
    /// <summary>
    /// Текст для печати (опционально)
    /// </summary>
    public string? Text { get; set; }
    
    public override byte[] ToBytes()
    {
        var data = new List<byte>();
        
        // Байт 0: Модификаторы
        data.Add((byte)Modifiers);
        
        // Байт 1-6: Keycodes (пока только один)
        data.Add(KeyCode);
        data.AddRange(new byte[5]); // Остальные keycodes пустые
        
        // Байт 7-N: Текст (если есть)
        if (!string.IsNullOrEmpty(Text))
        {
            var textBytes = System.Text.Encoding.UTF8.GetBytes(Text);
            data.AddRange(textBytes);
        }
        
        return data.ToArray();
    }
}

/// <summary>
/// Модификаторы клавиатуры
/// </summary>
[Flags]
public enum KeyModifiers : byte
{
    None = 0x00,
    LeftCtrl = 0x01,
    LeftShift = 0x02,
    LeftAlt = 0x04,
    LeftGui = 0x08,
    RightCtrl = 0x10,
    RightShift = 0x20,
    RightAlt = 0x40,
    RightGui = 0x80
}

/// <summary>
/// Конфигурация пользовательского HID действия
/// </summary>
public class CustomHidAction : ActionConfig
{
    public override ActionType ActionType => ActionType.CustomHid;
    
    /// <summary>
    /// Пользовательские данные HID report
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();
    
    public override byte[] ToBytes()
    {
        return Data;
    }
}

/// <summary>
/// Конфигурация переключения профиля
/// </summary>
public class ProfileSwitchAction : ActionConfig
{
    public override ActionType ActionType => ActionType.ProfileSwitch;
    
    /// <summary>
    /// ID профиля для переключения
    /// </summary>
    public byte TargetProfileId { get; set; }
    
    public override byte[] ToBytes()
    {
        return new[] { TargetProfileId };
    }
}
