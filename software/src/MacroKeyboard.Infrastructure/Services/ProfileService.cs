using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Repositories;
using MacroKeyboard.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Infrastructure.Services;

/// <summary>
/// Реализация сервиса для управления профилями
/// </summary>
public class ProfileService : IProfileService
{
    private readonly ProfileRepository _repository;
    private readonly IDeviceService _deviceService;
    private readonly ImageService _imageService;
    private readonly ILogger<ProfileService> _logger;
    
    public ProfileService(
        ProfileRepository repository,
        IDeviceService deviceService,
        ImageService imageService,
        ILogger<ProfileService> logger)
    {
        _repository = repository;
        _deviceService = deviceService;
        _imageService = imageService;
        _logger = logger;
    }
    
    public async Task<List<Profile>> GetAllProfilesAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetAllAsync();
    }
    
    public async Task<Profile?> GetProfileAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        return await _repository.GetByIdAsync(profileId);
    }
    
    public async Task<Profile> CreateProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        // Найти свободный ID
        var existingProfiles = await _repository.GetAllAsync();
        byte profileId = 0;
        
        for (byte i = 0; i < 5; i++)
        {
            if (!existingProfiles.Any(p => p.ProfileId == i))
            {
                profileId = i;
                break;
            }
        }
        
        var profile = Profile.CreateEmpty(profileId, name);
        await _repository.SaveAsync(profile);
        
        _logger.LogInformation("Profile {ProfileId} created: {Name}", profileId, name);
        
        return profile;
    }
    
    public async Task<bool> UpdateProfileAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        return await _repository.SaveAsync(profile);
    }
    
    public async Task<bool> DeleteProfileAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        return await _repository.DeleteAsync(profileId);
    }
    
    public async Task<Profile> DuplicateProfileAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        var source = await _repository.GetByIdAsync(profileId);
        if (source == null)
            throw new InvalidOperationException($"Profile {profileId} not found");
        
        var duplicate = await CreateProfileAsync($"{source.Name} (Copy)", cancellationToken);
        
        // Копировать кнопки
        for (int i = 0; i < source.Buttons.Count; i++)
        {
            duplicate.Buttons[i].Action = source.Buttons[i].Action;
            duplicate.Buttons[i].Led = source.Buttons[i].Led;
            
            // Копировать изображение
            if (!string.IsNullOrEmpty(source.Buttons[i].ImagePath) && File.Exists(source.Buttons[i].ImagePath))
            {
                var sourceImagePath = source.Buttons[i].ImagePath;
                var targetImagePath = Path.Combine(
                    AppDataManager.GetProfileImagesPath(duplicate.ProfileId),
                    $"button_{i}.jpg");
                
                File.Copy(sourceImagePath, targetImagePath, overwrite: true);
                duplicate.Buttons[i].ImagePath = targetImagePath;
            }
        }
        
        await _repository.SaveAsync(duplicate);
        
        return duplicate;
    }
    
    public async Task<bool> SendProfileToDeviceAsync(
        Profile profile, 
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sending profile {ProfileId} to device", profile.ProfileId);
            
            // 1. Установить профиль
            var profileSet = await _deviceService.SetProfileAsync(profile.ProfileId, cancellationToken);
            if (!profileSet)
            {
                _logger.LogError("Failed to set profile");
                return false;
            }
            
            progress?.Report(10);
            
            // 2. Отправить конфигурацию каждой кнопки
            for (int i = 0; i < profile.Buttons.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;
                
                var button = profile.Buttons[i];
                
                // Отправить изображение
                if (!string.IsNullOrEmpty(button.ImagePath) && File.Exists(button.ImagePath))
                {
                    var imageData = await File.ReadAllBytesAsync(button.ImagePath, cancellationToken);
                    var imageProgress = new Progress<int>(p => 
                        progress?.Report(10 + (i * 70 / profile.Buttons.Count) + (p * 70 / profile.Buttons.Count / 100)));
                    
                    var imageSent = await _deviceService.SendButtonImageAsync(
                        profile.ProfileId,
                        button.ButtonId,
                        imageData,
                        imageProgress,
                        cancellationToken);
                    
                    if (!imageSent)
                    {
                        _logger.LogWarning("Failed to send image for button {ButtonId}", button.ButtonId);
                    }
                }
                
                // Отправить действие
                if (button.Action != null)
                {
                    await _deviceService.SetButtonActionAsync(
                        profile.ProfileId,
                        button.ButtonId,
                        button.Action,
                        cancellationToken);
                }
                
                // Отправить LED
                await _deviceService.SetLedColorAsync(
                    profile.ProfileId,
                    button.ButtonId,
                    button.Led,
                    cancellationToken);
                
                progress?.Report(10 + ((i + 1) * 70 / profile.Buttons.Count));
            }
            
            // 3. Сохранить профиль на устройстве
            var saved = await _deviceService.SaveProfileAsync(profile.ProfileId, cancellationToken);
            
            progress?.Report(100);
            
            _logger.LogInformation("Profile {ProfileId} sent successfully", profile.ProfileId);
            
            return saved;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending profile to device");
            return false;
        }
    }
    
    public Task<Profile?> LoadProfileFromDeviceAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        // TODO: Реализовать загрузку профиля с устройства
        throw new NotImplementedException("Loading profile from device is not yet implemented");
    }
    
    public async Task<bool> ExportProfileAsync(Profile profile, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(profile, Newtonsoft.Json.Formatting.Indented);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);
            
            _logger.LogInformation("Profile {ProfileId} exported to {FilePath}", profile.ProfileId, filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting profile");
            return false;
        }
    }
    
    public async Task<Profile?> ImportProfileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var profile = Newtonsoft.Json.JsonConvert.DeserializeObject<Profile>(json);
            
            if (profile == null)
                return null;
            
            // Найти свободный ID
            var existingProfiles = await _repository.GetAllAsync();
            for (byte i = 0; i < 5; i++)
            {
                if (!existingProfiles.Any(p => p.ProfileId == i))
                {
                    profile.ProfileId = i;
                    break;
                }
            }
            
            await _repository.SaveAsync(profile);
            
            _logger.LogInformation("Profile imported from {FilePath}", filePath);
            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing profile");
            return null;
        }
    }
}
