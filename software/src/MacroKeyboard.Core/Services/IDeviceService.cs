using MacroKeyboard.Core.Models;

namespace MacroKeyboard.Core.Services;

/// <summary>
/// Сервис для работы с устройством
/// </summary>
public interface IDeviceService
{
    /// <summary>
    /// Событие подключения устройства
    /// </summary>
    event EventHandler<DeviceEventArgs>? DeviceConnected;
    
    /// <summary>
    /// Событие отключения устройства
    /// </summary>
    event EventHandler<DeviceEventArgs>? DeviceDisconnected;

    /// <summary>
    /// Прошивка полностью загружена и готова к командам (EVENT_DEVICE_READY 0xF4).
    /// </summary>
    event EventHandler? DeviceReady;
    
    /// <summary>
    /// Событие нажатия кнопки
    /// </summary>
    event EventHandler<ButtonEventArgs>? ButtonPressed;
    
    /// <summary>
    /// Событие отпускания кнопки
    /// </summary>
    event EventHandler<ButtonEventArgs>? ButtonReleased;
    
    /// <summary>
    /// Событие вращения энкодера
    /// </summary>
    event EventHandler<EncoderEventArgs>? EncoderRotated;
    
    /// <summary>
    /// Событие смены профиля
    /// </summary>
    event EventHandler<ProfileChangedEventArgs>? ProfileChanged;
    
    /// <summary>
    /// Событие входа в папку
    /// </summary>
    event EventHandler<FolderEventArgs>? FolderEntered;
    
    /// <summary>
    /// Событие выхода из папки
    /// </summary>
    event EventHandler<FolderEventArgs>? FolderExited;
    
    /// <summary>
    /// Устройство подключено
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Подключиться к устройству
    /// </summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Отключиться от устройства
    /// </summary>
    void Disconnect();
    
    /// <summary>
    /// Получить информацию об устройстве
    /// </summary>
    Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Получить информацию о профиле с устройства (CMD_GET_PROFILE_INFO 0x11)
    /// </summary>
    Task<ProfileInfoResult?> GetProfileInfoAsync(byte profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить текущее состояние папки: (folderId=0xFF, depth=0) если в корне.
    /// </summary>
    Task<(byte FolderId, byte Depth)> GetFolderStateAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Проверить связь с устройством (ping)
    /// </summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Перезагрузить устройство (ESP32 soft reboot)
    /// </summary>
    Task<bool> RebootDeviceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Установить активный профиль
    /// </summary>
    Task<bool> SetProfileAsync(byte profileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Отправить изображение на кнопку. noStore=true пропускает запись в SPIFFS (плагиновые временные изображения).
    /// </summary>
    Task<bool> SendButtonImageAsync(byte profileId, byte buttonId, byte[] imageData,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default,
        bool noStore = false);

    /// <summary>
    /// Нарисовать кольцо + текст прямо на ESP (CMD_PLUGIN_DISPLAY 0x44).
    /// Не передаёт JPEG и не пишет в SPIFFS. Используется плагинами для near-realtime обновлений.
    /// </summary>
    Task<bool> SendPluginDisplayAsync(byte buttonId, string text, bool isOn,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Установить действие для кнопки
    /// </summary>
    Task<bool> SetButtonActionAsync(byte profileId, byte buttonId, ActionConfig action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Установить отображаемое имя кнопки (показывается на дисплее если нет картинки)
    /// </summary>
    Task<bool> SetButtonNameAsync(byte profileId, byte buttonId, string? name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear stored image for a button (sets image_size=0), switching it to text-render mode.
    /// </summary>
    Task<bool> ClearButtonImageAsync(byte profileId, byte buttonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Установить цвет LED для кнопки
    /// </summary>
    Task<bool> SetLedColorAsync(byte profileId, byte buttonId, LedConfig led, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Установить яркость подсветки дисплеев (0-255)
    /// </summary>
    Task<byte?> SetDisplayBrightnessAsync(byte brightness, CancellationToken cancellationToken = default);
    
    Task<bool> SetFolderButtonActionAsync(byte profileId, byte folderId, byte buttonId, ActionConfig action,
        CancellationToken cancellationToken = default);

    Task<bool> SetFolderButtonNameAsync(byte profileId, byte folderId, byte buttonId, string? name,
        CancellationToken cancellationToken = default);

    Task<bool> SetFolderButtonLedAsync(byte profileId, byte folderId, byte buttonId, LedConfig led,
        CancellationToken cancellationToken = default);

    Task<bool> SendFolderButtonImageAsync(byte profileId, byte folderId, byte buttonId, byte[] imageData,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Установить действие для слота энкодера (0=CW, 1=CCW, 2=press, 3=long press)
    /// </summary>
    Task<bool> SetEncoderActionAsync(byte slot, ActionConfig? action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Установить long press action. folderId=0xFF → root buttons.
    /// </summary>
    Task<bool> SetButtonLongPressActionAsync(byte profileId, byte buttonId, ActionConfig? action,
        byte folderId = 0xFF, CancellationToken cancellationToken = default);

    /// <summary>
    /// Установить имя long press. folderId=0xFF → root buttons.
    /// </summary>
    Task<bool> SetButtonLongPressNameAsync(byte profileId, byte buttonId, string? name,
        byte folderId = 0xFF, CancellationToken cancellationToken = default);
    Task<bool> RefreshDisplaysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Передать произвольные бинарные данные в SPIFFS через chunked transfer (CMD 0x38/0x39/0x3A).
    /// Используется для длинных текстов (stepIndex=TEXT_STEP_DIRECT) и sequence-блобов (stepIndex=TEXT_STEP_SEQ_BLOB).
    /// </summary>
    Task<bool> SendSpiffsDataAsync(byte profileId, byte folderId, byte buttonId,
        byte actionSlot, byte stepIndex, byte[] data,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отправить действие кнопки на устройство с автоматическим выбором пути:
    /// длинный текст и крупные sequence-блобы идут через SPIFFS; остальное — inline.
    /// actionSlot: TEXT_ACTION_SHORT (0) или TEXT_ACTION_LONG (1).
    /// </summary>
    Task<bool> SendActionAsync(byte profileId, byte folderId, byte buttonId,
        byte actionSlot, ActionConfig? action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохранить профиль в энергонезависимую память
    /// </summary>
    Task<bool> SaveProfileAsync(byte profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удалить профиль из энергонезависимой памяти устройства
    /// </summary>
    Task<bool> DeleteProfileFromDeviceAsync(byte profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Загрузить кэш CRC32 хешей изображений с устройства.
    /// Последующие вызовы SendButtonImageAsync пропустят передачу, если хеш совпадает.
    /// </summary>
    Task LoadImageHashCacheAsync(byte profileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Получить действие кнопки с устройства
    /// </summary>
    Task<ActionConfig?> GetButtonActionAsync(byte profileId, byte buttonId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Получить цвет LED кнопки с устройства
    /// </summary>
    Task<LedConfig?> GetLedColorAsync(byte profileId, byte buttonId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Аргументы события устройства
/// </summary>
public class DeviceEventArgs : EventArgs
{
    public DeviceInfo? DeviceInfo { get; set; }
}

/// <summary>
/// Аргументы события кнопки
/// </summary>
public class ButtonEventArgs : EventArgs
{
    public byte ButtonId { get; set; }
    public byte ProfileId { get; set; }
    public ActionType ActionType { get; set; }
}

/// <summary>
/// Аргументы события энкодера
/// </summary>
public class EncoderEventArgs : EventArgs
{
    public EncoderDirection Direction { get; set; }
    public byte Steps { get; set; }
    public byte NewProfileId { get; set; }
}

public enum EncoderDirection : byte
{
    CounterClockwise = 0x00,
    Clockwise = 0x01
}

/// <summary>
/// Аргументы события смены профиля
/// </summary>
public class ProfileChangedEventArgs : EventArgs
{
    public byte OldProfileId { get; set; }
    public byte NewProfileId { get; set; }
    public ProfileChangeReason Reason { get; set; }
}

public enum ProfileChangeReason : byte
{
    Encoder = 0x01,
    Command = 0x02,
    Boot = 0x03
}

/// <summary>
/// Аргументы события папки
/// </summary>
public class FolderEventArgs : EventArgs
{
    public byte FolderId { get; set; }
    public byte FolderDepth { get; set; }
    public byte ProfileId { get; set; }
    public byte ParentFolderId { get; set; } = 0xFF; // 0xFF = root
}

/// <summary>
/// Результат запроса информации о профиле с устройства (CMD_GET_PROFILE_INFO)
/// </summary>
public class ProfileInfoResult
{
    public byte ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
}
