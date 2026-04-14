# Архитектура Backend для макроклавиатуры

## Обзор

Архитектура backend приложения для управления ESP32-S3 макроклавиатурой, совместимая с прошивкой и обеспечивающая красивый пользовательский интерфейс в стиле Mad Catz.

**Ключевые особенности:**
- Работа в системном трее (как Mad Catz, Logitech G HUB)
- Двойной клик на иконке открывает конфигуратор
- Красивый современный UI (не стандартные кнопки)
- Совместимость с плагинами Elgato Stream Deck
- Полная интеграция с прошивкой через USB HID

## Архитектура системы

```
┌─────────────────────────────────────────────────────────────┐
│  MacroKeyboard.TrayApp (Системный трей)                     │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  • Иконка в трее                                      │  │
│  │  • Быстрое переключение профилей                      │  │
│  │  • Уведомления                                        │  │
│  │  • Глобальные горячие клавиши                         │  │
│  │  • Запуск конфигуратора (двойной клик)               │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                          ↕ IPC / Named Pipes
┌─────────────────────────────────────────────────────────────┐
│  MacroKeyboard.Backend (Background Service)                 │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Device Manager                                       │  │
│  │  • USB HID коммуникация                               │  │
│  │  • Протокол обмена с прошивкой                        │  │
│  │  • Мониторинг подключения                             │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  WebSocket Server (port 28196)                        │  │
│  │  • Stream Deck API эмуляция                           │  │
│  │  • Регистрация плагинов                               │  │
│  │  • Маршрутизация событий                              │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Event Router                                         │  │
│  │  • Обработка событий кнопок                           │  │
│  │  • Обработка событий энкодера                         │  │
│  │  • Синхронизация состояния                            │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                          ↕ WebSocket
┌─────────────────────────────────────────────────────────────┐
│  Plugins (HTML/JS, Node.js, Python, C#)                     │
│  • OBS Studio Control                                       │
│  • Spotify Control                                          │
│  • Discord Integration                                      │
│  • Custom Actions                                           │
└─────────────────────────────────────────────────────────────┘
                          ↕ IPC / Named Pipes
┌─────────────────────────────────────────────────────────────┐
│  MacroKeyboard.UI (Configuration Application)               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Красивый современный интерфейс                       │  │
│  │  • Dashboard (статус устройства)                      │  │
│  │  • Profile Editor (настройка профилей)                │  │
│  │  • Button Configurator (настройка кнопок)             │  │
│  │  • Plugin Browser (установка плагинов)                │  │
│  │  • Settings (настройки приложения)                    │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                          ↕ USB HID
┌─────────────────────────────────────────────────────────────┐
│  ESP32-S3 Device (Firmware)                                 │
│  • 10 кнопок с дисплеями GC9A01                             │
│  • RGB LED подсветка                                        │
│  • Энкодер для переключения профилей                        │
│  • USB HID Keyboard + Raw HID                               │
└─────────────────────────────────────────────────────────────┘
```

## Технологический стек

### Backend Service & Tray App

**Платформа:**
- **.NET 8.0** - современная, кроссплатформенная
- **C#** - основной язык разработки

**UI Framework:**
- **WPF** (Windows) - для красивого UI с аппаратным ускорением
- **Avalonia UI** (опционально) - для кроссплатформенности

**Ключевые библиотеки:**
```xml
<!-- USB HID коммуникация -->
<PackageReference Include="HidLibrary" Version="3.3.40" />

<!-- MVVM и реактивность -->
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
<PackageReference Include="ReactiveUI" Version="19.5.31" />

<!-- Обработка изображений -->
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.0" />

<!-- WebSocket сервер -->
<PackageReference Include="Fleck" Version="1.2.0" />

<!-- Логирование -->
<PackageReference Include="Serilog" Version="3.1.1" />
<PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />

<!-- JSON сериализация -->
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />

<!-- Системный трей -->
<PackageReference Include="Hardcodet.NotifyIcon.Wpf" Version="1.1.0" />

<!-- Dependency Injection -->
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />

<!-- IPC коммуникация -->
<PackageReference Include="H.Pipes" Version="2.0.59" />

<!-- Современный UI -->
<PackageReference Include="ModernWpfUI" Version="0.9.6" />
<PackageReference Include="MaterialDesignThemes" Version="4.9.0" />
```

## Структура решения

```
MacroKeyboard.sln
│
├── src/
│   ├── MacroKeyboard.Core/              # Бизнес-логика (общая)
│   │   ├── Models/
│   │   │   ├── Profile.cs
│   │   │   ├── ButtonConfig.cs
│   │   │   ├── LedConfig.cs
│   │   │   ├── ActionConfig.cs
│   │   │   └── DeviceInfo.cs
│   │   │
│   │   ├── Services/
│   │   │   ├── IDeviceService.cs
│   │   │   ├── IProfileService.cs
│   │   │   ├── IImageService.cs
│   │   │   └── ISettingsService.cs
│   │   │
│   │   └── Utilities/
│   │       ├── CrcCalculator.cs
│   │       └── ImageProcessor.cs
│   │
│   ├── MacroKeyboard.Communication/     # USB HID протокол
│   │   ├── HidDevice/
│   │   │   ├── HidDeviceManager.cs
│   │   │   ├── HidDeviceMonitor.cs
│   │   │   └── HidDeviceInfo.cs
│   │   │
│   │   ├── Protocol/
│   │   │   ├── ProtocolHandler.cs
│   │   │   ├── PacketBuilder.cs
│   │   │   ├── PacketParser.cs
│   │   │   └── ProtocolConstants.cs
│   │   │
│   │   └── Commands/
│   │       ├── PingCommand.cs
│   │       ├── GetDeviceInfoCommand.cs
│   │       ├── SetProfileCommand.cs
│   │       ├── ImageTransferCommand.cs
│   │       ├── SetButtonActionCommand.cs
│   │       └── SetLedColorCommand.cs
│   │
│   ├── MacroKeyboard.Backend/           # Backend сервис
│   │   ├── Services/
│   │   │   ├── BackendService.cs        # Главный сервис
│   │   │   ├── DeviceManager.cs         # Управление устройством
│   │   │   ├── EventRouter.cs           # Маршрутизация событий
│   │   │   └── IpcServer.cs             # IPC для UI
│   │   │
│   │   ├── WebSocket/
│   │   │   ├── StreamDeckWebSocketServer.cs
│   │   │   ├── MessageHandler.cs
│   │   │   └── ProtocolAdapter.cs
│   │   │
│   │   ├── PluginSystem/
│   │   │   ├── PluginManager.cs
│   │   │   ├── PluginLoader.cs
│   │   │   └── PluginProcess.cs
│   │   │
│   │   └── Program.cs
│   │
│   ├── MacroKeyboard.TrayApp/           # Tray приложение
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── TrayIcon/
│   │   │   ├── TrayIconManager.cs
│   │   │   ├── TrayContextMenu.cs
│   │   │   └── TrayNotifications.cs
│   │   │
│   │   ├── HotKeys/
│   │   │   ├── HotKeyManager.cs
│   │   │   └── GlobalHotKey.cs
│   │   │
│   │   ├── Services/
│   │   │   ├── IpcClient.cs             # Связь с Backend
│   │   │   └── ConfiguratorLauncher.cs  # Запуск UI
│   │   │
│   │   └── Resources/
│   │       ├── Icons/
│   │       └── Sounds/
│   │
│   ├── MacroKeyboard.UI/                # Configuration UI
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── MainWindow.xaml / MainWindow.xaml.cs
│   │   │
│   │   ├── Views/
│   │   │   ├── DashboardView.xaml       # Главный экран
│   │   │   ├── ProfileEditorView.xaml   # Редактор профилей
│   │   │   ├── ButtonConfigView.xaml    # Настройка кнопки
│   │   │   ├── PluginBrowserView.xaml   # Браузер плагинов
│   │   │   └── SettingsView.xaml        # Настройки
│   │   │
│   │   ├── ViewModels/
│   │   │   ├── MainViewModel.cs
│   │   │   ├── DashboardViewModel.cs
│   │   │   ├── ProfileEditorViewModel.cs
│   │   │   ├── ButtonConfigViewModel.cs
│   │   │   ├── PluginBrowserViewModel.cs
│   │   │   └── SettingsViewModel.cs
│   │   │
│   │   ├── Controls/                    # Кастомные контролы
│   │   │   ├── CircularButtonGrid.xaml  # Сетка кнопок
│   │   │   ├── CircularButton.xaml      # Круглая кнопка
│   │   │   ├── ColorPicker.xaml         # Выбор цвета
│   │   │   ├── KeySelector.xaml         # Выбор клавиши
│   │   │   ├── ImageEditor.xaml         # Редактор изображений
│   │   │   └── LedPreview.xaml          # Предпросмотр LED
│   │   │
│   │   ├── Converters/
│   │   │   ├── BoolToVisibilityConverter.cs
│   │   │   ├── ColorToBrushConverter.cs
│   │   │   └── ByteArrayToImageConverter.cs
│   │   │
│   │   ├── Styles/                      # Современные стили
│   │   │   ├── Colors.xaml              # Цветовая палитра
│   │   │   ├── Buttons.xaml             # Стили кнопок
│   │   │   ├── TextBoxes.xaml           # Стили текстовых полей
│   │   │   ├── Cards.xaml               # Карточки
│   │   │   └── Animations.xaml          # Анимации
│   │   │
│   │   ├── Resources/
│   │   │   ├── Icons/                   # SVG иконки
│   │   │   ├── Images/                  # Изображения
│   │   │   └── Fonts/                   # Шрифты
│   │   │
│   │   └── Services/
│   │       ├── IpcClient.cs             # Связь с Backend
│   │       ├── NavigationService.cs     # Навигация
│   │       └── DialogService.cs         # Диалоги
│   │
│   ├── MacroKeyboard.Infrastructure/    # Реализация сервисов
│   │   ├── Services/
│   │   │   ├── DeviceService.cs
│   │   │   ├── ProfileService.cs
│   │   │   ├── ImageService.cs
│   │   │   └── SettingsService.cs
│   │   │
│   │   ├── Repositories/
│   │   │   ├── ProfileRepository.cs
│   │   │   └── ImageRepository.cs
│   │   │
│   │   └── Persistence/
│   │       ├── JsonFileStorage.cs
│   │       └── AppDataManager.cs
│   │
│   └── MacroKeyboard.Shared/            # Общие компоненты
│       ├── Constants.cs
│       ├── Enums.cs
│       └── Extensions/
│
├── tests/
│   ├── MacroKeyboard.Core.Tests/
│   ├── MacroKeyboard.Communication.Tests/
│   └── MacroKeyboard.UI.Tests/
│
└── plugins/                             # Директория плагинов
    ├── com.macrokeyboard.keyboard/
    ├── com.macrokeyboard.text/
    └── com.macrokeyboard.system/
```

## Детальное описание компонентов

### 1. MacroKeyboard.Backend (Background Service)

Backend работает как фоновый процесс и управляет устройством.

**BackendService.cs**
```csharp
public class BackendService : BackgroundService
{
    private readonly IDeviceService _deviceService;
    private readonly StreamDeckWebSocketServer _webSocketServer;
    private readonly PluginManager _pluginManager;
    private readonly EventRouter _eventRouter;
    private readonly IpcServer _ipcServer;
    private readonly ILogger<BackendService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backend service starting...");
        
        // 1. Запустить IPC сервер для связи с UI и Tray
        await _ipcServer.StartAsync();
        
        // 2. Подключиться к устройству
        await _deviceService.ConnectAsync();
        
        // 3. Запустить WebSocket сервер для плагинов
        _ = _webSocketServer.StartAsync(28196);
        
        // 4. Загрузить плагины
        await _pluginManager.LoadPluginsAsync();
        
        // 5. Основной цикл обработки событий
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(100, stoppingToken);
        }
        
        _logger.LogInformation("Backend service stopping...");
    }
}
```

**DeviceManager.cs**
```csharp
public class DeviceManager
{
    private readonly HidDeviceManager _hidDevice;
    private readonly IProtocolHandler _protocol;
    private readonly ILogger _logger;
    
    public event EventHandler<ButtonEventArgs> ButtonPressed;
    public event EventHandler<ButtonEventArgs> ButtonReleased;
    public event EventHandler<EncoderEventArgs> EncoderRotated;
    public event EventHandler<ProfileChangedEventArgs> ProfileChanged;
    
    public async Task<bool> ConnectAsync()
    {
        var connected = await _hidDevice.ConnectAsync();
        if (connected)
        {
            // Запустить мониторинг событий
            _ = MonitorDeviceEventsAsync();
            _logger.LogInformation("Device connected successfully");
        }
        return connected;
    }
    
    private async Task MonitorDeviceEventsAsync()
    {
        while (_hidDevice.IsConnected)
        {
            var packet = await _hidDevice.ReadAsync();
            if (packet != null)
            {
                await ProcessDeviceEventAsync(packet);
            }
        }
    }
    
    private async Task ProcessDeviceEventAsync(byte[] packet)
    {
        var commandId = packet[1];
        
        switch (commandId)
        {
            case 0xF0: // BUTTON_PRESSED
                var buttonId = packet[6];
                ButtonPressed?.Invoke(this, new ButtonEventArgs(buttonId));
                break;
                
            case 0xF1: // ENCODER_ROTATED
                var direction = packet[6];
                EncoderRotated?.Invoke(this, new EncoderEventArgs(direction));
                break;
                
            case 0xF3: // PROFILE_CHANGED
                var newProfileId = packet[7];
                ProfileChanged?.Invoke(this, new ProfileChangedEventArgs(newProfileId));
                break;
        }
    }
}
```

**IpcServer.cs** - для связи с UI и Tray
```csharp
public class IpcServer
{
    private PipeServer<IpcMessage> _server;
    private readonly IDeviceService _deviceService;
    
    public async Task StartAsync()
    {
        _server = new PipeServer<IpcMessage>("MacroKeyboard");
        
        _server.ClientConnected += OnClientConnected;
        _server.MessageReceived += OnMessageReceived;
        
        await _server.StartAsync();
    }
    
    private async void OnMessageReceived(object sender, ConnectionMessageEventArgs<IpcMessage> e)
    {
        var message = e.Message;
        
        switch (message.Type)
        {
            case "GetDeviceStatus":
                var status = await _deviceService.GetDeviceInfoAsync();
                await e.Connection.WriteAsync(new IpcMessage
                {
                    Type = "DeviceStatus",
                    Data = status
                });
                break;
                
            case "SwitchProfile":
                var profileId = (int)message.Data;
                await _deviceService.SetProfileAsync(profileId);
                break;
                
            case "GetProfiles":
                var profiles = await _profileService.GetAllProfilesAsync();
                await e.Connection.WriteAsync(new IpcMessage
                {
                    Type = "Profiles",
                    Data = profiles
                });
                break;
        }
    }
}
```

### 2. MacroKeyboard.TrayApp (Системный трей)

Приложение в трее для быстрого доступа к функциям.

**TrayIconManager.cs**
```csharp
public class TrayIconManager
{
    private readonly TaskbarIcon _trayIcon;
    private readonly IpcClient _ipcClient;
    private readonly ConfiguratorLauncher _configuratorLauncher;
    private readonly HotKeyManager _hotKeyManager;
    
    public TrayIconManager(
        IpcClient ipcClient,
        ConfiguratorLauncher configuratorLauncher,
        HotKeyManager hotKeyManager)
    {
        _ipcClient = ipcClient;
        _configuratorLauncher = configuratorLauncher;
        _hotKeyManager = hotKeyManager;
        
        InitializeTrayIcon();
        RegisterHotKeys();
    }
    
    private void InitializeTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            Icon = new Icon("Resources/Icons/tray-icon.ico"),
            ToolTipText = "Macro Keyboard",
            ContextMenu = CreateContextMenu()
        };
        
        // Двойной клик открывает конфигуратор
        _trayIcon.TrayMouseDoubleClick += OnTrayDoubleClick;
        
        // Показать уведомление при запуске
        _trayIcon.ShowBalloonTip(
            "Macro Keyboard", 
            "Приложение запущено в трее", 
            BalloonIcon.Info);
    }
    
    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        
        // Профили
        var profilesMenu = new MenuItem { Header = "Профили" };
        for (int i = 0; i < 5; i++)
        {
            int profileId = i;
            profilesMenu.Items.Add(new MenuItem
            {
                Header = $"Профиль {i + 1}",
                Command = new RelayCommand(() => SwitchProfile(profileId))
            });
        }
        menu.Items.Add(profilesMenu);
        
        menu.Items.Add(new Separator());
        
        // Открыть конфигуратор
        menu.Items.Add(new MenuItem
        {
            Header = "Открыть конфигуратор",
            Command = new RelayCommand(OpenConfigurator)
        });
        
        // Настройки
        menu.Items.Add(new MenuItem
        {
            Header = "Настройки",
            Command = new RelayCommand(OpenSettings)
        });
        
        menu.Items.Add(new Separator());
        
        // Выход
        menu.Items.Add(new MenuItem
        {
            Header = "Выход",
            Command = new RelayCommand(Exit)
        });
        
        return menu;
    }
    
    private async void OnTrayDoubleClick(object sender, RoutedEventArgs e)
    {
        await _configuratorLauncher.LaunchAsync();
    }
    
    private async void SwitchProfile(int profileId)
    {
        await _ipcClient.SendAsync(new IpcMessage
        {
            Type = "SwitchProfile",
            Data = profileId
        });
        
        _trayIcon.ShowBalloonTip(
            "Macro Keyboard",
            $"Переключено на профиль {profileId + 1}",
            BalloonIcon.Info);
    }
    
    private void RegisterHotKeys()
    {
        // Ctrl+Alt+1-5 для переключения профилей
        for (int i = 0; i < 5; i++)
        {
            int profileId = i;
            _hotKeyManager.Register(
                ModifierKeys.Control | ModifierKeys.Alt,
                Key.D1 + i,
                () => SwitchProfile(profileId));
        }
        
        // Ctrl+Alt+C для открытия конфигуратора
        _hotKeyManager.Register(
            ModifierKeys.Control | ModifierKeys.Alt,
            Key.C,
            OpenConfigurator);
    }
}
```

**ConfiguratorLauncher.cs**
```csharp
public class ConfiguratorLauncher
{
    private Process _configuratorProcess;
    
    public async Task LaunchAsync()
    {
        // Проверить, не запущен ли уже конфигуратор
        if (_configuratorProcess != null && !_configuratorProcess.HasExited)
        {
            // Активировать существующее окно
            ActivateWindow(_configuratorProcess.MainWindowHandle);
            return;
        }
        
        // Запустить новый процесс конфигуратора
        var exePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "MacroKeyboard.UI.exe");
        
        _configuratorProcess = Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true
        });
    }
    
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    private void ActivateWindow(IntPtr handle)
    {
        SetForegroundWindow(handle);
    }
}
```

### 3. MacroKeyboard.UI (Configuration Application)

Красивое приложение для настройки устройства.

**Дизайн-система**

**Colors.xaml** - Цветовая палитра в стиле Mad Catz
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Темная тема -->
    <Color x:Key="BackgroundDark">#1E1E1E</Color>
    <Color x:Key="BackgroundMedium">#252526</Color>
    <Color x:Key="BackgroundLight">#2D2D30</Color>
    
    <!-- Акцентные цвета -->
    <Color x:Key="AccentPrimary">#00D9FF</Color>
    <Color x:Key="AccentSecondary">#FF006E</Color>
    <Color x:Key="AccentSuccess">#00FF88</Color>
    <Color x:Key="AccentWarning">#FFB800</Color>
    <Color x:Key="AccentError">#FF3838</Color>
    
    <!-- Текст -->
    <Color x:Key="TextPrimary">#FFFFFF</Color>
    <Color x:Key="TextSecondary">#CCCCCC</Color>
    <Color x:Key="TextDisabled">#808080</Color>
    
    <!-- Кисти -->
    <SolidColorBrush x:Key="BackgroundDarkBrush" Color="{StaticResource BackgroundDark}"/>
    <SolidColorBrush x:Key="BackgroundMediumBrush" Color="{StaticResource BackgroundMedium}"/>
    <SolidColorBrush x:Key="BackgroundLightBrush" Color="{StaticResource BackgroundLight}"/>
    
    <SolidColorBrush x:Key="AccentPrimaryBrush" Color="{StaticResource AccentPrimary}"/>
    <SolidColorBrush x:Key="AccentSecondaryBrush" Color="{StaticResource AccentSecondary}"/>
    
    <SolidColorBrush x:Key="TextPrimaryBrush" Color="{StaticResource TextPrimary}"/>
    <SolidColorBrush x:Key="TextSecondaryBrush" Color="{StaticResource TextSecondary}"/>
    
</ResourceDictionary>
```

**CircularButton.xaml** - Кастомная круглая кнопка
```xml
<UserControl x:Class="MacroKeyboard.UI.Controls.CircularButton"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <Grid Width="120" Height="120">
        <!-- Внешнее свечение -->
        <Ellipse x:Name="GlowEllipse">
            <Ellipse.Fill>
                <RadialGradientBrush>
                    <GradientStop Color="{Binding LedColor}" Offset="0"/>
                    <GradientStop Color="Transparent" Offset="1"/>
                </RadialGradientBrush>
            </Ellipse.Fill>
            <Ellipse.Effect>
                <BlurEffect Radius="20"/>
            </Ellipse.Effect>
        </Ellipse>
        
        <!-- Основная кнопка -->
        <Button x:Name="MainButton"
                Style="{StaticResource CircularButtonStyle}"
                Command="{Binding SelectCommand}">
            
            <Grid>
                <!-- Изображение кнопки -->
                <Ellipse>
                    <Ellipse.Fill>
                        <ImageBrush ImageSource="{Binding ButtonImage}"
                                    Stretch="UniformToFill"/>
                    </Ellipse.Fill>
                </Ellipse>
                
                <!-- Оверлей при наведении -->
                <Ellipse x:Name="HoverOverlay"
                         Fill="#40FFFFFF"
                         Opacity="0"/>
                
                <!-- Индикатор настройки -->
                <Ellipse Width="12" Height="12"
                         HorizontalAlignment="Right"
                         VerticalAlignment="Top"
                         Margin="0,8,8,0"
                         Fill="{StaticResource AccentSuccessBrush}"
                         Visibility="{Binding IsConfigured, Converter={StaticResource BoolToVisibilityConverter}}"/>
            </Grid>
        </Button>
        
        <!-- Номер кнопки -->
        <TextBlock Text="{Binding ButtonNumber}"
                   FontSize="10"
                   FontWeight="Bold"
