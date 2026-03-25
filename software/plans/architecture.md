# Архитектура управляющего софта на C#

## Обзор

Управляющее приложение построено на базе .NET 8.0 с использованием паттерна MVVM (Model-View-ViewModel) и принципов Clean Architecture.

## Технологический стек

### Основные технологии

- **.NET**: 8.0 LTS
- **UI Framework**: WPF (Windows Presentation Foundation) или Avalonia UI (кроссплатформенный)
- **Архитектурный паттерн**: MVVM
- **DI Container**: Microsoft.Extensions.DependencyInjection
- **Logging**: Serilog
- **Testing**: xUnit, Moq

### Ключевые библиотеки

```xml
<PackageReference Include="HidLibrary" Version="3.3.40" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
<PackageReference Include="Serilog" Version="3.1.1" />
<PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.0" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
<PackageReference Include="Hardcodet.NotifyIcon.Wpf" Version="1.1.0" />
```

## Структура решения

```
MacroKeyboard.sln
│
├── src/
│   ├── MacroKeyboard.Core/              # Бизнес-логика (не зависит от UI)
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
│   │   │   ├── ISettingsService.cs
│   │   │   └── IOtaService.cs
│   │   │
│   │   ├── Interfaces/
│   │   │   ├── IRepository.cs
│   │   │   └── IProtocolHandler.cs
│   │   │
│   │   └── Utilities/
│   │       ├── CrcCalculator.cs
│   │       └── ImageProcessor.cs
│   │
│   ├── MacroKeyboard.Communication/     # USB/HID коммуникация
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
│   │       ├── SetLedColorCommand.cs
│   │       └── OtaUpdateCommand.cs
│   │
│   ├── MacroKeyboard.Infrastructure/    # Реализация сервисов
│   │   ├── Services/
│   │   │   ├── DeviceService.cs
│   │   │   ├── ProfileService.cs
│   │   │   ├── ImageService.cs
│   │   │   ├── SettingsService.cs
│   │   │   └── OtaService.cs
│   │   │
│   │   ├── Repositories/
│   │   │   ├── ProfileRepository.cs
│   │   │   └── ImageRepository.cs
│   │   │
│   │   └── Persistence/
│   │       ├── JsonFileStorage.cs
│   │       └── AppDataManager.cs
│   │
│   ├── MacroKeyboard.UI/                # Пользовательский интерфейс
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── MainWindow.xaml / MainWindow.xaml.cs
│   │   │
│   │   ├── Views/
│   │   │   ├── DashboardView.xaml
│   │   │   ├── ProfileEditorView.xaml
│   │   │   ├── ButtonConfigView.xaml
│   │   │   ├── SettingsView.xaml
│   │   │   └── DiagnosticsView.xaml
│   │   │
│   │   ├── ViewModels/
│   │   │   ├── MainViewModel.cs
│   │   │   ├── DashboardViewModel.cs
│   │   │   ├── ProfileEditorViewModel.cs
│   │   │   ├── ButtonConfigViewModel.cs
│   │   │   ├── SettingsViewModel.cs
│   │   │   └── DiagnosticsViewModel.cs
│   │   │
│   │   ├── Controls/
│   │   │   ├── ButtonGrid.xaml
│   │   │   ├── ColorPicker.xaml
│   │   │   ├── KeySelector.xaml
│   │   │   └── ImageEditor.xaml
│   │   │
│   │   ├── Converters/
│   │   │   ├── BoolToVisibilityConverter.cs
│   │   │   ├── ColorToBrushConverter.cs
│   │   │   └── ByteArrayToImageConverter.cs
│   │   │
│   │   ├── Resources/
│   │   │   ├── Styles/
│   │   │   │   ├── Colors.xaml
│   │   │   │   ├── Buttons.xaml
│   │   │   │   └── TextBoxes.xaml
│   │   │   │
│   │   │   └── Icons/
│   │   │       └── (SVG/PNG иконки)
│   │   │
│   │   └── Behaviors/
│   │       ├── DragDropBehavior.cs
│   │       └── NumericTextBoxBehavior.cs
│   │
│   ├── MacroKeyboard.TrayApp/           # Tray приложение
│   │   ├── Program.cs
│   │   ├── TrayApplicationContext.cs
│   │   ├── TrayIcon/
│   │   │   └── TrayIconManager.cs
│   │   ├── Notifications/
│   │   │   └── NotificationService.cs
│   │   └── HotKeys/
│   │       ├── HotKeyManager.cs
│   │       └── GlobalHotKey.cs
│   │
│   └── MacroKeyboard.Shared/            # Общие компоненты
│       ├── Constants.cs
│       ├── Enums.cs
│       └── Extensions/
│           ├── ColorExtensions.cs
│           └── ImageExtensions.cs
│
├── tests/
│   ├── MacroKeyboard.Core.Tests/
│   ├── MacroKeyboard.Communication.Tests/
│   └── MacroKeyboard.UI.Tests/
│
└── docs/
    ├── API.md
    ├── UserGuide.md
    └── DeveloperGuide.md
```

## Детальное описание модулей

### 1. MacroKeyboard.Core

Ядро приложения, содержит бизнес-логику и модели данных.

#### Models

**Profile.cs**
```csharp
public class Profile
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<ButtonConfig> Buttons { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
}
```

**ButtonConfig.cs**
```csharp
public class ButtonConfig
{
    public int Id { get; set; }
    public ActionType ActionType { get; set; }
    public ActionConfig Action { get; set; }
    public string ImagePath { get; set; }
    public LedConfig Led { get; set; }
}

public enum ActionType
{
    None,
    Keyboard,
    CustomHid,
    Macro
}
```

**ActionConfig.cs**
```csharp
public abstract class ActionConfig
{
    public abstract byte[] ToBytes();
}

public class KeyboardAction : ActionConfig
{
    public List<ModifierKey> Modifiers { get; set; } = new();
    public Key Key { get; set; }
    public string Text { get; set; }
    
    public override byte[] ToBytes()
    {
        // Конвертация в формат протокола
    }
}

public class CustomHidAction : ActionConfig
{
    public byte[] Data { get; set; }
    
    public override byte[] ToBytes() => Data;
}
```

**LedConfig.cs**
```csharp
public class LedConfig
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public byte Brightness { get; set; }
    public LedEffect Effect { get; set; }
}

public enum LedEffect
{
    Static = 0,
    Breathing = 1,
    Rainbow = 2
}
```

#### Services (Interfaces)

**IDeviceService.cs**
```csharp
public interface IDeviceService
{
    event EventHandler<DeviceEventArgs> DeviceConnected;
    event EventHandler<DeviceEventArgs> DeviceDisconnected;
    
    Task<bool> ConnectAsync();
    Task DisconnectAsync();
    bool IsConnected { get; }
    Task<DeviceInfo> GetDeviceInfoAsync();
    Task<bool> PingAsync();
}
```

**IProfileService.cs**
```csharp
public interface IProfileService
{
    Task<List<Profile>> GetAllProfilesAsync();
    Task<Profile> GetProfileAsync(int id);
    Task<Profile> CreateProfileAsync(string name);
    Task UpdateProfileAsync(Profile profile);
    Task DeleteProfileAsync(int id);
    Task<Profile> DuplicateProfileAsync(int id);
    Task<bool> SendProfileToDeviceAsync(Profile profile);
    Task<Profile> LoadProfileFromDeviceAsync(int id);
}
```

**IImageService.cs**
```csharp
public interface IImageService
{
    Task<byte[]> LoadImageAsync(string path);
    Task<byte[]> ResizeImageAsync(byte[] image, int width, int height);
    Task<byte[]> ConvertToJpegAsync(byte[] image, int quality = 90);
    Task SaveImageAsync(string path, byte[] image);
    Task<byte[]> CropImageAsync(byte[] image, Rectangle cropArea);
}
```

### 2. MacroKeyboard.Communication

Модуль для коммуникации с устройством через USB HID.

#### HidDevice

**HidDeviceManager.cs**
```csharp
public class HidDeviceManager : IDisposable
{
    private const int VendorId = 0x303A;  // Espressif
    private const int ProductId = 0x4001;
    
    private HidDevice _device;
    private readonly ILogger _logger;
    
    public async Task<bool> ConnectAsync()
    {
        var devices = HidDevices.Enumerate(VendorId, ProductId);
        _device = devices.FirstOrDefault();
        
        if (_device == null)
            return false;
            
        _device.OpenDevice();
        _device.MonitorDeviceEvents = true;
        _device.Inserted += OnDeviceInserted;
        _device.Removed += OnDeviceRemoved;
        
        return _device.IsOpen;
    }
    
    public async Task<byte[]> SendCommandAsync(byte[] command, int timeoutMs = 1000)
    {
        if (!_device.IsOpen)
            throw new InvalidOperationException("Device not connected");
            
        // Отправка команды
        _device.Write(command);
        
        // Ожидание ответа
        var response = await Task.Run(() => 
        {
            var data = _device.Read(timeoutMs);
            return data.Data;
        });
        
        return response;
    }
}
```

**HidDeviceMonitor.cs**
```csharp
public class HidDeviceMonitor
{
    public event EventHandler<DeviceEventArgs> DeviceArrived;
    public event EventHandler<DeviceEventArgs> DeviceRemoved;
    
    public void StartMonitoring()
    {
        // Мониторинг подключения/отключения устройств
        // Использование WMI или ManagementEventWatcher
    }
}
```

#### Protocol

**ProtocolHandler.cs**
```csharp
public class ProtocolHandler : IProtocolHandler
{
    private readonly HidDeviceManager _deviceManager;
    private readonly ILogger _logger;
    
    public async Task<TResponse> SendCommandAsync<TResponse>(ICommand<TResponse> command)
    {
        var packet = PacketBuilder.Build(command);
        var response = await _deviceManager.SendCommandAsync(packet);
        return PacketParser.Parse<TResponse>(response);
    }
}
```

**PacketBuilder.cs**
```csharp
public static class PacketBuilder
{
    public static byte[] Build(byte commandId, byte[] payload)
    {
        var packet = new byte[64];
        packet[0] = 0xA5;  // Magic byte
        packet[1] = commandId;
        
        ushort payloadLength = (ushort)payload.Length;
        packet[2] = (byte)(payloadLength & 0xFF);
        packet[3] = (byte)((payloadLength >> 8) & 0xFF);
        
        // Sequence number
        packet[4] = 0;
        packet[5] = 0;
        
        // Payload
        Array.Copy(payload, 0, packet, 6, Math.Min(payload.Length, 56));
        
        // Checksum
        packet[62] = CalculateChecksum(packet, 0, 62);
        
        // End byte
        packet[63] = 0x5A;
        
        return packet;
    }
    
    private static byte CalculateChecksum(byte[] data, int start, int length)
    {
        byte checksum = 0;
        for (int i = start; i < start + length; i++)
        {
            checksum ^= data[i];
        }
        return checksum;
    }
}
```

#### Commands

**Base Command**
```csharp
public interface ICommand<TResponse>
{
    byte CommandId { get; }
    byte[] GetPayload();
}

public abstract class CommandBase<TResponse> : ICommand<TResponse>
{
    public abstract byte CommandId { get; }
    public abstract byte[] GetPayload();
}
```

**PingCommand.cs**
```csharp
public class PingCommand : CommandBase<PingResponse>
{
    public override byte CommandId => 0x01;
    
    public override byte[] GetPayload() => Array.Empty<byte>();
}

public class PingResponse
{
    public Version FirmwareVersion { get; set; }
    public uint Uptime { get; set; }
    public byte CurrentProfile { get; set; }
}
```

**ImageTransferCommand.cs**
```csharp
public class ImageTransferCommand : CommandBase<ImageTransferResponse>
{
    public byte ProfileId { get; set; }
    public byte ButtonId { get; set; }
    public byte[] ImageData { get; set; }
    
    public override byte CommandId => 0x20;  // START_IMAGE_TRANSFER
    
    public override byte[] GetPayload()
    {
        var payload = new byte[11];
        payload[0] = ProfileId;
        payload[1] = ButtonId;
        
        // Image size (little-endian)
        uint size = (uint)ImageData.Length;
        payload[2] = (byte)(size & 0xFF);
        payload[3] = (byte)((size >> 8) & 0xFF);
        payload[4] = (byte)((size >> 16) & 0xFF);
        payload[5] = (byte)((size >> 24) & 0xFF);
        
        payload[6] = 0x01;  // Format: JPEG
        
        // Width and height
        payload[7] = 160;
        payload[8] = 0;
        payload[9] = 160;
        payload[10] = 0;
        
        return payload;
    }
    
    public async Task<bool> SendAsync(IProtocolHandler protocol)
    {
        // 1. Start transfer
        var startResponse = await protocol.SendCommandAsync(this);
        if (!startResponse.Success)
            return false;
            
        // 2. Send chunks
        const int chunkSize = 50;
        int totalChunks = (ImageData.Length + chunkSize - 1) / chunkSize;
        
        for (int i = 0; i < totalChunks; i++)
        {
            int offset = i * chunkSize;
            int length = Math.Min(chunkSize, ImageData.Length - offset);
            
            var chunk = new byte[length];
            Array.Copy(ImageData, offset, chunk, 0, length);
            
            var chunkCommand = new ImageChunkCommand
            {
                TransferId = startResponse.TransferId,
                ChunkNumber = (ushort)i,
                Data = chunk
            };
            
            var chunkResponse = await protocol.SendCommandAsync(chunkCommand);
            if (!chunkResponse.Success)
                return false;
        }
        
        // 3. End transfer
        var crc32 = Crc32.Calculate(ImageData);
        var endCommand = new ImageEndCommand
        {
            TransferId = startResponse.TransferId,
            TotalChunks = (uint)totalChunks,
            Crc32 = crc32
        };
        
        var endResponse = await protocol.SendCommandAsync(endCommand);
        return endResponse.Success && endResponse.Crc32 == crc32;
    }
}
```

### 3. MacroKeyboard.Infrastructure

Реализация сервисов и репозиториев.

**DeviceService.cs**
```csharp
public class DeviceService : IDeviceService
{
    private readonly HidDeviceManager _deviceManager;
    private readonly IProtocolHandler _protocol;
    private readonly ILogger _logger;
    
    public event EventHandler<DeviceEventArgs> DeviceConnected;
    public event EventHandler<DeviceEventArgs> DeviceDisconnected;
    
    public bool IsConnected => _deviceManager.IsConnected;
    
    public async Task<bool> ConnectAsync()
    {
        var connected = await _deviceManager.ConnectAsync();
        if (connected)
        {
            DeviceConnected?.Invoke(this, new DeviceEventArgs());
            _logger.Information("Device connected");
        }
        return connected;
    }
    
    public async Task<DeviceInfo> GetDeviceInfoAsync()
    {
        var command = new GetDeviceInfoCommand();
        var response = await _protocol.SendCommandAsync(command);
        
        return new DeviceInfo
        {
            DeviceId = response.DeviceId,
            FirmwareVersion = response.FirmwareVersion,
            ButtonCount = response.ButtonCount,
            ProfileCount = response.ProfileCount,
            CurrentProfile = response.CurrentProfile,
            FreeSpace = response.FreeSpace
        };
    }
}
```

**ProfileService.cs**
```csharp
public class ProfileService : IProfileService
{
    private readonly IProfileRepository _repository;
    private readonly IDeviceService _deviceService;
    private readonly IProtocolHandler _protocol;
    
    public async Task<List<Profile>> GetAllProfilesAsync()
    {
        return await _repository.GetAllAsync();
    }
    
    public async Task<bool> SendProfileToDeviceAsync(Profile profile)
    {
        // 1. Send profile metadata
        var setProfileCommand = new SetProfileCommand
        {
            ProfileId = (byte)profile.Id
        };
        await _protocol.SendCommandAsync(setProfileCommand);
        
        // 2. Send button configurations
        foreach (var button in profile.Buttons)
        {
            // Send action
            var actionCommand = new SetButtonActionCommand
            {
                ProfileId = (byte)profile.Id,
                ButtonId = (byte)button.Id,
                ActionType = (byte)button.ActionType,
                ActionData = button.Action.ToBytes()
            };
            await _protocol.SendCommandAsync(actionCommand);
            
            // Send image
            if (!string.IsNullOrEmpty(button.ImagePath))
            {
                var imageData = await File.ReadAllBytesAsync(button.ImagePath);
                var imageCommand = new ImageTransferCommand
                {
                    ProfileId = (byte)profile.Id,
                    ButtonId = (byte)button.Id,
                    ImageData = imageData
                };
                await imageCommand.SendAsync(_protocol);
            }
            
            // Send LED config
            var ledCommand = new SetLedColorCommand
            {
                ProfileId = (byte)profile.Id,
                ButtonId = (byte)button.Id,
                R = button.Led.R,
                G = button.Led.G,
                B = button.Led.B,
                Brightness = button.Led.Brightness,
                Effect = (byte)button.Led.Effect
            };
            await _protocol.SendCommandAsync(ledCommand);
        }
        
        // 3. Save profile on device
        var saveCommand = new SaveProfileCommand
        {
            ProfileId = (byte)profile.Id
        };
        var saveResponse = await _protocol.SendCommandAsync(saveCommand);
        
        return saveResponse.Success;
    }
}
```

### 4. MacroKeyboard.UI

Пользовательский интерфейс на WPF с MVVM.

**MainViewModel.cs**
```csharp
public partial class MainViewModel : ObservableObject
{
    private readonly IDeviceService _deviceService;
    private readonly IProfileService _profileService;
    private readonly INavigationService _navigationService;
    
    [ObservableProperty]
    private bool _isDeviceConnected;
    
    [ObservableProperty]
    private string _deviceStatus;
    
    [ObservableProperty]
    private ObservableCollection<Profile> _profiles;
    
    [ObservableProperty]
    private Profile _selectedProfile;
    
    public MainViewModel(
        IDeviceService deviceService,
        IProfileService profileService,
        INavigationService navigationService)
    {
        _deviceService = deviceService;
        _profileService = profileService;
        _navigationService = navigationService;
        
        _deviceService.DeviceConnected += OnDeviceConnected;
        _deviceService.DeviceDisconnected += OnDeviceDisconnected;
        
        InitializeAsync();
    }
    
    private async void InitializeAsync()
    {
        await _deviceService.ConnectAsync();
        await LoadProfilesAsync();
    }
    
    private async Task LoadProfilesAsync()
    {
        var profiles = await _profileService.GetAllProfilesAsync();
        Profiles = new ObservableCollection<Profile>(profiles);
    }
    
    [RelayCommand]
    private async Task CreateProfileAsync()
    {
        var profile = await _profileService.CreateProfileAsync("New Profile");
        Profiles.Add(profile);
        SelectedProfile = profile;
    }
    
    [RelayCommand]
    private async Task SendToDeviceAsync()
    {
        if (SelectedProfile == null)
            return;
            
        var success = await _profileService.SendProfileToDeviceAsync(SelectedProfile);
        if (success)
        {
            // Show success notification
        }
    }
    
    private void OnDeviceConnected(object sender, DeviceEventArgs e)
    {
        IsDeviceConnected = true;
        DeviceStatus = "Connected";
    }
    
    private void OnDeviceDisconnected(object sender, DeviceEventArgs e)
    {
        IsDeviceConnected = false;
        DeviceStatus = "Disconnected";
    }
}
```

**ProfileEditorViewModel.cs**
```csharp
public partial class ProfileEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private Profile _currentProfile;
    
    [ObservableProperty]
    private ButtonConfig _selectedButton;
    
    [ObservableProperty]
    private ObservableCollection<ButtonConfig> _buttons;
    
    [RelayCommand]
    private void SelectButton(int buttonId)
    {
        SelectedButton = Buttons.FirstOrDefault(b => b.Id == buttonId);
    }
    
    [RelayCommand]
    private async Task LoadImageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp"
        };
        
        if (dialog.ShowDialog() == true)
        {
            SelectedButton.ImagePath = dialog.FileName;
            // Trigger UI update
            OnPropertyChanged(nameof(SelectedButton));
        }
    }
    
    [RelayCommand]
    private void SetLedColor(Color color)
    {
        SelectedButton.Led.R = color.R;
        SelectedButton.Led.G = color.G;
        SelectedButton.Led.B = color.B;
    }
}
```

### 5. MacroKeyboard.TrayApp

Приложение в системном трее.

**TrayApplicationContext.cs**
```csharp
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly IDeviceService _deviceService;
    private readonly IProfileService _profileService;
    private readonly HotKeyManager _hotKeyManager;
    
    public TrayApplicationContext(
        IDeviceService deviceService,
        IProfileService profileService)
    {
        _deviceService = deviceService;
        _profileService = profileService;
        _hotKeyManager = new HotKeyManager();
        
        InitializeTrayIcon();
        RegisterHotKeys();
    }
    
    private void InitializeTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = Resources.TrayIcon,
            ContextMenuStrip = CreateContextMenu(),
            Visible = true
        };
        
        _trayIcon.DoubleClick += OnTrayIconDoubleClick;
    }
    
    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();
        
        // Profile submenu
        var profilesMenu = new ToolStripMenuItem("Profiles");
        for (int i = 0; i < 5; i++)
        {
            int profileId = i;
            profilesMenu.DropDownItems.Add($"Profile {i + 1}", null, 
                (s, e) => SwitchProfile(profileId));
        }
        menu.Items.Add(profilesMenu);
        
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, OnSettingsClick);
        menu.Items.Add("Exit", null, OnExitClick);
        
        return menu;
    }
    
    private void RegisterHotKeys()
    {
        // Ctrl+Alt+1-5 для переключения профилей
        for (int i = 0; i < 5; i++)
        {
            int profileId = i;
            _hotKeyManager.Register(
                ModifierKeys.Control | ModifierKeys.Alt,
                Keys.D1 + i,
                () => SwitchProfile(profileId));
        }
    }
    
    private async void SwitchProfile(int profileId)
    {
        var command = new SetProfileCommand { ProfileId = (byte)profileId };
        // Send to device
        
        ShowNotification($"Switched to Profile {profileId + 1}");
    }
    
    private void ShowNotification(string message)
    {
        _trayIcon.ShowBalloonTip(2000, "Macro Keyboard", message, ToolTipIcon.Info);
    }
}
```

## Dependency Injection

**ServiceCollectionExtensions.cs**
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMacroKeyboardServices(
        this IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IImageService, ImageService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IOtaService, OtaService>();
        
        // Communication
        services.AddSingleton<HidDeviceManager>();
        services.AddSingleton<IProtocolHandler, ProtocolHandler>();
        
        // Repositories
        services.AddSingleton<IProfileRepository, ProfileRepository>();
        services.AddSingleton<IImageRepository, ImageRepository>();
        
        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ProfileEditorViewModel>();
        services.AddTransient<ButtonConfigViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddSerilog(new LoggerConfiguration()
                .WriteTo.File("logs/app.log", rollingInterval: RollingInterval.Day)
                .CreateLogger());
        });
        
        return services;
    }
}
```

**App.xaml.cs**
```csharp
public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    
    public App()
    {
        var services = new ServiceCollection();
        services.AddMacroKeyboardServices();
        _serviceProvider = services.BuildServiceProvider();
    }
    
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        var mainWindow = new MainWindow
        {
            DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
        };
        
        mainWindow.Show();
    }
}
```

## Продолжение в следующем файле

См. [`diagrams.md`](diagrams.md) для диаграмм взаимодействия компонентов.
