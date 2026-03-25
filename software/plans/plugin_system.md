# Система плагинов совместимая с Elgato Stream Deck

## Обзор

Elgato Stream Deck использует архитектуру с плагинами, где:
- **Backend** (наше приложение) управляет устройством
- **Плагины** - это отдельные приложения (обычно HTML/JS/CSS)
- **Коммуникация** через WebSocket и JSON-RPC

## Архитектура плагинов Elgato Stream Deck

### Компоненты

```
┌─────────────────────────────────────────────────────┐
│  Stream Deck Software (Backend)                     │
│  ┌───────────────────────────────────────────────┐  │
│  │  WebSocket Server (port 28196)                │  │
│  │  - Регистрация плагинов                       │  │
│  │  - Обмен событиями                            │  │
│  │  - Управление состоянием                      │  │
│  └───────────────────────────────────────────────┘  │
│                      ↕                              │
│  ┌───────────────────────────────────────────────┐  │
│  │  Device Manager                               │  │
│  │  - Управление устройством                     │  │
│  │  - Отправка изображений                       │  │
│  │  - Обработка нажатий                          │  │
│  └───────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
                      ↕
┌─────────────────────────────────────────────────────┐
│  Plugin (HTML/JS/CSS или Native)                    │
│  ┌───────────────────────────────────────────────┐  │
│  │  Property Inspector (UI для настройки)        │  │
│  └───────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────┐  │
│  │  Plugin Logic                                 │  │
│  │  - Обработка событий                          │  │
│  │  - Генерация изображений                      │  │
│  │  - Выполнение действий                        │  │
│  └───────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
```

## Протокол плагинов Stream Deck

### WebSocket API

**Endpoint**: `ws://localhost:28196`

### Регистрация плагина

```json
{
    "event": "registerPlugin",
    "uuid": "com.example.myplugin"
}
```

### События от Backend к плагину

**keyDown** - кнопка нажата
```json
{
    "event": "keyDown",
    "context": "unique-context-id",
    "device": "device-id",
    "payload": {
        "settings": {},
        "coordinates": {"column": 0, "row": 0},
        "state": 0,
        "userDesiredState": 0,
        "isInMultiAction": false
    }
}
```

**keyUp** - кнопка отпущена
```json
{
    "event": "keyUp",
    "context": "unique-context-id",
    "device": "device-id",
    "payload": { /* аналогично keyDown */ }
}
```

**willAppear** - кнопка появилась на экране
```json
{
    "event": "willAppear",
    "context": "unique-context-id",
    "device": "device-id",
    "payload": {
        "settings": {},
        "coordinates": {"column": 0, "row": 0},
        "state": 0,
        "isInMultiAction": false
    }
}
```

**willDisappear** - кнопка исчезла (смена профиля)
```json
{
    "event": "willDisappear",
    "context": "unique-context-id",
    "device": "device-id"
}
```

**deviceDidConnect** - устройство подключено
```json
{
    "event": "deviceDidConnect",
    "device": "device-id",
    "deviceInfo": {
        "name": "Stream Deck",
        "type": 0,
        "size": {"columns": 5, "rows": 2}
    }
}
```

### События от плагина к Backend

**setTitle** - установить текст на кнопке
```json
{
    "event": "setTitle",
    "context": "unique-context-id",
    "payload": {
        "title": "My Title",
        "target": 0
    }
}
```

**setImage** - установить изображение
```json
{
    "event": "setImage",
    "context": "unique-context-id",
    "payload": {
        "image": "data:image/png;base64,iVBORw0KG...",
        "target": 0
    }
}
```

**setState** - изменить состояние кнопки
```json
{
    "event": "setState",
    "context": "unique-context-id",
    "payload": {
        "state": 1
    }
}
```

**sendToPropertyInspector** - отправить данные в UI настройки
```json
{
    "event": "sendToPropertyInspector",
    "context": "unique-context-id",
    "payload": { /* custom data */ }
}
```

**logMessage** - логирование
```json
{
    "event": "logMessage",
    "payload": {
        "message": "Debug info"
    }
}
```

## Структура плагина Stream Deck

```
com.example.myplugin/
├── manifest.json           # Метаданные плагина
├── plugin.js              # Основная логика (Node.js)
├── propertyinspector/     # UI для настройки
│   ├── index.html
│   ├── style.css
│   └── script.js
└── images/                # Иконки и изображения
    ├── icon.png
    ├── action.png
    └── key.png
```

### manifest.json

```json
{
    "Name": "My Plugin",
    "Version": "1.0.0",
    "Author": "Author Name",
    "Description": "Plugin description",
    "Category": "Custom",
    "Icon": "images/icon",
    "URL": "https://example.com",
    "SDKVersion": 2,
    "Software": {
        "MinimumVersion": "6.0"
    },
    "OS": [
        {"Platform": "windows", "MinimumVersion": "10"},
        {"Platform": "mac", "MinimumVersion": "10.14"}
    ],
    "Actions": [
        {
            "UUID": "com.example.myplugin.action1",
            "Name": "My Action",
            "Icon": "images/action",
            "States": [
                {"Image": "images/key"}
            ],
            "PropertyInspectorPath": "propertyinspector/index.html",
            "SupportedInMultiActions": true,
            "Tooltip": "Action tooltip"
        }
    ]
}
```

## Адаптация для нашего устройства

### Архитектура с поддержкой плагинов

```
MacroKeyboard Software
├── Backend Service (C#)
│   ├── Device Manager          # Управление нашим устройством
│   ├── WebSocket Server        # Совместимый с Stream Deck API
│   ├── Plugin Manager          # Загрузка и управление плагинами
│   └── Event Router            # Маршрутизация событий
│
├── Configuration UI (C#/WPF)
│   ├── Profile Editor          # Настройка профилей
│   ├── Button Config           # Настройка кнопок
│   └── Plugin Browser          # Установка плагинов
│
└── Plugins (HTML/JS или C#)
    ├── Built-in Plugins        # Встроенные плагины
    │   ├── Keyboard            # Эмуляция клавиш
    │   ├── Text                # Отображение текста
    │   ├── Image               # Статичное изображение
    │   └── System              # Системные действия
    │
    └── Third-party Plugins     # Сторонние плагины
        ├── OBS Studio
        ├── Spotify
        ├── Discord
        └── ... (любые Stream Deck плагины)
```

### Модифицированная архитектура

```csharp
// Новые компоненты

MacroKeyboard.PluginSystem/
├── PluginHost/
│   ├── PluginManager.cs        # Управление плагинами
│   ├── PluginLoader.cs         # Загрузка плагинов
│   └── PluginSandbox.cs        # Изоляция плагинов
│
├── WebSocketServer/
│   ├── StreamDeckWebSocketServer.cs  # WebSocket сервер
│   ├── MessageHandler.cs             # Обработка сообщений
│   └── EventRouter.cs                # Маршрутизация событий
│
├── PluginAPI/
│   ├── IPlugin.cs              # Интерфейс плагина
│   ├── PluginContext.cs        # Контекст плагина
│   └── PluginManifest.cs       # Манифест плагина
│
└── BuiltInPlugins/
    ├── KeyboardPlugin/
    ├── TextPlugin/
    ├── ImagePlugin/
    └── SystemPlugin/
```

## Реализация WebSocket Server

```csharp
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;

public class StreamDeckWebSocketServer
{
    private HttpListener _listener;
    private Dictionary<string, WebSocket> _connectedPlugins = new();
    private readonly IDeviceService _deviceService;
    
    public async Task StartAsync(int port = 28196)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        
        while (true)
        {
            var context = await _listener.GetContextAsync();
            
            if (context.Request.IsWebSocketRequest)
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                _ = HandleWebSocketAsync(wsContext.WebSocket);
            }
        }
    }
    
    private async Task HandleWebSocketAsync(WebSocket webSocket)
    {
        var buffer = new byte[4096];
        
        while (webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), 
                CancellationToken.None);
            
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await HandleMessageAsync(webSocket, message);
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, 
                    "", 
                    CancellationToken.None);
            }
        }
    }
    
    private async Task HandleMessageAsync(WebSocket webSocket, string message)
    {
        var json = JsonConvert.DeserializeObject<PluginMessage>(message);
        
        switch (json.Event)
        {
            case "registerPlugin":
                _connectedPlugins[json.Uuid] = webSocket;
                await SendDeviceInfoToPluginAsync(webSocket);
                break;
                
            case "setImage":
                await HandleSetImageAsync(json);
                break;
                
            case "setTitle":
                await HandleSetTitleAsync(json);
                break;
                
            // ... другие события
        }
    }
    
    // Отправка события плагину
    public async Task SendEventToPluginAsync(string pluginUuid, object eventData)
    {
        if (_connectedPlugins.TryGetValue(pluginUuid, out var webSocket))
        {
            var json = JsonConvert.SerializeObject(eventData);
            var bytes = Encoding.UTF8.GetBytes(json);
            
            await webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }
    }
}
```

## Plugin Manager

```csharp
public class PluginManager
{
    private readonly string _pluginsDirectory;
    private readonly Dictionary<string, PluginInfo> _loadedPlugins = new();
    private readonly StreamDeckWebSocketServer _webSocketServer;
    
    public PluginManager(StreamDeckWebSocketServer webSocketServer)
    {
        _webSocketServer = webSocketServer;
        _pluginsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MacroKeyboard", "Plugins");
    }
    
    public async Task LoadPluginsAsync()
    {
        if (!Directory.Exists(_pluginsDirectory))
            Directory.CreateDirectory(_pluginsDirectory);
        
        var pluginDirs = Directory.GetDirectories(_pluginsDirectory);
        
        foreach (var dir in pluginDirs)
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                var manifest = JsonConvert.DeserializeObject<PluginManifest>(
                    await File.ReadAllTextAsync(manifestPath));
                
                var plugin = new PluginInfo
                {
                    Uuid = manifest.Actions[0].UUID,
                    Name = manifest.Name,
                    Version = manifest.Version,
                    Path = dir,
                    Manifest = manifest
                };
                
                _loadedPlugins[plugin.Uuid] = plugin;
                
                // Запустить плагин (если это Node.js приложение)
                if (File.Exists(Path.Combine(dir, "plugin.js")))
                {
                    await StartNodePluginAsync(plugin);
                }
            }
        }
    }
    
    private async Task StartNodePluginAsync(PluginInfo plugin)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = Path.Combine(plugin.Path, "plugin.js"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment =
            {
                ["STREAMDECK_PORT"] = "28196",
                ["STREAMDECK_UUID"] = plugin.Uuid,
                ["STREAMDECK_REGISTER_EVENT"] = "registerPlugin"
            }
        };
        
        var process = Process.Start(startInfo);
        plugin.Process = process;
    }
}
```

## Интеграция с нашим устройством

### Маппинг событий

```csharp
public class EventRouter
{
    private readonly IDeviceService _deviceService;
    private readonly StreamDeckWebSocketServer _webSocketServer;
    private readonly Dictionary<int, string> _buttonToPluginMap = new();
    
    public EventRouter(
        IDeviceService deviceService,
        StreamDeckWebSocketServer webSocketServer)
    {
        _deviceService = deviceService;
        _webSocketServer = webSocketServer;
        
        // Подписка на события устройства
        _deviceService.ButtonPressed += OnButtonPressed;
        _deviceService.ButtonReleased += OnButtonReleased;
        _deviceService.ProfileChanged += OnProfileChanged;
    }
    
    private async void OnButtonPressed(object sender, ButtonEventArgs e)
    {
        // Получить плагин, привязанный к кнопке
        if (_buttonToPluginMap.TryGetValue(e.ButtonId, out var pluginUuid))
        {
            // Отправить событие keyDown плагину
            var eventData = new
            {
                @event = "keyDown",
                context = $"button-{e.ButtonId}",
                device = "macro-keyboard-001",
                payload = new
                {
                    settings = GetButtonSettings(e.ButtonId),
                    coordinates = new { column = e.ButtonId % 5, row = e.ButtonId / 5 },
                    state = 0
                }
            };
            
            await _webSocketServer.SendEventToPluginAsync(pluginUuid, eventData);
        }
    }
    
    // Обработка команд от плагина
    public async Task HandlePluginCommandAsync(string pluginUuid, PluginCommand command)
    {
        switch (command.Event)
        {
            case "setImage":
                // Конвертировать base64 в JPEG
                var imageData = Convert.FromBase64String(
                    command.Payload.Image.Replace("data:image/png;base64,", ""));
                
                // Отправить на устройство
                await _deviceService.SetButtonImageAsync(
                    command.ButtonId, 
                    imageData);
                break;
                
            case "setTitle":
                // Сгенерировать изображение с текстом
                var titleImage = GenerateTextImage(command.Payload.Title);
                await _deviceService.SetButtonImageAsync(
                    command.ButtonId, 
                    titleImage);
                break;
                
            case "showAlert":
                // Мигнуть LED
                await _deviceService.FlashLedAsync(command.ButtonId);
                break;
        }
    }
}
```

## Обновленная архитектура софта

```
MacroKeyboard.sln
│
├── src/
│   ├── MacroKeyboard.Core/              # Бизнес-логика
│   ├── MacroKeyboard.Communication/     # USB HID
│   ├── MacroKeyboard.Infrastructure/    # Сервисы
│   │
│   ├── MacroKeyboard.PluginSystem/      # НОВОЕ: Система плагинов
│   │   ├── WebSocketServer/
│   │   ├── PluginHost/
│   │   ├── PluginAPI/
│   │   └── BuiltInPlugins/
│   │
│   ├── MacroKeyboard.Backend/           # НОВОЕ: Backend сервис
│   │   ├── Services/
│   │   │   ├── BackendService.cs       # Главный сервис
│   │   │   ├── DeviceManager.cs        # Управление устройством
│   │   │   └── EventRouter.cs          # Маршрутизация событий
│   │   └── Program.cs                  # Точка входа
│   │
│   ├── MacroKeyboard.UI/                # Configuration UI
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   └── PluginBrowser/              # НОВОЕ: Браузер плагинов
│   │
│   └── MacroKeyboard.TrayApp/           # Tray приложение
│
└── plugins/                             # Директория плагинов
    ├── com.macrokeyboard.keyboard/      # Встроенный плагин
    ├── com.macrokeyboard.text/
    └── ... (сторонние плагины)
```

## Backend Service

Backend должен работать как Windows Service или Linux daemon.

```csharp
// Program.cs
public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddHostedService<BackendService>();
                services.AddSingleton<IDeviceService, DeviceService>();
                services.AddSingleton<StreamDeckWebSocketServer>();
                services.AddSingleton<PluginManager>();
                services.AddSingleton<EventRouter>();
            })
            .Build();
        
        await host.RunAsync();
    }
}

// BackendService.cs
public class BackendService : BackgroundService
{
    private readonly IDeviceService _deviceService;
    private readonly StreamDeckWebSocketServer _webSocketServer;
    private readonly PluginManager _pluginManager;
    private readonly EventRouter _eventRouter;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Подключиться к устройству
        await _deviceService.ConnectAsync();
        
        // 2. Запустить WebSocket сервер
        _ = _webSocketServer.StartAsync();
        
        // 3. Загрузить плагины
        await _pluginManager.LoadPluginsAsync();
        
        // 4. Ждать событий
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(100, stoppingToken);
        }
    }
}
```

## Встроенные плагины

### Keyboard Plugin

```javascript
// plugin.js
const streamDeck = require('@elgato/streamdeck');

streamDeck.connect().then(sd => {
    // Регистрация действия
    sd.on('keyDown', (event) => {
        const settings = event.payload.settings;
        
        // Отправить команду на устройство через backend
        // Backend преобразует в USB HID команду
        sd.sendToBackend({
            action: 'keyboard',
            key: settings.key,
            modifiers: settings.modifiers
        });
    });
    
    // Обновление изображения
    sd.on('willAppear', (event) => {
        const settings = event.payload.settings;
        
        // Сгенерировать изображение с текстом клавиши
        const image = generateKeyImage(settings.key);
        sd.setImage(event.context, image);
    });
});
```

### Text Plugin

```javascript
// plugin.js
streamDeck.connect().then(sd => {
    sd.on('willAppear', (event) => {
        const settings = event.payload.settings;
        
        // Сгенерировать изображение с текстом
        const canvas = createCanvas(160, 160);
        const ctx = canvas.getContext('2d');
        
        ctx.fillStyle = settings.backgroundColor || '#000000';
        ctx.fillRect(0, 0, 160, 160);
        
        ctx.fillStyle = settings.textColor || '#FFFFFF';
        ctx.font = `${settings.fontSize || 24}px Arial`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(settings.text, 80, 80);
        
        const image = canvas.toDataURL();
        sd.setImage(event.context, image);
    });
    
    // Обновление при изменении настроек
    sd.on('didReceiveSettings', (event) => {
        // Перерисовать изображение
    });
});
```

## Совместимость с существующими плагинами

### Что нужно эмулировать

1. **WebSocket API** - полностью совместимый
2. **Device Info** - адаптировать под наше устройство
3. **Координаты кнопок** - маппинг 0-9 → (column, row)
4. **Изображения** - конвертация PNG/SVG → JPEG 160×160

### Отличия от Stream Deck

| Параметр | Stream Deck | Наше устройство |
|----------|-------------|-----------------|
| Размер кнопки | 72×72 | 160×160 |
| Форма | Квадрат | Круг |
| Количество кнопок | 6-32 | 10 |
| Формат изображения | PNG/BMP | JPEG |
| Дополнительно | - | RGB LED, Encoder |

### Адаптер изображений

```csharp
public class ImageAdapter
{
    public async Task<byte[]> ConvertStreamDeckImageAsync(string base64Image)
    {
        // 1. Декодировать base64
        var imageData = Convert.FromBase64String(
            base64Image.Replace("data:image/png;base64,", ""));
        
        // 2. Загрузить изображение
        using var image = Image.Load(imageData);
        
        // 3. Масштабировать 72×72 → 160×160
        image.Mutate(x => x.Resize(160, 160));
        
        // 4. Применить круглую маску
        ApplyCircularMask(image);
        
        // 5. Конвертировать в JPEG
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 90 });
        
        return ms.ToArray();
    }
    
    private void ApplyCircularMask(Image<Rgba32> image)
    {
        int centerX = 80, centerY = 80, radius = 80;
        
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    int dx = x - centerX;
                    int dy = y - centerY;
                    if (dx * dx + dy * dy > radius * radius)
                    {
                        row[x] = new Rgba32(0, 0, 0, 0);  // Прозрачный
                    }
                }
            }
        });
    }
}
```

## Установка плагинов

### Plugin Browser в UI

```csharp
public class PluginBrowserViewModel : ObservableObject
{
    private readonly PluginManager _pluginManager;
    
    [ObservableProperty]
    private ObservableCollection<PluginInfo> _availablePlugins;
    
    [ObservableProperty]
    private ObservableCollection<PluginInfo> _installedPlugins;
    
    [RelayCommand]
    private async Task InstallPluginAsync(PluginInfo plugin)
    {
        // Скачать плагин
        var zipPath = await DownloadPluginAsync(plugin.DownloadUrl);
        
        // Распаковать в директорию плагинов
        var pluginDir = Path.Combine(_pluginsDirectory, plugin.Uuid);
        ZipFile.ExtractToDirectory(zipPath, pluginDir);
        
        // Загрузить плагин
        await _pluginManager.LoadPluginAsync(pluginDir);
        
        // Обновить список
        InstalledPlugins.Add(plugin);
    }
    
    [RelayCommand]
    private async Task UninstallPluginAsync(PluginInfo plugin)
    {
        // Остановить плагин
        await _pluginManager.UnloadPluginAsync(plugin.Uuid);
        
        // Удалить директорию
        Directory.Delete(
            Path.Combine(_pluginsDirectory, plugin.Uuid), 
            recursive: true);
        
        // Обновить список
        InstalledPlugins.Remove(plugin);
    }
}
```

## Преимущества такой архитектуры

1. **Совместимость** - работают существующие плагины Stream Deck
2. **Расширяемость** - легко добавлять новые плагины
3. **Изоляция** - плагины не могут сломать основное приложение
4. **Экосистема** - доступ к сотням готовых плагинов
5. **Гибкость** - плагины на любом языке (JS, Python, C#, etc.)

## Недостатки и ограничения

1. **Сложность** - дополнительный слой абстракции
2. **Производительность** - WebSocket overhead
3. **Безопасность** - нужна изоляция плагинов
4. **Адаптация изображений** - конвертация 72×72 → 160×160
5. **Node.js зависимость** - для JS плагинов нужен Node.js

## Альтернативный подход: Упрощенная система плагинов

Если полная совместимость не критична, можно сделать упрощенную систему:

```csharp
// Плагины как .NET библиотеки
public interface IButtonPlugin
{
    string Name { get; }
    string Description { get; }
    
    Task<byte[]> GenerateImageAsync(Dictionary<string, object> settings);
    Task OnButtonPressedAsync(Dictionary<string, object> settings);
    Task OnButtonReleasedAsync(Dictionary<string, object> settings);
}

// Пример плагина
public class ClockPlugin : IButtonPlugin
{
    public string Name => "Clock";
    public string Description => "Shows current time";
    
    public async Task<byte[]> GenerateImageAsync(Dictionary<string, object> settings)
    {
        var time = DateTime.Now.ToString("HH:mm");
        return await ImageGenerator.CreateTextImageAsync(time, 160, 160);
    }
    
    public Task OnButtonPressedAsync(Dictionary<string, object> settings)
    {
        // Ничего не делать
        return Task.CompletedTask;
    }
}
```

## Рекомендация

**Фаза 1**: Начать без плагинов
- Реализовать базовую функциональность
- Keyboard actions, Custom HID, LED control
- Профили и изображения

**Фаза 2**: Добавить упрощенную систему плагинов (.NET)
- Плагины как .NET библиотеки
- Простой API
- Встроенные плагины (Clock, Weather, System Info)

**Фаза 3**: Добавить совместимость с Stream Deck (опционально)
- WebSocket сервер
- Полная эмуляция Stream Deck API
- Поддержка существующих плагинов

Это позволит поэтапно добавлять функциональность без усложнения начальной разработки.
