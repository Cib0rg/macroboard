namespace MacroKeyboard.Core.Models;

/// <summary>
/// Эффекты RGB LED (совместимо с прошивкой)
/// </summary>
public enum LedEffect : byte
{
    /// <summary>
    /// Статичный цвет
    /// </summary>
    Static = 0x00,
    
    /// <summary>
    /// Эффект дыхания
    /// </summary>
    Breathing = 0x01,
    
    /// <summary>
    /// Радуга
    /// </summary>
    Rainbow = 0x02,
    
    /// <summary>
    /// Волна
    /// </summary>
    Wave = 0x03
}
