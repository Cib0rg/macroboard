# MacroKeyboard - Статус разработки

**Дата:** 2026-04-15  
**Версия:** 1.0-beta

---

## 📊 Общий статус проекта

### Компоненты

| Компонент | Статус | Готовность | Примечания |
|-----------|--------|------------|------------|
| **Прошивка (ESP32-S3)** | ✅ Стабильно | 95% | Основной функционал работает |
| **Backend (C#)** | ✅ Стабильно | 90% | Все критичные функции реализованы |
| **TrayApp** | 🟡 Работает | 80% | Нужно добавить запуск UI |
| **UI (Avalonia)** | 🟡 Работает | 75% | Нужны UX улучшения |
| **Плагины** | ✅ Готово | 100% | Поддержка .NET и executable |
| **Протокол** | ✅ Расширен | 95% | Добавлены команды чтения |

### Общая готовность: **85%**

---

## ✅ Реализованные функции

### Прошивка
- ✅ USB HID (Keyboard + Raw HID)
- ✅ 10 кнопок с RGB LED
- ✅ Круглые дисплеи GC9A01 (160x160)
- ✅ Энкодер для переключения профилей
- ✅ 5 профилей с сохранением в NVS
- ✅ Система папок (16 папок, вложенность 4 уровня)
- ✅ Типы действий: Keyboard, CustomHID, ProfileSwitch, Folder
- ✅ LED эффекты: Static, Breathing, Rainbow, Wave
- ✅ Загрузка изображений через USB
- ✅ Протокол обмена с PC

### Backend
- ✅ HID коммуникация (кроссплатформенная)
- ✅ Протокол обмена с устройством
- ✅ Управление профилями
- ✅ Загрузка/выгрузка профилей с устройства
- ✅ IPC сервер для связи с UI
- ✅ WebSocket сервер для плагинов
- ✅ Система плагинов (.NET + executable)
- ✅ Обработка событий от устройства
- ✅ Логирование

### UI
- ✅ Dashboard с информацией об устройстве
- ✅ Редактор профилей
- ✅ Конфигурация кнопок
- ✅ Загрузка изображений
- ✅ Настройки LED
- ✅ Настройки приложения
- ✅ Системный трей

---

## 🔧 Недавние исправления (2026-04-14)

### Критичные проблемы (все исправлены)

#### 1. Async void в обработчиках событий ✅
**Проблема:** 6 обработчиков использовали `async void`, что могло привести к необработанным исключениям.

**Решение:** Добавлена обработка исключений через try-catch во всех методах.

**Файл:** [`EventRouter.cs`](software/src/MacroKeyboard.Backend/Services/EventRouter.cs)

#### 2. Загрузка профилей с устройства ✅
**Проблема:** Метод `LoadProfileFromDeviceAsync()` выбрасывал `NotImplementedException`.

**Решение:** 
- Расширен протокол прошивки (CMD_GET_BUTTON_ACTION, CMD_GET_LED_COLOR, CMD_GET_BUTTON_IMAGE)
- Созданы команды в C# (GetButtonActionCommand, GetLedColorCommand)
- Полностью реализован метод загрузки профилей

**Файлы:** 
- Прошивка: [`protocol_types.h`](firmware/main/protocol/protocol_types.h), [`protocol_handler.c`](firmware/main/protocol/protocol_handler.c)
- C#: [`GetButtonActionCommand.cs`](software/src/MacroKeyboard.Communication/Commands/GetButtonActionCommand.cs), [`ProfileService.cs`](software/src/MacroKeyboard.Infrastructure/Services/ProfileService.cs)

**Детали:** [`PROFILE_LOADING_IMPLEMENTATION.md`](software/PROFILE_LOADING_IMPLEMENTATION.md)

#### 3. Managed плагины ✅
**Проблема:** Система плагинов не поддерживала .NET плагины.

**Решение:**
- Создан интерфейс `IPlugin`
- Реализована загрузка через `AssemblyLoadContext` с изоляцией
- Поддержка выгрузки плагинов

**Файлы:** [`IPlugin.cs`](software/src/MacroKeyboard.Shared/Plugin/IPlugin.cs), [`PluginManager.cs`](software/src/MacroKeyboard.Backend/Plugin/PluginManager.cs)

**Детали:** [`CRITICAL_FIXES_REPORT.md`](software/CRITICAL_FIXES_REPORT.md)

---

## 🎯 Планируемые улучшения

### Новые функции (приоритет)

#### 1. Action Sequences - Последовательные действия 🆕
**Приоритет:** Высокий  
**Оценка:** 2-3 дня

Возможность назначить на кнопку несколько действий с задержками:
- Ctrl+C → 100ms → Ctrl+V
- Win+R → 200ms → "notepad" → Enter

**Требует изменений:**
- Прошивка: 200-300 строк
- Backend: 150-200 строк
- UI: 400-500 строк

#### 2. ColorPicker для LED 🆕
**Приоритет:** Средний  
**Оценка:** 0.5-1 день

Визуальный выбор цвета вместо числовых полей:
- Color wheel/grid
- Hex input
- Brightness slider
- Предустановленные цвета

**Требует изменений:**
- UI: 200-300 строк (только UI)

#### 3. Nested Folders UI 🆕
**Приоритет:** Средний  
**Оценка:** 1-2 дня

Улучшение навигации по папкам:
- Breadcrumbs (Root > Git > Branches)
- Кнопка "Back"
- Визуальная индикация текущей папки

**Требует изменений:**
- Прошивка: 50 строк
- Backend: 100 строк
- UI: 300-400 строк

### Оставшиеся TODO

| Задача | Приоритет | Оценка | Файл |
|--------|-----------|--------|------|
| Запуск UI из TrayApp | Средний | 0.5 дня | [`TrayIconViewModel.cs:176`](software/src/MacroKeyboard.TrayApp/ViewModels/TrayIconViewModel.cs) |
| Сохранение настроек | Средний | 0.5 дня | [`SettingsViewModel.cs:45`](software/src/MacroKeyboard.UI/ViewModels/SettingsViewModel.cs) |
| Диалог выбора изображения | Средний | 0.5 дня | [`ButtonConfigDialogViewModel.cs:67`](software/src/MacroKeyboard.UI/ViewModels/ButtonConfigDialogViewModel.cs) |
| Текст на изображениях | Средний | 1 день | [`ImageService.cs:93`](software/src/MacroKeyboard.Infrastructure/Services/ImageService.cs) |
| ConvertBack | Низкий | 0.1 дня | [`BoolToColorConverter.cs:24`](software/src/MacroKeyboard.UI/Converters/BoolToColorConverter.cs) |

**Общая оценка оставшейся работы:** 6-9 дней

---

## 📈 Метрики проекта

### Код

| Метрика | Значение |
|---------|----------|
| **Прошивка (C)** | ~8,000 строк |
| **Backend (C#)** | ~6,000 строк |
| **UI (C#/XAML)** | ~4,000 строк |
| **Всего** | ~18,000 строк |

### Файлы

| Компонент | Файлов |
|-----------|--------|
| Прошивка | 35 |
| Backend | 25 |
| UI | 20 |
| Документация | 15 |
| **Всего** | 95 |

### Последние изменения (2026-04-14)

| Метрика | Значение |
|---------|----------|
| Файлов изменено | 10 |
| Новых файлов | 4 |
| Строк добавлено | ~1,500 |
| Строк удалено | ~50 |

---

## 🏗️ Архитектура

### Компоненты системы

```
┌─────────────────────────────────────────────────────────┐
│                    User Interface                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │  TrayApp     │  │  UI (Avalonia)│  │   Plugins    │  │
│  │  (Systray)   │  │  (Config UI)  │  │  (External)  │  │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  │
└─────────┼──────────────────┼──────────────────┼─────────┘
          │                  │                  │
          │    IPC (Named    │                  │ WebSocket
          │      Pipes)      │                  │
          │                  │                  │
┌─────────▼──────────────────▼──────────────────▼─────────┐
│                      Backend Service                     │
│  ┌────────────────────────────────────────────────────┐ │
│  │  Device Manager  │  Profile Service  │  IPC Server │ │
│  │  Event Router    │  Image Service    │  WS Server  │ │
│  │  Plugin Manager  │  Device Service   │  Logging    │ │
│  └────────────────────────────────────────────────────┘ │
└──────────────────────────┬───────────────────────────────┘
                           │ HID Protocol
                           │ (USB)
┌──────────────────────────▼───────────────────────────────┐
│                    ESP32-S3 Firmware                     │
│  ┌────────────────────────────────────────────────────┐ │
│  │  USB HID (Keyboard + Raw)                          │ │
│  │  Protocol Handler  │  Profile Manager              │ │
│  │  Action Executor   │  Image Storage                │ │
│  │  Button Handler    │  LED Controller               │ │
│  │  Display Manager   │  NVS Storage                  │ │
│  └────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

### Протокол обмена

```
PC → Device:
- CMD_PING (0x01)
- CMD_GET_DEVICE_INFO (0x02)
- CMD_SET_PROFILE (0x10)
- CMD_GET_PROFILE_INFO (0x11)
- CMD_START_IMAGE_TRANSFER (0x20)
- CMD_IMAGE_DATA_CHUNK (0x21)
- CMD_END_IMAGE_TRANSFER (0x22)
- CMD_GET_BUTTON_IMAGE (0x23) ✨ NEW
- CMD_SET_BUTTON_ACTION (0x30)
- CMD_GET_BUTTON_ACTION (0x31) ✨ NEW
- CMD_SET_LED_COLOR (0x40)
- CMD_GET_LED_COLOR (0x42) ✨ NEW
- CMD_SAVE_PROFILE (0x50)

Device → PC:
- EVENT_BUTTON_PRESSED (0xF0)
- EVENT_ENCODER_ROTATED (0xF1)
- EVENT_PROFILE_CHANGED (0xF3)
- EVENT_DEVICE_READY (0xF4)
- EVENT_ERROR (0xFF)
```

---

## 🚀 Быстрый старт

### Сборка прошивки
```bash
cd firmware
./scripts/docker-build.sh
```

### Сборка Backend
```bash
cd software/src/MacroKeyboard.Backend
dotnet build
dotnet run
```

### Сборка UI
```bash
cd software/src/MacroKeyboard.UI
dotnet build
dotnet run
```

### Сборка TrayApp
```bash
cd software/src/MacroKeyboard.TrayApp
dotnet build
dotnet run
```

---

## 📚 Документация

### Основные документы

| Документ | Описание |
|----------|----------|
| [`README.md`](README.md) | Общее описание проекта |
| [`QUICK_BUILD_GUIDE.md`](QUICK_BUILD_GUIDE.md) | Быстрая сборка |
| [`TODO_ANALYSIS.md`](software/TODO_ANALYSIS.md) | Анализ TODO и план |
| [`ROADMAP.md`](plans/ROADMAP.md) | Дорожная карта развития |
| [`CRITICAL_FIXES_REPORT.md`](software/CRITICAL_FIXES_REPORT.md) | Отчет об исправлениях |
| [`PROFILE_LOADING_IMPLEMENTATION.md`](software/PROFILE_LOADING_IMPLEMENTATION.md) | Загрузка профилей |

### Технические документы

| Документ | Описание |
|----------|----------|
| [`protocol.md`](plans/protocol.md) | Протокол обмена |
| [`folder_system.md`](plans/folder_system.md) | Система папок |
| [`architecture.md`](plans/architecture.md) | Архитектура системы |
| [`storage.md`](plans/storage.md) | Система хранения |
| [`plugin_system.md`](software/plans/plugin_system.md) | Система плагинов |

### Setup документы

| Документ | Описание |
|----------|----------|
| [`firmware/SETUP.md`](firmware/SETUP.md) | Настройка прошивки |
| [`software/SETUP.md`](software/SETUP.md) | Настройка ПО |
| [`ENVIRONMENT_STATUS.md`](ENVIRONMENT_STATUS.md) | Статус окружения |

---

## 🐛 Известные проблемы

### Критичные
Нет критичных проблем ✅

### Некритичные

1. **SixLabors.ImageSharp уязвимости**
   - Версия 3.1.0 имеет известные уязвимости
   - Рекомендация: Обновить до последней версии
   - Приоритет: Средний

2. **Отсутствие валидации профилей**
   - Нет проверки на максимум 5 профилей
   - Может привести к ошибкам
   - Приоритет: Низкий

3. **Таймауты команд**
   - Некоторые команды могут зависнуть
   - Рекомендация: Добавить глобальный таймаут
   - Приоритет: Низкий

---

## 🎯 Следующие шаги

### Краткосрочные (1-2 недели)
1. ✅ Реализовать ColorPicker для LED
2. ✅ Добавить запуск UI из TrayApp
3. ✅ Реализовать сохранение настроек
4. ✅ Добавить диалог выбора файлов

### Среднесрочные (2-4 недели)
5. ✅ Улучшить навигацию по папкам (Breadcrumbs)
6. ✅ Добавить текст на изображениях
7. ✅ Реализовать Action Sequences

### Долгосрочные (1-2 месяца)
8. ✅ Добавить поддержку макросов
9. ✅ Реализовать облачную синхронизацию профилей
10. ✅ Добавить marketplace для плагинов

---

## 👥 Контрибьюторы

- **Основной разработчик:** Andrew P.
- **Дата начала:** 2026-03-01
- **Последнее обновление:** 2026-04-15

---

## 📄 Лицензия

MIT License (см. LICENSE файл)

---

**Статус:** Активная разработка  
**Версия:** 1.0-beta  
**Последнее обновление:** 2026-04-15
