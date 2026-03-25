# Диаграммы взаимодействия управляющего софта

## Архитектура приложения

```mermaid
graph TB
    subgraph UI["Presentation Layer (WPF/Avalonia)"]
        VIEWS[Views]
        VM[ViewModels]
        CONTROLS[Custom Controls]
    end
    
    subgraph CORE["Core Layer"]
        MODELS[Models]
        INTERFACES[Interfaces]
        SERVICES[Service Interfaces]
    end
    
    subgraph INFRA["Infrastructure Layer"]
        IMPL[Service Implementations]
        REPOS[Repositories]
        STORAGE[File Storage]
    end
    
    subgraph COMM["Communication Layer"]
        HID[HID Device Manager]
        PROTOCOL[Protocol Handler]
        COMMANDS[Commands]
    end
    
    VIEWS --> VM
    VM --> SERVICES
    SERVICES --> IMPL
    IMPL --> REPOS
    IMPL --> PROTOCOL
    PROTOCOL --> HID
    PROTOCOL --> COMMANDS
    REPOS --> STORAGE
    
    VM --> MODELS
    IMPL --> MODELS
```

## Запуск приложения

```mermaid
sequenceDiagram
    participant USER as Пользователь
    participant APP as App.xaml.cs
    participant DI as DI Container
    participant MAIN as MainWindow
    participant VM as MainViewModel
    participant DEV as DeviceService
    participant PROF as ProfileService
    
    USER->>APP: Запуск приложения
    APP->>DI: ConfigureServices()
    DI->>DI: Register all services
    DI-->>APP: ServiceProvider
    
    APP->>MAIN: Create MainWindow
    APP->>DI: GetService<MainViewModel>()
    DI->>VM: new MainViewModel(services...)
    VM->>DEV: Subscribe to events
    VM->>DEV: ConnectAsync()
    
    DEV->>DEV: Search for device
    alt Device found
        DEV-->>VM: DeviceConnected event
        VM->>PROF: LoadProfilesAsync()
        PROF-->>VM: List<Profile>
        VM->>VM: Update UI
    else Device not found
        DEV-->>VM: Device not found
        VM->>VM: Show "Connect device" message
    end
    
    APP->>MAIN: Show()
    MAIN-->>USER: Application ready
```

## Подключение к устройству

```mermaid
sequenceDiagram
    participant UI as UI
    participant VM as ViewModel
    participant DEV as DeviceService
    participant HID as HidDeviceManager
    participant MON as DeviceMonitor
    participant PROTO as ProtocolHandler
    
    UI->>VM: User clicks "Connect"
    VM->>DEV: ConnectAsync()
    DEV->>HID: ConnectAsync()
    
    HID->>HID: Enumerate USB devices
    HID->>HID: Find device (VID/PID)
    
    alt Device found
        HID->>HID: OpenDevice()
        HID->>MON: StartMonitoring()
        HID-->>DEV: Connected
        
        DEV->>PROTO: PingAsync()
        PROTO->>HID: SendCommand(PING)
        HID-->>PROTO: Response
        PROTO-->>DEV: Device responsive
        
        DEV->>PROTO: GetDeviceInfoAsync()
        PROTO->>HID: SendCommand(GET_DEVICE_INFO)
        HID-->>PROTO: DeviceInfo
        PROTO-->>DEV: DeviceInfo
        
        DEV-->>VM: DeviceConnected event
        VM->>VM: Update UI (show device info)
        VM-->>UI: "Connected" status
    else Device not found
        HID-->>DEV: Not found
        DEV-->>VM: Connection failed
        VM-->>UI: "Device not found" error
    end
```

## Создание и настройка профиля

```mermaid
sequenceDiagram
    participant UI as ProfileEditorView
    participant VM as ProfileEditorViewModel
    participant PROF as ProfileService
    participant REPO as ProfileRepository
    participant FS as FileSystem
    
    UI->>VM: User clicks "New Profile"
    VM->>PROF: CreateProfileAsync("Gaming")
    
    PROF->>PROF: Create Profile object
    PROF->>PROF: Initialize 10 buttons
    PROF->>REPO: SaveAsync(profile)
    
    REPO->>FS: Write JSON file
    FS-->>REPO: Success
    REPO-->>PROF: Profile saved
    PROF-->>VM: Profile created
    
    VM->>VM: Add to Profiles list
    VM->>VM: Set as SelectedProfile
    VM-->>UI: Update UI
    
    Note over UI,VM: User configures buttons
    
    loop For each button
        UI->>VM: SelectButton(buttonId)
        VM->>VM: Set SelectedButton
        
        UI->>VM: LoadImage()
        VM->>VM: OpenFileDialog
        UI-->>VM: Image path selected
        VM->>VM: Update button.ImagePath
        
        UI->>VM: SetAction(KeyboardAction)
        VM->>VM: Update button.Action
        
        UI->>VM: SetLedColor(color)
        VM->>VM: Update button.Led
        
        VM->>PROF: UpdateProfileAsync(profile)
        PROF->>REPO: SaveAsync(profile)
        REPO->>FS: Write JSON
    end
    
    UI->>VM: User clicks "Send to Device"
    VM->>PROF: SendProfileToDeviceAsync(profile)
    Note over PROF: See "Отправка профиля на устройство"
```

## Отправка профиля на устройство

```mermaid
sequenceDiagram
    participant VM as ViewModel
    participant PROF as ProfileService
    participant PROTO as ProtocolHandler
    participant IMG as ImageService
    participant DEV as Device
    
    VM->>PROF: SendProfileToDeviceAsync(profile)
    
    PROF->>PROTO: SetProfile(profileId)
    PROTO->>DEV: SET_PROFILE command
    DEV-->>PROTO: OK
    
    loop For each button (0-9)
        Note over PROF: Send button configuration
        
        PROF->>PROTO: SetButtonAction(profileId, buttonId, action)
        PROTO->>DEV: SET_BUTTON_ACTION command
        DEV-->>PROTO: OK
        
        alt Button has image
            PROF->>IMG: LoadImageAsync(imagePath)
            IMG-->>PROF: imageData
            
            PROF->>IMG: ConvertToJpegAsync(imageData)
            IMG-->>PROF: jpegData
            
            PROF->>PROTO: StartImageTransfer(profileId, buttonId, jpegData)
            PROTO->>DEV: START_IMAGE_TRANSFER
            DEV-->>PROTO: transferId
            
            PROF->>PROF: Split image into chunks
            loop For each chunk
                PROF->>PROTO: SendImageChunk(transferId, chunkNum, data)
                PROTO->>DEV: IMAGE_DATA_CHUNK
                DEV-->>PROTO: ACK
            end
            
            PROF->>PROTO: EndImageTransfer(transferId, crc32)
            PROTO->>DEV: END_IMAGE_TRANSFER
            DEV-->>PROTO: OK (CRC verified)
        end
        
        PROF->>PROTO: SetLedColor(profileId, buttonId, led)
        PROTO->>DEV: SET_LED_COLOR command
        DEV-->>PROTO: OK
        
        PROF->>VM: Progress update (button X/10)
        VM->>VM: Update progress bar
    end
    
    PROF->>PROTO: SaveProfile(profileId)
    PROTO->>DEV: SAVE_PROFILE command
    DEV->>DEV: Write to flash
    DEV-->>PROTO: OK
    
    PROF-->>VM: Profile sent successfully
    VM->>VM: Show success notification
```

## OTA обновление

```mermaid
sequenceDiagram
    participant UI as SettingsView
    participant VM as SettingsViewModel
    participant OTA as OtaService
    participant HTTP as HTTP Server
    participant PROTO as ProtocolHandler
    participant DEV as Device
    
    UI->>VM: User selects firmware file
    VM->>VM: OpenFileDialog
    UI-->>VM: firmware.bin selected
    
    UI->>VM: User clicks "Update Firmware"
    VM->>OTA: StartOtaUpdateAsync(firmwarePath)
    
    OTA->>OTA: Calculate MD5
    OTA->>HTTP: Start local HTTP server
    HTTP->>HTTP: Serve firmware file
    HTTP-->>OTA: Server URL
    
    OTA->>PROTO: SetWifiCredentials(ssid, password)
    PROTO->>DEV: SET_WIFI_CREDENTIALS
    DEV-->>PROTO: OK
    
    OTA->>PROTO: GetWifiStatus()
    PROTO->>DEV: GET_WIFI_STATUS
    
    loop Wait for WiFi connection
        DEV->>DEV: Connect to WiFi
        PROTO->>DEV: GET_WIFI_STATUS
        DEV-->>PROTO: Status
        
        alt Connected
            PROTO-->>OTA: WiFi connected
        else Connecting
            OTA->>OTA: Wait 1 second
        else Error
            PROTO-->>OTA: WiFi error
            OTA-->>VM: Update failed
            VM-->>UI: Show error
        end
    end
    
    OTA->>PROTO: StartOtaUpdate(url, size, md5)
    PROTO->>DEV: START_OTA_UPDATE
    DEV->>DEV: Start OTA task
    DEV-->>PROTO: Started
    
    loop Monitor OTA progress
        OTA->>PROTO: GetOtaStatus()
        PROTO->>DEV: GET_OTA_STATUS
        DEV-->>PROTO: Status + Progress
        PROTO-->>OTA: Status info
        
        OTA->>VM: Progress update
        VM->>VM: Update progress bar
        VM-->>UI: Show progress (X%)
        
        alt Complete
            OTA-->>VM: OTA complete
            VM-->>UI: "Update successful, device rebooting"
        else Error
            OTA-->>VM: OTA error
            VM-->>UI: Show error message
        end
    end
    
    Note over DEV: Device reboots with new firmware
    
    OTA->>HTTP: Stop HTTP server
    HTTP-->>OTA: Stopped
```

## Tray Application - Переключение профиля

```mermaid
sequenceDiagram
    participant USER as Пользователь
    participant TRAY as TrayIcon
    participant CTX as TrayContext
    participant HK as HotKeyManager
    participant PROF as ProfileService
    participant PROTO as ProtocolHandler
    participant DEV as Device
    
    alt Via Context Menu
        USER->>TRAY: Right click
        TRAY->>CTX: Show context menu
        CTX-->>USER: Menu with profiles
        USER->>CTX: Select "Profile 2"
        CTX->>PROF: SwitchProfileAsync(1)
    else Via Hot Key
        USER->>HK: Press Ctrl+Alt+2
        HK->>CTX: HotKey triggered
        CTX->>PROF: SwitchProfileAsync(1)
    end
    
    PROF->>PROTO: SetProfile(profileId=1)
    PROTO->>DEV: SET_PROFILE command
    DEV->>DEV: Switch to profile 1
    DEV->>DEV: Update displays
    DEV->>DEV: Update LEDs
    DEV-->>PROTO: OK
    PROTO-->>PROF: Success
    
    PROF-->>CTX: Profile switched
    CTX->>TRAY: ShowBalloonTip("Switched to Profile 2")
    TRAY-->>USER: Notification
```

## Диагностика - Просмотр логов

```mermaid
sequenceDiagram
    participant UI as DiagnosticsView
    participant VM as DiagnosticsViewModel
    participant LOG as LoggingService
    participant CDC as USB CDC
    participant DEV as Device
    
    UI->>VM: User opens Diagnostics
    VM->>LOG: EnableDeviceLogging()
    
    LOG->>CDC: Connect to CDC port
    CDC->>DEV: Enable logging
    DEV-->>CDC: OK
    
    loop Continuous logging
        DEV->>CDC: Log message
        CDC->>LOG: Parse log entry
        LOG->>LOG: Filter by level
        LOG->>VM: New log entry
        VM->>VM: Add to ObservableCollection
        VM-->>UI: Update log view
    end
    
    alt User filters logs
        UI->>VM: SetLogLevel(Warning)
        VM->>VM: Filter logs
        VM-->>UI: Show filtered logs
    end
    
    alt User saves logs
        UI->>VM: SaveLogs()
        VM->>LOG: SaveToFile(path)
        LOG->>LOG: Write to file
        LOG-->>VM: Saved
        VM-->>UI: "Logs saved"
    end
    
    UI->>VM: User closes Diagnostics
    VM->>LOG: DisableDeviceLogging()
    LOG->>CDC: Disconnect
```

## Обработка ошибок

```mermaid
flowchart TB
    START([Operation Start]) --> TRY{Try operation}
    
    TRY -->|Success| SUCCESS[Operation successful]
    TRY -->|Exception| CATCH[Catch exception]
    
    CATCH --> TYPE{Exception type?}
    
    TYPE -->|DeviceNotConnectedException| RECONNECT[Attempt reconnect]
    RECONNECT --> RETRY{Retry?}
    RETRY -->|Yes| TRY
    RETRY -->|No| ERROR[Show error]
    
    TYPE -->|TimeoutException| TIMEOUT[Operation timeout]
    TIMEOUT --> RETRY2{Retry?}
    RETRY2 -->|Yes| TRY
    RETRY2 -->|No| ERROR
    
    TYPE -->|InvalidDataException| VALIDATE[Validate data]
    VALIDATE --> FIX{Can fix?}
    FIX -->|Yes| TRY
    FIX -->|No| ERROR
    
    TYPE -->|Other| LOG[Log exception]
    LOG --> ERROR
    
    ERROR --> NOTIFY[Notify user]
    NOTIFY --> END([End])
    
    SUCCESS --> END
```

## State Machine приложения

```mermaid
stateDiagram-v2
    [*] --> Initializing
    Initializing --> Disconnected: Init complete
    
    Disconnected --> Connecting: Connect attempt
    Connecting --> Connected: Device found
    Connecting --> Disconnected: Connection failed
    
    Connected --> Idle: Ready
    Idle --> ConfiguringProfile: Edit profile
    Idle --> SendingProfile: Send to device
    Idle --> UpdatingFirmware: Start OTA
    Idle --> Disconnected: Device unplugged
    
    ConfiguringProfile --> Idle: Save/Cancel
    
    SendingProfile --> SendingAction: For each button
    SendingAction --> SendingImage: Action sent
    SendingImage --> SendingLED: Image sent
    SendingLED --> SendingAction: LED sent (next button)
    SendingLED --> SavingProfile: All buttons done
    SavingProfile --> Idle: Profile saved
    SendingProfile --> Idle: Error
    
    UpdatingFirmware --> ConnectingWiFi: WiFi credentials sent
    ConnectingWiFi --> Downloading: WiFi connected
    Downloading --> Installing: Download complete
    Installing --> Rebooting: Install complete
    Rebooting --> Disconnected: Device reboots
    UpdatingFirmware --> Idle: Error
    
    Idle --> [*]: Application exit
```

## Архитектура данных

```mermaid
erDiagram
    PROFILE ||--o{ BUTTON_CONFIG : contains
    BUTTON_CONFIG ||--|| ACTION_CONFIG : has
    BUTTON_CONFIG ||--|| LED_CONFIG : has
    BUTTON_CONFIG ||--o| IMAGE : has
    
    PROFILE {
        int Id
        string Name
        DateTime CreatedAt
        DateTime ModifiedAt
    }
    
    BUTTON_CONFIG {
        int Id
        int ProfileId
        ActionType ActionType
        string ImagePath
    }
    
    ACTION_CONFIG {
        int Id
        ActionType Type
        byte[] Data
    }
    
    LED_CONFIG {
        int Id
        byte R
        byte G
        byte B
        byte Brightness
        LedEffect Effect
    }
    
    IMAGE {
        int Id
        string Path
        byte[] Data
        int Width
        int Height
    }
```

## Поток данных при настройке кнопки

```mermaid
flowchart LR
    USER[Пользователь] -->|Выбирает изображение| UI[UI]
    UI -->|LoadImageAsync| IMG[ImageService]
    IMG -->|Загружает файл| FS[FileSystem]
    FS -->|Данные изображения| IMG
    IMG -->|Масштабирует 160x160| IMG
    IMG -->|Конвертирует в JPEG| IMG
    IMG -->|Сохраняет| REPO[ImageRepository]
    REPO -->|Путь к файлу| UI
    
    USER -->|Настраивает действие| UI
    UI -->|UpdateAction| VM[ViewModel]
    VM -->|Обновляет модель| MODEL[ButtonConfig]
    
    USER -->|Выбирает цвет LED| UI
    UI -->|SetLedColor| VM
    VM -->|Обновляет модель| MODEL
    
    USER -->|Нажимает Save| UI
    UI -->|SaveAsync| PROF[ProfileService]
    PROF -->|Сохраняет профиль| REPO2[ProfileRepository]
    REPO2 -->|Записывает JSON| FS
    
    USER -->|Нажимает Send| UI
    UI -->|SendToDeviceAsync| PROF
    PROF -->|Отправляет команды| PROTO[ProtocolHandler]
    PROTO -->|USB HID| DEV[Device]
```

## Многопоточность

```mermaid
graph TB
    subgraph UI_THREAD["UI Thread"]
        XAML[XAML Views]
        VM[ViewModels]
        DISPATCHER[Dispatcher]
    end
    
    subgraph WORKER_THREADS["Worker Threads"]
        USB[USB Communication]
        IMG[Image Processing]
        FILE[File I/O]
        NET[Network OTA]
    end
    
    subgraph BACKGROUND["Background Tasks"]
        MONITOR[Device Monitor]
        LOG[Log Reader]
    end
    
    XAML --> VM
    VM -->|async/await| USB
    VM -->|async/await| IMG
    VM -->|async/await| FILE
    VM -->|async/await| NET
    
    USB -->|Invoke| DISPATCHER
    IMG -->|Invoke| DISPATCHER
    FILE -->|Invoke| DISPATCHER
    NET -->|Invoke| DISPATCHER
    
    DISPATCHER --> VM
    
    MONITOR -->|Events| DISPATCHER
    LOG -->|Events| DISPATCHER
```

## Производительность

### Целевые показатели

| Операция | Целевое время | Приемлемое время |
|----------|---------------|------------------|
| Запуск приложения | < 2 с | < 3 с |
| Подключение к устройству | < 500 мс | < 1 с |
| Загрузка профиля | < 100 мс | < 200 мс |
| Отправка профиля | < 5 с | < 10 с |
| Обработка изображения | < 500 мс | < 1 с |
| Переключение профиля | < 200 мс | < 500 мс |
| OTA обновление | < 2 мин | < 5 мин |

### Оптимизации

1. **Асинхронные операции**: Все I/O операции async/await
2. **Кэширование**: Кэширование обработанных изображений
3. **Lazy loading**: Загрузка данных по требованию
4. **Виртуализация UI**: Виртуализация списков профилей
5. **Параллелизм**: Параллельная отправка данных на устройство
