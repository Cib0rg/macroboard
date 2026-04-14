# MacroKeyboard Software

Управляющее программное обеспечение для ESP32-S3 макроклавиатуры с круглыми дисплеями.

## 🎯 Описание

Полнофункциональная система управления макроклавиатурой, включающая:
- **Backend Service** - фоновый сервис для коммуникации с устройством
- **TrayApp** - приложение в системном трее
- **Configuration UI** - графический интерфейс настройки
- **Plugin System** - поддержка плагинов (Stream Deck API совместимость)

## ✨ Особенности

✅ **Кроссплатформенность** - Windows, Linux, macOS
✅ **USB HID** - HidSharp (работает везде)
✅ **Backend Service** - Windows Service + Linux Systemd
✅ **Modern UI** - Avalonia UI (темная тема)
✅ **Plugin System** - Node.js, Python, C# плагины
✅ **IPC Communication** - TCP сокеты
✅ **WebSocket Server** - Stream Deck API
✅ **Clean Architecture** - MVVM + DI + Logging

## 🏗️ Архитектура

```
ESP32-S3 Device
    ↕ USB HID
Backend Service
├── IPC Server (:28195) → TrayApp/UI
├── WebSocket (:28196) → Plugins
└── DeviceManager → USB коммуникация
```

## 📦 Проекты

| Проект | Описание | Статус |
|--------|----------|--------|
| MacroKeyboard.Core | Модели и интерфейсы | ✅ 100% |
| MacroKeyboard.Communication | USB HID протокол | ✅ 100% |
| MacroKeyboard.Infrastructure | Реализация сервисов | ✅ 100% |
| MacroKeyboard.Shared | IPC + Events + Plugins | ✅ 100% |
| MacroKeyboard.Backend | Backend Service | ✅ 100% |
| MacroKeyboard.TrayApp | Системный трей | ✅ 100% |
| MacroKeyboard.UI | Configuration UI | ✅ 100% |
| MacroKeyboard.TestConsole | Тестовое приложение | ✅ 100% |

## 🚀 Быстрый старт

### Требования

- .NET 8.0 SDK
- Linux: `libudev-dev libusb-1.0-0-dev`

### Сборка

```bash
cd /home/andrewp/elgato/software
dotnet restore
dotnet build
```

### Запуск

```bash
# Backend Service
dotnet run --project src/MacroKeyboard.Backend

# TrayApp (в другом терминале)
dotnet run --project src/MacroKeyboard.TrayApp

# Configuration UI (в другом терминале)
dotnet run --project src/MacroKeyboard.UI
```

Подробнее: [`QUICK_START.md`](QUICK_START.md)

## 📝 Документация

- [`QUICK_START.md`](QUICK_START.md) - Быстрый старт
- [`FINAL_IMPLEMENTATION_REPORT.md`](FINAL_IMPLEMENTATION_REPORT.md) - Финальный отчет
- [`LINUX_SUPPORT.md`](LINUX_SUPPORT.md) - Поддержка Linux
- [`BACKEND_IMPLEMENTATION.md`](BACKEND_IMPLEMENTATION.md) - Backend Service
- [`TRAYAPP_REPORT.md`](TRAYAPP_REPORT.md) - TrayApp
- [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md) - Статус проекта

## 🔌 Plugin System

### Создание плагина

```javascript
// manifest.json
{
  "Id": "com.example.myplugin",
  "Name": "My Plugin",
  "Version": "1.0.0",
  "Type": "executable",
  "Runtime": "node",
  "EntryPoint": "index.js"
}

// index.js
const WebSocket = require('ws');
const ws = new WebSocket('ws://localhost:28196');

ws.on('message', (data) => {
  const message = JSON.parse(data);
  console.log('Received:', message);
});
```

Подробнее: [`plans/plugin_system.md`](plans/plugin_system.md)

## 📊 Статистика

**Проекты:** 8
**Файлов:** 84 (C# + XAML)
**Строк кода:** 5300+
**Сборка:** ✅ Build succeeded

## 🤝 Contributing

Проект находится в активной разработке. Вклад приветствуется!

## 📄 License

TBD

## 🔗 Ссылки

- Firmware: [`../firmware/`](../firmware/)
- Plans: [`../plans/`](../plans/)
- Protocol: [`../plans/protocol.md`](../plans/protocol.md)

---

**Версия:** 1.0.0
**Дата:** 2026-04-10
**Статус:** ✅ Ready for testing
