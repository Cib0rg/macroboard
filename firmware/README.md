# ESP32-S3 Macro Keyboard Firmware

Прошивка для макроклавиатуры на базе ESP32-S3 с 10 кнопками, дисплеями и RGB подсветкой.

## Возможности

- 10 программируемых кнопок с дисплеями GC9A01 (160×160)
- Rotary encoder с настраиваемыми действиями (CW/CCW/нажатие/долгое нажатие)
- Долгое нажатие на любой кнопке с отдельным действием и названием
- RGB подсветка WS2812 под каждой кнопкой
- USB Composite Device: HID Keyboard + Consumer Control + Vendor Bulk interface
- Папки кнопок с вложенностью до 8 уровней
- Передача изображений JPEG с дедупликацией по CRC32
- Длинный текст для Keyboard-действия: до 4096 байт через SPIFFS (пароли, авто-ответы)
- Конфигурация с сохранением в SPIFFS

## Структура проекта

```
firmware/
├── main/
│   ├── main.c                  # Точка входа
│   ├── config.h                # Конфигурация (пины, лимиты, пути)
│   ├── hardware/               # Драйверы железа (дисплеи, LED, кнопки, энкодер)
│   ├── usb/                    # USB интерфейсы (HID keyboard, vendor bulk)
│   ├── protocol/               # Протокол обмена (handler, image_transfer, text_transfer)
│   ├── storage/                # Работа с памятью (profile_storage, image_storage, text_storage)
│   ├── profile/                # Управление профилями (profile_manager, action_executor)
│   └── utils/                  # Утилиты (crc, jpeg, text_render, logger)
├── CMakeLists.txt
├── partitions.csv
└── sdkconfig.defaults
```

## Сборка

### Через Docker (рекомендуется)

```bash
cd firmware
docker run --rm -v "$(pwd)":/project -w /project espressif/idf:v5.4 idf.py build
```

### Локально (требуется ESP-IDF v5.4)

```bash
cd firmware
idf.py build
idf.py -p /dev/ttyUSB0 flash monitor
```

## Конфигурация

Основные параметры в [`config.h`](main/config.h):

- `NUM_BUTTONS` — количество кнопок (10)
- `NUM_FOLDERS` — количество папок (16)
- `FOLDER_STACK_DEPTH` — максимальная вложенность папок (8)
- `DISPLAY_WIDTH/HEIGHT` — размер дисплеев (160×160)
- `STORAGE_BASE_PATH` — базовый путь SPIFFS (`/storage`)
- GPIO пины для всех компонентов

### Мультиплексор дисплеев

Два 74HC138 дешифратора, по 5 дисплеев на каждый:

```
Decoder 1 (SEL=1): Displays 0-4 (верхний ряд)
Decoder 2 (SEL=0): Displays 5-9 (нижний ряд)

GPIO:
  PIN_MUX_A0 (GPIO16) — адресная линия 0
  PIN_MUX_A1 (GPIO17) — адресная линия 1
  PIN_MUX_A2 (GPIO18) — адресная линия 2
  PIN_MUX_SEL (GPIO21) — выбор дешифратора (1=первый, 0=второй)
```

## Протокол

Обмен данными через **USB Vendor Bulk** интерфейс (Interface 1 составного USB-устройства).

### Формат пакета (64 байта)

```
Offset  Size  Description
------  ----  -----------
0       1     Magic byte (0xA5)
1       1     Command ID
2       2     Payload length (little-endian)
4       2     Sequence number (little-endian)
6       56    Payload data
62      1     Checksum (XOR bytes 0-61)
63      1     End byte (0x5A)
```

### Команды (PC → Device)

| ID | Команда | Описание |
|----|---------|----------|
| `0x01` | `CMD_PING` | Проверка связи |
| `0x02` | `CMD_GET_DEVICE_INFO` | Информация об устройстве |
| `0x10` | `CMD_SET_PROFILE` | Активация профиля |
| `0x11` | `CMD_GET_PROFILE_INFO` | Информация о профиле |
| `0x12` | `CMD_GET_FOLDER_STATE` | Текущее состояние папки |
| `0x20` | `CMD_START_IMAGE_TRANSFER` | Начало передачи изображения |
| `0x21` | `CMD_IMAGE_DATA_CHUNK` | Фрагмент изображения |
| `0x22` | `CMD_END_IMAGE_TRANSFER` | Завершение передачи изображения |
| `0x30` | `CMD_SET_BUTTON_ACTION` | Настройка действия кнопки |
| `0x31` | `CMD_GET_BUTTON_ACTION` | Чтение действия кнопки |
| `0x32` | `CMD_SET_BUTTON_NAME` | Название кнопки |
| `0x33` | `CMD_SET_FOLDER_BUTTON_ACTION` | Действие кнопки в папке |
| `0x34` | `CMD_SET_FOLDER_BUTTON_NAME` | Название кнопки в папке |
| `0x35` | `CMD_SET_ENCODER_ACTION` | Настройка действий энкодера |
| `0x36` | `CMD_SET_BUTTON_LONG_PRESS_ACTION` | Действие долгого нажатия |
| `0x37` | `CMD_SET_BUTTON_LONG_PRESS_NAME` | Название действия долгого нажатия |
| `0x38` | `CMD_SET_BUTTON_TEXT_START` | Начало передачи длинного текста |
| `0x39` | `CMD_SET_BUTTON_TEXT_CHUNK` | Фрагмент длинного текста |
| `0x3A` | `CMD_SET_BUTTON_TEXT_END` | Завершение передачи текста → сохраняет в SPIFFS |
| `0x40` | `CMD_SET_LED_COLOR` | Настройка цвета LED |
| `0x41` | `CMD_SET_BACKLIGHT` | Яркость дисплеев |
| `0x42` | `CMD_GET_LED_COLOR` | Чтение цвета LED |
| `0x43` | `CMD_SET_FOLDER_BUTTON_LED` | LED кнопки в папке |
| `0x50` | `CMD_SAVE_PROFILE` | Сохранение профиля в SPIFFS |
| `0x53` | `CMD_REFRESH_DISPLAYS` | Обновление всех дисплеев |

### События (Device → PC)

| ID | Событие | Описание |
|----|---------|----------|
| `0xF0` | `EVENT_BUTTON_PRESSED` | Кнопка нажата |
| `0xF1` | `EVENT_ENCODER_ROTATED` | Энкодер повёрнут |
| `0xF4` | `EVENT_DEVICE_READY` | Устройство готово |
| `0xF5` | `EVENT_FOLDER_ENTERED` | Вход в папку |
| `0xF6` | `EVENT_FOLDER_EXITED` | Выход из папки |

### Типы действий

| ID | Тип | Описание |
|----|-----|----------|
| `0x01` | `Keyboard` | HID клавиатура; текст ≤ 44 байт — inline, > 44 байт — SPIFFS |
| `0x02` | `CustomHID` | Произвольный HID report |
| `0x04` | `Folder` | Вход в папку |
| `0x06` | `Shell` | Команда на стороне PC |
| `0x07` | `Sequence` | Последовательность действий |
| `0x08` | `LaunchApp` | Запуск приложения на PC |
| `0x09` | `Media` | Consumer Control (Volume, Mute, Play/Pause…) |
| `0x0A` | `NightMode` | Переключение ночного режима |
| `0x0B` | `Plugin` | Stream Deck-совместимый плагин |

## SPIFFS-хранилище

```
/storage/
├── profile_0.bin        # Профиль устройства (binary)
├── img_<crc32>.raw      # Изображения с дедупликацией по CRC32
├── img_map.bin          # Таблица маппинга (profile, button) → CRC32
└── txt_<pid>_<bid>.bin  # Длинный текст для Keyboard-действий (до 4096 байт)
```

Синтетический `button_id` для хранения:
- Корневые кнопки: `bid = button_id` (0–9)
- Кнопки в папке: `bid = NUM_BUTTONS + folder_id * NUM_BUTTONS + button_id`

## Разработка

### Добавление новой команды протокола

1. Добавить константу в [`protocol/protocol_types.h`](main/protocol/protocol_types.h)
2. Создать forward-декларацию и handler в [`protocol/protocol_handler.c`](main/protocol/protocol_handler.c)
3. Добавить запись в `command_table[]`
4. Зарегистрировать `.c` файл нового модуля в [`CMakeLists.txt`](main/CMakeLists.txt)

### Добавление нового действия кнопки

1. Добавить тип в [`profile/profile_types.h`](main/profile/profile_types.h)
2. Реализовать в [`profile/action_executor.c`](main/profile/action_executor.c)

## Отладка

Логи выводятся через UART0 (GPIO43/44). Уровень логирования настраивается в `sdkconfig`.

## Лицензия

MIT License
