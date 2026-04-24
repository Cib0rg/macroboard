# ESP32-S3 Macro Keyboard Firmware

Прошивка для макроклавиатуры на базе ESP32-S3 с 10 кнопками, дисплеями и RGB подсветкой.

## Возможности

- 10 программируемых кнопок с дисплеями GC9A01 (160x160)
- Rotary encoder для переключения профилей
- RGB подсветка WS2812 под каждой кнопкой
- USB Composite Device: HID Keyboard + Vendor Bulk interface
- 5 профилей с сохранением в flash
- WiFi OTA обновления
- Протокол обмена с управляющим софтом

## Структура проекта

```
firmware/
├── main/
│   ├── main.c                  # Точка входа
│   ├── config.h                # Конфигурация
│   ├── hardware/               # Драйверы железа
│   ├── usb/                    # USB интерфейсы
│   ├── protocol/               # Протокол обмена
│   ├── storage/                # Работа с памятью
│   ├── profile/                # Управление профилями
│   ├── network/                # WiFi и OTA
│   └── utils/                  # Утилиты
├── CMakeLists.txt
├── partitions.csv
└── sdkconfig.defaults
```

## Сборка

### Через Docker (рекомендуется)

```bash
cd firmware
./scripts/docker-build.sh
```

### Локально (требуется ESP-IDF v5.x)

```bash
cd firmware
idf.py build
idf.py -p /dev/ttyUSB0 flash monitor
```

## Конфигурация

Основные параметры в [`config.h`](main/config.h):

- `NUM_BUTTONS` — количество кнопок (10)
- `NUM_PROFILES` — количество профилей (5)
- `NUM_FOLDERS` — количество папок на профиль (16)
- `FOLDER_STACK_DEPTH` — максимальная вложенность папок (4)
- `DISPLAY_WIDTH/HEIGHT` — размер дисплеев (160×160)
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

Выходы 5-7 каждого дешифратора не используются.

## Протокол

Обмен данными через **USB Vendor Bulk** интерфейс (Interface 1 составного USB-устройства).
Полное описание: [`plans/protocol.md`](../plans/protocol.md).

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
| `0x10` | `CMD_SET_PROFILE` | Переключение профиля |
| `0x11` | `CMD_GET_PROFILE_INFO` | Информация о профиле |
| `0x20` | `CMD_START_IMAGE_TRANSFER` | Начало передачи изображения |
| `0x21` | `CMD_IMAGE_DATA_CHUNK` | Фрагмент изображения |
| `0x22` | `CMD_END_IMAGE_TRANSFER` | Завершение передачи |
| `0x30` | `CMD_SET_BUTTON_ACTION` | Настройка действия кнопки |
| `0x31` | `CMD_GET_BUTTON_ACTION` | Чтение действия кнопки |
| `0x40` | `CMD_SET_LED_COLOR` | Настройка цвета LED |
| `0x42` | `CMD_GET_LED_COLOR` | Чтение цвета LED |
| `0x50` | `CMD_SAVE_PROFILE` | Сохранение профиля в NVS |

### События (Device → PC)

| ID | Событие | Описание |
|----|---------|----------|
| `0xF0` | `EVENT_BUTTON_PRESSED` | Кнопка нажата |
| `0xF1` | `EVENT_ENCODER_ROTATED` | Энкодер повёрнут |
| `0xF3` | `EVENT_PROFILE_CHANGED` | Профиль изменён |
| `0xF4` | `EVENT_DEVICE_READY` | Устройство готово |
| `0xF5` | `EVENT_FOLDER_ENTERED` | Вход в папку |
| `0xF6` | `EVENT_FOLDER_EXITED` | Выход из папки |

### USB Endpoints

```
Interface 0: HID Keyboard (стандартный HID)
Interface 1: Vendor Bulk (протокол обмена)
  EP 0x02 (OUT) — команды от PC к устройству
  EP 0x82 (IN)  — ответы и события от устройства к PC
```

## Профили

Каждый профиль содержит:
- Имя профиля
- Конфигурацию 10 кнопок (действия, LED цвета)
- Изображения для дисплеев (JPEG, хранятся отдельно)

Профили сохраняются в SPIFFS раздел (10 MB).

## OTA обновления

1. Подключение к WiFi через команду `SET_WIFI_CREDENTIALS`
2. Запуск OTA через команду `START_OTA_UPDATE` с URL firmware
3. Автоматическая загрузка, проверка и установка
4. Rollback при ошибке

## Разработка

### Добавление новой команды протокола

1. Добавить ID команды в [`protocol/protocol_types.h`](main/protocol/protocol_types.h)
2. Создать handler функцию в [`protocol/protocol_handler.c`](main/protocol/protocol_handler.c)
3. Добавить в `command_table`

### Добавление нового действия кнопки

1. Добавить тип в [`profile/profile_types.h`](main/profile/profile_types.h)
2. Реализовать в [`profile/action_executor.c`](main/profile/action_executor.c)

## Отладка

Логи выводятся через:
- USB CDC (виртуальный COM порт)
- UART0 (GPIO43/44)

Уровень логирования настраивается в `sdkconfig`.

## Лицензия

MIT License
