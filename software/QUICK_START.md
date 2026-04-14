# MacroKeyboard - Quick Start Guide

## 🚀 Быстрый старт

### Требования

- ✅ .NET 8.0 SDK
- ✅ Linux: `libudev-dev libusb-1.0-0-dev`
- ⏭️ ESP32-S3 устройство (опционально для тестирования)

### Установка зависимостей (Linux)

```bash
# Ubuntu/Debian
sudo apt-get update
sudo apt-get install libudev-dev libusb-1.0-0-dev

# Fedora/RHEL
sudo dnf install systemd-devel libusb-devel
```

### Настройка USB доступа (Linux)

```bash
# Создать udev правило
sudo nano /etc/udev/rules.d/99-macrokeyboard.rules

# Добавить (замените VID:PID на ваши):
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="303a", ATTRS{idProduct}=="4001", MODE="0666"

# Перезагрузить udev
sudo udevadm control --reload-rules
sudo udevadm trigger
```

---

## 📦 Сборка проекта

```bash
cd /home/andrewp/elgato/software

# Восстановить зависимости
dotnet restore

# Собрать все проекты
dotnet build

# Результат: ✅ Build succeeded (0 errors)
```

---

## 🎮 Запуск компонентов

### Вариант 1: Полная система (3 терминала)

**Терминал 1 - Backend Service:**
```bash
cd /home/andrewp/elgato/software
dotnet run --project src/MacroKeyboard.Backend

# Ожидаемый вывод:
# [INFO] Starting MacroKeyboard Backend Service
# [INFO] Starting IPC Server on port 28195...
# [INFO] IPC Server started successfully
# [INFO] Device monitoring started
```

**Терминал 2 - TrayApp:**
```bash
cd /home/andrewp/elgato/software
dotnet run --project src/MacroKeyboard.TrayApp

# Ожидаемый вывод:
# [INFO] Connecting to backend...
# [INFO] Connected to backend
# Иконка появится в системном трее
```

**Терминал 3 - Configuration UI:**
```bash
cd /home/andrewp/elgato/software
dotnet run --project src/MacroKeyboard.UI

# Ожидаемый вывод:
# [INFO] Connecting to backend...
# [INFO] Connected to backend
# Откроется окно конфигурации
```

### Вариант 2: Только Backend + TestConsole

**Терминал 1 - Backend:**
```bash
dotnet run --project src/MacroKeyboard.Backend
```

**Терминал 2 - TestConsole:**
```bash
dotnet run --project src/MacroKeyboard.TestConsole

# Интерактивное меню:
# 1. Switch Profile
# 2. Send Profile to Device
# 3. Set LED Color
# 4. Show Device Info
# 5. List Profiles
# 0. Exit
```

---

## 🧪 Тестирование

### 1. Проверка Backend

```bash
# Запустить Backend
dotnet run --project src/MacroKeyboard.Backend

# В другом терминале проверить IPC
nc localhost 28195
# Отправить: {"MessageType":"system.ping"}
# Ожидается ответ с "system.ping.response"
```

### 2. Проверка TrayApp

```bash
# Запустить Backend
dotnet run --project src/MacroKeyboard.Backend

# Запустить TrayApp
dotnet run --project src/MacroKeyboard.TrayApp

# Проверить:
# ✅ Иконка в трее появилась
# ✅ Правый клик → меню работает
# ✅ Статус "Connected"
```

### 3. Проверка UI

```bash
# Запустить Backend
dotnet run --project src/MacroKeyboard.Backend

# Запустить UI
dotnet run --project src/MacroKeyboard.UI

# Проверить:
# ✅ Окно открылось
# ✅ Статус "Connected" (зеленый индикатор)
# ✅ Dashboard показывает информацию
# ✅ Profile Editor работает
# ✅ Settings открывается
```

### 4. Проверка с устройством

```bash
# Подключить ESP32-S3 устройство

# Запустить Backend
dotnet run --project src/MacroKeyboard.Backend

# Ожидаемый вывод:
# [INFO] Found device: MacroKeyboard by Espressif
# [INFO] Device connected successfully
# [INFO] Device connected: MacroKeyboard (FW: 1.0.0)

# В UI/TrayApp должно появиться:
# Device: MacroKeyboard
# Firmware: 1.0.0
# Status: Connected
```

---

## 📁 Структура проекта

```
software/
├── MacroKeyboard.sln                    # Solution файл
├── src/
│   ├── MacroKeyboard.Core/              # ✅ Модели и интерфейсы
│   ├── MacroKeyboard.Communication/     # ✅ USB HID протокол
│   ├── MacroKeyboard.Infrastructure/    # ✅ Реализация сервисов
│   ├── MacroKeyboard.Shared/            # ✅ IPC + Events + Plugins
│   ├── MacroKeyboard.Backend/           # ✅ Backend Service
│   ├── MacroKeyboard.TrayApp/           # ✅ Системный трей
│   ├── MacroKeyboard.UI/                # ✅ Configuration UI
│   └── MacroKeyboard.TestConsole/       # ✅ Тестовое приложение
├── logs/                                # Логи приложений
└── docs/                                # Документация
```

---

## 🔧 Production Deployment

### Windows Service

```powershell
# Собрать Release
dotnet publish src/MacroKeyboard.Backend -c Release -o publish/Backend

# Установить как службу
sc create "MacroKeyboard Backend" binPath="C:\Path\To\publish\Backend\MacroKeyboard.Backend.exe"
sc start "MacroKeyboard Backend"

# Проверить статус
sc query "MacroKeyboard Backend"
```

### Linux Systemd

```bash
# Собрать Release
dotnet publish src/MacroKeyboard.Backend -c Release -o publish/Backend

# Создать service файл
sudo nano /etc/systemd/system/macrokeyboard-backend.service

# Содержимое (см. LINUX_SUPPORT.md)

# Включить и запустить
sudo systemctl enable macrokeyboard-backend.service
sudo systemctl start macrokeyboard-backend.service

# Проверить статус
sudo systemctl status macrokeyboard-backend.service

# Логи
sudo journalctl -u macrokeyboard-backend.service -f
```

---

## 🐛 Troubleshooting

### Backend не запускается

```bash
# Проверить порты
netstat -tuln | grep -E "28195|28196"

# Если заняты, изменить в appsettings.json
```

### Устройство не обнаруживается (Linux)

```bash
# Проверить USB устройства
lsusb

# Проверить HID устройства
ls -la /dev/hidraw*

# Проверить права доступа
ls -l /dev/hidraw0

# Если нет прав, запустить с sudo (временно)
sudo dotnet run --project src/MacroKeyboard.Backend
```

### TrayApp/UI не подключается к Backend

```bash
# Проверить что Backend запущен
ps aux | grep MacroKeyboard.Backend

# Проверить логи Backend
tail -f logs/backend-*.log

# Проверить IPC порт
nc localhost 28195
```

---

## 📚 Документация

- [`README.md`](README.md) - Общая информация
- [`FINAL_IMPLEMENTATION_REPORT.md`](FINAL_IMPLEMENTATION_REPORT.md) - Финальный отчет
- [`LINUX_SUPPORT.md`](LINUX_SUPPORT.md) - Поддержка Linux
- [`BACKEND_IMPLEMENTATION.md`](BACKEND_IMPLEMENTATION.md) - Backend Service
- [`TRAYAPP_REPORT.md`](TRAYAPP_REPORT.md) - TrayApp
- [`BUILD_STATUS.md`](BUILD_STATUS.md) - Статус сборки

---

## 🎯 Следующие шаги

### Для разработчиков

1. **Доработать UI Views**
   - ButtonConfigView - настройка кнопок
   - ImageEditorView - редактор изображений
   - PluginBrowserView - браузер плагинов

2. **Добавить unit тесты**
   - xUnit проекты для каждого компонента
   - Moq для мокирования

3. **Создать плагины**
   - OBS Studio Control
   - Spotify Control
   - Discord Integration

### Для пользователей

1. **Подключить устройство**
2. **Запустить Backend**
3. **Запустить TrayApp или UI**
4. **Настроить профили**
5. **Наслаждаться!**

---

## 💡 Полезные команды

```bash
# Очистить build артефакты
dotnet clean

# Пересобрать всё
dotnet build --no-incremental

# Запустить с логированием Debug
dotnet run --project src/MacroKeyboard.Backend --configuration Debug

# Собрать Release версию
dotnet publish -c Release

# Проверить версии пакетов
dotnet list package

# Обновить пакеты
dotnet restore
```

---

**Дата:** 2026-04-10
**Статус:** ✅ Готово к использованию
**Прогресс:** 90%
