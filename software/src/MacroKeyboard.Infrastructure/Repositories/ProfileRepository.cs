using MacroKeyboard.Core.Models;
using MacroKeyboard.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MacroKeyboard.Infrastructure.Repositories;

/// <summary>
/// Репозиторий для работы с профилями
/// </summary>
public class ProfileRepository
{
    private readonly ILogger<ProfileRepository> _logger;
    private readonly string _profilesPath;
    
    public ProfileRepository(ILogger<ProfileRepository> logger)
    {
        _logger = logger;
        _profilesPath = AppDataManager.GetProfilesPath();
    }
    
    /// <summary>
    /// Получить все профили
    /// </summary>
    public async Task<List<Profile>> GetAllAsync()
    {
        try
        {
            var profiles = new List<Profile>();
            var files = Directory.GetFiles(_profilesPath, "profile_*.json");
            
            foreach (var file in files)
            {
                var profile = await LoadProfileFromFileAsync(file);
                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }
            
            // Сортировать по ID
            profiles.Sort((a, b) => a.ProfileId.CompareTo(b.ProfileId));
            
            return profiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading profiles");
            return new List<Profile>();
        }
    }
    
    /// <summary>
    /// Получить профиль по ID
    /// </summary>
    public async Task<Profile?> GetByIdAsync(byte profileId)
    {
        try
        {
            var filePath = GetProfileFilePath(profileId);
            
            if (!File.Exists(filePath))
                return null;
            
            return await LoadProfileFromFileAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading profile {ProfileId}", profileId);
            return null;
        }
    }
    
    /// <summary>
    /// Сохранить профиль
    /// </summary>
    public async Task<bool> SaveAsync(Profile profile)
    {
        try
        {
            profile.Touch();
            
            var filePath = GetProfileFilePath(profile.ProfileId);
            var json = JsonConvert.SerializeObject(profile, Formatting.Indented);
            
            await File.WriteAllTextAsync(filePath, json);
            
            _logger.LogInformation("Profile {ProfileId} saved", profile.ProfileId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving profile {ProfileId}", profile.ProfileId);
            return false;
        }
    }
    
    /// <summary>
    /// Удалить профиль
    /// </summary>
    public async Task<bool> DeleteAsync(byte profileId)
    {
        try
        {
            var filePath = GetProfileFilePath(profileId);
            
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Profile {ProfileId} deleted", profileId);
            }
            
            // Удалить изображения профиля
            var imagesPath = AppDataManager.GetProfileImagesPath(profileId);
            if (Directory.Exists(imagesPath))
            {
                Directory.Delete(imagesPath, recursive: true);
            }
            
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile {ProfileId}", profileId);
            return false;
        }
    }
    
    /// <summary>
    /// Проверить существование профиля
    /// </summary>
    public Task<bool> ExistsAsync(byte profileId)
    {
        var filePath = GetProfileFilePath(profileId);
        return Task.FromResult(File.Exists(filePath));
    }
    
    private string GetProfileFilePath(byte profileId)
    {
        return Path.Combine(_profilesPath, $"profile_{profileId}.json");
    }
    
    private async Task<Profile?> LoadProfileFromFileAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<Profile>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading profile from {FilePath}", filePath);
            return null;
        }
    }
}
