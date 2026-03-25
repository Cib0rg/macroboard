# Настройка окружения для разработки прошивки ESP32-S3

## Установка ESP-IDF

ESP-IDF (Espressif IoT Development Framework) - это официальный фреймворк для разработки прошивок ESP32.

### Системные требования

```bash
# Установить необходимые пакеты
sudo apt-get update
sudo apt-get install -y git wget flex bison gperf python3 python3-pip python3-venv \
    cmake ninja-build ccache libffi-dev libssl-dev dfu-util libusb-1.0-0
```

### Установка ESP-IDF v5.x

```bash
# Создать директорию для ESP-IDF
mkdir -p ~/esp
cd ~/esp

# Клонировать ESP-IDF (версия 5.3 - последняя стабильная)
git clone -b v5.3 --recursive https://github.com/espressif/esp-idf.git

# Перейти в директорию ESP-IDF
cd esp-idf

# Установить инструменты для ESP32-S3
./install.sh esp32s3

# Это займет несколько минут, будут установлены:
# - Компилятор xtensa-esp32s3-elf-gcc
# - OpenOCD для отладки
# - Python зависимости
# - Другие инструменты
```

### Настройка окружения

После установки нужно активировать ESP-IDF окружение:

```bash
# Активировать ESP-IDF (нужно делать в каждой новой сессии терминала)
. ~/esp/esp-idf/export.sh

# Проверить установку
idf.py --version
# Должно показать: ESP-IDF v5.3.x
```

### Автоматическая активация (опционально)

Чтобы не активировать вручную каждый раз, добавьте alias в `~/.bashrc`:

```bash
# Добавить в ~/.bashrc
echo 'alias get_idf=". ~/esp/esp-idf/export.sh"' >> ~/.bashrc
source ~/.bashrc

# Теперь можно просто писать:
get_idf
```

## Установка расширений VS Code

### Обязательные расширения

```bash
# ESP-IDF Extension
code --install-extension espressif.esp-idf-extension

# C/C++ Extension
code --install-extension ms-vscode.cpptools

# CMake Tools
code --install-extension ms-vscode.cmake-tools
```

### Настройка ESP-IDF Extension

1. Откройте VS Code
2. Нажмите `Ctrl+Shift+P`
3. Введите "ESP-IDF: Configure ESP-IDF Extension"
4. Выберите "Use Existing Setup"
5. Укажите путь: `/home/andrewp/esp/esp-idf`
6. Выберите Python: `/home/andrewp/esp/esp-idf/.espressif/python_env/idf5.3_py3.10_env/bin/python`

## Создание проекта

### Вариант 1: Через командную строку

```bash
cd /home/andrewp/elgato/firmware

# Активировать ESP-IDF
. ~/esp/esp-idf/export.sh

# Создать проект из шаблона
idf.py create-project macro-keyboard

# Или скопировать пример
cp -r $IDF_PATH/examples/get-started/hello_world ./macro-keyboard
```

### Вариант 2: Через VS Code

1. Откройте VS Code
2. `Ctrl+Shift+P` → "ESP-IDF: Show Examples Projects"
3. Выберите "get-started/hello_world"
4. Укажите путь: `/home/andrewp/elgato/firmware/macro-keyboard`

## Структура проекта ESP-IDF

```
firmware/macro-keyboard/
├── CMakeLists.txt              # Главный CMake файл
├── sdkconfig                   # Конфигурация проекта
├── sdkconfig.defaults          # Дефолтная конфигурация
├── partitions.csv              # Таблица разделов flash
│
└── main/
    ├── CMakeLists.txt          # CMake для main компонента
    ├── main.c                  # Точка входа (app_main)
    ├── Kconfig.projbuild       # Опции конфигурации
    │
    ├── hardware/               # Драйверы hardware
    ├── usb/                    # USB функциональность
    ├── protocol/               # Протокол обмена
    ├── storage/                # Работа с памятью
    ├── profile/                # Управление профилями
    ├── network/                # WiFi и OTA
    ├── ui/                     # Дисплеи
    └── utils/                  # Утилиты
```

## Конфигурация проекта

### Настройка для ESP32-S3

```bash
cd firmware/macro-keyboard

# Активировать ESP-IDF
. ~/esp/esp-idf/export.sh

# Открыть menuconfig
idf.py menuconfig
```

### Ключевые настройки

В menuconfig настройте:

1. **Serial flasher config**
   - Flash size: 16 MB
   - Flash mode: QIO
   - Flash frequency: 80 MHz

2. **Partition Table**
   - Partition Table: Custom partition table CSV
   - Custom partition CSV file: partitions.csv

3. **Component config → ESP32S3-Specific**
   - Support for external, SPI-connected RAM: Enable
   - SPI RAM config → Mode: Octal
   - SPI RAM config → Speed: 80MHz

4. **Component config → FreeRTOS**
   - Tick rate (Hz): 1000

5. **Component config → USB**
   - Enable TinyUSB: Yes

6. **Component config → Wi-Fi**
   - WiFi Task Core ID: Core 0

## Компиляция и прошивка

### Компиляция

```bash
cd firmware/macro-keyboard

# Активировать ESP-IDF
. ~/esp/esp-idf/export.sh

# Собрать проект
idf.py build

# Первая сборка займет несколько минут
# Последующие сборки будут быстрее благодаря ccache
```

### Прошивка устройства

```bash
# Подключить ESP32-S3 через USB

# Определить порт
ls /dev/ttyUSB* /dev/ttyACM*
# Обычно /dev/ttyUSB0 или /dev/ttyACM0

# Прошить устройство
idf.py -p /dev/ttyUSB0 flash

# Или прошить и сразу открыть монитор
idf.py -p /dev/ttyUSB0 flash monitor
```

### Мониторинг

```bash
# Открыть serial monitor
idf.py -p /dev/ttyUSB0 monitor

# Выход из монитора: Ctrl+]
```

## Отладка

### OpenOCD и GDB

```bash
# В одном терминале запустить OpenOCD
openocd -f board/esp32s3-builtin.cfg

# В другом терминале запустить GDB
xtensa-esp32s3-elf-gdb build/macro-keyboard.elf
(gdb) target remote :3333
(gdb) mon reset halt
(gdb) continue
```

### VS Code Debugging

Создайте `.vscode/launch.json`:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "ESP32-S3 Debug",
            "type": "cppdbg",
            "request": "launch",
            "program": "${workspaceFolder}/firmware/macro-keyboard/build/macro-keyboard.elf",
            "cwd": "${workspaceFolder}",
            "MIMode": "gdb",
            "miDebuggerPath": "${env:HOME}/esp/esp-idf/.espressif/tools/xtensa-esp-elf-gdb/12.1_20221002/xtensa-esp-elf-gdb/bin/xtensa-esp32s3-elf-gdb",
            "miDebuggerServerAddress": "localhost:3333",
            "setupCommands": [
                {"text": "target remote :3333"},
                {"text": "mon reset halt"},
                {"text": "flushregs"}
            ]
        }
    ]
}
```

## Полезные команды

```bash
# Очистить сборку
idf.py fullclean

# Только очистить app (быстрее)
idf.py app-flash

# Стереть flash полностью
idf.py -p /dev/ttyUSB0 erase-flash

# Показать размер прошивки
idf.py size

# Показать размер компонентов
idf.py size-components

# Показать размер файлов
idf.py size-files

# Создать partition table
idf.py partition-table

# Прошить только partition table
idf.py partition-table-flash
```

## Troubleshooting

### Ошибка: Permission denied /dev/ttyUSB0

```bash
# Добавить пользователя в группу dialout
sudo usermod -a -G dialout $USER

# Перелогиниться или выполнить
newgrp dialout

# Или дать права на порт (временно)
sudo chmod 666 /dev/ttyUSB0
```

### Ошибка: Failed to connect to ESP32-S3

1. Проверьте подключение USB
2. Нажмите кнопку BOOT на плате
3. Попробуйте другой USB кабель
4. Проверьте, что используется data-кабель, а не только для зарядки

### Ошибка: ccache not found

```bash
sudo apt-get install ccache
```

### Ошибка: ninja not found

```bash
sudo apt-get install ninja-build
```

### Ошибка: Python packages missing

```bash
cd ~/esp/esp-idf
./install.sh esp32s3
```

## Проверка установки

Выполните эти команды для проверки:

```bash
# 1. Активировать ESP-IDF
. ~/esp/esp-idf/export.sh

# 2. Проверить версию
idf.py --version

# 3. Проверить компилятор
xtensa-esp32s3-elf-gcc --version

# 4. Проверить Python
python --version

# 5. Проверить cmake
cmake --version

# 6. Проверить ninja
ninja --version
```

Все команды должны выполниться без ошибок.

## Следующие шаги

После установки ESP-IDF:

1. Изучите документацию:
   - [`firmware/plans/REQUIREMENTS.md`](plans/REQUIREMENTS.md) - требования
   - [`../plans/architecture.md`](../plans/architecture.md) - архитектура
   - [`../plans/esp_idf_components.md`](../plans/esp_idf_components.md) - готовые компоненты

2. Создайте структуру проекта

3. Начните с базовой инициализации:
   - USB HID
   - GPIO для кнопок
   - SPI для дисплеев

4. Постепенно добавляйте функциональность

## Полезные ссылки

- [ESP-IDF Programming Guide](https://docs.espressif.com/projects/esp-idf/en/latest/esp32s3/)
- [ESP32-S3 Technical Reference](https://www.espressif.com/sites/default/files/documentation/esp32-s3_technical_reference_manual_en.pdf)
- [ESP-IDF Examples](https://github.com/espressif/esp-idf/tree/master/examples)
- [TinyUSB Documentation](https://docs.tinyusb.org/)
- [ESP-IDF VS Code Extension](https://github.com/espressif/vscode-esp-idf-extension)
