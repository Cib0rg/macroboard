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
    /// Проверить связь с устройством (ping)
    /// </summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Установить активный профиль
    /// </summary>
    Task<bool> SetProfileAsync(byte profileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Отправить изображение на кнопку
    /// </summary>
    Task<bool> SendButtonImageAsync(byte profileId, byte buttonId, byte[] imageData, 
        IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Установить действие для кнопки
    /// </summary>
    Task<bool> SetButtonActionAsync(byte profileId, byte buttonId, ActionConfig action, 
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
    
    /// <summary>
    /// Сохранить профиль в энергонезависимую память
    /// </summary>
    Task<bool> SaveProfileAsync(byte profileId, CancellationToken cancellationToken = default);
    
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
