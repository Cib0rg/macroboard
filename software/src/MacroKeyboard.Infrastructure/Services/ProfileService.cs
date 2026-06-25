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

    // Diff cache: (profileId, folderId, buttonId) → fingerprint of last successfully sent state.
    // folderId = 0xFF for root buttons; folderId = 0xFE + buttonId = 0xFF is the encoder slot.
    // Cleared on device disconnect or profile switch.
    private readonly Dictionary<(byte profileId, byte folderId, byte buttonId), string> _sentConfigCache = new();
    private byte _lastSentProfileId = 0xFF;

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

        _deviceService.DeviceDisconnected += (_, _) =>
        {
            _sentConfigCache.Clear();
            _lastSentProfileId = 0xFF;
            _logger.LogInformation("Device disconnected — diff cache cleared");
        };
    }

    // ── Fingerprint helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Produces a stable string that captures everything we send to the device for one button.
    /// Image identity is represented by file path + last-write timestamp so we skip JPEG
    /// processing entirely when the file hasn't changed.
    /// </summary>
    private static string ComputeButtonFingerprint(ButtonConfig btn)
    {
        string imagePart = "none";
        if (!string.IsNullOrEmpty(btn.ImagePath))
        {
            imagePart = File.Exists(btn.ImagePath)
                ? $"{btn.ImagePath}\x01{File.GetLastWriteTimeUtc(btn.ImagePath).Ticks}"
                : $"missing\x01{btn.ImagePath}";
        }

        string actionPart   = ActionFingerprint(btn.Action);
        string lpActionPart = ActionFingerprint(btn.LongPressAction);
        string ledPart      = $"{btn.Led?.R ?? 0},{btn.Led?.G ?? 0},{btn.Led?.B ?? 0},{btn.Led?.Brightness ?? 0}";

        return string.Join("\x1F",
            imagePart,
            btn.Name ?? "",
            actionPart,
            lpActionPart,
            btn.LongPressName ?? "",
            ledPart);
    }

    private static string ComputeEncoderFingerprint(EncoderConfig? enc)
    {
        if (enc == null) return "null";
        return string.Join("\x1F",
            ActionFingerprint(enc.RotateCwAction),
            ActionFingerprint(enc.RotateCcwAction),
            ActionFingerprint(enc.PressAction),
            ActionFingerprint(enc.LongPressAction));
    }

    // Include the concrete type name so NoneAction{} ≠ FolderAction{} even if both serialize to "{}".
    private static string ActionFingerprint(ActionConfig? action) =>
        action == null
            ? "null"
            : $"{action.GetType().Name}:{Newtonsoft.Json.JsonConvert.SerializeObject(action)}";

    // ── Profile CRUD ─────────────────────────────────────────────────────────

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
        var existingProfiles = await _repository.GetAllAsync();

        if (existingProfiles.Count >= 5)
            throw new InvalidOperationException(
                "Maximum number of profiles (5) reached. Delete an existing profile before creating a new one.");

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

        for (int i = 0; i < source.Buttons.Count; i++)
        {
            duplicate.Buttons[i].Action = source.Buttons[i].Action;
            duplicate.Buttons[i].Led    = source.Buttons[i].Led;

            if (!string.IsNullOrEmpty(source.Buttons[i].ImagePath) && File.Exists(source.Buttons[i].ImagePath))
            {
                var targetImagePath = Path.Combine(
                    AppDataManager.GetProfileImagesPath(duplicate.ProfileId),
                    $"button_{i}.jpg");
                File.Copy(source.Buttons[i].ImagePath!, targetImagePath, overwrite: true);
                duplicate.Buttons[i].ImagePath = targetImagePath;
            }
        }

        await _repository.SaveAsync(duplicate);
        return duplicate;
    }

    // ── Send profile to device (diff-based) ──────────────────────────────────

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

            // The device holds one profile slot. If we're now sending a different profile than
            // last time, every cached state on the device is stale — force a full resync.
            if (_lastSentProfileId != profile.ProfileId)
            {
                if (_lastSentProfileId != 0xFF)
                    _logger.LogInformation(
                        "Profile changed ({Old}→{New}) — clearing diff cache for full resync",
                        _lastSentProfileId, profile.ProfileId);
                _sentConfigCache.Clear();
                _lastSentProfileId = profile.ProfileId;
            }

            // 1. Switch device to profile slot 0 (always — cheap, ensures device is in sync)
            var profileSet = await _deviceService.SetProfileAsync(0, cancellationToken);
            if (!profileSet)
            {
                _logger.LogError("Failed to set profile");
                return false;
            }

            progress?.Report(10);
            bool anythingSent = false;

            // 2a. Resolve missing LaunchApp icons from the icon cache BEFORE fingerprinting,
            //     so the fingerprint already captures the resolved path.
            foreach (var btn in profile.Buttons)
            {
                if (!string.IsNullOrEmpty(btn.ImagePath)) continue;
                if (btn.Action is not MacroKeyboard.Core.Models.LaunchAppAction la) continue;
                if (string.IsNullOrEmpty(la.ExecutablePath)) continue;

                var iconPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MacroKeyboard", "icons",
                    Path.GetFileNameWithoutExtension(la.ExecutablePath) + ".png");

                if (File.Exists(iconPath))
                {
                    btn.ImagePath = iconPath;
                    _logger.LogInformation(
                        "Resolved missing icon for button {ButtonId} from cache: {Path}",
                        btn.ButtonId, iconPath);
                }
            }

            // 2. Root buttons — send only those that changed since the last send.
            for (int i = 0; i < profile.Buttons.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                var button      = profile.Buttons[i];
                var key         = (profile.ProfileId, (byte)0xFF, button.ButtonId);
                var fingerprint = ComputeButtonFingerprint(button);

                if (_sentConfigCache.TryGetValue(key, out var cachedFp) && cachedFp == fingerprint)
                {
                    _logger.LogDebug("Root button {ButtonId}: unchanged, skipping", button.ButtonId);
                    progress?.Report(10 + ((i + 1) * 40 / profile.Buttons.Count));
                    continue;
                }

                _logger.LogInformation("Root button {ButtonId}: changed, sending", button.ButtonId);

                await SendRootButtonAsync(profile, button, i, progress, cancellationToken);

                _sentConfigCache[key] = fingerprint;
                anythingSent = true;
                progress?.Report(10 + ((i + 1) * 40 / profile.Buttons.Count));
                await Task.Delay(50, cancellationToken);
            }

            // 3. Folder buttons — send only changed ones.
            foreach (var folder in profile.Folders)
            {
                for (int i = 0; i < 10; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return false;

                    var btn   = folder.Buttons.Count > i ? folder.Buttons[i] : null;
                    byte btnId = btn != null ? btn.ButtonId : (byte)i;

                    // Resolve LaunchApp icon before fingerprinting (same as root above).
                    if (btn != null && string.IsNullOrEmpty(btn.ImagePath) &&
                        btn.Action is MacroKeyboard.Core.Models.LaunchAppAction fla &&
                        !string.IsNullOrEmpty(fla.ExecutablePath))
                    {
                        var iconPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "MacroKeyboard", "icons",
                            Path.GetFileNameWithoutExtension(fla.ExecutablePath) + ".png");
                        if (File.Exists(iconPath))
                            btn.ImagePath = iconPath;
                    }

                    // Fingerprint the effective state we'd send (same defaults as the send path).
                    var effectiveBtn = btn ?? new ButtonConfig
                    {
                        ButtonId = (byte)i,
                        Led      = LedConfig.FromRgb(80, 80, 80),
                    };
                    var key         = (profile.ProfileId, folder.FolderId, btnId);
                    var fingerprint = ComputeButtonFingerprint(effectiveBtn);

                    if (_sentConfigCache.TryGetValue(key, out var cachedFp) && cachedFp == fingerprint)
                    {
                        _logger.LogDebug("Folder {FolderId} button {ButtonId}: unchanged, skipping",
                            folder.FolderId, btnId);
                        continue;
                    }

                    _logger.LogInformation("Folder {FolderId} button {ButtonId}: changed, sending",
                        folder.FolderId, btnId);

                    await SendFolderButtonAsync(folder, btn, btnId, cancellationToken);

                    _sentConfigCache[key] = fingerprint;
                    anythingSent = true;
                    await Task.Delay(50, cancellationToken);
                }
            }

            // 4. Encoder — send only if changed.
            var encoderKey = (profile.ProfileId, (byte)0xFE, (byte)0xFF);
            var encoderFp  = ComputeEncoderFingerprint(profile.Encoder);
            if (!_sentConfigCache.TryGetValue(encoderKey, out var cachedEncFp) || cachedEncFp != encoderFp)
            {
                _logger.LogInformation("Encoder: changed, sending");
                var enc = profile.Encoder;
                await _deviceService.SetEncoderActionAsync(0, enc?.RotateCwAction, cancellationToken);
                await _deviceService.SetEncoderActionAsync(1, enc?.RotateCcwAction, cancellationToken);
                await _deviceService.SetEncoderActionAsync(2, enc?.PressAction, cancellationToken);
                await _deviceService.SetEncoderActionAsync(3, enc?.LongPressAction, cancellationToken);
                _sentConfigCache[encoderKey] = encoderFp;
                anythingSent = true;
            }
            else
            {
                _logger.LogDebug("Encoder: unchanged, skipping");
            }

            // 5. Nothing changed — device is already up to date.
            if (!anythingSent)
            {
                _logger.LogInformation("Profile {ProfileId}: all up to date — skipping save/refresh",
                    profile.ProfileId);
                progress?.Report(100);
                return true;
            }

            // 6. Persist to device NVS and refresh displays.
            var saved = await _deviceService.SaveProfileAsync(0, cancellationToken);
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

    // ── Private send helpers ─────────────────────────────────────────────────

    // Returns the ring overlay colour for a given action, or null for no ring.
    private static SixLabors.ImageSharp.Color? GetRingColor(MacroKeyboard.Core.Models.ActionConfig? action) =>
        action switch
        {
            FolderAction       => SixLabors.ImageSharp.Color.FromRgb(0xFF, 0xA5, 0x00),
            PluginActionConfig => SixLabors.ImageSharp.Color.FromRgb(0x8B, 0x5C, 0xF6),
            _                  => null
        };

    // Processes and sends a button image, with blank/placeholder fallbacks.
    // folderId == null means root button (uses SendButtonImageAsync);
    // folderId != null means folder button (uses SendFolderButtonImageAsync).
    private async Task SendButtonImageDataAsync(
        byte profileId,
        byte? folderId,
        byte buttonId,
        string? imagePath,
        MacroKeyboard.Core.Models.ActionConfig? action,
        string? label,
        IProgress<int>? imageProgress,
        CancellationToken cancellationToken)
    {
        var ringColor = GetRingColor(action);

        async Task<bool> SendRaw(byte[] data) => folderId == null
            ? await _deviceService.SendButtonImageAsync(profileId, buttonId, data, imageProgress, cancellationToken)
            : await _deviceService.SendFolderButtonImageAsync(profileId, folderId.Value, buttonId, data, null, cancellationToken);

        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            _logger.LogInformation("Processing image for button {ButtonId}: {Path}", buttonId, imagePath);
            var processed = await _imageService.ProcessImageForButtonAsync(imagePath, ringColor);
            if (processed?.Length > 0)
            {
                _logger.LogInformation("Sending processed image for button {ButtonId}: {Size}B", buttonId, processed.Length);
                if (!await SendRaw(processed))
                    _logger.LogWarning("Failed to send image for button {ButtonId}", buttonId);
            }
            else
            {
                _logger.LogWarning("Failed to process image for button {ButtonId}: {Path}", buttonId, imagePath);
            }
        }
        else if (!string.IsNullOrEmpty(imagePath))
        {
            _logger.LogWarning("Image file not found for button {ButtonId}: {Path}", buttonId, imagePath);
        }
        else if (action == null || action is NoneAction)
        {
            var blank = await _imageService.CreateBlankImageAsync();
            await SendRaw(blank);
        }
        else if (ringColor.HasValue)
        {
            // FolderAction (orange ring) and PluginActionConfig (purple ring) both need a
            // placeholder so SPIFFS stores image_size > 0; otherwise profile_refresh_displays()
            // falls back to text rendering until willAppear fires.
            _logger.LogInformation("Sending ring placeholder for button {ButtonId} label='{Label}'", buttonId, label);
            var placeholder = await _imageService.CreateRingPlaceholderAsync(ringColor.Value, label ?? string.Empty);
            await SendRaw(placeholder);
        }
    }

    private async Task SendRootButtonAsync(
        Profile profile,
        ButtonConfig button,
        int buttonIndex,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var imageProgress = new Progress<int>(p =>
            progress?.Report(10 + (buttonIndex * 40 / 10) + (p * 40 / 10 / 100)));

        await SendButtonImageDataAsync(
            0, null, button.ButtonId,
            button.ImagePath, button.Action, button.Name,
            imageProgress, cancellationToken);

        var actionToSend = button.Action ?? new NoneAction();
        if (actionToSend is MacroKeyboard.Core.Models.KeyboardAction ka &&
            ka.KeyCode == 0 &&
            System.Text.Encoding.UTF8.GetByteCount(ka.Text ?? "") > 44)
        {
            await _deviceService.SetButtonLongTextAsync(
                0, 0xFF, button.ButtonId, ka.Text!, null, cancellationToken);
        }
        else
        {
            await _deviceService.SetButtonActionAsync(0, button.ButtonId, actionToSend, cancellationToken);
        }

        await _deviceService.SetButtonNameAsync(0, button.ButtonId, button.Name ?? string.Empty, cancellationToken);
        await _deviceService.SetLedColorAsync(0, button.ButtonId, button.Led, cancellationToken);
        await _deviceService.SetButtonLongPressActionAsync(
            0, button.ButtonId, button.LongPressAction, cancellationToken: cancellationToken);
        await _deviceService.SetButtonLongPressNameAsync(
            0, button.ButtonId, button.LongPressName ?? string.Empty, cancellationToken: cancellationToken);
    }

    private async Task SendFolderButtonAsync(
        Folder folder,
        ButtonConfig? btn,
        byte btnId,
        CancellationToken cancellationToken)
    {
        await SendButtonImageDataAsync(
            0, folder.FolderId, btnId,
            btn?.ImagePath, btn?.Action, btn?.Name,
            null, cancellationToken);

        var folderAction = btn?.Action ?? new NoneAction();
        if (folderAction is MacroKeyboard.Core.Models.KeyboardAction fka &&
            fka.KeyCode == 0 &&
            System.Text.Encoding.UTF8.GetByteCount(fka.Text ?? "") > 44)
        {
            await _deviceService.SetButtonLongTextAsync(
                0, folder.FolderId, btnId, fka.Text!, null, cancellationToken);
        }
        else
        {
            await _deviceService.SetFolderButtonActionAsync(
                0, folder.FolderId, btnId, folderAction, cancellationToken);
        }

        await _deviceService.SetFolderButtonNameAsync(
            0, folder.FolderId, btnId, btn?.Name ?? string.Empty, cancellationToken);

        var led = btn?.Led ?? LedConfig.FromRgb(80, 80, 80);
        await _deviceService.SetFolderButtonLedAsync(0, folder.FolderId, btnId, led, cancellationToken);

        await _deviceService.SetButtonLongPressActionAsync(
            0, btnId, btn?.LongPressAction, folder.FolderId, cancellationToken);
        await _deviceService.SetButtonLongPressNameAsync(
            0, btnId, btn?.LongPressName ?? string.Empty, folder.FolderId, cancellationToken);
    }

    // ── Load from device ─────────────────────────────────────────────────────

    public async Task<Profile?> LoadProfileFromDeviceAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading profile {ProfileId} from device", profileId);

            if (!_deviceService.IsConnected)
            {
                _logger.LogWarning("Device is not connected");
                return null;
            }

            var profile = await _repository.GetByIdAsync(profileId)
                          ?? Profile.CreateEmpty(profileId, $"Profile {profileId}");

            for (byte buttonId = 0; buttonId < 10; buttonId++)
            {
                if (cancellationToken.IsCancellationRequested) return null;

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

    // ── Import / Export ──────────────────────────────────────────────────────

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
            var json    = await File.ReadAllTextAsync(filePath, cancellationToken);
            var profile = Newtonsoft.Json.JsonConvert.DeserializeObject<Profile>(json);
            if (profile == null) return null;

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
