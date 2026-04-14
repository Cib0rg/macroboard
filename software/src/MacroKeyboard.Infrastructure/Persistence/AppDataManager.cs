namespace MacroKeyboard.Infrastructure.Persistence;

/// <summary>
/// Менеджер для работы с директориями AppData
/// </summary>
public static class AppDataManager
{
    private const string AppName = "MacroKeyboard";
    
    /// <summary>
    /// Получить базовую директорию приложения
    /// </summary>
    public static string GetAppDataPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appPath = Path.Combine(appData, AppName);
        
        if (!Directory.Exists(appPath))
        {
            Directory.CreateDirectory(appPath);
        }
        
        return appPath;
    }
    
    /// <summary>
    /// Получить директорию профилей
    /// </summary>
    public static string GetProfilesPath()
    {
        var path = Path.Combine(GetAppDataPath(), "Profiles");
        
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        
        return path;
    }
    
    /// <summary>
    /// Получить директорию изображений
    /// </summary>
    public static string GetImagesPath()
    {
        var path = Path.Combine(GetAppDataPath(), "Images");
        
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        
        return path;
    }
    
    /// <summary>
    /// Получить директорию изображений для профиля
    /// </summary>
    public static string GetProfileImagesPath(byte profileId)
    {
        var path = Path.Combine(GetImagesPath(), $"profile_{profileId}");
        
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        
        return path;
    }
    
    /// <summary>
    /// Получить директорию плагинов
    /// </summary>
    public static string GetPluginsPath()
    {
        var path = Path.Combine(GetAppDataPath(), "Plugins");
        
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        
        return path;
    }
    
    /// <summary>
    /// Получить директорию логов
    /// </summary>
    public static string GetLogsPath()
    {
        var path = Path.Combine(GetAppDataPath(), "Logs");
        
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        
        return path;
    }
    
    /// <summary>
    /// Получить путь к файлу настроек
    /// </summary>
    public static string GetSettingsFilePath()
    {
        return Path.Combine(GetAppDataPath(), "settings.json");
    }
}
