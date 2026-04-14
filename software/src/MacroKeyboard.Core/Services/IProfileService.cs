using MacroKeyboard.Core.Models;

namespace MacroKeyboard.Core.Services;

/// <summary>
/// Сервис для управления профилями
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Получить все профили
    /// </summary>
    Task<List<Profile>> GetAllProfilesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Получить профиль по ID
    /// </summary>
    Task<Profile?> GetProfileAsync(byte profileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Создать новый профиль
    /// </summary>
    Task<Profile> CreateProfileAsync(string name, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Обновить профиль
    /// </summary>
    Task<bool> UpdateProfileAsync(Profile profile, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Удалить профиль
    /// </summary>
    Task<bool> DeleteProfileAsync(byte profileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Дублировать профиль
    /// </summary>
    Task<Profile> DuplicateProfileAsync(byte profileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Отправить профиль на устройство
    /// </summary>
    Task<bool> SendProfileToDeviceAsync(Profile profile, IProgress<int>? progress = null, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Загрузить профиль с устройства
    /// </summary>
    Task<Profile?> LoadProfileFromDeviceAsync(byte profileId, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Экспортировать профиль в файл
    /// </summary>
    Task<bool> ExportProfileAsync(Profile profile, string filePath, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Импортировать профиль из файла
    /// </summary>
    Task<Profile?> ImportProfileAsync(string filePath, 
        CancellationToken cancellationToken = default);
}
