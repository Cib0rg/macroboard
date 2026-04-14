# Архитектура Backend - Часть 2: UI и Стили

## Продолжение стилей и компонентов

### Пример стилей (продолжение)

```xml
<!-- Акцентная кнопка -->
<Style x:Key="AccentButtonStyle" TargetType="Button">
    <Setter Property="Background">
        <Setter.Value>
            <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                <GradientStop Color="#00D9FF" Offset="0"/>
                <GradientStop Color="#0099CC" Offset="1"/>
            </LinearGradientBrush>
        </Setter.Value>
    </Setter>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Padding" Value="16,8"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Effect">
        <Setter.Value>
            <DropShadowEffect Color="#00D9FF" BlurRadius="20" 
                            ShadowDepth="0" Opacity="0.6"/>
        </Setter.Value>
    </Setter>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border Background="{TemplateBinding Background}"
                       CornerRadius="8"
                       Padding="{TemplateBinding Padding}">
                    <ContentPresenter HorizontalAlignment="Center"
                                    VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter Property="Effect">
                            <Setter.Value>
                                <DropShadowEffect Color="#00D9FF" BlurRadius="30" 
                                                ShadowDepth="0" Opacity="1"/>
                            </Setter.Value>
                        </Setter>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>

<!-- Карточка (Card) -->
<Style x:Key="CardStyle" TargetType="Border">
    <Setter Property="Background" Value="{StaticResource BackgroundMediumBrush}"/>
    <Setter Property="CornerRadius" Value="12"/>
    <Setter Property="Padding" Value="20"/>
    <Setter Property="Effect">
        <Setter.Value>
            <DropShadowEffect Color="Black" BlurRadius="20" 
                            ShadowDepth="5" Opacity="0.3"/>
        </Setter.Value>
    </Setter>
</Style>
```

## Производительность и оптимизация

### Оптимизация передачи изображений

**Проблема:** Передача 10 изображений по 10 КБ занимает ~2-3 секунды

**Решения:**
1. **Параллельная передача** - отправлять несколько изображений одновременно
2. **Кэширование** - не отправлять изображения, которые не изменились
3. **Сжатие** - использовать JPEG с качеством 85-90%
4. **Прогрессивная загрузка** - показывать прогресс пользователю

```csharp
public class OptimizedImageTransfer
{
    private readonly IDeviceService _deviceService;
    private readonly Dictionary<string, string> _imageHashCache = new();
    
    public async Task<bool> SendProfileImagesAsync(Profile profile, IProgress<int> progress)
    {
        var tasks = new List<Task<bool>>();
        var semaphore = new SemaphoreSlim(3); // Максимум 3 одновременных передачи
        
        for (int i = 0; i < profile.Buttons.Count; i++)
        {
            var button = profile.Buttons[i];
            var buttonIndex = i;
            
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // Проверить, изменилось ли изображение
                    var imageHash = CalculateHash(button.ImagePath);
                    var cacheKey = $"{profile.ProfileId}_{button.ButtonId}";
                    
                    if (_imageHashCache.TryGetValue(cacheKey, out var cachedHash) 
                        && cachedHash == imageHash)
                    {
                        // Изображение не изменилось, пропустить
                        progress?.Report((buttonIndex + 1) * 100 / profile.Buttons.Count);
                        return true;
                    }
                    
                    // Отправить изображение
                    var result = await _deviceService.SendButtonImageAsync(
                        profile.ProfileId, 
                        button.ButtonId, 
                        button.ImagePath);
                    
                    if (result)
                    {
                        _imageHashCache[cacheKey] = imageHash;
                    }
                    
                    progress?.Report((buttonIndex + 1) * 100 / profile.Buttons.Count);
                    return result;
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }
        
        var results = await Task.WhenAll(tasks);
        return results.All(r => r);
    }
}
```

### Оптимизация UI

**Виртуализация списков:**
```xml
<ListBox ItemsSource="{Binding LargeCollection}"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"/>
```

**Асинхронная загрузка изображений:**
```csharp
public class AsyncImageLoader
{
    public static async Task<BitmapImage> LoadImageAsync(string path)
    {
        return await Task.Run(() =>
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze(); // Для использования в UI потоке
            return bitmap;
        });
    }
}
```

## Безопасность

### Изоляция плагинов

**Sandboxing для плагинов:**
```csharp
public class PluginSandbox
{
    public async Task<Process> StartPluginAsync(PluginInfo plugin)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = Path.Combine(plugin.Path, "plugin.js"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            
            // Ограничения безопасности
            WorkingDirectory = plugin.Path,
            
            Environment =
            {
                ["STREAMDECK_PORT"] = "28196",
                ["STREAMDECK_UUID"] = plugin.Uuid,
                ["PLUGIN_SANDBOX"] = "true"
            }
        };
        
        // Ограничить доступ к файловой системе
        // (требует дополнительной настройки на уровне ОС)
        
        var process = Process.Start(startInfo);
        
        // Установить лимиты ресурсов
        SetProcessLimits(process);
        
        return process;
    }
    
    private void SetProcessLimits(Process process)
    {
        // Ограничить использование CPU и памяти
        // Реализация зависит от платформы
    }
}
```

### Валидация данных

```csharp
public class ProtocolValidator
{
    public bool ValidatePacket(byte[] packet)
    {
        if (packet.Length != 64)
            return false;
        
        // Проверить magic byte
        if (packet[0] != 0xA5)
            return false;
        
        // Проверить end byte
        if (packet[63] != 0x5A)
            return false;
        
        // Проверить checksum
        byte calculatedChecksum = CalculateChecksum(packet, 0, 62);
        if (packet[62] != calculatedChecksum)
            return false;
        
        return true;
    }
    
    public bool ValidateImageData(byte[] imageData)
    {
        // Проверить размер
        if (imageData.Length > 50 * 1024) // Максимум 50 КБ
            return false;
        
        // Проверить формат (JPEG magic bytes)
        if (imageData.Length < 2 || imageData[0] != 0xFF || imageData[1] != 0xD8)
            return false;
        
        return true;
    }
}
```

## Тестирование

### Unit тесты

```csharp
[Fact]
public async Task DeviceService_Connect_ShouldReturnTrue_WhenDeviceIsAvailable()
{
    // Arrange
    var mockHidDevice = new Mock<IHidDeviceManager>();
    mockHidDevice.Setup(x => x.ConnectAsync()).ReturnsAsync(true);
    
    var deviceService = new DeviceService(mockHidDevice.Object);
    
    // Act
    var result = await deviceService.ConnectAsync();
    
    // Assert
    Assert.True(result);
}

[Fact]
public void PacketBuilder_Build_ShouldCreateValidPacket()
{
    // Arrange
    byte commandId = 0x01;
    byte[] payload = new byte[] { 0x01, 0x02, 0x03 };
    
    // Act
    var packet = PacketBuilder.Build(commandId, payload);
    
    // Assert
    Assert.Equal(64, packet.Length);
    Assert.Equal(0xA5, packet[0]); // Magic byte
    Assert.Equal(commandId, packet[1]);
    Assert.Equal(0x5A, packet[63]); // End byte
}

[Fact]
public async Task ProfileService_SendToDevice_ShouldSendAllButtons()
{
    // Arrange
    var mockDevice = new Mock<IDeviceService>();
    var profile = CreateTestProfile();
    var profileService = new ProfileService(mockDevice.Object);
    
    // Act
    var result = await profileService.SendProfileToDeviceAsync(profile);
    
    // Assert
    Assert.True(result);
    mockDevice.Verify(x => x.SendButtonImageAsync(
        It.IsAny<byte>(), 
        It.IsAny<byte>(), 
        It.IsAny<string>()), 
        Times.Exactly(10));
}
```

### Integration тесты

```csharp
[Fact]
public async Task EndToEnd_ConfigureButton_ShouldUpdateDevice()
{
    // Arrange
    using var testHarness = new TestHarness();
    await testHarness.StartBackendAsync();
    await testHarness.ConnectDeviceAsync();
    
    // Act
    var profile = await testHarness.CreateProfileAsync("Test");
    profile.Buttons[0].ActionType = ActionType.Keyboard;
    profile.Buttons[0].ActionData = new KeyboardAction { Key = Key.A };
    
    await testHarness.SendProfileToDeviceAsync(profile);
    
    // Assert
    var deviceProfile = await testHarness.GetDeviceProfileAsync(0);
    Assert.Equal(ActionType.Keyboard, deviceProfile.Buttons[0].ActionType);
}
```

## Логирование и диагностика

### Структурированное логирование

```csharp
public class DeviceService
{
    private readonly ILogger<DeviceService> _logger;
    
    public async Task<bool> ConnectAsync()
    {
        _logger.LogInformation("Attempting to connect to device...");
        
        try
        {
            var connected = await _hidDevice.ConnectAsync();
            
            if (connected)
            {
                var deviceInfo = await GetDeviceInfoAsync();
                _logger.LogInformation(
                    "Device connected successfully. " +
                    "DeviceId: {DeviceId}, FirmwareVersion: {FirmwareVersion}",
                    deviceInfo.DeviceId,
                    deviceInfo.FirmwareVersion);
            }
            else
            {
                _logger.LogWarning("Failed to connect to device");
            }
            
            return connected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to device");
            return false;
        }
    }
}
```

### Диагностический интерфейс

```csharp
public class DiagnosticsViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<LogEntry> _logs;
    
    [ObservableProperty]
    private string _deviceStatus;
    
    [ObservableProperty]
    private Dictionary<string, string> _deviceInfo;
    
    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        Logs.Clear();
        
        // Test 1: Device connection
        AddLog("Testing device connection...");
        var connected = await _deviceService.ConnectAsync();
        AddLog(connected ? "✓ Device connected" : "✗ Device not found", 
               connected ? LogLevel.Success : LogLevel.Error);
        
        if (!connected) return;
        
        // Test 2: Device info
        AddLog("Reading device info...");
        var info = await _deviceService.GetDeviceInfoAsync();
        AddLog($"✓ Device ID: {info.DeviceId}");
        AddLog($"✓ Firmware: {info.FirmwareVersion}");
        
        // Test 3: Button test
        AddLog("Testing buttons (press any button)...");
        var buttonPressed = await WaitForButtonPressAsync(TimeSpan.FromSeconds(10));
        AddLog(buttonPressed ? "✓ Button test passed" : "✗ No button pressed", 
               buttonPressed ? LogLevel.Success : LogLevel.Warning);
        
        // Test 4: Display test
        AddLog("Testing displays...");
        for (int i = 0; i < 10; i++)
        {
            await _deviceService.SendTestImageAsync(i);
            await Task.Delay(100);
        }
        AddLog("✓ Display test completed");
        
        // Test 5: LED test
        AddLog("Testing LEDs...");
        await TestLedsAsync();
        AddLog("✓ LED test completed");
    }
}
```

## Обновления и версионирование

### Автоматическая проверка обновлений

```csharp
public class UpdateService
{
    private const string UpdateUrl = "https://api.macrokeyboard.com/updates";
    
    public async Task<UpdateInfo> CheckForUpdatesAsync()
    {
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
        
        using var client = new HttpClient();
        var response = await client.GetStringAsync(UpdateUrl);
        var updateInfo = JsonConvert.DeserializeObject<UpdateInfo>(response);
        
        if (updateInfo.Version > currentVersion)
        {
            return updateInfo;
        }
        
        return null;
    }
    
    public async Task<bool> DownloadAndInstallUpdateAsync(UpdateInfo update, IProgress<int> progress)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "MacroKeyboard_Update.exe");
        
        using var client = new HttpClient();
        using var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        using var fileStream = File.Create(tempPath);
        
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var buffer = new byte[8192];
        var totalRead = 0L;
        
        using var stream = await response.Content.ReadAsStreamAsync();
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            totalRead += bytesRead;
            progress?.Report((int)(totalRead * 100 / totalBytes));
        }
        
        // Запустить инсталлятор
        Process.Start(new ProcessStartInfo
        {
            FileName = tempPath,
            Arguments = "/silent /update",
            UseShellExecute = true
        });
        
        // Закрыть приложение
        Application.Current.Shutdown();
        
        return true;
    }
}
```

### Миграция данных

```csharp
public class DataMigration
{
    public async Task MigrateAsync(Version fromVersion, Version toVersion)
    {
        if (fromVersion < new Version(1, 1, 0))
        {
            await MigrateTo_1_1_0();
        }
        
        if (fromVersion < new Version(1, 2, 0))
        {
            await MigrateTo_1_2_0();
        }
    }
    
    private async Task MigrateTo_1_1_0()
    {
        // Добавлена поддержка папок (folders)
        var profiles = await LoadProfilesAsync();
        foreach (var profile in profiles)
        {
            if (profile.Folders == null)
            {
                profile.Folders = new List<Folder>();
            }
        }
        await SaveProfilesAsync(profiles);
    }
    
    private async Task MigrateTo_1_2_0()
    {
        // Изменен формат хранения изображений
        // Конвертировать PNG → JPEG
        var imagesDir = Path.Combine(AppDataPath, "Images");
        var pngFiles = Directory.GetFiles(imagesDir, "*.png", SearchOption.AllDirectories);
        
        foreach (var pngFile in pngFiles)
        {
            var jpegFile = Path.ChangeExtension(pngFile, ".jpg");
            await ConvertPngToJpegAsync(pngFile, jpegFile);
            File.Delete(pngFile);
        }
    }
}
```

## Локализация

### Поддержка нескольких языков

```csharp
// Resources/Strings.resx (English)
// DeviceConnected = "Device connected"
// ProfileSaved = "Profile saved successfully"

// Resources/Strings.ru.resx (Russian)
// DeviceConnected = "Устройство подключено"
// ProfileSaved = "Профиль успешно сохранен"

public class LocalizationService
{
    private ResourceManager _resourceManager;
    private CultureInfo _currentCulture;
    
    public LocalizationService()
    {
        _resourceManager = new ResourceManager("MacroKeyboard.UI.Resources.Strings", 
                                              Assembly.GetExecutingAssembly());
        _currentCulture = CultureInfo.CurrentUICulture;
    }
    
    public string GetString(string key)
    {
        return _resourceManager.GetString(key, _currentCulture);
    }
    
    public void SetLanguage(string languageCode)
    {
        _currentCulture = new CultureInfo(languageCode);
        // Уведомить UI об изменении языка
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
    
    public event EventHandler LanguageChanged;
}
```

## Инсталлятор

### WiX Toolset конфигурация

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
  <Product Id="*" 
           Name="Macro Keyboard" 
           Language="1033" 
           Version="1.0.0.0" 
           Manufacturer="Your Company" 
           UpgradeCode="PUT-GUID-HERE">
    
    <Package InstallerVersion="200" Compressed="yes" InstallScope="perMachine" />
    
    <MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />
    <MediaTemplate EmbedCab="yes" />
    
    <Feature Id="ProductFeature" Title="Macro Keyboard" Level="1">
      <ComponentGroupRef Id="ProductComponents" />
      <ComponentGroupRef Id="PluginComponents" />
    </Feature>
    
    <!-- Install Backend as Windows Service -->
    <Component Id="BackendService" Guid="PUT-GUID-HERE">
      <File Id="BackendExe" Source="$(var.Backend.TargetPath)" KeyPath="yes" />
      <ServiceInstall Id="BackendServiceInstall"
                      Name="MacroKeyboardBackend"
                      DisplayName="Macro Keyboard Backend Service"
                      Description="Background service for Macro Keyboard device"
                      Type="ownProcess"
                      Start="auto"
                      ErrorControl="normal" />
      <ServiceControl Id="BackendServiceControl"
                      Name="MacroKeyboardBackend"
                      Start="install"
                      Stop="both"
                      Remove="uninstall" />
    </Component>
    
    <!-- Tray App autostart -->
    <Component Id="TrayAppAutostart" Guid="PUT-GUID-HERE">
      <RegistryValue Root="HKCU" 
                     Key="Software\Microsoft\Windows\CurrentVersion\Run"
                     Name="MacroKeyboard"
                     Value="[INSTALLFOLDER]MacroKeyboard.TrayApp.exe"
                     Type="string" />
    </Component>
  </Product>
</Wix>
```

## Заключение

Данная архитектура обеспечивает:

✅ **Совместимость с прошивкой** - полная поддержка протокола USB HID
✅ **Красивый UI** - современный дизайн в стиле Mad Catz
✅ **Работа в трее** - удобный доступ к функциям
✅ **Система плагинов** - совместимость с Elgato Stream Deck
✅ **Расширяемость** - модульная архитектура
✅ **Производительность** - оптимизированная передача данных
✅ **Безопасность** - изоляция плагинов и валидация данных
✅ **Надежность** - обработка ошибок и логирование

### Следующие шаги

1. Создать структуру проектов в Visual Studio
2. Настроить CI/CD pipeline
3. Начать реализацию с Фазы 1 (Core и Communication)
4. Провести интеграционное тестирование с прошивкой
5. Итеративно добавлять функциональность

### Ссылки на связанные документы

- [Протокол обмена данными](protocol.md)
- [Требования к прошивке](../firmware/plans/REQUIREMENTS.md)
- [Система плагинов](../software/plans/plugin_system.md)
- [Требования к софту](../software/REQUIREMENTS.md)
