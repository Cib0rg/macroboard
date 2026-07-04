# ESP32-S3 Macro Keyboard

Программируемая макроклавиатура с открытым исходным кодом на базе ESP32-S3 — 10 кнопок с индивидуальными круглыми дисплеями, RGB подсветкой и настраиваемыми действиями.

![Project Status](https://img.shields.io/badge/status-working%20prototype-green)
![License](https://img.shields.io/badge/license-TBD-blue)

## Обзор

Устройство аналогично Elgato Stream Deck, но с открытым исходным кодом. Каждая кнопка имеет собственный круглый дисплей 160×160 для отображения иконок, RGB LED подсветку и программируемое действие.
Отсутствует ограничение по входному напряжению, подключение зарядок с QC скорее всего сожжёт часть платы!
Включать сначала питание от 2А блока (нижний разъём), потом уже подключать ESP через встроенные. Иначе есть небольшой шанс выхода из строя DC-DC преобразователя на плате ESP.

## Реализованные возможности

### Прошивка (Firmware)

- ✅ **Драйвер дисплеев GC9A01** — 10 круглых дисплеев 160×160 через SPI с мультиплексором 2× 74HC138
- ✅ **PWM подсветка дисплеев** — регулировка яркости 0-255 через LEDC
- ✅ **WS2812 RGB LED** — индивидуальная подсветка каждой кнопки с настройкой цвета и яркости
- ✅ **USB Composite Device** — HID Keyboard + Consumer Control + Vendor Bulk (TinyUSB)
- ✅ **Протокол обмена** — 64-байтовые пакеты через Vendor Bulk endpoint
- ✅ **Конфигурация** — хранение настроек кнопок и энкодера в LittleFS
- ✅ **Папки** — вложенные папки кнопок (до 8 уровней глубины)
- ✅ **Передача изображений** — JPEG изображения на кнопки с дедупликацией по CRC32
- ✅ **Асинхронная запись LittleFS** — `save_task` на Core 0, очередь 20 слотов; протокол не блокируется на I/O, sync fallback при нехватке слотов; полная синхронизация ~17 изображений: ~11 с вместо ~39 с
- ✅ **LittleFS хранилище текстов** — тексты до 4096 байт в LittleFS по 5-компонентному ключу `(profile/folder/button/action_slot/step_index)`; покрывает: Keyboard > 44 байт, long press Keyboard, каждый шаг Sequence с длинным текстом, Sequence-блоб > 51 байт
- ✅ **Rotary encoder** — настраиваемые действия на CW/CCW/нажатие/долгое нажатие
- ✅ **Долгое нажатие** — отдельное действие и название для каждой кнопки (корневой и в папках)
- ✅ **Debounced кнопки** — обработка нажатий через прерывания с программным debounce
- ✅ **Действия кнопок**: Keyboard, Media (Consumer Control), Shell, Folder, Sequence, CustomHID, LaunchApp, NightMode

### Управляющий софт (Software)

- ✅ **Кроссплатформенный UI** — Avalonia UI (Linux, Windows, macOS)
- ✅ **Backend Service** — фоновый сервис для связи с устройством
- ✅ **IPC** — TCP-based коммуникация между UI и Backend с автоматическим переподключением
- ✅ **Редактор конфигурации** — создание, редактирование, синхронизация конфигураций с устройством (JSON)
- ✅ **Конфигурация кнопок** — inline-редактор с поддержкой всех типов действий
- ✅ **Захват клавиш** — запись комбинаций клавиш в реальном времени
- ✅ **Последовательности действий** — до 16 шагов с задержками
- ✅ **Медиа-клавиши** — Volume Up/Down, Mute, Play/Pause, Next/Prev Track
- ✅ **Превью изображений** — миниатюры в списке кнопок и превью в редакторе
- ✅ **Настройка LED** — выбор цвета через ColorPicker, яркость
- ✅ **Настройка подсветки дисплеев** — слайдер яркости в Dashboard и Settings
- ✅ **Tray icon** — работа в фоне, сворачивание в трей
- ✅ **Dashboard** — статус устройства, лог событий, управление яркостью
- ✅ **Diff-based синхронизация профиля** — при Send пересылаются только изменённые кнопки; fingerprint учитывает image path+mtime, action type+JSON, LED; типовая правка одной кнопки: ~5 с; холодная синхронизация ~17 изображений: ~11 с (async SPIFFS)
- ✅ **Синхронизация с устройством** — отправка/загрузка профилей через USB
- ✅ **Сохранение профиля** — Save пишет в исходный файл (откуда был загружен через Load), а не в AppData
- ✅ **LibUsbDotNet** — кроссплатформенная USB коммуникация (Linux/Windows/macOS)
- ✅ **Плагины (Stream Deck совместимость)** — запуск `.streamDeckPlugin` архивов и исполняемых плагинов, WebSocket API на порту 28196, Property Inspector на порту 8787; плагины хранятся в `plugins/` рядом с бинарником бэкенда
- ✅ **Валидация ввода** — Shell-команда ≤ 49 байт UTF-8, Custom HID ≤ 51 байт (соответствует `ACTION_DATA_MAX_LEN` прошивки); превышение показывает ошибку вместо молчаливой обрезки
- ✅ **Кэш иконок приложений** — иконки извлекаются один раз через P/Invoke и сохраняются в `%APPDATA%\MacroKeyboard\icons\`; повторное открытие диалога не вызывает P/Invoke

## Плагины

MacroKeyboard поддерживает плагины в стиле Elgato Stream Deck. Каждая кнопка может быть привязана к действию стороннего плагина, который получает события нажатий и управляет дисплеем.

Два режима работы:
- **Executable** — отдельный процесс на любом языке (C#, Node.js, Python), общается с бекендом через WebSocket на порту 28196.
- **Managed** — .NET DLL, загружается в процесс бекенда, получает прямой доступ к устройству через `IPlugin` / `IPluginContext`.

Плагины кладутся в папку `plugins/` рядом с бинарником бекенда в виде директории или `.zip`-архива с расширением `.sdPlugin`. Существующие плагины для Stream Deck работают с минимальными изменениями.

Подробное руководство по разработке плагинов: **[PLUGIN_DEVELOPMENT.html](PLUGIN_DEVELOPMENT.html)**

## Структура проекта

```
elgato/
├── firmware/                    # Прошивка ESP32-S3 (ESP-IDF v5.4, C)
│   └── main/
│       ├── hardware/            # Драйверы: дисплеи, LED, кнопки, энкодер
│       ├── profile/             # Менеджер профилей, исполнитель действий
│       ├── protocol/            # Обработчик протокола, передача изображений и текста
│       ├── storage/             # NVS, LittleFS, профили, изображения, длинный текст, async save_task
│       └── usb/                 # USB дескрипторы, HID keyboard, vendor endpoint
│
├── hardware/                    # Аппаратная часть
│   ├── case/                    # 3D-модели корпуса (OpenSCAD + STL для печати)
│   └── pcb/                     # Схемотехника и разводка PCB (KiCad)
│
├── software/                    # Управляющее приложение (.NET 10, C#)
│   └── src/
│       ├── MacroKeyboard.Core/          # Модели, IPC, интерфейсы сервисов
│       ├── MacroKeyboard.Communication/ # USB протокол, команды устройства
│       ├── MacroKeyboard.Infrastructure/# Реализация сервисов, репозитории
│       ├── MacroKeyboard.Backend/       # Фоновый сервис (IPC сервер, роутинг)
│       └── MacroKeyboard.UI/            # Avalonia UI (MVVM, CommunityToolkit)
│
└── plans/                       # Проектная документация
```

## Быстрый старт

### Сборка прошивки

```bash
cd firmware
# Через Docker (рекомендуется):
docker run --rm -v "$(pwd)":/project -w /project espressif/idf:v5.4 idf.py build
# Или локально с установленным ESP-IDF v5.4:
idf.py build
idf.py flash
```

### Сборка софта

```bash
cd software
dotnet build MacroKeyboard.sln
# Запуск Backend:
dotnet run --project src/MacroKeyboard.Backend
# Запуск UI:
dotnet run --project src/MacroKeyboard.UI
```

### Linux: доступ к USB без root

```bash
sudo cp software/scripts/99-macrokeyboard.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules
```

## Технологии

### Прошивка

| Компонент | Технология |
|-----------|-----------|
| Платформа | ESP32-S3 N16R8 (16MB Flash, 8MB PSRAM) |
| Framework | ESP-IDF v5.4 |
| RTOS | FreeRTOS |
| USB | TinyUSB (HID + Vendor composite) |
| Язык | C99 |

### Софт

| Компонент | Технология |
|-----------|-----------|
| Runtime | .NET 10 |
| UI | Avalonia UI 12 |
| Архитектура | MVVM (CommunityToolkit.Mvvm) |
| USB | LibUsbDotNet 3.x |
| Изображения | SixLabors.ImageSharp |
| Сериализация | Newtonsoft.Json |
| Логирование | Serilog |

## Аппаратные компоненты

| Компонент | Модель | Кол-во | Примечания |
|-----------|--------|--------|------------|
| Микроконтроллер | ESP32-S3 N16R8 | 1 | 16MB Flash, 8MB PSRAM |
| Дисплей | GC9A01 160×160 | 10 | Круглые TFT, SPI |
| Кнопки | Тактовые | 10 | GPIO с прерываниями |
| RGB LED | WS2812 | 10 | Адресные, RMT peripheral |
| Энкодер | Rotary encoder | 1 | С кнопкой |
| Мультиплексор | 74HC138 | 2 | Выбор дисплея (5+5) |

Схемотехника и разводка PCB находятся в директории [`hardware/pcb/`](hardware/pcb/) (формат KiCad).
3D-модели корпуса для печати — в [`hardware/case/`](hardware/case/) (OpenSCAD + STL).

## Протокол обмена данными

64-байтовые пакеты через USB Vendor Bulk endpoint:

```
[0]    Magic (0xA5)
[1]    Command ID
[2-3]  Payload Length
[4-5]  Sequence Number
[6-61] Payload (56 bytes)
[62]   Checksum (XOR)
[63]   End Byte (0x5A)
```

### Команды

| ID | Команда | Описание |
|----|---------|----------|
| 0x01 | PING | Проверка связи |
| 0x02 | GET_DEVICE_INFO | Информация об устройстве |
| 0x10 | SET_PROFILE | Активировать профиль |
| 0x11–0x12 | GET_PROFILE_INFO / FOLDER_STATE | Состояние профиля и текущей папки |
| 0x20–0x22 | IMAGE_TRANSFER | Передача изображений (start/chunk/end) |
| 0x30–0x34 | BUTTON_ACTION/NAME | Установка действия и имени кнопки (корневые и папки) |
| 0x35 | SET_ENCODER_ACTION | Настройка действий энкодера |
| 0x36–0x37 | LONG_PRESS_ACTION/NAME | Действие и название долгого нажатия |
| 0x38–0x3A | TEXT_TRANSFER | Передача текста/блоба в SPIFFS (start/chunk/end); Keyboard > 44 байт, long press, Sequence-шаги и блобы > 51 байт |
| 0x40–0x43 | LED/BACKLIGHT | Управление LED и подсветкой дисплеев |
| 0x50–0x53 | PROFILE_SAVE/LOAD/DELETE/REFRESH | Управление профилями и обновление дисплеев |

### Типы действий

| ID | Тип | Описание |
|----|-----|----------|
| 0x01 | Keyboard | Эмуляция клавиатуры; текст ≤ 44 байт — inline, > 44 байт — SPIFFS |
| 0x02 | CustomHID | Произвольный HID report |
| 0x04 | Folder | Открытие папки кнопок |
| 0x06 | Shell | Выполнение shell-команды на PC |
| 0x07 | Sequence | Последовательность до 16 действий |
| 0x08 | LaunchApp | Запуск приложения на PC |
| 0x09 | Media | Медиа-клавиши (Volume, Mute, Play/Pause и др.) |
| 0x0A | NightMode | Переключение ночного режима (снижение яркости) |
| 0x0B | Plugin | Действие стороннего плагина (Stream Deck-совместимого) |

### LittleFS-хранилище текстов

Команды 0x38–0x3A передают текстовые данные, которые не влезают inline. На устройстве файл называется `/storage/txt_{P}_{F}_{B}_{A}_{S}.bin`, где:

| Поле | Смысл |
|------|-------|
| P | profile_id (всегда 0) |
| F | folder_id: 0..15 = папка, 255 = root |
| B | button_id: 0..9; энкодер: 0x40–0x43 |
| A | action slot: 0 = short press, 1 = long press |
| S | step: 0xFF = прямое Keyboard, 0xFE = Sequence-блоб, 0..15 = шаг Sequence |

Что хранится в SPIFFS:
- **Keyboard** текст > 44 байт — `step=0xFF` (short), `step=0xFF, slot=1` (long press)
- **Sequence-шаг Keyboard** > 44 байт — `step=N` (индекс шага)
- **Sequence-блоб** > 51 байт — `step=0xFE`; в `action_data` кнопки записывается маркер `[0x01]`

ПО автоматически выбирает между inline и SPIFFS. Payload CMD_SET_BUTTON_TEXT_START: 9 байт — `[profile_id][folder_id][button_id][action_slot][step_index][data_size(4 LE)]`.

## Лицензия

WTFPL
