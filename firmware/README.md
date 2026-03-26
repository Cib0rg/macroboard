# ESP32-S3 Macro Keyboard Firmware

Прошивка для макроклавиатуры на базе ESP32-S3 с 10 кнопками, дисплеями и RGB подсветкой.

## Возможности

- 10 программируемых кнопок с дисплеями GC9A01 (160x160)
- Rotary encoder для переключения профилей
- RGB подсветка WS2812 под каждой кнопкой
- USB HID Keyboard эмуляция
- USB HID Raw для кастомных команд
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

- `NUM_BUTTONS` - количество кнопок (10)
- `NUM_PROFILES` - количество профилей (5)
- `DISPLAY_WIDTH/HEIGHT` - размер дисплеев (160x160)
- GPIO пины для всех компонентов

## Протокол

Протокол обмена через USB HID Raw описан в [`plans/protocol.md`](../plans/protocol.md).

Размер пакета: 64 байта
- Magic byte: 0xA5
- Command ID
- Payload (56 байт)
- Checksum
- End byte: 0x5A

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
