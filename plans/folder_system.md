# Система кнопок-папок (Folder Button System)

## Обзор

Система кнопок-папок позволяет организовать иерархическую навигацию по действиям, расширяя функциональность устройства с 10 кнопок до практически неограниченного количества действий через вложенные папки.

## Архитектура

### Структуры данных

#### Action Type
```c
typedef enum {
    ACTION_TYPE_NONE = 0x00,
    ACTION_TYPE_KEYBOARD = 0x01,
    ACTION_TYPE_CUSTOM_HID = 0x02,
    ACTION_TYPE_PROFILE_SWITCH = 0x03,
    ACTION_TYPE_FOLDER = 0x04,  // Новый тип
} action_type_t;
```

#### Button Configuration
```c
typedef struct {
    uint8_t button_id;
    action_type_t action_type;
    uint16_t action_data_len;
    uint8_t action_data[ACTION_DATA_MAX_LEN];
    
    uint8_t folder_id;  // ID папки для ACTION_TYPE_FOLDER
    
    // LED configuration
    uint8_t led_r, led_g, led_b;
    uint8_t led_brightness;
    led_effect_t led_effect;
    
    // Image metadata
    uint32_t image_offset;
    uint32_t image_size;
    uint8_t image_format;
} button_config_t;
```

#### Folder Structure
```c
typedef struct {
    uint8_t folder_id;
    char name[PROFILE_NAME_MAX_LEN];
    button_config_t buttons[NUM_BUTTONS];  // 10 кнопок в папке
} folder_t;
```

#### Profile Structure
```c
typedef struct {
    uint8_t profile_id;
    char name[PROFILE_NAME_MAX_LEN];
    button_config_t buttons[NUM_BUTTONS];  // Корневые кнопки
    folder_t folders[NUM_FOLDERS];         // До 16 папок
    uint32_t crc32;
} profile_t;
```

### Константы

```c
#define NUM_FOLDERS         16  // Максимум папок на профиль
#define FOLDER_STACK_DEPTH  4   // Максимальная вложенность
```

## Логика работы

### Навигация

1. **Вход в папку** (`profile_folder_enter(folder_id)`):
   - Проверка валидности folder_id
   - Проверка глубины стека (не более FOLDER_STACK_DEPTH)
   - Добавление folder_id в стек
   - Обновление LED и дисплеев кнопками из папки
   - Сохранение ID кнопки, которая открыла папку

2. **Выход из папки** (`profile_folder_exit()`):
   - Проверка, что мы не на корневом уровне
   - Извлечение folder_id из стека
   - Восстановление кнопок родительского контекста
   - Обновление LED и дисплеев

3. **Toggle-логика** (в `action_executor.c`):
   ```c
   if (profile_is_in_folder() && folder_entry_button_id == button_id) {
       // Выход из папки
       profile_folder_exit();
   } else {
       // Вход в папку
       profile_folder_enter(folder_id);
   }
   ```

### Контекст выполнения

Функция `profile_get_button_config(button_id)` автоматически возвращает конфигурацию из текущего контекста:
- Если на корневом уровне → возвращает из `profile.buttons[button_id]`
- Если внутри папки → возвращает из `profile.folders[current_folder].buttons[button_id]`

## Пример использования

### Сценарий: Git команды

**Корневой уровень:**
```
[Button 0] Git (folder_id=0)
[Button 1] Docker (folder_id=1)
[Button 2] Media (folder_id=2)
...
```

**Нажатие на Button 0 → Вход в папку Git:**
```
[Button 0] Git (toggle для выхода)
[Button 1] git push
[Button 2] git pull
[Button 3] git commit
[Button 4] git reset
[Button 5] git checkout
...
```

**Повторное нажатие на Button 0 → Выход из папки:**
```
[Button 0] Git (folder_id=0)
[Button 1] Docker (folder_id=1)
[Button 2] Media (folder_id=2)
...
```

### Вложенные папки

Можно создавать вложенность до 4 уровней:
```
Root → Git → Branches → Feature Branches
```

## Протокол обмена с PC

### Команды для управления папками

#### 1. Установка кнопки-папки
```
CMD_SET_BUTTON_ACTION (0x20)
Payload:
  - profile_id: uint8
  - button_id: uint8
  - action_type: uint8 (0x04 = ACTION_TYPE_FOLDER)
  - folder_id: uint8
  - action_data_len: uint16 (0)
```

#### 2. Конфигурация кнопок внутри папки
```
CMD_SET_FOLDER_BUTTON (новая команда, 0x2A)
Payload:
  - profile_id: uint8
  - folder_id: uint8
  - button_id: uint8
  - action_type: uint8
  - action_data_len: uint16
  - action_data: uint8[]
```

#### 3. Установка изображения для кнопки в папке
```
CMD_SET_FOLDER_IMAGE (новая команда, 0x2B)
Payload:
  - profile_id: uint8
  - folder_id: uint8
  - button_id: uint8
  - image_format: uint8
  - image_size: uint32
  - image_data: uint8[] (chunked transfer)
```

#### 4. Установка LED для кнопки в папке
```
CMD_SET_FOLDER_LED (новая команда, 0x2C)
Payload:
  - profile_id: uint8
  - folder_id: uint8
  - button_id: uint8
  - r: uint8
  - g: uint8
  - b: uint8
  - brightness: uint8
  - effect: uint8
```

### События от устройства

```
EVENT_FOLDER_ENTERED (новое событие, 0xF8)
Payload:
  - folder_id: uint8
  - depth: uint8

EVENT_FOLDER_EXITED (новое событие, 0xF9)
Payload:
  - folder_id: uint8
  - depth: uint8
```

## Реализация в C# (для управляющего софта)

### Модель данных

```csharp
public enum ActionType : byte
{
    None = 0x00,
    Keyboard = 0x01,
    CustomHID = 0x02,
    ProfileSwitch = 0x03,
    Folder = 0x04
}

public class ButtonConfig
{
    public byte ButtonId { get; set; }
    public ActionType ActionType { get; set; }
    public byte[] ActionData { get; set; }
    public byte FolderId { get; set; }  // Для ACTION_TYPE_FOLDER
    
    public LedConfig Led { get; set; }
    public ImageConfig Image { get; set; }
}

public class Folder
{
    public byte FolderId { get; set; }
    public string Name { get; set; }
    public ButtonConfig[] Buttons { get; set; } = new ButtonConfig[10];
}

public class Profile
{
    public byte ProfileId { get; set; }
    public string Name { get; set; }
    public ButtonConfig[] Buttons { get; set; } = new ButtonConfig[10];
    public Folder[] Folders { get; set; } = new Folder[16];
}
```

### API для работы с папками

```csharp
public interface IFolderManager
{
    // Создание папки
    Task<Folder> CreateFolderAsync(byte profileId, string folderName);
    
    // Удаление папки
    Task DeleteFolderAsync(byte profileId, byte folderId);
    
    // Настройка кнопки как папки
    Task SetButtonAsFolderAsync(byte profileId, byte buttonId, byte folderId);
    
    // Настройка кнопки внутри папки
    Task SetFolderButtonAsync(byte profileId, byte folderId, byte buttonId, ButtonConfig config);
    
    // Установка изображения для кнопки в папке
    Task SetFolderButtonImageAsync(byte profileId, byte folderId, byte buttonId, byte[] imageData);
    
    // Установка LED для кнопки в папке
    Task SetFolderButtonLedAsync(byte profileId, byte folderId, byte buttonId, LedConfig led);
    
    // Получение списка папок профиля
    Task<Folder[]> GetFoldersAsync(byte profileId);
}
```

### UI компоненты

1. **FolderTreeView** - дерево папок для навигации
2. **FolderEditor** - редактор содержимого папки
3. **ButtonFolderSelector** - выбор папки для кнопки
4. **FolderBreadcrumb** - навигационная цепочка (Root > Git > Branches)

## Ограничения

- Максимум 16 папок на профиль
- Максимальная вложенность 4 уровня
- В каждой папке 10 кнопок (как на корневом уровне)
- Папки хранятся в профиле, занимают место в SPIFFS

## Преимущества

1. **Расширяемость**: 10 кнопок × 16 папок = 160 действий на профиль
2. **Организация**: Логическая группировка команд (Git, Docker, Media и т.д.)
3. **Интуитивность**: Toggle-логика естественна для пользователя
4. **Гибкость**: Можно создавать вложенные структуры
5. **Визуализация**: Каждая кнопка в папке имеет свое изображение и LED

## Тестирование

### Unit тесты
- Вход/выход из папок
- Переполнение стека
- Валидация folder_id
- Toggle-логика

### Integration тесты
- Обновление LED при навигации
- Обновление дисплеев при навигации
- Сохранение/загрузка папок из SPIFFS
- Протокол обмена с PC

### User тесты
- Создание папки через UI
- Настройка кнопок в папке
- Навигация на устройстве
- Вложенные папки
