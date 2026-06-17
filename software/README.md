# MacroKeyboard Software

Управляющее программное обеспечение для ESP32-S3 макроклавиатуры с круглыми дисплеями.

## Описание

Полнофункциональная система управления макроклавиатурой, включающая:
- **Backend Service** — фоновый сервис для коммуникации с устройством
- **Configuration UI** — графический интерфейс настройки (Avalonia UI)
- **Plugin System** — поддержка плагинов (Stream Deck API совместимость)

## Возможности

- Кроссплатформенность — Windows, Linux, macOS
- USB Vendor Bulk — LibUsbDotNet (работает везде без драйверов)
- Backend Service — Windows Service / Linux Systemd
- Modern UI — Avalonia UI (тёмная тема, MVVM)
- Plugin System — Node.js, Python, C# плагины; WebSocket Stream Deck API
- IPC — TCP-сокеты между UI и Backend
- Длинный текст для Keyboard-действий — до 4096 байт через SPIFFS (пароли, авто-ответы, API-ключи)
- Сохранение профиля — Save пишет в файл, откуда был загружен (не в AppData)
- Загрузка с устройства — не затирает настройки папок, только мержит корневые кнопки

## Архитектура

```
ESP32-S3 Device
    ↕ USB Vendor Bulk
Backend Service
├── IPC Server (:28195)  → UI
├── WebSocket (:28196)   → Plugins
└── DeviceService        → USB протокол
```

## Проекты

| Проект | Описание |
|--------|----------|
| MacroKeyboard.Core | Модели, интерфейсы сервисов |
| MacroKeyboard.Communication | USB протокол, команды устройства |
| MacroKeyboard.Infrastructure | Реализация сервисов, репозитории |
| MacroKeyboard.Shared | IPC типы, Plugin API |
| MacroKeyboard.Backend | Фоновый сервис (IPC сервер, роутинг) |
| MacroKeyboard.UI | Avalonia UI (MVVM, CommunityToolkit) |

## Быстрый старт

### Требования

- .NET 10 SDK
- Linux: `libudev-dev libusb-1.0-0-dev`

### Сборка и запуск

```bash
cd software
dotnet restore
dotnet build MacroKeyboard.sln

# Backend Service
dotnet run --project src/MacroKeyboard.Backend

# UI (в другом терминале)
dotnet run --project src/MacroKeyboard.UI
```

### Linux: доступ к USB без root

```bash
sudo cp scripts/99-macrokeyboard.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules
```

## Технологии

| Компонент | Технология |
|-----------|-----------|
| Runtime | .NET 10 |
| UI | Avalonia UI 12 |
| Архитектура | MVVM (CommunityToolkit.Mvvm) |
| USB | LibUsbDotNet 3.x |
| Изображения | SixLabors.ImageSharp |
| Сериализация | Newtonsoft.Json |
| Логирование | Serilog |

## Работа с профилями

Профили хранятся как JSON-файлы в `%APPDATA%/MacroKeyboard/Profiles/`. При открытии профиля через File → Load устанавливается `SourceFilePath`, и последующее нажатие Save пишет именно в этот файл. Если профиль не был загружен из файла (создан в UI или пришёл с устройства), Save пишет в AppData.

Загрузка с устройства (Load from Device) объединяет данные с устройства только для корневых кнопок — действия и LED. Настройки папок, изображения и пользовательские имена сохраняются из локального профиля.

## Длинный текст (SPIFFS)

Для Keyboard-действий с текстом длиннее 44 байт ПО автоматически использует трёхфазный протокол передачи (CMD 0x38/0x39/0x3A). Текст сохраняется на устройстве в SPIFFS и загружается при нажатии кнопки. Лимит — 4096 байт.

## Plugin System

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

## Лицензия

TBD
