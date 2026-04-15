# Анализ TODO и план развития C# приложения

**Дата анализа:** 2026-04-14  
**Дата обновления:** 2026-04-15

---

## ✅ ИСПРАВЛЕНО (2026-04-14)

### 1. ~~Загрузка профиля с устройства~~ ✅
- **Статус:** **ИСПРАВЛЕНО**
- **Решение:** Расширен протокол прошивки, добавлены команды GET_BUTTON_ACTION, GET_LED_COLOR, GET_BUTTON_IMAGE
- **Детали:** См. [`PROFILE_LOADING_IMPLEMENTATION.md`](PROFILE_LOADING_IMPLEMENTATION.md)

### 2. ~~Managed плагины~~ ✅
- **Статус:** **ИСПРАВЛЕНО**
- **Решение:** Реализована загрузка через AssemblyLoadContext с изоляцией
- **Детали:** См. [`CRITICAL_FIXES_REPORT.md`](CRITICAL_FIXES_REPORT.md)

### 3. ~~Async void в обработчиках событий~~ ✅
- **Статус:** **ИСПРАВЛЕНО**
- **Решение:** Добавлена обработка исключений во всех async void методах
- **Детали:** См. [`CRITICAL_FIXES_REPORT.md`](CRITICAL_FIXES_REPORT.md)

---

## 🎯 НОВЫЕ ТРЕБОВАНИЯ (2026-04-15)

### 1. **Action Sequences - Последовательные действия** 🆕
- **Приоритет:** **ВЫСОКИЙ**
- **Описание:** Возможность назначить на кнопку несколько действий, выполняемых последовательно с задержками
- **Примеры:** 
  - Ctrl+C → 100ms → Ctrl+V
  - Win+R → 200ms → "notepad" → Enter
- **Масштаб:** 2-3 дня разработки
  - Прошивка: 200-300 строк (новый ACTION_TYPE_SEQUENCE)
  - Backend: 150-200 строк (модели и команды)
  - UI: 400-500 строк (редактор последовательностей)
- **Детали:** См. [`ROADMAP.md`](../plans/ROADMAP.md#1-action-sequences)

### 2. **Nested Folders UI - Улучшение навигации по папкам** 🆕
- **Приоритет:** **СРЕДНИЙ**
- **Описание:** Визуальная навигация с breadcrumbs и индикацией текущей папки
- **Функции:**
  - Breadcrumbs (Root > Git > Branches)
  - Кнопка "Back"
  - Подсветка активной папки
  - История навигации
- **Масштаб:** 1-2 дня разработки
  - Прошивка: 50 строк (команда GET_FOLDER_STACK)
  - Backend: 100 строк (события навигации)
  - UI: 300-400 строк (breadcrumbs компонент)
- **Детали:** См. [`ROADMAP.md`](../plans/ROADMAP.md#2-nested-folders-ui)

### 3. **ColorPicker для LED** 🆕
- **Приоритет:** **СРЕДНИЙ**
- **Описание:** Заменить числовые поля RGB на визуальный ColorPicker
- **Функции:**
  - Color wheel/grid
  - Hex input (#RRGGBB)
  - Brightness slider
  - Effect dropdown
  - Предустановленные цвета
- **Масштаб:** 0.5-1 день разработки
  - Прошивка: без изменений
  - Backend: без изменений
  - UI: 200-300 строк (ColorPicker control)
- **Детали:** См. [`ROADMAP.md`](../plans/ROADMAP.md#3-colorpicker)

---

## 🟡 СРЕДНИЙ ПРИОРИТЕТ (Оставшиеся TODO)

### 4. **Запуск UI из TrayApp**
- **Файл:** [`TrayIconViewModel.cs:176`](src/MacroKeyboard.TrayApp/ViewModels/TrayIconViewModel.cs:176)
- **Проблема:** Метод `ShowConfiguration()` не запускает UI приложение
- **Решение:** Реализовать `Process.Start()` для MacroKeyboard.UI с проверкой на уже запущенный процесс
- **Оценка:** 0.5 дня
- **Код:**
```csharp
public void ShowConfiguration()
{
    var uiPath = Path.Combine(AppContext.BaseDirectory, "MacroKeyboard.UI.exe");
    if (File.Exists(uiPath))
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uiPath,
            UseShellExecute = true
        });
    }
}
```

### 5. **Сохранение настроек**
- **Файл:** [`SettingsViewModel.cs:45`](src/MacroKeyboard.UI/ViewModels/SettingsViewModel.cs:45)
- **Проблема:** Настройки не сохраняются между сеансами
- **Решение:** Использовать `AppDataManager` для JSON сериализации
- **Оценка:** 0.5 дня
- **Код:**
```csharp
private async Task SaveSettings()
{
    var settings = new AppSettings
    {
        AutoStart = AutoStart,
        MinimizeToTray = MinimizeToTray,
        // ...
    };
    var json = JsonSerializer.Serialize(settings);
    var path = Path.Combine(AppDataManager.GetAppDataPath(), "settings.json");
    await File.WriteAllTextAsync(path, json);
}
```

### 6. **Диалог выбора изображения**
- **Файл:** [`ButtonConfigDialogViewModel.cs:67`](src/MacroKeyboard.UI/ViewModels/ButtonConfigDialogViewModel.cs:67)
- **Проблема:** Кнопка "Browse" не работает
- **Решение:** Использовать Avalonia file picker
- **Оценка:** 0.5 дня
- **Код:**
```csharp
private async Task BrowseImage()
{
    var dialog = new OpenFileDialog
    {
        Title = "Select Image",
        Filters = new List<FileDialogFilter>
        {
            new() { Name = "Images", Extensions = { "jpg", "jpeg", "png" } }
        }
    };
    var result = await dialog.ShowAsync(GetWindow());
    if (result?.Length > 0)
    {
        ImagePath = result[0];
    }
}
```

### 7. **Текст на изображениях**
- **Файл:** [`ImageService.cs:93`](src/MacroKeyboard.Infrastructure/Services/ImageService.cs:93)
- **Проблема:** Метод `CreateTextImageAsync()` не добавляет текст
- **Решение:** Добавить `SixLabors.Fonts` и реализовать рендеринг
- **Оценка:** 1 день
- **Код:**
```csharp
public async Task<byte[]?> CreateTextImageAsync(string text, int fontSize = 24)
{
    using var image = new Image<Rgba32>(TargetSize, TargetSize);
    image.Mutate(x => x.BackgroundColor(Color.Black));
    
    var font = SystemFonts.CreateFont("Arial", fontSize);
    var textOptions = new TextOptions(font)
    {
        Origin = new PointF(TargetSize / 2, TargetSize / 2),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    
    image.Mutate(x => x.DrawText(textOptions, text, Color.White));
    
    using var ms = new MemoryStream();
    await image.SaveAsJpegAsync(ms);
    return ms.ToArray();
}
```

---

## 🟢 НИЗКИЙ ПРИОРИТЕТ

### 8. **ConvertBack в BoolToColorConverter**
- **Файл:** [`BoolToColorConverter.cs:24`](src/MacroKeyboard.UI/Converters/BoolToColorConverter.cs:24)
- **Проблема:** Выбрасывает `NotImplementedException`
- **Решение:** Вернуть `Binding.DoNothing`
- **Оценка:** 0.1 дня
- **Код:**
```csharp
public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
{
    return AvaloniaProperty.UnsetValue;
}
```

---

## 📊 СТАТИСТИКА

### Выполнено
- **Критичных исправлений:** 3/3 (100%)
- **Строк кода добавлено:** ~1500
- **Файлов изменено:** 10
- **Новых файлов:** 4

### Осталось

| Категория | Количество | Оценка |
|-----------|------------|--------|
| **Новые требования** | 3 | 3.5-6 дней |
| **Средний приоритет** | 4 | 2.5 дня |
| **Низкий приоритет** | 1 | 0.1 дня |
| **ИТОГО** | 8 | **6.1-8.6 дней** |

---

## 🔍 ДОПОЛНИТЕЛЬНЫЕ НАБЛЮДЕНИЯ

### Потенциальные улучшения (не критичные)

1. **Валидация входных данных**
   - В `ProfileService.CreateProfileAsync()` нет проверки на максимум 5 профилей
   - Рекомендация: Добавить проверку и выбросить исключение

2. **Обработка ошибок в HidDeviceManager**
   - Методы `WriteAsync()` и `ReadAsync()` логируют ошибки без деталей
   - Рекомендация: Добавить специфичные исключения (DeviceNotConnectedException, WriteTimeoutException)

3. **Таймауты команд**
   - В `DeviceService` некоторые команды могут зависнуть
   - Рекомендация: Добавить глобальный таймаут (5-10 секунд)

4. **Утечка ресурсов в плагинах**
   - `ExecutablePluginInstance` может не завершиться при сбое
   - Рекомендация: Добавить мониторинг и автоматический перезапуск

5. **Обновление SixLabors.ImageSharp**
   - Текущая версия 3.1.0 имеет известные уязвимости
   - Рекомендация: Обновить до последней стабильной версии

---

## 🎯 РЕКОМЕНДУЕМЫЙ ПОРЯДОК РЕАЛИЗАЦИИ

### Фаза 1: Quick Wins (2-3 дня)
Быстрые улучшения UX без изменений прошивки:

1. **ColorPicker для LED** (0.5-1 день) 🆕
   - Только UI изменения
   - Значительно улучшает UX
   - Можно использовать готовую библиотеку

2. **Запуск UI из TrayApp** (0.5 дня)
   - Простая реализация через Process.Start()
   - Критично для удобства пользователя

3. **Сохранение настроек** (0.5 дня)
   - JSON сериализация через AppDataManager
   - Улучшает UX

4. **Диалог выбора изображения** (0.5 дня)
   - Avalonia file picker
   - Улучшает UX

5. **ConvertBack** (0.1 дня)
   - Тривиальное исправление

### Фаза 2: Средние задачи (2-3 дня)

6. **Nested Folders UI** (1-2 дня) 🆕
   - Минимальные изменения прошивки
   - Значительно улучшает навигацию
   - Breadcrumbs + визуализация

7. **Текст на изображениях** (1 день)
   - Добавление SixLabors.Fonts
   - Полезная функция для быстрого создания кнопок

### Фаза 3: Крупные фичи (2-3 дня)

8. **Action Sequences** (2-3 дня) 🆕
   - Требует изменений прошивки, backend и UI
   - Мощная функция для автоматизации
   - Разбить на подзадачи:
     - День 1: Прошивка + протокол
     - День 2: Backend + модели
     - День 3: UI редактор

---

## 📋 Чеклист перед релизом

### Обязательно
- [ ] Все критичные TODO исправлены ✅
- [ ] Код компилируется без ошибок ✅
- [ ] Базовые функции работают ✅
- [ ] Документация обновлена ✅

### Желательно (Фаза 1)
- [ ] ColorPicker реализован
- [ ] UI запускается из трея
- [ ] Настройки сохраняются
- [ ] Диалог выбора файлов работает

### Опционально (Фаза 2-3)
- [ ] Nested Folders UI улучшен
- [ ] Текст на изображениях работает
- [ ] Action Sequences реализованы

---

## 📚 Связанные документы

- [`ROADMAP.md`](../plans/ROADMAP.md) - Полная дорожная карта с деталями
- [`CRITICAL_FIXES_REPORT.md`](CRITICAL_FIXES_REPORT.md) - Отчет об исправлениях
- [`PROFILE_LOADING_IMPLEMENTATION.md`](PROFILE_LOADING_IMPLEMENTATION.md) - Загрузка профилей
- [`folder_system.md`](../plans/folder_system.md) - Система папок
- [`protocol.md`](../plans/protocol.md) - Протокол обмена

---

## 🔄 История изменений

### 2026-04-15
- ✅ Добавлены новые требования: Action Sequences, Nested Folders UI, ColorPicker
- ✅ Обновлены оценки и приоритеты
- ✅ Создана детальная дорожная карта

### 2026-04-14
- ✅ Исправлены все критичные проблемы
- ✅ Реализована загрузка профилей с устройства
- ✅ Реализованы managed плагины
- ✅ Исправлены async void обработчики

---

**Последнее обновление:** 2026-04-15  
**Статус:** Критичные проблемы решены, планирование новых функций
