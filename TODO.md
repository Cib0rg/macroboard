# TODO — Известные проблемы и задачи

**Дата создания:** 2026-04-23  
**Последнее обновление:** 2026-04-23

---

## 🔴 Критические (блокируют функциональность)

### 1. UI: Нет редактора содержимого папок
- **Где:** `software/src/MacroKeyboard.UI/ViewModels/ButtonConfigDialogViewModel.cs`
- **Проблема:** Тип `ActionType.Folder` есть в списке экшенов, но нет UI для настройки кнопок внутри папки. Пользователь может выбрать "Folder", но не может задать содержимое.
- **Решение:** Добавить вложенный редактор папок (список кнопок внутри папки) или отдельный диалог.

### 2. Прошивка: Дисплеи не обновляются при входе/выходе из папки
- **Где:** `firmware/main/profile/profile_manager.c:268-274`, `firmware/main/profile/profile_manager.c:310-316`
- **Проблема:** При входе в папку LED обновляются, но изображения на дисплеях GC9A01 — нет. Есть TODO-комментарии в коде.
- **Решение:** Реализовать загрузку и отображение изображений кнопок папки через `image_storage_load()` + JPEG decode + GC9A01.

### 3. Прошивка: Нет кнопки "Назад" для выхода из папки
- **Где:** `firmware/main/profile/action_executor.c:72-91`
- **Проблема:** Выход из папки возможен только нажатием той же кнопки, которая привела в папку (toggle). Если пользователь забыл какая кнопка — нет способа выйти (кроме смены профиля).
- **Решение:** Зарезервировать одну кнопку (например, последнюю) как "Назад" внутри папки, или добавить выход по длинному нажатию.

---

## 🟡 Важные (ухудшают UX)

### 4. UI: `CMD_GET_PROFILE_INFO` (0x11) не реализован в IpcCommandHandler
- **Где:** `software/src/MacroKeyboard.Backend/Services/IpcCommandHandler.cs`
- **Проблема:** Команда получения информации о профиле с устройства не обрабатывается через IPC.
- **Решение:** Добавить обработчик в `IpcCommandHandler`.

### 5. UI: Нет визуальной индикации нахождения в папке
- **Где:** Dashboard / Profile Editor
- **Проблема:** PC-сторона не знает, что прошивка находится внутри папки. Нет события/команды для синхронизации состояния папки.
- **Решение:** Добавить IPC-событие `folder.entered` / `folder.exited` и отображать breadcrumb в UI.

### 6. NuGet: Уязвимости в зависимостях
- **Где:** `software/src/MacroKeyboard.Infrastructure/MacroKeyboard.Infrastructure.csproj`, `software/src/MacroKeyboard.UI/MacroKeyboard.UI.csproj`
- **Проблема:** `SixLabors.ImageSharp 3.1.0` имеет множество известных уязвимостей (high/moderate). `Tmds.DBus.Protocol 0.90.3` — high severity.
- **Решение:** Обновить `SixLabors.ImageSharp` до последней версии (≥3.1.6). Обновить `Tmds.DBus.Protocol`.

### 7. Avalonia: Устаревшие API
- **Где:** `software/src/MacroKeyboard.UI/Views/ButtonConfigDialog.axaml`, `ProfileEditorView.axaml`
- **Проблема:** `TextBox.Watermark` помечен как obsolete в Avalonia 12 — нужно использовать `PlaceholderText`.
- **Решение:** Заменить `Watermark` на `PlaceholderText` во всех AXAML файлах.

### 8. UI: MainWindow не имеет публичного конструктора
- **Где:** `software/src/MacroKeyboard.UI/Views/MainWindow.axaml`
- **Проблема:** Avalonia warning AVLN3001 — XAML resource не будет доступен через runtime loader.
- **Решение:** Добавить публичный конструктор без параметров в `MainWindow.axaml.cs`.

---

## 🟢 Улучшения (nice to have)

### 9. Прошивка: Расширенная поддержка символов в `usb_hid_keyboard_type_text()`
- **Где:** `firmware/main/usb/usb_hid_keyboard.c:53-90`
- **Проблема:** Поддерживаются только базовые ASCII символы (a-z, A-Z, 0-9, пробел, Enter). Нет поддержки знаков препинания, спецсимволов, Unicode.
- **Решение:** Расширить таблицу маппинга ASCII→HID keycode (добавить `.`, `,`, `!`, `@`, `-`, `_`, `/`, `\`, `(`, `)` и т.д.).

### 10. UI: Нет предпросмотра изображений кнопок в Profile Editor
- **Где:** `software/src/MacroKeyboard.UI/Views/ProfileEditorView.axaml`
- **Проблема:** Кнопки в сетке показывают только "Button N" и тип экшена, но не превью изображения.
- **Решение:** Добавить `Image` контрол в шаблон кнопки, загружать превью из `ButtonConfig.ImagePath`.

### 11. UI: Нет индикации прогресса при отправке профиля на устройство
- **Где:** `software/src/MacroKeyboard.UI/ViewModels/ProfileEditorViewModel.cs`
- **Проблема:** `SyncProgress` свойство есть, но не обновляется во время отправки (Backend не шлёт промежуточные отчёты).
- **Решение:** Добавить IPC-события прогресса из Backend при `SendProfileToDeviceAsync`.

### 12. Backend: `DeviceService.ButtonReleased` event никогда не используется
- **Где:** `software/src/MacroKeyboard.Infrastructure/Services/DeviceService.cs:32`
- **Проблема:** Warning CS0067 — событие объявлено но не вызывается. Прошивка не отправляет `EVENT_BUTTON_RELEASED`.
- **Решение:** Либо добавить обработку в прошивке (отправлять событие при отпускании кнопки), либо убрать неиспользуемое событие.

### 13. UI: Нет валидации ввода в ButtonConfigDialog
- **Где:** `software/src/MacroKeyboard.UI/ViewModels/ButtonConfigDialogViewModel.cs`
- **Проблема:** Нет проверки корректности введённых данных (пустой KeySequence, невалидный ProfileId для ProfileSwitch, и т.д.).
- **Решение:** Добавить валидацию перед сохранением.

### 14. Backend: Имена USB-интерфейсов в ОС
- **Где:** Системный уровень
- **Проблема:** Vendor-интерфейс показывается как "Vendor Interface", HID как "USB устройство ввода". На Windows может потребоваться кастомный INF-файл.
- **Решение:** Создать INF-файл для Windows с правильными именами. На Linux — обновить udev rules.

### 15. UI: Тёмная тема захардкожена
- **Где:** `software/src/MacroKeyboard.UI/Views/*.axaml`
- **Проблема:** Цвета фона (`#2D2D30`, `#3E3E42`) захардкожены вместо использования Avalonia theme resources.
- **Решение:** Использовать `{DynamicResource ...}` для поддержки светлой/тёмной темы.

---

## ✅ Исправлено (в текущей сессии)

- [x] IPC десериализация: `JObject` → типизированные объекты (добавлен `GetData<T>()`)
- [x] IPC: `IpcResponse` не распознавался клиентом (добавлена проверка `"Success"` поля)
- [x] IPC: Сервер отправлял generic response вместо обработки команд (добавлен `IpcCommandHandler`)
- [x] USB: Устройство не переподключалось после отключения (добавлен `_deviceLost` флаг, `HandleDisconnect()`)
- [x] USB: Лог засорялся "Device disconnected during read" (monitor loop теперь break'ает)
- [x] Профили: Не загружались при открытии Profile Editor (добавлен `OnAttachedToVisualTree`)
- [x] Профили: `ActionConfig` не десериализовался из JSON (добавлен `ActionConfigConverter`)
- [x] Профили: `ActionConfigConverter.WriteJson()` вызывал stack overflow (добавлен `CanWrite = false`)
- [x] Профили: Save/Load не открывали диалог выбора файла (добавлены file picker'ы)
- [x] ButtonConfigDialog: `StorageProvider not set` при Browse Image (добавлен `OnOpened`)
- [x] Прошивка: Keyboard action с текстом не работал (добавлена поддержка `type_text` при `keycode=0`)
- [x] Прошивка: Button press events не отправлялись для keyboard actions (добавлен `protocol_send_event` в `buttons.c`)
- [x] Dashboard: Не показывал encoder events (добавлена обработка `EncoderRotated`)
- [x] Dashboard: Не запрашивал device info при подключении (добавлен `RequestDeviceInfoAsync`)
