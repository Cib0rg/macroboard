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

- ✅ **Драйвер дисплеев GC9D01** — 10 круглых дисплеев 160×160 через SPI с мультиплексором 2× 74HC138
- ✅ **PWM подсветка дисплеев** — регулировка яркости 0-255 через LEDC
- ✅ **WS2812 RGB LED** — индивидуальная подсветка каждой кнопки с настройкой цвета и яркости
- ✅ **USB Composite Device** — HID Keyboard + Consumer Control + Vendor Bulk (TinyUSB)
- ✅ **Протокол обмена** — 64-байтовые пакеты через Vendor Bulk endpoint
- ✅ **Система профилей** — до 5 профилей с сохранением в SPIFFS
- ✅ **Папки** — вложенные папки кнопок (до 4 уровней глубины)
- ✅ **Передача изображений** — JPEG изображения на кнопки с дедупликацией по CRC32
- ✅ **Rotary encoder** — переключение профилей вращением
- ✅ **Debounced кнопки** — обработка нажатий через прерывания с программным debounce
- ✅ **Действия кнопок**: Keyboard, Media (Consumer Control), Shell, ProfileSwitch, Folder, Sequence, CustomHID, LaunchApp

### Управляющий софт (Software)

- ✅ **Кроссплатформенный UI** — Avalonia UI (Linux, Windows, macOS)
- ✅ **Backend Service** — фоновый сервис для связи с устройством
- ✅ **IPC** — TCP-based коммуникация между UI и Backend с автоматическим переподключением
- ✅ **Редактор профилей** — создание, редактирование, сохранение/загрузка профилей (JSON)
- ✅ **Конфигурация кнопок** — inline-редактор с drag-n-drop палитрой действий
- ✅ **Захват клавиш** — запись комбинаций клавиш в реальном времени
- ✅ **Последовательности действий** — до 16 шагов с задержками
- ✅ **Медиа-клавиши** — Volume Up/Down, Mute, Play/Pause, Next/Prev Track
- ✅ **Превью изображений** — миниатюры в списке кнопок и превью в редакторе
- ✅ **Настройка LED** — выбор цвета через ColorPicker, яркость
- ✅ **Настройка подсветки дисплеев** — слайдер яркости в Dashboard и Settings
- ✅ **Tray icon** — работа в фоне, сворачивание в трей
- ✅ **Dashboard** — статус устройства, лог событий, управление яркостью
- ✅ **Синхронизация с устройством** — отправка/загрузка профилей через USB
- ✅ **LibUsbDotNet** — кроссплатформенная USB коммуникация (Linux/Windows/macOS)

## Структура проекта

```
elgato/
├── firmware/                    # Прошивка ESP32-S3 (ESP-IDF v5.4, C)
│   └── main/
│       ├── hardware/            # Драйверы: дисплеи, LED, кнопки, энкодер
│       ├── profile/             # Менеджер профилей, исполнитель действий
│       ├── protocol/            # Обработчик протокола, передача изображений
│       ├── storage/             # NVS, SPIFFS, хранение профилей и изображений
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
| Дисплей | GC9D01 160×160 | 10 | Круглые TFT, SPI |
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
| 0x10 | SET_PROFILE | Переключение профиля |
| 0x20-0x22 | IMAGE_TRANSFER | Передача изображений (start/chunk/end) |
| 0x30-0x31 | BUTTON_ACTION | Установка/чтение действия кнопки |
| 0x40-0x42 | LED/BACKLIGHT | Управление LED и подсветкой дисплеев |
| 0x50-0x52 | PROFILE_STORAGE | Сохранение/загрузка/удаление профилей |

### Типы действий

| ID | Тип | Описание |
|----|-----|----------|
| 0x01 | Keyboard | Эмуляция клавиатуры (HID keycodes + модификаторы) |
| 0x02 | CustomHID | Произвольный HID report |
| 0x03 | ProfileSwitch | Переключение профиля |
| 0x04 | Folder | Открытие папки кнопок |
| 0x06 | Shell | Выполнение shell-команды на PC |
| 0x07 | Sequence | Последовательность до 16 действий |
| 0x08 | LaunchApp | Запуск приложения на PC |
| 0x09 | Media | Медиа-клавиши (Volume, Mute, Play/Pause и др.) |

## Лицензия

WTFPL
