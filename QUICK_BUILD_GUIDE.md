# Быстрое руководство по сборке и запуску

Это краткое руководство для быстрого старта работы с проектом макроклавиатуры.

## 📋 Содержание

- [Прошивка ESP32-S3](#прошивка-esp32-s3)
- [C# Проекты](#c-проекты)
  - [Backend](#backend)
  - [TrayApp](#trayapp)
  - [UI](#ui)

---

## 🔧 Прошивка ESP32-S3

### Вариант 1: Через Docker (Рекомендуется)

Docker позволяет собрать и прошить устройство без установки ESP-IDF локально.

#### Предварительные требования

- Docker установлен
- ESP32-S3 подключен через USB

#### Установка Docker (если еще нет)

```bash
# Установить Docker
sudo apt-get update
sudo apt-get install docker.io

# Добавить пользователя в группу docker
sudo usermod -aG docker $USER
newgrp docker

# Проверить установку
docker --version
```

#### Сборка прошивки через Docker

```bash
# Перейти в директорию прошивки
cd /home/andrewp/elgato/firmware

# Собрать проект используя Docker
./scripts/docker-build.sh

# Или с очисткой перед сборкой
./scripts/docker-build.sh --clean
```

#### Прошивка устройства через Docker

```bash
# Определить порт (обычно /dev/ttyUSB0 или /dev/ttyACM0)
ls /dev/ttyUSB* /dev/ttyACM*

# Прошить устройство
docker run --rm \
    -v "$PWD:/project" \
    -w /project \
    --device=/dev/ttyACM0:/dev/ttyUSB0 \
    espressif/idf:v5.3 \
    idf.py -p /dev/ttyUSB0 flash

# Прошить и открыть монитор
docker run --rm -it \
    -v "$PWD:/project" \
    -w /project \
    --device=/dev/ttyUSB0:/dev/ttyUSB0 \
    espressif/idf:v5.3 \
    idf.py -p /dev/ttyUSB0 flash monitor
```

#### Быстрая команда (сборка и прошивка)

```bash
cd /home/andrewp/elgato/firmware && \
./scripts/docker-build.sh && \
docker run --rm -it \
    -v "$PWD:/project" \
    -w /project \
    --device=/dev/ttyUSB0:/dev/ttyUSB0 \
    espressif/idf:v5.3 \
    idf.py -p /dev/ttyUSB0 flash monitor
```

#### Troubleshooting для Docker

**Проблема: Permission denied для /dev/ttyUSB0**

```bash
# Решение 1: Добавить в группу dialout
sudo usermod -aG dialout $USER
newgrp dialout

# Решение 2: Дать права на устройство (временно)
sudo chmod 666 /dev/ttyUSB0
```

**Проблема: Docker не установлен**

```bash
# Установить Docker
sudo apt-get update
sudo apt-get install docker.io
sudo systemctl start docker
sudo usermod -aG docker $USER
newgrp docker
```

---

### Вариант 2: Локальная установка ESP-IDF

#### Предварительные требования

- ESP-IDF v5.3 установлен в `~/esp/esp-idf`
- ESP32-S3 подключен через USB

#### Сборка прошивки

```bash
# 1. Перейти в директорию прошивки
cd /home/andrewp/elgato/firmware

# 2. Активировать ESP-IDF окружение
. ~/esp/esp-idf/export.sh

# 3. Собрать проект
idf.py build
```

### Прошивка устройства

```bash
# Определить порт (обычно /dev/ttyUSB0 или /dev/ttyACM0)
ls /dev/ttyUSB* /dev/ttyACM*

# Прошить устройство
idf.py -p /dev/ttyUSB0 flash

# Или прошить и сразу открыть монитор
idf.py -p /dev/ttyUSB0 flash monitor
```

### Быстрая команда (всё в одной строке)

```bash
cd /home/andrewp/elgato/firmware && . ~/esp/esp-idf/export.sh && idf.py build && idf.py -p /dev/ttyUSB0 flash monitor
```

### Результаты сборки

После успешной сборки файлы будут в `firmware/build/`:
- `bootloader/bootloader.bin` - загрузчик
- `partition_table/partition-table.bin` - таблица разделов
- `*.bin` - основная прошивка
- `*.elf` - файл для отладки

### Полезные команды

```bash
# Открыть конфигурацию
idf.py menuconfig

# Очистить сборку
idf.py fullclean

# Показать размер прошивки
idf.py size

# Только монитор (без прошивки)
idf.py -p /dev/ttyUSB0 monitor
# Выход: Ctrl+]
```

### Подробная документация

См. [`firmware/SETUP.md`](firmware/SETUP.md)

---

## 💻 C# Проекты

### Предварительные требования

- .NET 8.0 SDK установлен
- Проверка: `dotnet --version` должна показать 8.0.x

### Структура проектов

```
software/src/
├── MacroKeyboard.Backend/      # Фоновый сервис
├── MacroKeyboard.TrayApp/      # Приложение в системном трее
├── MacroKeyboard.UI/           # Главное UI приложение
├── MacroKeyboard.Core/         # Основные модели и интерфейсы
├── MacroKeyboard.Communication/# HID коммуникация
├── MacroKeyboard.Infrastructure/# Реализация сервисов
└── MacroKeyboard.Shared/       # Общие компоненты
```

### Сборка всех проектов

```bash
# Перейти в директорию с проектами
cd /home/andrewp/elgato/software/src

# Восстановить зависимости
dotnet restore

# Собрать все проекты
dotnet build

# Собрать в Release режиме (оптимизированная версия)
dotnet build -c Release
```

---

## 🚀 Запуск компонентов

### Backend

Backend - это фоновый сервис, который управляет устройством.

```bash
cd /home/andrewp/elgato/software/src/MacroKeyboard.Backend
dotnet run
```

**Функции:**
- Подключение к HID устройству
- Обработка нажатий кнопок
- Выполнение действий (запуск программ, эмуляция клавиш)
- IPC интерфейс для UI и TrayApp

### TrayApp

TrayApp - приложение в системном трее для быстрого доступа.

```bash
cd /home/andrewp/elgato/software/src/MacroKeyboard.TrayApp
dotnet run
```

**Функции:**
- Иконка в системном трее
- Быстрое переключение профилей
- Статус подключения устройства
- Открытие главного UI

### UI

UI - главное приложение для настройки.

```bash
cd /home/andrewp/elgato/software/src/MacroKeyboard.UI
dotnet run
```

**Функции:**
- Управление профилями
- Настройка действий для кнопок
- Загрузка изображений на дисплеи
- Мониторинг состояния

---

## 🎯 Запуск всей системы

Для полноценной работы нужно запустить все три компонента в разных терминалах:

### Терминал 1: Backend
```bash
cd /home/andrewp/elgato/software/src/MacroKeyboard.Backend
dotnet run
```

### Терминал 2: TrayApp
```bash
cd /home/andrewp/elgato/software/src/MacroKeyboard.TrayApp
dotnet run
```

### Терминал 3: UI (опционально)
```bash
cd /home/andrewp/elgato/software/src/MacroKeyboard.UI
dotnet run
```

### Альтернатива: Запуск в фоне

```bash
cd /home/andrewp/elgato/software/src

# Запустить Backend в фоне
dotnet run --project MacroKeyboard.Backend/MacroKeyboard.Backend.csproj &

# Запустить TrayApp в фоне
dotnet run --project MacroKeyboard.TrayApp/MacroKeyboard.TrayApp.csproj &

# Запустить UI (в текущем терминале)
dotnet run --project MacroKeyboard.UI/MacroKeyboard.UI.csproj
```

---

## 📦 Публикация приложений

### Создание исполняемых файлов

```bash
cd /home/andrewp/elgato/software/src

# Backend
dotnet publish MacroKeyboard.Backend/MacroKeyboard.Backend.csproj \
    -c Release \
    -o ../publish/backend

# TrayApp
dotnet publish MacroKeyboard.TrayApp/MacroKeyboard.TrayApp.csproj \
    -c Release \
    -o ../publish/trayapp

# UI
dotnet publish MacroKeyboard.UI/MacroKeyboard.UI.csproj \
    -c Release \
    -o ../publish/ui
```

Результат будет в `software/publish/`.

### Self-contained сборка (со встроенным .NET)

Для распространения без требования установки .NET:

```bash
cd /home/andrewp/elgato/software/src

# Для Linux
dotnet publish MacroKeyboard.Backend/MacroKeyboard.Backend.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o ../publish/backend-linux

# Для Windows
dotnet publish MacroKeyboard.Backend/MacroKeyboard.Backend.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -o ../publish/backend-windows

# Для macOS
dotnet publish MacroKeyboard.Backend/MacroKeyboard.Backend.csproj \
    -c Release \
    -r osx-x64 \
    --self-contained true \
    -o ../publish/backend-macos
```

---

## 🔍 Полезные команды

### Прошивка

```bash
# Очистить сборку
cd /home/andrewp/elgato/firmware && idf.py fullclean

# Размер прошивки
idf.py size

# Стереть flash полностью
idf.py -p /dev/ttyUSB0 erase-flash
```

### C# проекты

```bash
cd /home/andrewp/elgato/software/src

# Очистить сборку
dotnet clean

# Восстановить пакеты
dotnet restore

# Запустить тесты (если есть)
dotnet test

# Очистить кэш NuGet
dotnet nuget locals all --clear
```

---

## 📚 Дополнительная документация

### Прошивка
- [`firmware/SETUP.md`](firmware/SETUP.md) - Полная инструкция по настройке ESP-IDF
- [`firmware/README.md`](firmware/README.md) - Описание структуры прошивки
- [`firmware/plans/REQUIREMENTS.md`](firmware/plans/REQUIREMENTS.md) - Требования к прошивке

### C# Проекты
- [`software/SETUP.md`](software/SETUP.md) - Полная инструкция по настройке .NET
- [`software/README.md`](software/README.md) - Описание архитектуры
- [`software/REQUIREMENTS.md`](software/REQUIREMENTS.md) - Требования к софту
- [`software/plans/architecture.md`](software/plans/architecture.md) - Архитектура системы
- [`software/plans/plugin_system.md`](software/plans/plugin_system.md) - Система плагинов

### Общее
- [`plans/README.md`](plans/README.md) - Общий обзор проекта
- [`plans/protocol.md`](plans/protocol.md) - Протокол обмена данными
- [`plans/system_flow.md`](plans/system_flow.md) - Потоки данных в системе

---

## ⚠️ Troubleshooting

### Прошивка не собирается

```bash
# Проверить ESP-IDF
. ~/esp/esp-idf/export.sh
idf.py --version

# Переустановить инструменты
cd ~/esp/esp-idf
./install.sh esp32s3
```

### Устройство не прошивается

```bash
# Проверить порт
ls -l /dev/ttyUSB* /dev/ttyACM*

# Добавить права
sudo usermod -a -G dialout $USER
newgrp dialout

# Или временно
sudo chmod 666 /dev/ttyUSB0
```

### .NET проекты не собираются

```bash
# Проверить .NET
dotnet --version

# Очистить и восстановить
cd /home/andrewp/elgato/software/src
dotnet clean
dotnet nuget locals all --clear
dotnet restore
dotnet build
```

### Backend не видит устройство

1. Проверьте, что устройство прошито и подключено
2. Проверьте права доступа к USB устройству
3. Проверьте логи Backend для ошибок

---

## 🎓 Быстрый старт для новичков

### 1. Первая сборка прошивки

```bash
cd /home/andrewp/elgato/firmware
. ~/esp/esp-idf/export.sh
idf.py build
idf.py -p /dev/ttyUSB0 flash monitor
```

### 2. Первая сборка C# проектов

```bash
cd /home/andrewp/elgato/software/src
dotnet restore
dotnet build
```

### 3. Первый запуск

```bash
# Терминал 1
cd /home/andrewp/elgato/software/src/MacroKeyboard.Backend
dotnet run

# Терминал 2
cd /home/andrewp/elgato/software/src/MacroKeyboard.UI
dotnet run
```

---

**Успешной разработки! 🚀**
