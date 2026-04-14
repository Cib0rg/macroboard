# Linux Support для MacroKeyboard

## ✅ Изменения для кроссплатформенности

### Замена HidLibrary на HidSharp

**Проблема:** HidLibrary работает только на Windows (использует Windows API)

**Решение:** Заменили на **HidSharp** - чистая .NET библиотека, работающая на:
- ✅ Windows
- ✅ Linux
- ✅ macOS

### Изменения в коде

**MacroKeyboard.Communication.csproj:**
```xml
<!-- Было -->
<PackageReference Include="HidLibrary" Version="3.3.40" />

<!-- Стало -->
<PackageReference Include="HidSharp" Version="2.1.0" />
```

**HidDeviceManager.cs:**
- Переписан с использованием HidSharp API
- Поддержка всех платформ
- Улучшенная обработка ошибок

## 🐧 Запуск на Linux (WSL)

### 1. Установка зависимостей

```bash
# Ubuntu/Debian
sudo apt-get update
sudo apt-get install libudev-dev libusb-1.0-0-dev

# Fedora/RHEL
sudo dnf install systemd-devel libusb-devel
```

### 2. Настройка udev правил

Создайте файл `/etc/udev/rules.d/99-macrokeyboard.rules`:

```bash
sudo nano /etc/udev/rules.d/99-macrokeyboard.rules
```

Добавьте правило (замените VID:PID на ваши значения):

```
# MacroKeyboard ESP32-S3
SUBSYSTEM=="usb", ATTRS{idVendor}=="303a", ATTRS{idProduct}=="4001", MODE="0666", GROUP="plugdev"
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="303a", ATTRS{idProduct}=="4001", MODE="0666", GROUP="plugdev"
```

Перезагрузите udev:

```bash
sudo udevadm control --reload-rules
sudo udevadm trigger
```

### 3. Добавьте пользователя в группу plugdev

```bash
sudo usermod -a -G plugdev $USER
# Перелогиньтесь для применения изменений
```

### 4. Запуск приложения

```bash
cd /home/andrewp/elgato/software

# TestConsole
dotnet run --project src/MacroKeyboard.TestConsole

# Backend Service
dotnet run --project src/MacroKeyboard.Backend
```

## 🔧 Настройка Backend как systemd service

### 1. Создайте service файл

```bash
sudo nano /etc/systemd/system/macrokeyboard-backend.service
```

### 2. Содержимое файла

```ini
[Unit]
Description=MacroKeyboard Backend Service
After=network.target

[Service]
Type=notify
User=andrewp
WorkingDirectory=/home/andrewp/elgato/software/src/MacroKeyboard.Backend/bin/Release/net8.0
ExecStart=/usr/bin/dotnet /home/andrewp/elgato/software/src/MacroKeyboard.Backend/bin/Release/net8.0/MacroKeyboard.Backend.dll
Restart=on-failure
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=macrokeyboard-backend
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

### 3. Включите и запустите сервис

```bash
# Перезагрузите systemd
sudo systemctl daemon-reload

# Включите автозапуск
sudo systemctl enable macrokeyboard-backend.service

# Запустите сервис
sudo systemctl start macrokeyboard-backend.service

# Проверьте статус
sudo systemctl status macrokeyboard-backend.service

# Просмотр логов
sudo journalctl -u macrokeyboard-backend.service -f
```

## 🔍 Отладка на Linux

### Проверка USB устройства

```bash
# Список всех USB устройств
lsusb

# Детальная информация
lsusb -v -d 303a:4001

# HID устройства
ls -la /dev/hidraw*

# Права доступа
ls -l /dev/hidraw0
```

### Проверка HID доступа

```bash
# Установите hidapi-tools
sudo apt-get install libhidapi-hidraw0 libhidapi-libusb0

# Список HID устройств
sudo hidapi-test-libusb
```

### Тестирование с правами root

Если устройство не обнаруживается, попробуйте запустить с sudo:

```bash
sudo dotnet run --project src/MacroKeyboard.TestConsole
```

Если это помогло, значит проблема в правах доступа (см. udev правила выше).

## 📝 Различия между платформами

### Windows
- ✅ Работает из коробки
- ✅ Windows Service support
- ✅ Не требует дополнительных прав

### Linux
- ✅ Требует udev правила для доступа к HID
- ✅ Systemd daemon support
- ✅ Требует группу plugdev или root права
- ⚠️ В WSL может потребоваться USB/IP для доступа к USB устройствам

### macOS
- ✅ Работает из коробки
- ✅ Может потребовать разрешение в System Preferences
- ✅ launchd daemon support

## 🐳 WSL USB Support

WSL2 по умолчанию не имеет прямого доступа к USB устройствам. Есть два варианта:

### Вариант 1: usbipd-win (рекомендуется)

1. Установите на Windows: https://github.com/dorssel/usbipd-win
2. В PowerShell (от администратора):

```powershell
# Список устройств
usbipd list

# Привязать устройство (один раз)
usbipd bind --busid 1-4

# Подключить к WSL
usbipd attach --wsl --busid 1-4
```

3. В WSL проверьте:

```bash
lsusb
ls /dev/hidraw*
```

### Вариант 2: Запуск Backend на Windows, UI в WSL

Backend работает на Windows (имеет доступ к USB), а UI/TrayApp подключаются через IPC (TCP localhost:28195).

## ✅ Преимущества HidSharp

1. **Кроссплатформенность**: Windows, Linux, macOS
2. **Чистый .NET**: Нет нативных зависимостей
3. **Активная поддержка**: Регулярные обновления
4. **Лучшая производительность**: Асинхронные операции
5. **Больше возможностей**: Поддержка Serial, Bluetooth

## 🧪 Тестирование

### Проверка на Linux

```bash
cd /home/andrewp/elgato/software

# Сборка
dotnet build

# Запуск TestConsole
dotnet run --project src/MacroKeyboard.TestConsole

# Ожидаемый вывод:
# [INFO] Searching for device (VID: 0x303A, PID: 0x4001)...
# [INFO] Found device: MacroKeyboard by Espressif
# [INFO] Device connected successfully
```

### Проверка Backend Service

```bash
# Запуск Backend
dotnet run --project src/MacroKeyboard.Backend

# В другом терминале - проверка IPC
nc localhost 28195
# Отправьте: {"MessageType":"system.ping"}
# Ожидается: {"MessageType":"system.pong"}
```

## 📚 Дополнительные ресурсы

- HidSharp документация: https://github.com/IntergatedCircuits/HidSharp
- udev правила: https://wiki.archlinux.org/title/Udev
- systemd services: https://www.freedesktop.org/software/systemd/man/systemd.service.html
- WSL USB: https://learn.microsoft.com/en-us/windows/wsl/connect-usb

---

**Статус:** ✅ Полная поддержка Linux реализована
**Тестирование:** Требуется проверка с реальным устройством
