using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MacroKeyboard.Core.Models;

/// <summary>
/// Базовый класс для конфигурации действия кнопки
/// </summary>
[JsonConverter(typeof(ActionConfigConverter))]
public abstract class ActionConfig
{
    /// <summary>
    /// Тип действия
    /// </summary>
    public abstract ActionType ActionType { get; }
    
    /// <summary>
    /// Конвертировать в байтовый массив для отправки на устройство
    /// </summary>
    public abstract byte[] ToBytes();
}

/// <summary>
/// Конфигурация действия клавиатуры
/// </summary>
public class KeyboardAction : ActionConfig
{
    public override ActionType ActionType => ActionType.Keyboard;
    
    /// <summary>
    /// Модификаторы (Ctrl, Shift, Alt, GUI)
    /// </summary>
    public KeyModifiers Modifiers { get; set; }
    
    /// <summary>
    /// HID keycode основной клавиши
    /// </summary>
    public byte KeyCode { get; set; }
    
    /// <summary>
    /// Текст для печати (опционально)
    /// </summary>
    public string? Text { get; set; }
    
    public override byte[] ToBytes()
    {
        var data = new List<byte>();
        
        // Байт 0: Модификаторы
        data.Add((byte)Modifiers);
        
        // Байт 1-6: Keycodes (пока только один)
        data.Add(KeyCode);
        data.AddRange(new byte[5]); // Остальные keycodes пустые
        
        // Байт 7-N: Текст (если есть)
        if (!string.IsNullOrEmpty(Text))
        {
            var textBytes = System.Text.Encoding.UTF8.GetBytes(Text);
            data.AddRange(textBytes);
        }
        
        return data.ToArray();
    }
}

/// <summary>
/// Модификаторы клавиатуры
/// </summary>
[Flags]
public enum KeyModifiers : byte
{
    None = 0x00,
    LeftCtrl = 0x01,
    LeftShift = 0x02,
    LeftAlt = 0x04,
    LeftGui = 0x08,
    RightCtrl = 0x10,
    RightShift = 0x20,
    RightAlt = 0x40,
    RightGui = 0x80
}

/// <summary>
/// Конфигурация пользовательского HID действия
/// </summary>
public class CustomHidAction : ActionConfig
{
    public override ActionType ActionType => ActionType.CustomHid;
    
    /// <summary>
    /// Пользовательские данные HID report
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();
    
    public override byte[] ToBytes()
    {
        return Data;
    }
}

/// <summary>
/// Конфигурация переключения профиля
/// </summary>
public class ProfileSwitchAction : ActionConfig
{
    public override ActionType ActionType => ActionType.ProfileSwitch;
    
    /// <summary>
    /// ID профиля для переключения
    /// </summary>
    public byte TargetProfileId { get; set; }
    
    public override byte[] ToBytes()
    {
        return new[] { TargetProfileId };
    }
}

/// <summary>
/// Конфигурация действия "Папка" (открытие папки)
/// </summary>
public class FolderAction : ActionConfig
{
    public override ActionType ActionType => ActionType.Folder;
    
    /// <summary>
    /// ID папки для открытия
    /// </summary>
    public byte FolderId { get; set; }
    
    public override byte[] ToBytes()
    {
        return new[] { FolderId };
    }
}

/// <summary>
/// Конфигурация действия задержки
/// </summary>
public class DelayAction : ActionConfig
{
    public override ActionType ActionType => ActionType.Delay;
    
    /// <summary>
    /// Задержка в миллисекундах (0-65535)
    /// </summary>
    public ushort DelayMs { get; set; }
    
    public override byte[] ToBytes()
    {
        return BitConverter.GetBytes(DelayMs);
    }
}

/// <summary>
/// Конфигурация действия выполнения shell-команды на PC
/// </summary>
public class ShellAction : ActionConfig
{
    public override ActionType ActionType => ActionType.Shell;
    
    /// <summary>
    /// Shell-команда для выполнения
    /// </summary>
    public string Command { get; set; } = string.Empty;
    
    /// <summary>
    /// Рабочая директория (опционально)
    /// </summary>
    public string? WorkingDirectory { get; set; }
    
    /// <summary>
    /// Ожидать завершения команды перед следующим шагом
    /// </summary>
    public bool WaitForExit { get; set; } = true;
    
    /// <summary>
    /// Таймаут ожидания в миллисекундах (0 = без таймаута)
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;
    
    public override byte[] ToBytes()
    {
        var data = new List<byte>();
        
        // Флаги: bit 0 = WaitForExit
        byte flags = 0;
        if (WaitForExit) flags |= 0x01;
        data.Add(flags);
        
        // Команда (UTF-8, null-terminated)
        var commandBytes = System.Text.Encoding.UTF8.GetBytes(Command);
        data.AddRange(commandBytes);
        data.Add(0); // null terminator
        
        return data.ToArray();
    }
}

/// <summary>
/// Один шаг в последовательности действий
/// </summary>
public class SequenceStep
{
    /// <summary>
    /// Действие для выполнения
    /// </summary>
    public ActionConfig Action { get; set; } = new KeyboardAction();
    
    /// <summary>
    /// Задержка ПЕРЕД выполнением действия (мс)
    /// </summary>
    public ushort DelayBeforeMs { get; set; }
}

/// <summary>
/// Конфигурация последовательности действий (макс. 16 шагов)
/// </summary>
public class SequenceAction : ActionConfig
{
    public const int MaxSteps = 16;
    
    public override ActionType ActionType => ActionType.Sequence;
    
    /// <summary>
    /// Шаги последовательности (максимум 16)
    /// </summary>
    public List<SequenceStep> Steps { get; set; } = new();
    
    public override byte[] ToBytes()
    {
        var data = new List<byte>();
        
        // Количество шагов
        var stepCount = Math.Min(Steps.Count, MaxSteps);
        data.Add((byte)stepCount);
        
        foreach (var step in Steps.Take(MaxSteps))
        {
            // Тип действия шага
            data.Add((byte)step.Action.ActionType);
            
            // Задержка перед (2 байта, little-endian)
            data.AddRange(BitConverter.GetBytes(step.DelayBeforeMs));
            
            // Данные действия
            var actionData = step.Action.ToBytes();
            data.AddRange(BitConverter.GetBytes((ushort)actionData.Length));
            data.AddRange(actionData);
        }
        
        return data.ToArray();
    }
}

/// <summary>
/// Конфигурация действия запуска приложения на PC
/// </summary>
public class LaunchAppAction : ActionConfig
{
    public override ActionType ActionType => ActionType.LaunchApp;
    
    /// <summary>
    /// Путь к исполняемому файлу приложения
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Аргументы командной строки (опционально)
    /// </summary>
    public string? Arguments { get; set; }
    
    /// <summary>
    /// Рабочая директория (опционально, по умолчанию — директория приложения)
    /// </summary>
    public string? WorkingDirectory { get; set; }
    
    /// <summary>
    /// Путь к иконке приложения (извлекается автоматически из exe)
    /// </summary>
    public string? IconPath { get; set; }
    
    public override byte[] ToBytes()
    {
        var data = new List<byte>();
        
        // Путь к приложению (UTF-8, null-terminated)
        var pathBytes = System.Text.Encoding.UTF8.GetBytes(ExecutablePath);
        data.AddRange(pathBytes);
        data.Add(0); // null terminator
        
        // Аргументы (UTF-8, null-terminated)
        var argsBytes = System.Text.Encoding.UTF8.GetBytes(Arguments ?? string.Empty);
        data.AddRange(argsBytes);
        data.Add(0); // null terminator
        
        return data.ToArray();
    }
}

/// <summary>
/// Типы медиа-клавиш (Consumer Control usage codes)
/// </summary>
public enum MediaKey : ushort
{
    VolumeUp = 0x00E9,
    VolumeDown = 0x00EA,
    Mute = 0x00E2,
    PlayPause = 0x00CD,
    NextTrack = 0x00B5,
    PreviousTrack = 0x00B6,
    Stop = 0x00B7
}

/// <summary>
/// Конфигурация действия медиа-клавиши (Consumer Control)
/// </summary>
public class MediaAction : ActionConfig
{
    public override ActionType ActionType => ActionType.Media;
    
    /// <summary>
    /// Тип медиа-клавиши
    /// </summary>
    public MediaKey Key { get; set; } = MediaKey.Mute;
    
    public override byte[] ToBytes()
    {
        // 2 bytes: usage code (little-endian)
        return BitConverter.GetBytes((ushort)Key);
    }
}

/// <summary>
/// Ночной режим — не имеет параметров, просто переключает состояние.
/// </summary>
public class NightModeAction : ActionConfig
{
    public override ActionType ActionType => ActionType.NightMode;
    public override byte[] ToBytes() => Array.Empty<byte>();
}

/// <summary>
/// JSON converter for ActionConfig abstract class.
/// Uses the ActionType property to determine the concrete type during deserialization.
/// Writing is handled by the default serializer (CanWrite = false).
/// </summary>
public class ActionConfigConverter : JsonConverter<ActionConfig>
{
    /// <summary>
    /// Let the default serializer handle writing — it works fine for concrete types.
    /// Only deserialization needs custom logic (to pick the right concrete type).
    /// </summary>
    public override bool CanWrite => false;

    public override ActionConfig? ReadJson(JsonReader reader, Type objectType, ActionConfig? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var jObject = JObject.Load(reader);
        
        // Determine the concrete type from ActionType
        var actionTypeValue = jObject["ActionType"]?.Value<int>() ?? jObject["actionType"]?.Value<int>() ?? 0;
        var actionType = (ActionType)actionTypeValue;

        ActionConfig? result = actionType switch
        {
            ActionType.Keyboard => new KeyboardAction(),
            ActionType.CustomHid => new CustomHidAction(),
            ActionType.ProfileSwitch => new ProfileSwitchAction(),
            ActionType.Folder => new FolderAction(),
            ActionType.Delay => new DelayAction(),
            ActionType.Shell => new ShellAction(),
            ActionType.Sequence => new SequenceAction(),
            ActionType.LaunchApp => new LaunchAppAction(),
            ActionType.Media => new MediaAction(),
            ActionType.NightMode => new NightModeAction(),
            _ => null
        };

        if (result == null)
            return null;

        // Populate the object from JSON (skip the converter to avoid recursion)
        using var subReader = jObject.CreateReader();
        serializer.Populate(subReader, result);

        return result;
    }

    public override void WriteJson(JsonWriter writer, ActionConfig? value, JsonSerializer serializer)
    {
        // This should never be called because CanWrite = false
        throw new NotImplementedException("WriteJson should not be called when CanWrite is false");
    }
}
