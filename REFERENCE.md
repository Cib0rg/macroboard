# Architecture Reference

Текущее состояние реализации и неочевидные детали, которые не видны из кода напрямую.

---

## Что реализовано, что нет

### Firmware (реализовано)
- `hardware/`: gc9a01 (дисплей), display_mux, buttons, encoder, leds, night_mode
- `protocol/`: protocol_handler, packet_parser, image_transfer, protocol_types
- `profile/`: profile_manager, action_executor, profile_types
- `storage/`: profile_storage, image_storage, nvs_manager
- `usb/`: usb_hid_keyboard, usb_vendor, usb_descriptors
- `utils/`: logger, crc, jpeg_decoder, text_render

### Firmware (не реализовано)
- WiFi — конфиги в `config.h` есть, `wifi_manager.c` нет
- OTA — `ota_updater.c` нет
- USB CDC — `usb_cdc.c` нет, вместо него `usb_vendor.c`

### Software (реализовано)
- MacroKeyboard.Backend — `ActionExecutorService`, `DeviceManager`, `EventRouter`, `IpcCommandHandler`, `IpcServer`, `ShellCommandExecutor`
- MacroKeyboard.Communication — все команды протокола
- MacroKeyboard.Core — все модели, IPC интерфейсы
- MacroKeyboard.Infrastructure — `DeviceService`, `ImageService`, `ProfileService`
- MacroKeyboard.UI — `ProfileEditorView`, `ButtonConfigDialogViewModel`, полный UI

### Software (стабы, не подключены)
- `PluginManager` — файл есть, в DI не регистрируется
- `WebSocketServer` — файл есть, не стартует
- `PluginContext` — все методы заглушки (`// TODO`)
- `ActionType.Plugin = 0x0B` — не добавлен в enum

### Software (не существует)
- **TrayApp** — проекта `MacroKeyboard.TrayApp` нет, UI открывается напрямую

---

## Протокол (актуальная таблица команд)

Пакет: 64 байта. Magic = **0xA5**, End = 0x5A.

> Внимание: в `config.h` есть `#define PROTOCOL_MAGIC 0xEA` — это комментарий-задумка, а не
> реальный байт. Реальный magic определён в `protocol_types.h` как `PROTOCOL_MAGIC_BYTE = 0xA5`.

```
0x01  CMD_PING
0x02  CMD_GET_DEVICE_INFO
0x10  CMD_SET_PROFILE
0x11  CMD_GET_PROFILE_INFO
0x20  CMD_START_IMAGE_TRANSFER
0x21  CMD_IMAGE_DATA_CHUNK
0x22  CMD_END_IMAGE_TRANSFER
0x23  CMD_GET_BUTTON_IMAGE

0x30  CMD_SET_BUTTON_ACTION
0x31  CMD_GET_BUTTON_ACTION
0x32  CMD_SET_BUTTON_NAME
0x33  CMD_SET_FOLDER_BUTTON_ACTION
0x34  CMD_SET_FOLDER_BUTTON_NAME
0x35  CMD_SET_ENCODER_ACTION
0x36  CMD_SET_BUTTON_LONG_PRESS_ACTION
0x37  CMD_SET_BUTTON_LONG_PRESS_NAME

0x40  CMD_SET_LED_COLOR
0x41  CMD_SET_BACKLIGHT
0x42  CMD_GET_LED_COLOR
0x43  CMD_SET_FOLDER_BUTTON_LED

0x50  CMD_SAVE_PROFILE
0x51  CMD_LOAD_PROFILE
0x52  CMD_DELETE_PROFILE
0x53  CMD_REFRESH_DISPLAYS

0x60  CMD_START_OTA_UPDATE
0x61  CMD_GET_OTA_STATUS
0x70  CMD_SET_WIFI_CREDENTIALS
0x71  CMD_GET_WIFI_STATUS
0x80  CMD_ENABLE_DEBUG_LOG
0x81  CMD_FACTORY_RESET

0xF0  EVENT_BUTTON_PRESSED
0xF1  EVENT_ENCODER_ROTATED
0xF2  EVENT_ENCODER_BUTTON
0xF3  EVENT_PROFILE_CHANGED
0xF4  EVENT_DEVICE_READY
0xF5  EVENT_FOLDER_ENTERED
0xF6  EVENT_FOLDER_EXITED
0xFF  EVENT_ERROR
```

---

## Типы действий (firmware ↔ software)

| Hex  | Firmware                  | C# (ActionType)       |
|------|---------------------------|-----------------------|
| 0x00 | ACTION_TYPE_NONE          | None                  |
| 0x01 | ACTION_TYPE_KEYBOARD      | Keyboard              |
| 0x02 | ACTION_TYPE_CUSTOM_HID    | CustomHid             |
| 0x03 | ACTION_TYPE_PROFILE_SWITCH| ProfileSwitch         |
| 0x04 | ACTION_TYPE_FOLDER        | Folder                |
| 0x05 | ACTION_TYPE_DELAY         | Delay                 |
| 0x06 | ACTION_TYPE_SHELL         | Shell                 |
| 0x07 | ACTION_TYPE_SEQUENCE      | Sequence              |
| 0x08 | ACTION_TYPE_LAUNCH_APP    | LaunchApp             |
| 0x09 | ACTION_TYPE_MEDIA         | Media                 |
| 0x0A | ACTION_TYPE_NIGHT_MODE    | NightMode             |
| 0x0B | (не добавлен)             | (Plugin — plan only)  |

Delay (0x05) и Shell/LaunchApp/Media/NightMode — реализованы в firmware и software.

---

## Firmware: неочевидные детали

### Дисплей

- Чип фактически **GC9D01** (схожий с GC9A01 но с отличиями в инициализации). Файл называется `gc9a01.c`.
- **Partial window write работает некорректно** — при записи в частичный регион дисплей отображает мусор. Обходное решение: всегда composit полный кадр 160×160 в буфер, затем отправлять целиком.
- **Split display**: каждый дисплей делится на зоны:
  - `SPLIT_SHORT_H = 96` px — верхние 60% (short press action label)
  - `SPLIT_LONG_H = 64` px — нижние 40% (long press action label)
  - Белый разделитель между зонами
  - Реализовано в `text_render.c` через `text_render_fill_region()`

### Энкодер

- Направление определяется lookup table `encoder_table[16]` в `encoder.c`, индексируется 4-битным состоянием `(prev_AB << 2) | curr_AB`.
- Шаги **накапливаются**: только при `abs(accumulator) >= ENCODER_STEPS_PER_PROFILE` (= 4) срабатывает действие. Это предотвращает ложные срабатывания при вибрации.
- Пины **A=41, B=40** (были перепутаны и исправлены — не менять обратно).

### Профиль и хранение

- Профили: `/storage/profile_%d.bin`
- Изображения: `/storage/img_%d_%d.jpg` (profile_id, button_id)
- `button_config_t` содержит два независимых набора action data: основной (short press) и `long_press_*`.
- Поле `name[32]` — отображаемая метка на дисплее (если нет изображения).
- Поле `long_press_name[32]` — кастомная метка для нижней зоны split display; если пустое — генерируется автоматически из типа действия.
- `FOLDER_STACK_DEPTH = 8` (в `config.h`) — максимальная вложенность папок.

### USB

- Устройство идентифицируется как **"Elgato" / "Stream Deck"** (`USB_MANUFACTURER` и `USB_PRODUCT` в `config.h`). Это обеспечивает частичную совместимость с экосистемой Stream Deck.
- Raw HID реализован через **`usb_vendor.c`**, не `usb_hid_raw.c` (тот не написан).

---

## Software: неочевидные детали

### IPC и процессы

- Backend ↔ UI общаются через **Named Pipes** (библиотека `H.Pipes`), не WebSocket.
- Нет отдельного TrayApp процесса — UI открывается напрямую.
- Backend запускается как `IHostedService` внутри того же процесса или отдельно — зависит от точки входа.

### Encoder ButtonId convention

В software encoder слоты имеют условные ButtonId:
```
200 = Clockwise (CW)
201 = Counter-Clockwise (CCW)
202 = Short Press
203 = Long Press
```
Это не firmware ID — firmware имеет отдельный `encoder_config_t`. Эти числа используются только в UI для идентификации слота конфигурации.

### Long press synthetic ID

При открытии конфигурации long press через `ConfigureButtonLongPress`, создаётся синтетический `ButtonConfig` с `ButtonId = originalButtonId + 100`. Это позволяет повторно использовать `ButtonConfigDialogViewModel` для long press без дублирования UI. **Фаза 3 ui_refactor.md этот механизм убирает.**

### Inline Config Panel visibility

`ProfileEditorView.axaml` содержит inline-панель внутри `DataTemplate` для `FlattenedButtonItem`. Панель видима только когда `FlattenedButtonItem.Button == ConfiguredButtonConfig` (сравнение по ссылке через `ObjectEqualityConverter`). Из-за этого encoder config (ButtonId 200-203 не входят в `FlattenedButtons`) требует отдельной панели — `IsEncoderConfigVisible`.

### ActionPaletteItem и drag-and-drop

`ActionPaletteItem` в правой колонке имеет опциональное поле `PreConfiguredAction`. Если оно заполнено (например, конкретная медиа-клавиша), drop сразу применяет действие без открытия конфигурации. Если null — открывает конфигурацию для выбора параметров.

### Папки в UI

- `FlattenedButtonItem` содержит `ParentFolder` (null для корневых кнопок).
- Кнопки-папки и кнопки внутри папок плоско перечислены в `FlattenedButtons`, сгруппированные по папке.
- `ProfileEditorViewModel.OpenFolderAsync` переключает контекст отображения.

---

## Система папок

Полностью реализована в firmware и software.

### Firmware

`profile_t` содержит `folder_t folders[16]`. Каждая `folder_t` — 10 кнопок со своими `button_config_t`. Стек навигации хранится в RAM: `folder_stack[FOLDER_STACK_DEPTH]`, `folder_stack_depth`, `folder_entry_button`.

`profile_get_button_config(id)` автоматически возвращает кнопку из текущего контекста (root или активная папка).

**Toggle-логика** в `action_executor.c`:
```c
if (profile_is_in_folder() && folder_entry_button_id == button_id)
    profile_folder_exit();
else
    profile_folder_enter(folder_id);
```
Повторное нажатие на кнопку, открывшую папку — выход. Кнопка входа автоматически показывает back-иконку пока пользователь внутри.

Ограничения: 16 папок на профиль (`NUM_FOLDERS`), вложенность до 8 уровней (`FOLDER_STACK_DEPTH`).

События: `EVENT_FOLDER_ENTERED (0xF5)`, `EVENT_FOLDER_EXITED (0xF6)` с payload `{folder_id, depth, profile_id, reserved}`.

### Software

Папка создаётся автоматически в `SaveButtonConfig` при назначении кнопке типа Folder. Имя берётся из `ButtonConfigDialogViewModel.FolderName` и используется как ключ поиска — если папка с таким именем уже есть, переиспользуется существующая.

`FlattenedButtons` отображает кнопки папок с `NestingLevel=1` под своей кнопкой-родителем. `IsBackButton` на кнопке входа блокирует её редактирование изнутри папки.

Протокольные команды для кнопок внутри папок: `0x33` (action), `0x34` (name), `0x43` (LED).

### Об именах папок

Имя папки на дисплее — это `button_config_t.name` самой кнопки с `ACTION_TYPE_FOLDER`, передаётся обычным `CMD_SET_BUTTON_NAME (0x32)`. `folder_t.name` в firmware используется только в `ESP_LOGI`. `Folder.Name` в C# — ключ для поиска папки в памяти, на устройство не отправляется.

---

## Последовательности (Sequence)

- Максимум 16 шагов (`MAX_SEQUENCE_STEPS = 16`).
- Каждый шаг: `{action_type, delay_before_ms, action_data}`.
- `delay_before_ms` — задержка перед конкретным шагом (0–65535 мс).
- Шаг типа `ACTION_TYPE_DELAY` — чистая пауза без действия.
- Sequence не может содержать другой Sequence (запрещено).

---

## Ночной режим (NightMode, 0x0A)

Toggle: первое нажатие сохраняет текущие LED-настройки в RAM, выключает все LED и устанавливает яркость дисплеев в 0. Повторное нажатие восстанавливает сохранённое состояние.

Реализовано в firmware в `hardware/night_mode.c` и вызывается из `action_executor.c`.
