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

    public byte ActiveProfileId { get; private set; }
    
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
        
        if (existingProfiles.Count >= 5)
        {
            throw new InvalidOperationException(
                "Maximum number of profiles (5) reached. Delete an existing profile before creating a new one.");
        }
        
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
            _logger.LogInformation("Sending profile {ProfileId} ({Name}) to device, {ButtonCount} buttons",
                profile.ProfileId, profile.Name, profile.Buttons.Count);
            
            for (int b = 0; b < profile.Buttons.Count; b++)
            {
                var btn = profile.Buttons[b];
                _logger.LogInformation("  Button {Id}: Action={Action}, ImagePath={ImagePath}",
                    btn.ButtonId, btn.Action?.ActionType, btn.ImagePath ?? "(null)");
            }
            
            ActiveProfileId = profile.ProfileId;

            // 1. Установить профиль (device always uses slot 0)
            var profileSet = await _deviceService.SetProfileAsync(0, cancellationToken);
            if (!profileSet)
            {
                _logger.LogError("Failed to set profile");
                return false;
            }

            progress?.Report(10);

            // 2a. Resolve missing LaunchApp icons from cache (happens when profile JSON
            //     was saved before icon extraction, e.g. after a profile reset)
            foreach (var btn in profile.Buttons)
            {
                if (!string.IsNullOrEmpty(btn.ImagePath)) continue;
                if (btn.Action is not MacroKeyboard.Core.Models.LaunchAppAction la) continue;
                if (string.IsNullOrEmpty(la.ExecutablePath)) continue;

                var iconDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MacroKeyboard", "icons");
                var iconPath = Path.Combine(iconDir,
                    Path.GetFileNameWithoutExtension(la.ExecutablePath) + ".png");
                if (File.Exists(iconPath))
                {
                    btn.ImagePath = iconPath;
                    _logger.LogInformation(
                        "Resolved missing icon for button {ButtonId} from cache: {Path}",
                        btn.ButtonId, iconPath);
                }
            }

            // 2. Отправить конфигурацию каждой кнопки
            for (int i = 0; i < profile.Buttons.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;

                var button = profile.Buttons[i];

                // Отправить изображение (обработать через ImageService → 160x160 JPEG)
                var ringColor = button.Action switch
                {
                    FolderAction       => (SixLabors.ImageSharp.Color?)SixLabors.ImageSharp.Color.FromRgb(0xFF, 0xA5, 0x00),
                    PluginActionConfig => (SixLabors.ImageSharp.Color?)SixLabors.ImageSharp.Color.FromRgb(0x8B, 0x5C, 0xF6),
                    _                  => null
                };

                if (!string.IsNullOrEmpty(button.ImagePath) && File.Exists(button.ImagePath))
                {
                    _logger.LogInformation("Processing image for button {ButtonId}: {Path}",
                        button.ButtonId, button.ImagePath);

                    var processedImage = await _imageService.ProcessImageForButtonAsync(button.ImagePath, ringColor);
                    if (processedImage != null && processedImage.Length > 0)
                    {
                        var imageProgress = new Progress<int>(p =>
                            progress?.Report(10 + (i * 70 / profile.Buttons.Count) + (p * 70 / profile.Buttons.Count / 100)));

                        _logger.LogInformation("Sending processed image for button {ButtonId}: {Size} bytes (JPEG)",
                            button.ButtonId, processedImage.Length);

                        var imageSent = await _deviceService.SendButtonImageAsync(
                            0,
                            button.ButtonId,
                            processedImage,
                            imageProgress,
                            cancellationToken);

                        if (!imageSent)
                        {
                            _logger.LogWarning("Failed to send image for button {ButtonId}", button.ButtonId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to process image for button {ButtonId}: {Path}",
                            button.ButtonId, button.ImagePath);
                    }
                }
                else if (!string.IsNullOrEmpty(button.ImagePath))
                {
                    _logger.LogWarning("Image file not found for button {ButtonId}: {Path}",
                        button.ButtonId, button.ImagePath);
                }
                else if (button.Action == null || button.Action is NoneAction)
                {
                    // Explicitly clear the display so a previously-sent image doesn't persist.
                    var blank = await _imageService.CreateBlankImageAsync();
                    await _deviceService.SendButtonImageAsync(0, button.ButtonId, blank, null, cancellationToken);
                }
                else if (ringColor.HasValue)
                {
                    // No custom image but this button type needs a ring indicator —
                    // send a black circle with text + ring so the device shows both.
                    var placeholderLabel = !string.IsNullOrEmpty(button.Name) ? button.Name
                        : button.Action switch
                        {
                            FolderAction       => "Folder",
                            PluginActionConfig pa when !string.IsNullOrEmpty(pa.ActionName) => pa.ActionName,
                            PluginActionConfig => "Plugin",
                            _                  => null
                        };
                    _logger.LogInformation("Sending ring placeholder for button {ButtonId} label='{Label}'",
                        button.ButtonId, placeholderLabel);
                    var placeholder = await _imageService.CreateRingPlaceholderAsync(ringColor.Value, placeholderLabel);
                    await _deviceService.SendButtonImageAsync(0, button.ButtonId, placeholder, null, cancellationToken);
                }

                // Отправить действие (null → NoneAction чтобы очистить предыдущую конфигурацию на устройстве)
                var actionToSend = button.Action ?? new NoneAction();
                if (actionToSend is MacroKeyboard.Core.Models.KeyboardAction ka &&
                    ka.KeyCode == 0 &&
                    System.Text.Encoding.UTF8.GetByteCount(ka.Text ?? "") > 44)
                {
                    await _deviceService.SetButtonLongTextAsync(
                        0, 0xFF, button.ButtonId, ka.Text!,
                        null, cancellationToken);
                }
                else
                {
                    await _deviceService.SetButtonActionAsync(
                        0, button.ButtonId, actionToSend, cancellationToken);
                }

                // Отправить имя кнопки (пустая строка — firmware сгенерирует имя из типа команды)
                await _deviceService.SetButtonNameAsync(
                    0,
                    button.ButtonId,
                    button.Name ?? string.Empty,
                    cancellationToken);

                // Отправить LED
                await _deviceService.SetLedColorAsync(
                    0,
                    button.ButtonId,
                    button.Led,
                    cancellationToken);

                // Отправить действие long press
                await _deviceService.SetButtonLongPressActionAsync(
                    0, button.ButtonId, button.LongPressAction,
                    cancellationToken: cancellationToken);

                // Отправить имя long press (пустая строка — firmware сгенерирует из типа действия)
                await _deviceService.SetButtonLongPressNameAsync(
                    0, button.ButtonId, button.LongPressName ?? string.Empty,
                    cancellationToken: cancellationToken);

                progress?.Report(10 + ((i + 1) * 70 / profile.Buttons.Count));

                // Small delay between buttons to let firmware process commands
                await Task.Delay(50, cancellationToken);
            }

            // 3. Отправить кнопки внутри папок
            foreach (var folder in profile.Folders)
            {
                for (int i = 0; i < 10; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return false;

                    var btn = folder.Buttons.Count > i ? folder.Buttons[i] : null;
                    byte btnId = btn != null ? btn.ButtonId : (byte)i;

                    var folderAction = btn?.Action ?? new NoneAction();
                    if (folderAction is MacroKeyboard.Core.Models.KeyboardAction fka &&
                        fka.KeyCode == 0 &&
                        System.Text.Encoding.UTF8.GetByteCount(fka.Text ?? "") > 44)
                    {
                        await _deviceService.SetButtonLongTextAsync(
                            0, folder.FolderId, btnId, fka.Text!,
                            null, cancellationToken);
                    }
                    else
                    {
                        await _deviceService.SetFolderButtonActionAsync(
                            0, folder.FolderId, btnId, folderAction, cancellationToken);
                    }

                    await _deviceService.SetFolderButtonNameAsync(
                        0, folder.FolderId, btnId,
                        btn?.Name ?? string.Empty, cancellationToken);

                    var led = btn?.Led ?? LedConfig.FromRgb(80, 80, 80);
                    await _deviceService.SetFolderButtonLedAsync(
                        0, folder.FolderId, btnId, led, cancellationToken);

                    await _deviceService.SetButtonLongPressActionAsync(
                        0, btnId, btn?.LongPressAction, folder.FolderId, cancellationToken);

                    await _deviceService.SetButtonLongPressNameAsync(
                        0, btnId, btn?.LongPressName ?? string.Empty, folder.FolderId, cancellationToken);

                    // Send image for folder button (if configured)
                    if (btn != null && !string.IsNullOrEmpty(btn.ImagePath) && File.Exists(btn.ImagePath))
                    {
                        var processedImage = await _imageService.ProcessImageForButtonAsync(btn.ImagePath, null);
                        if (processedImage != null && processedImage.Length > 0)
                        {
                            await _deviceService.SendFolderButtonImageAsync(
                                0, folder.FolderId, btnId, processedImage, null, cancellationToken);
                        }
                    }

                    await Task.Delay(50, cancellationToken);
                }
            }

            // 4. Отправить конфигурацию энкодера
            var enc = profile.Encoder;
            await _deviceService.SetEncoderActionAsync(0, enc?.RotateCwAction, cancellationToken);
            await _deviceService.SetEncoderActionAsync(1, enc?.RotateCcwAction, cancellationToken);
            await _deviceService.SetEncoderActionAsync(2, enc?.PressAction, cancellationToken);
            await _deviceService.SetEncoderActionAsync(3, enc?.LongPressAction, cancellationToken);

            // 5. Сохранить профиль на устройстве
            var saved = await _deviceService.SaveProfileAsync(0, cancellationToken);

            // 6. Обновить дисплеи — теперь все данные (включая long press) актуальны,
            //    и кнопки с картинками тоже получат правильный split-layout
            await _deviceService.RefreshDisplaysAsync(cancellationToken);

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
    
    public async Task<Profile?> LoadProfileFromDeviceAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading profile {ProfileId} from device", profileId);
            
            // Проверить подключение к устройству
            if (!_deviceService.IsConnected)
            {
                _logger.LogWarning("Device is not connected");
                return null;
            }
            
            // Start from the existing stored profile so folders/images/names are preserved.
            // The device only knows about root-button actions and LEDs — everything else
            // (folders, ImagePath, custom names) lives on the PC side only.
            var profile = await _repository.GetByIdAsync(profileId)
                          ?? Profile.CreateEmpty(profileId, $"Profile {profileId}");

            // Merge root-button actions and LEDs from device into the stored profile.
            for (byte buttonId = 0; buttonId < 10; buttonId++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return null;

                var button = profile.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);
                if (button == null) continue;

                var action = await _deviceService.GetButtonActionAsync(profileId, buttonId, cancellationToken);
                if (action != null)
                {
                    button.Action = action;
                    _logger.LogDebug("Loaded action for button {ButtonId}: {ActionType}", buttonId, action.ActionType);
                }

                var led = await _deviceService.GetLedColorAsync(profileId, buttonId, cancellationToken);
                if (led != null)
                {
                    button.Led = led;
                    _logger.LogDebug("Loaded LED for button {ButtonId}: RGB({R},{G},{B})",
                        buttonId, led.R, led.G, led.B);
                }
            }

            await _repository.SaveAsync(profile);
            
            _logger.LogInformation("Profile {ProfileId} loaded from device successfully", profileId);
            
            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading profile from device");
            return null;
        }
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
