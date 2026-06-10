# Быстрое руководство по сборке и запуску

## Содержание

- [Прошивка ESP32-S3](#прошивка-esp32-s3)
  - [Linux](#linux)
  - [Windows](#windows)
- [C# Приложение](#c-приложение)
- [Windows Installer](#windows-installer)
- [Troubleshooting](#troubleshooting)

---

## Прошивка ESP32-S3

Рабочая директория для всех команд прошивки — `firmware/`.

### Linux

#### Сборка через Docker (рекомендуется)

```bash
cd firmware

# Обычная сборка
./scripts/docker-build.sh

# Сборка с очисткой
./scripts/docker-build.sh --clean
```

#### Прошивка через Docker

```bash
# Определить порт устройства
ls /dev/ttyUSB* /dev/ttyACM*

# Прошить
docker run --rm \
    -v "$PWD:/project" \
    -w /project \
    --device=/dev/ttyACM0:/dev/ttyUSB0 \
    espressif/idf:v5.3 \
    idf.py -p /dev/ttyUSB0 flash

# Прошить + открыть монитор
docker run --rm -it \
    -v "$PWD:/project" \
    -w /project \
    --device=/dev/ttyACM0:/dev/ttyUSB0 \
    espressif/idf:v5.3 \
    idf.py -p /dev/ttyUSB0 flash monitor
# Выход из монитора: Ctrl+]
```

#### Сборка и прошивка одной командой (локальный ESP-IDF)

```bash
cd firmware
. ~/esp/esp-idf/export.sh
idf.py build flash monitor -p /dev/ttyUSB0
```

---

### Windows

Docker Desktop на Windows **не поддерживает проброс COM-портов** (`--device` не работает). Поэтому сборка — в Docker, прошивка — отдельно.

#### Вариант А: Сборка в Docker, прошивка через esptool (проще)

**Шаг 1 — сборка:**

```powershell
cd firmware

docker run --rm `
    -v "${PWD}:/project" `
    -w /project `
    -e IDF_TARGET=esp32s3 `
    espressif/idf:v5.3 `
    idf.py build
```

**Шаг 2 — установить esptool:**

```powershell
pip install esptool
```

**Шаг 3 — найти COM-порт:**

Диспетчер устройств → Порты (COM и LPT) → найти `USB Serial Device (COMx)`.

**Шаг 4 — прошить:**

```powershell
# Заменить COM3 на свой порт
esptool.py -p COM3 -b 460800 --before default_reset --after hard_reset write_flash "@build/flash_args"
```

`build/flash_args` генерируется автоматически при сборке и содержит все нужные адреса и бинарники.

---

#### Вариант Б: WSL2 + usbipd (полный Docker-воркфлоу, как на Linux)

Позволяет пробросить USB-устройство в WSL2 и использовать `--device` в Docker.

**Однократная установка (PowerShell от администратора):**

```powershell
winget install usbipd
```

**Перед каждой прошивкой (PowerShell от администратора):**

```powershell
# Найти busid устройства (столбец BUSID)
usbipd list

# Первый раз — разрешить устройство
usbipd bind --busid 2-3   # подставить свой busid

# Подключить к WSL2
usbipd attach --wsl --busid 2-3
```

**Сборка и прошивка в WSL2 (точно как на Linux):**

```bash
cd <project-dir>/elgato/firmware   # или ваш путь

docker run --rm -it \
    -v "$PWD:/project" \
    -w /project \
    --device=/dev/ttyACM0:/dev/ttyUSB0 \
    espressif/idf:v5.3 \
    idf.py -p /dev/ttyUSB0 build flash monitor
```

---

## C# Приложение

Требования: **.NET 10 SDK** — скачать на [dot.net](https://dotnet.microsoft.com/download).

```bash
dotnet --version   # должно показать 10.x.x
```

### Структура проектов

```
software/src/
├── MacroKeyboard.Backend/       # Windows Service / фоновый демон
├── MacroKeyboard.UI/            # Avalonia UI приложение
├── MacroKeyboard.Core/          # Модели и интерфейсы
├── MacroKeyboard.Communication/ # HID протокол
├── MacroKeyboard.Infrastructure/# Реализация сервисов
└── MacroKeyboard.Shared/        # Общие компоненты
```

### Сборка

```bash
cd software/src
dotnet restore
dotnet build
```

### Запуск

Запускать в двух отдельных терминалах:

```bash
# Терминал 1 — Backend (должен стартовать первым)
cd software/src/MacroKeyboard.Backend
dotnet run
```

```bash
# Терминал 2 — UI
cd software/src/MacroKeyboard.UI
dotnet run
```

---

## Windows Installer

Создаёт `.exe`-установщик с помощью [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
cd software/installer/windows

# Собрать приложение и создать installer
.\build-windows.ps1 -Installer

# Указать версию явно
.\build-windows.ps1 -Installer -Version 1.2.0

# Только сборка без installer
.\build-windows.ps1
```

Готовый `.exe` появится в `software/installer/windows/output/`.

Версия берётся из (по приоритету):
1. Параметр `-Version`
2. Файл `software/installer/windows/version.txt`
3. Дефолт `1.0.0`

---

## Troubleshooting

### Прошивка: Permission denied для /dev/ttyUSB0 (Linux)

```bash
sudo usermod -aG dialout $USER
newgrp dialout
```

### Прошивка: устройство не определяется (Windows)

- Проверить Диспетчер устройств → Порты (COM и LPT)
- Если порт не появился — переустановить драйвер CP210x / CH340

### Прошивка: ошибка при использовании esptool (Windows)

```powershell
# Попробовать явно указать чип
esptool.py -p COM3 -b 460800 --chip esp32s3 --before default_reset --after hard_reset write_flash "@build/flash_args"
```

### Прошивка: Device Manager показывает порт, но esptool не может подключиться

Зажать кнопку BOOT на плате, подключить USB, отпустить — войдёт в режим загрузчика.

### C# проекты не собираются

```bash
cd software/src
dotnet clean
dotnet nuget locals all --clear
dotnet restore
dotnet build
```

### Backend не видит устройство

1. Убедиться, что прошивка залита и устройство подключено
2. На Linux: проверить права (`ls -l /dev/ttyUSB*`), добавить в группу `dialout`
3. Смотреть логи Backend — пишет в консоль и в `logs/backend-*.log`
