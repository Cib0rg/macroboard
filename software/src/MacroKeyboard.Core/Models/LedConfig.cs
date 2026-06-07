namespace MacroKeyboard.Core.Models;

/// <summary>
/// Конфигурация RGB LED для кнопки
/// </summary>
public class LedConfig
{
    /// <summary>
    /// Красный компонент (0-255)
    /// </summary>
    public byte R { get; set; }
    
    /// <summary>
    /// Зеленый компонент (0-255)
    /// </summary>
    public byte G { get; set; }
    
    /// <summary>
    /// Синий компонент (0-255)
    /// </summary>
    public byte B { get; set; }
    
    /// <summary>
    /// Яркость (0-100)
    /// </summary>
    public byte Brightness { get; set; } = 80;
    
    /// <summary>
    /// Эффект подсветки
    /// </summary>
    public LedEffect Effect { get; set; } = LedEffect.Static;
    
    /// <summary>
    /// Создать конфигурацию LED с заданным цветом
    /// </summary>
    public static LedConfig FromRgb(byte r, byte g, byte b, byte brightness = 80)
    {
        return new LedConfig
        {
            R = r,
            G = g,
            B = b,
            Brightness = brightness
        };
    }
    
    /// <summary>
    /// Создать конфигурацию LED из HEX цвета
    /// </summary>
    public static LedConfig FromHex(string hex, byte brightness = 80)
    {
        hex = hex.TrimStart('#');
        
        if (hex.Length != 6)
            throw new ArgumentException("Invalid hex color format", nameof(hex));
        
        var r = Convert.ToByte(hex.Substring(0, 2), 16);
        var g = Convert.ToByte(hex.Substring(2, 2), 16);
        var b = Convert.ToByte(hex.Substring(4, 2), 16);
        
        return FromRgb(r, g, b, brightness);
    }
    
    /// <summary>
    /// Конвертировать в HEX строку
    /// </summary>
    public string ToHex()
    {
        return $"#{R:X2}{G:X2}{B:X2}";
    }
}
