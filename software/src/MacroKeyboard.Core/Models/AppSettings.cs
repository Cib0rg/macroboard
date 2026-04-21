namespace MacroKeyboard.Core.Models;

/// <summary>
/// Настройки приложения
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Автозапуск при старте системы
    /// </summary>
    public bool AutoStart { get; set; }
    
    /// <summary>
    /// Сворачивать в трей при закрытии
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;
    
    /// <summary>
    /// Показывать уведомления
    /// </summary>
    public bool ShowNotifications { get; set; } = true;
    
    /// <summary>
    /// Порт IPC сервера
    /// </summary>
    public int IpcPort { get; set; } = 28195;
    
    /// <summary>
    /// Порт WebSocket сервера для плагинов
    /// </summary>
    public int WebSocketPort { get; set; } = 28196;
    
    /// <summary>
    /// Директория плагинов
    /// </summary>
    public string PluginsDirectory { get; set; } = "Plugins";
    
    /// <summary>
    /// Язык интерфейса
    /// </summary>
    public string Language { get; set; } = "en";
    
    /// <summary>
    /// Тема оформления (Light/Dark/System)
    /// </summary>
    public string Theme { get; set; } = "System";
}
