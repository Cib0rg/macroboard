# MacroKeyboard - Дорожная карта развития

## Статус проекта

### ✅ Реализовано (2026-04-14)

#### Критичные исправления
1. **Async void в обработчиках событий** - добавлена обработка исключений
2. **Managed плагины** - полная поддержка .NET плагинов с изоляцией
3. **Загрузка профилей с устройства** - расширен протокол, реализовано чтение конфигурации кнопок

#### Текущая функциональность
- ✅ Базовая система профилей (5 профилей)
- ✅ Система папок (16 папок на профиль, вложенность до 4 уровней)
- ✅ Типы действий: Keyboard, CustomHID, ProfileSwitch, Folder
- ✅ LED эффекты: Static, Breathing, Rainbow, Wave
- ✅ Загрузка изображений на кнопки (JPEG, 160x160)
- ✅ HID протокол обмена с устройством
- ✅ Backend с IPC и WebSocket для плагинов
- ✅ TrayApp для системного трея
- ✅ UI приложение на Avalonia

---

## 🎯 Новые требования

### 1. 🔄 Последовательные действия (Action Sequences)

**Описание:** Возможность назначить на одну кнопку несколько действий, выполняемых последовательно.

**Примеры использования:**
- Ctrl+C → задержка 100ms → Ctrl+V (копировать и вставить)
- Win+R → задержка 200ms → "notepad" → Enter (открыть блокнот)
- Переключить профиль → задержка 500ms → нажать кнопку 3

**Архитектура:**

```c
// Прошивка
typedef enum {
    ACTION_TYPE_NONE = 0x00,
    ACTION_TYPE_KEYBOARD = 0x01,
    ACTION_TYPE_CUSTOM_HID = 0x02,
    ACTION_TYPE_PROFILE_SWITCH = 0x03,
    ACTION_TYPE_FOLDER = 0x04,
    ACTION_TYPE_SEQUENCE = 0x05,  // НОВЫЙ ТИП
} action_type_t;

typedef struct {
    uint8_t action_type;
    uint16_t delay_ms;  // Задержка перед выполнением
    uint16_t data_len;
    uint8_t data[ACTION_DATA_MAX_LEN];
} sequence_step_t;

typedef struct {
    uint8_t num_steps;  // Количество шагов (макс 8)
    sequence_step_t steps[MAX_SEQUENCE_STEPS];
} action_sequence_t;
```

```csharp
// C# модели
public class ActionSequence : ActionConfig
{
    public override ActionType ActionType => ActionType.Sequence;
    public List<SequenceStep> Steps { get; set; } = new();
}

public class SequenceStep
{
    public ActionConfig Action { get; set; }
    public int DelayMs { get; set; }  // Задержка перед выполнением
}
```

**Масштаб изменений:**
- 🔴 **Прошивка:** Средние изменения
  - Добавить `ACTION_TYPE_SEQUENCE` в `profile_types.h`
  - Реализовать `execute_action_sequence()` в `action_executor.c`
  - Добавить поддержку в протокол (CMD_SET_BUTTON_ACTION)
  - ~200-300 строк кода

- 🔴 **C# Backend:** Средние изменения
  - Добавить `ActionSequence` класс в Models
  - Обновить `SetButtonActionCommand` для сериализации последовательностей
  - Обновить `GetButtonActionCommand` для десериализации
  - ~150-200 строк кода

- 🟡 **UI:** Значительные изменения
  - Новый UI для создания последовательностей (drag-and-drop?)
  - Визуализация шагов с задержками
  - Редактор последовательностей
  - ~400-500 строк кода

**Оценка:** 2-3 дня разработки

---

### 2. 📁 Вложенные папки команд (Nested Folder Navigation)

**Описание:** Улучшение существующей системы папок - добавление визуальной навигации и breadcrumbs.

**Текущее состояние:**
- ✅ Папки уже реализованы в прошивке (16 папок, вложенность 4 уровня)
- ✅ Toggle-логика работает (повторное нажатие = выход)
- ❌ UI не показывает текущий путь навигации
- ❌ Нет визуального индикатора "вы в папке"

**Требуемые улучшения:**

**UI компоненты:**
```
┌─────────────────────────────────────┐
│ Profile: Work                       │
│ Path: Root > Git > Branches         │  ← Breadcrumbs
├─────────────────────────────────────┤
│ [←Back] [Feature] [Bugfix] [Main]  │  ← Кнопки
└─────────────────────────────────────┘
```

**Функции:**
- Breadcrumbs навигация (показывает путь: Root > Git > Branches)
- Кнопка "Back" всегда видна когда внутри папки
- Подсветка кнопки-папки, которая открыла текущий контекст
- История навигации (можно вернуться на N уровней назад)

**Масштаб изменений:**
- 🟢 **Прошивка:** Минимальные изменения
  - Уже реализовано, нужно только добавить команду GET_FOLDER_STACK
  - ~50 строк кода

- 🟡 **C# Backend:** Небольшие изменения
  - Добавить команду GetFolderStackCommand
  - Добавить события навигации
  - ~100 строк кода

- 🟡 **UI:** Средние изменения
  - Breadcrumbs компонент
  - Визуализация текущей папки
  - Анимации переходов
  - ~300-400 строк кода

**Оценка:** 1-2 дня разработки

---

### 3. 🎨 ColorPicker для LED

**Описание:** Заменить текущий ввод RGB значений на визуальный ColorPicker.

**Текущее состояние:**
```csharp
// Сейчас: числовые поля
R: [255] G: [128] B: [0]
```

**Требуемое:**
```
┌──────────────────────┐
│  🎨 Color Picker     │
│  ┌────────────────┐  │
│  │    [Color]     │  │  ← Визуальный выбор
│  │   Wheel/Grid   │  │
│  └────────────────┘  │
│  RGB: #FF8000        │  ← Hex код
│  R: 255 G: 128 B: 0  │  ← Числа (опционально)
│  Brightness: [████░] │  ← Слайдер яркости
│  Effect: [Static ▼]  │  ← Выбор эффекта
└──────────────────────┘
```

**Компоненты:**
- Color wheel или color grid
- Hex input (#RRGGBB)
- RGB sliders (опционально)
- Brightness slider (0-255)
- Effect dropdown (Static, Breathing, Rainbow, Wave)
- Предустановленные цвета (палитра)

**Масштаб изменений:**
- 🟢 **Прошивка:** Без изменений
- 🟢 **C# Backend:** Без изменений
- 🟡 **UI:** Средние изменения
  - Создать ColorPickerControl (можно использовать готовую библиотеку)
  - Интегрировать в ButtonConfigDialog
  - Добавить предустановленные палитры
  - ~200-300 строк кода (или меньше с библиотекой)

**Рекомендуемые библиотеки:**
- `Avalonia.ColorPicker` (если существует)
- Или custom control на Canvas

**Оценка:** 0.5-1 день разработки

---

## 📋 Оставшиеся TODO (из анализа)

### 🟡 Средний приоритет

#### 4. Запуск UI из TrayApp
- **Файл:** `TrayIconViewModel.cs:176`
- **Описание:** Реализовать `Process.Start()` для запуска MacroKeyboard.UI
- **Оценка:** 0.5 дня

#### 5. Сохранение настроек
- **Файл:** `SettingsViewModel.cs:45`
- **Описание:** Использовать AppDataManager для JSON сериализации настроек
- **Оценка:** 0.5 дня

#### 6. Диалог выбора изображения
- **Файл:** `ButtonConfigDialogViewModel.cs:67`
- **Описание:** Avalonia file picker для выбора изображений
- **Оценка:** 0.5 дня

#### 7. Текст на изображениях
- **Файл:** `ImageService.cs:93`
- **Описание:** Добавить SixLabors.Fonts и рендеринг текста
- **Оценка:** 1 день

### 🟢 Низкий приоритет

#### 8. ConvertBack в BoolToColorConverter
- **Файл:** `BoolToColorConverter.cs:24`
- **Описание:** Вернуть `Binding.DoNothing` вместо исключения
- **Оценка:** 0.1 дня

---

## 📊 Сводная оценка масштаба

### Новые требования

| Функция | Прошивка | Backend | UI | Общая оценка |
|---------|----------|---------|-----|--------------|
| **Action Sequences** | 🔴 Средние (200-300 строк) | 🔴 Средние (150-200 строк) | 🔴 Значительные (400-500 строк) | **2-3 дня** |
| **Nested Folders UI** | 🟢 Минимальные (50 строк) | 🟡 Небольшие (100 строк) | 🟡 Средние (300-400 строк) | **1-2 дня** |
| **ColorPicker** | 🟢 Нет изменений | 🟢 Нет изменений | 🟡 Средние (200-300 строк) | **0.5-1 день** |

### Оставшиеся TODO

| Задача | Сложность | Оценка |
|--------|-----------|--------|
| Запуск UI из TrayApp | 🟢 Низкая | 0.5 дня |
| Сохранение настроек | 🟢 Низкая | 0.5 дня |
| Диалог выбора изображения | 🟢 Низкая | 0.5 дня |
| Текст на изображениях | 🟡 Средняя | 1 день |
| ConvertBack | 🟢 Минимальная | 0.1 дня |

**Итого:** 
- **Новые требования:** 3.5-6 дней
- **Оставшиеся TODO:** 3.1 дня
- **Общий объем:** ~6.5-9 дней разработки

---

## 🎯 Рекомендуемый порядок реализации

### Фаза 1: Quick Wins (1-2 дня)
1. ✅ ColorPicker для LED (0.5-1 день)
2. ✅ Nested Folders UI улучшения (1-2 дня)
3. ✅ Запуск UI из TrayApp (0.5 дня)
4. ✅ Сохранение настроек (0.5 дня)
5. ✅ Диалог выбора изображения (0.5 дня)

### Фаза 2: Средние задачи (2 дня)
6. ✅ Текст на изображениях (1 день)
7. ✅ ConvertBack (0.1 дня)

### Фаза 3: Крупные фичи (2-3 дня)
8. ✅ Action Sequences (2-3 дня)
   - День 1: Прошивка + протокол
   - День 2: Backend + модели
   - День 3: UI редактор последовательностей

---

## 🔧 Технические детали

### Action Sequences - Детальный план

#### Прошивка
```c
// profile_types.h
#define MAX_SEQUENCE_STEPS 8
#define ACTION_TYPE_SEQUENCE 0x05

typedef struct {
    action_type_t action_type;
    uint16_t delay_ms;
    uint16_t data_len;
    uint8_t data[32];  // Ограничение на размер одного шага
} sequence_step_t;

// action_executor.c
esp_err_t execute_action_sequence(const sequence_step_t* steps, uint8_t num_steps) {
    for (uint8_t i = 0; i < num_steps; i++) {
        // Выполнить действие
        execute_action(steps[i].action_type, steps[i].data, steps[i].data_len);
        
        // Задержка перед следующим шагом
        if (i < num_steps - 1 && steps[i].delay_ms > 0) {
            vTaskDelay(pdMS_TO_TICKS(steps[i].delay_ms));
        }
    }
    return ESP_OK;
}
```

#### C# Models
```csharp
public class ActionSequence : ActionConfig
{
    public override ActionType ActionType => ActionType.Sequence;
    public List<SequenceStep> Steps { get; set; } = new();
    
    public override byte[] ToBytes()
    {
        var data = new List<byte>();
        data.Add((byte)Steps.Count);
        
        foreach (var step in Steps)
        {
            data.Add((byte)step.Action.ActionType);
            data.AddRange(BitConverter.GetBytes(step.DelayMs));
            var actionBytes = step.Action.ToBytes();
            data.AddRange(BitConverter.GetBytes((ushort)actionBytes.Length));
            data.AddRange(actionBytes);
        }
        
        return data.ToArray();
    }
}
```

#### UI Component
```xml
<UserControl x:Class="MacroKeyboard.UI.Controls.SequenceEditor">
    <StackPanel>
        <TextBlock Text="Action Sequence" FontWeight="Bold"/>
        
        <!-- Список шагов -->
        <ItemsControl Items="{Binding Steps}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border BorderBrush="Gray" BorderThickness="1" Margin="5">
                        <StackPanel>
                            <TextBlock Text="{Binding Action.ActionType}"/>
                            <NumericUpDown Value="{Binding DelayMs}" 
                                          Minimum="0" Maximum="5000"
                                          Watermark="Delay (ms)"/>
                            <Button Content="Remove" Command="{Binding RemoveCommand}"/>
                        </StackPanel>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        
        <!-- Кнопки управления -->
        <StackPanel Orientation="Horizontal">
            <Button Content="+ Add Keyboard" Command="{Binding AddKeyboardCommand}"/>
            <Button Content="+ Add Delay" Command="{Binding AddDelayCommand}"/>
            <Button Content="+ Add Profile Switch" Command="{Binding AddProfileSwitchCommand}"/>
        </StackPanel>
    </StackPanel>
</UserControl>
```

---

## 📚 Связанные документы

- [`TODO_ANALYSIS.md`](../software/TODO_ANALYSIS.md) - Полный анализ TODO
- [`CRITICAL_FIXES_REPORT.md`](../software/CRITICAL_FIXES_REPORT.md) - Отчет об исправлениях
- [`PROFILE_LOADING_IMPLEMENTATION.md`](../software/PROFILE_LOADING_IMPLEMENTATION.md) - Загрузка профилей
- [`folder_system.md`](folder_system.md) - Система папок
- [`protocol.md`](protocol.md) - Протокол обмена

---

## 🎨 UI/UX Улучшения

### ColorPicker - Mockup
```
┌────────────────────────────────────┐
│ Button LED Configuration           │
├────────────────────────────────────┤
│                                    │
│  ┌──────────────┐  ┌────────────┐ │
│  │              │  │ Presets:   │ │
│  │   [Color]    │  │ ⬤ Red      │ │
│  │    Wheel     │  │ ⬤ Green    │ │
│  │              │  │ ⬤ Blue     │ │
│  └──────────────┘  │ ⬤ Yellow   │ │
│                    │ ⬤ Purple   │ │
│  Hex: #FF8000      │ ⬤ Cyan     │ │
│  [████████████]    └────────────┘ │
│                                    │
│  Brightness: 80%  [████████░░]    │
│  Effect: Static ▼                  │
│                                    │
│  [Cancel]              [Apply]     │
└────────────────────────────────────┘
```

### Sequence Editor - Mockup
```
┌────────────────────────────────────┐
│ Action Sequence Editor             │
├────────────────────────────────────┤
│                                    │
│  Step 1: Keyboard Action           │
│  ├─ Keys: Ctrl+C                   │
│  └─ Delay: 100ms [───────░]        │
│                                    │
│  Step 2: Keyboard Action           │
│  ├─ Keys: Ctrl+V                   │
│  └─ Delay: 0ms                     │
│                                    │
│  [+ Add Step ▼]                    │
│    ├─ Keyboard Action              │
│    ├─ Profile Switch               │
│    ├─ Custom HID                   │
│    └─ Delay Only                   │
│                                    │
│  [Test Sequence]  [Save]  [Cancel] │
└────────────────────────────────────┘
```

---

## ✅ Критерии готовности

### Action Sequences
- [ ] Прошивка поддерживает ACTION_TYPE_SEQUENCE
- [ ] Можно создать последовательность до 8 шагов
- [ ] Задержки работают корректно (0-5000ms)
- [ ] UI позволяет создавать/редактировать последовательности
- [ ] Последовательности сохраняются в профиле
- [ ] Последовательности загружаются с устройства

### Nested Folders UI
- [ ] Breadcrumbs показывает текущий путь
- [ ] Кнопка Back работает
- [ ] Визуальная индикация "вы в папке"
- [ ] Анимации переходов плавные
- [ ] История навигации сохраняется

### ColorPicker
- [ ] Визуальный выбор цвета работает
- [ ] Hex input синхронизирован с picker
- [ ] Brightness slider работает
- [ ] Effect dropdown работает
- [ ] Предустановленные цвета доступны
- [ ] Изменения применяются к LED

---

**Последнее обновление:** 2026-04-15
**Версия:** 1.0
