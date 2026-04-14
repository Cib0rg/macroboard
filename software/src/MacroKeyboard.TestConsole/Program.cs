using MacroKeyboard.Communication.HidDevice;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Services;
using MacroKeyboard.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Console.WriteLine("=== Macro Keyboard Test Console ===\n");

// Настроить DI контейнер
var services = new ServiceCollection();

// Логирование
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// Сервисы
services.AddSingleton<HidDeviceManager>();
services.AddSingleton<IDeviceService, DeviceService>();
services.AddSingleton<ProfileRepository>();
services.AddSingleton<ImageService>();
services.AddSingleton<ProfileService>();

var serviceProvider = services.BuildServiceProvider();

// Получить сервисы
var deviceService = serviceProvider.GetRequiredService<IDeviceService>();
var profileService = serviceProvider.GetRequiredService<ProfileService>();

// Подписаться на события
deviceService.DeviceConnected += (s, e) => Console.WriteLine("✓ Device connected!");
deviceService.DeviceDisconnected += (s, e) => Console.WriteLine("✗ Device disconnected!");
deviceService.ButtonPressed += (s, e) => Console.WriteLine($"Button {e.ButtonId} pressed");
deviceService.EncoderRotated += (s, e) => Console.WriteLine($"Encoder rotated {e.Direction}");
deviceService.ProfileChanged += (s, e) => Console.WriteLine($"Profile changed: {e.OldProfileId} → {e.NewProfileId}");

try
{
    // 1. Подключиться к устройству
    Console.WriteLine("Connecting to device...");
    var connected = await deviceService.ConnectAsync();
    
    if (!connected)
    {
        Console.WriteLine("✗ Device not found. Please connect the device and try again.");
        return;
    }
    
    // 2. Получить информацию об устройстве
    Console.WriteLine("\nGetting device info...");
    var deviceInfo = await deviceService.GetDeviceInfoAsync();
    
    Console.WriteLine($"Device ID: {deviceInfo.DeviceId}");
    Console.WriteLine($"Firmware: {deviceInfo.FirmwareVersion}");
    Console.WriteLine($"Buttons: {deviceInfo.ButtonCount}");
    Console.WriteLine($"Profiles: {deviceInfo.ProfileCount}");
    Console.WriteLine($"Current Profile: {deviceInfo.CurrentProfile}");
    Console.WriteLine($"Free Space: {deviceInfo.FreeSpace} bytes");
    
    // 3. Проверить PING
    Console.WriteLine("\nTesting PING...");
    var pingOk = await deviceService.PingAsync();
    Console.WriteLine(pingOk ? "✓ PING successful" : "✗ PING failed");
    
    // 4. Загрузить профили
    Console.WriteLine("\nLoading profiles...");
    var profiles = await profileService.GetAllProfilesAsync();
    Console.WriteLine($"Found {profiles.Count} profiles");
    
    foreach (var profile in profiles)
    {
        Console.WriteLine($"  - Profile {profile.ProfileId}: {profile.Name}");
    }
    
    // 5. Создать тестовый профиль (если нет)
    if (profiles.Count == 0)
    {
        Console.WriteLine("\nCreating test profile...");
        var testProfile = await profileService.CreateProfileAsync("Test Profile");
        
        // Настроить первую кнопку
        testProfile.Buttons[0].Action = new KeyboardAction
        {
            Modifiers = KeyModifiers.LeftCtrl,
            KeyCode = 0x06 // 'C'
        };
        testProfile.Buttons[0].Led = LedConfig.FromRgb(255, 0, 0, 200); // Красный
        
        await profileService.UpdateProfileAsync(testProfile);
        Console.WriteLine($"✓ Test profile created: {testProfile.Name}");
    }
    
    // 6. Меню
    while (true)
    {
        Console.WriteLine("\n=== Menu ===");
        Console.WriteLine("1. Switch profile");
        Console.WriteLine("2. Send profile to device");
        Console.WriteLine("3. Set LED color");
        Console.WriteLine("4. Device info");
        Console.WriteLine("5. List profiles");
        Console.WriteLine("0. Exit");
        Console.Write("\nSelect option: ");
        
        var input = Console.ReadLine();
        
        switch (input)
        {
            case "1":
                Console.Write("Enter profile ID (0-4): ");
                if (byte.TryParse(Console.ReadLine(), out var profileId) && profileId < 5)
                {
                    var success = await deviceService.SetProfileAsync(profileId);
                    Console.WriteLine(success ? "✓ Profile switched" : "✗ Failed to switch profile");
                }
                break;
                
            case "2":
                Console.Write("Enter profile ID to send (0-4): ");
                if (byte.TryParse(Console.ReadLine(), out var sendProfileId))
                {
                    var profile = await profileService.GetProfileAsync(sendProfileId);
                    if (profile != null)
                    {
                        Console.WriteLine("Sending profile to device...");
                        var progress = new Progress<int>(p => Console.Write($"\rProgress: {p}%"));
                        var sent = await profileService.SendProfileToDeviceAsync(profile, progress);
                        Console.WriteLine(sent ? "\n✓ Profile sent successfully" : "\n✗ Failed to send profile");
                    }
                    else
                    {
                        Console.WriteLine("✗ Profile not found");
                    }
                }
                break;
                
            case "3":
                Console.Write("Enter button ID (0-9): ");
                if (byte.TryParse(Console.ReadLine(), out var buttonId) && buttonId < 10)
                {
                    Console.Write("Enter R (0-255): ");
                    byte.TryParse(Console.ReadLine(), out var r);
                    Console.Write("Enter G (0-255): ");
                    byte.TryParse(Console.ReadLine(), out var g);
                    Console.Write("Enter B (0-255): ");
                    byte.TryParse(Console.ReadLine(), out var b);
                    
                    var led = LedConfig.FromRgb(r, g, b);
                    var success = await deviceService.SetLedColorAsync(0, buttonId, led);
                    Console.WriteLine(success ? "✓ LED color set" : "✗ Failed to set LED color");
                }
                break;
                
            case "4":
                var info = await deviceService.GetDeviceInfoAsync();
                Console.WriteLine($"\nDevice ID: {info.DeviceId}");
                Console.WriteLine($"Firmware: {info.FirmwareVersion}");
                Console.WriteLine($"Current Profile: {info.CurrentProfile}");
                Console.WriteLine($"Free Space: {info.FreeSpace} bytes");
                break;
                
            case "5":
                var allProfiles = await profileService.GetAllProfilesAsync();
                Console.WriteLine($"\nProfiles ({allProfiles.Count}):");
                foreach (var p in allProfiles)
                {
                    Console.WriteLine($"  {p.ProfileId}. {p.Name} ({p.Buttons.Count(b => b.IsConfigured)} buttons configured)");
                }
                break;
                
            case "0":
                Console.WriteLine("Exiting...");
                deviceService.Disconnect();
                return;
                
            default:
                Console.WriteLine("Invalid option");
                break;
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n✗ Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
finally
{
    deviceService.Disconnect();
}
