# Диаграммы взаимодействия компонентов системы

## Общая архитектура системы

```mermaid
graph TB
    subgraph PC[Управляющий софт на ПК]
        APP[Desktop Application]
    end
    
    subgraph USB[USB Interface]
        HID_KB[HID Keyboard]
        HID_RAW[HID Raw]
        CDC[CDC UART]
    end
    
    subgraph ESP32[ESP32-S3 Firmware]
        USB_LAYER[USB Layer]
        PROTO[Protocol Handler]
        PROFILE[Profile Manager]
        STORAGE[Storage Layer]
        HW[Hardware Layer]
        
        USB_LAYER --> PROTO
        PROTO --> PROFILE
        PROTO --> STORAGE
        PROFILE --> STORAGE
        PROFILE --> HW
    end
    
    subgraph HARDWARE[Аппаратура]
        DISPLAYS[10x GC9A01 Displays]
        BUTTONS[10x Buttons]
        ENCODER[Rotary Encoder]
        LEDS[10x WS2812 RGB]
    end
    
    APP -->|Commands| HID_RAW
    HID_RAW --> USB_LAYER
    USB_LAYER -->|Keyboard Events| HID_KB
    USB_LAYER -->|Logs| CDC
    
    HW --> DISPLAYS
    HW --> LEDS
    BUTTONS --> HW
    ENCODER --> HW
```

## Процесс инициализации устройства

```mermaid
sequenceDiagram
    participant BOOT as Bootloader
    participant MAIN as Main Task
    participant NVS as NVS Manager
    participant SPIFFS as SPIFFS
    participant USB as USB Stack
    participant HW as Hardware Init
    participant PROF as Profile Manager
    participant TASKS as FreeRTOS Tasks
    
    BOOT->>MAIN: Start app_main()
    
    Note over MAIN: Phase 1: Core Init
    MAIN->>NVS: nvs_flash_init()
    NVS-->>MAIN: OK
    
    MAIN->>SPIFFS: Mount /storage
    SPIFFS-->>MAIN: OK
    
    Note over MAIN: Phase 2: Hardware Init
    MAIN->>HW: Init SPI bus
    MAIN->>HW: Init display mux
    MAIN->>HW: Init buttons GPIO
    MAIN->>HW: Init encoder GPIO
    MAIN->>HW: Init WS2812 RMT
    HW-->>MAIN: All OK
    
    Note over MAIN: Phase 3: USB Init
    MAIN->>USB: Init TinyUSB
    MAIN->>USB: Register HID Keyboard
    MAIN->>USB: Register HID Raw
    MAIN->>USB: Register CDC
    USB-->>MAIN: USB Ready
    
    Note over MAIN: Phase 4: Load Profile
    MAIN->>NVS: Get current_profile
    NVS-->>MAIN: profile_id = 0
    
    MAIN->>PROF: Load profile 0
    PROF->>SPIFFS: Read profile_0.bin
    SPIFFS-->>PROF: Profile data
    PROF->>SPIFFS: Load images
    SPIFFS-->>PROF: Images data
    PROF-->>MAIN: Profile loaded
    
    Note over MAIN: Phase 5: Display Init
    MAIN->>HW: Init all displays
    loop For each display
        HW->>HW: Select display
        HW->>HW: Send init commands
        HW->>HW: Clear screen
    end
    HW-->>MAIN: Displays ready
    
    MAIN->>HW: Update all displays
    MAIN->>HW: Set LED colors
    
    Note over MAIN: Phase 6: Start Tasks
    MAIN->>TASKS: Create USB RX Task
    MAIN->>TASKS: Create Button Task
    MAIN->>TASKS: Create Encoder Task
    MAIN->>TASKS: Create Protocol Task
    MAIN->>TASKS: Create Display Task
    MAIN->>TASKS: Create LED Task
    TASKS-->>MAIN: All tasks running
    
    Note over MAIN: System Ready
    MAIN->>USB: Send DEVICE_READY event
```

## Обработка нажатия кнопки

```mermaid
sequenceDiagram
    participant BTN as Button Hardware
    participant ISR as GPIO ISR
    participant TASK as Button Task
    participant PROF as Profile Manager
    participant EXEC as Action Executor
    participant USB_KB as USB HID Keyboard
    participant USB_RAW as USB HID Raw
    participant LED as LED Driver
    
    BTN->>ISR: Button pressed (GPIO interrupt)
    ISR->>TASK: Send event to queue
    
    TASK->>TASK: Debounce (10ms delay)
    TASK->>TASK: Confirm button still pressed
    
    TASK->>PROF: Get button action
    PROF-->>TASK: action_type, action_data
    
    alt Action type = Keyboard
        TASK->>EXEC: Execute keyboard action
        EXEC->>USB_KB: Send HID report
        USB_KB-->>EXEC: Sent
        
        Note over EXEC: If text typing
        loop For each character
            EXEC->>USB_KB: Send keycode
            EXEC->>EXEC: Delay 20ms
        end
    else Action type = Custom HID
        TASK->>EXEC: Execute custom action
        EXEC->>USB_RAW: Send custom report
        USB_RAW-->>EXEC: Sent
    end
    
    TASK->>LED: Trigger button press effect
    LED->>LED: Flash LED briefly
    
    Note over TASK: Wait for button release
    BTN->>ISR: Button released
    ISR->>TASK: Release event
    
    TASK->>USB_KB: Release all keys
    TASK->>LED: Restore normal LED state
```

## Переключение профиля через энкодер

```mermaid
sequenceDiagram
    participant ENC as Encoder Hardware
    participant ISR as Encoder ISR
    participant TASK as Encoder Task
    participant PROF as Profile Manager
    participant STOR as Storage
    participant DISP as Display Manager
    participant LED as LED Driver
    participant USB as USB HID Raw
    
    ENC->>ISR: Rotation detected (A/B signals)
    ISR->>ISR: Determine direction
    ISR->>TASK: Send rotation event
    
    TASK->>TASK: Count steps
    Note over TASK: 4 steps = 1 profile change
    
    alt 4 steps accumulated
        TASK->>PROF: Get current profile
        PROF-->>TASK: current = 0
        
        TASK->>TASK: Calculate next profile
        Note over TASK: next = current + 1 mod 5
        
        TASK->>PROF: Switch to profile 1
        
        Note over PROF: Profile Switch Process
        PROF->>STOR: Load profile 1
        STOR-->>PROF: Profile data
        
        PROF->>STOR: Load images for profile 1
        loop For each button
            STOR->>STOR: Load and decode image
        end
        STOR-->>PROF: All images loaded
        
        PROF->>DISP: Update all displays
        loop For each display
            DISP->>DISP: Select display
            DISP->>DISP: Draw image with fade effect
        end
        
        PROF->>LED: Update LED colors
        LED->>LED: Set colors for all LEDs
        
        PROF->>STOR: Save current profile to NVS
        
        PROF-->>TASK: Profile switched
        
        TASK->>USB: Send PROFILE_CHANGED event
        USB-->>USB: Notify PC software
    end
```

## Передача изображения от ПК

```mermaid
sequenceDiagram
    participant PC as PC Software
    participant USB as USB HID Raw
    participant PROTO as Protocol Handler
    participant TRANS as Image Transfer
    participant STOR as Storage
    participant DISP as Display Manager
    
    PC->>USB: START_IMAGE_TRANSFER
    Note over PC: profile=0, button=5<br/>size=10240, format=JPEG
    
    USB->>PROTO: Parse command
    PROTO->>TRANS: Init transfer
    TRANS->>TRANS: Allocate buffer (10KB)
    TRANS->>TRANS: Generate transfer_id
    TRANS-->>PROTO: transfer_id, max_chunk_size
    PROTO->>USB: Response
    USB-->>PC: transfer_id=0x1234, chunk_size=50
    
    Note over PC: Split image into chunks
    loop For each chunk (205 chunks)
        PC->>USB: IMAGE_DATA_CHUNK
        Note over PC: transfer_id, chunk_num, data[50]
        
        USB->>PROTO: Parse chunk
        PROTO->>TRANS: Store chunk
        TRANS->>TRANS: Append to buffer
        TRANS->>TRANS: Verify chunk_num sequence
        TRANS-->>PROTO: ACK
        PROTO->>USB: Response
        USB-->>PC: Status=OK, next_chunk
    end
    
    PC->>USB: END_IMAGE_TRANSFER
    Note over PC: transfer_id, total_chunks, CRC32
    
    USB->>PROTO: Parse end command
    PROTO->>TRANS: Finalize transfer
    
    TRANS->>TRANS: Calculate CRC32
    alt CRC matches
        TRANS->>STOR: Save image
        Note over STOR: /storage/images/p0_b5.jpg
        STOR-->>TRANS: Saved
        
        TRANS->>DISP: Update display 5
        DISP->>DISP: Decode JPEG
        DISP->>DISP: Draw to display
        
        TRANS-->>PROTO: Success
        PROTO->>USB: Response
        USB-->>PC: Status=OK, CRC=0x12345678
    else CRC mismatch
        TRANS-->>PROTO: Error
        PROTO->>USB: Error response
        USB-->>PC: Status=ERROR, expected_CRC
        
        Note over PC: Retry transfer
    end
```

## WiFi OTA обновление

```mermaid
sequenceDiagram
    participant PC as PC Software
    participant USB as USB HID Raw
    participant PROTO as Protocol Handler
    participant WIFI as WiFi Manager
    participant OTA as OTA Updater
    participant HTTP as HTTP Client
    participant FLASH as Flash
    participant BOOT as Bootloader
    
    Note over PC: Step 1: Setup WiFi
    PC->>USB: SET_WIFI_CREDENTIALS
    USB->>PROTO: Parse command
    PROTO->>WIFI: Store credentials
    WIFI->>WIFI: Save to NVS
    WIFI-->>PROTO: OK
    PROTO->>USB: Response
    USB-->>PC: Status=OK
    
    PC->>USB: GET_WIFI_STATUS
    USB->>PROTO: Parse command
    PROTO->>WIFI: Connect to WiFi
    WIFI->>WIFI: Start connection
    
    loop Connection attempts
        WIFI->>WIFI: Try connect
        alt Connected
            WIFI-->>PROTO: Connected, IP address
            PROTO->>USB: Response
            USB-->>PC: Status=Connected, IP=192.168.1.100
        else Failed
            WIFI-->>PROTO: Connecting...
            PROTO->>USB: Response
            USB-->>PC: Status=Connecting
        end
    end
    
    Note over PC: Step 2: Start OTA
    PC->>USB: START_OTA_UPDATE
    Note over PC: URL, size, MD5
    
    USB->>PROTO: Parse command
    PROTO->>OTA: Start OTA task
    OTA-->>PROTO: Started
    PROTO->>USB: Response
    USB-->>PC: Status=Started
    
    Note over OTA: OTA Task Running
    OTA->>FLASH: Begin OTA partition
    FLASH-->>OTA: Partition ready
    
    OTA->>HTTP: GET firmware URL
    HTTP->>HTTP: Download firmware
    
    loop Download chunks
        HTTP->>OTA: Chunk received
        OTA->>FLASH: Write chunk
        OTA->>OTA: Update progress
        
        Note over PC: Poll status
        PC->>USB: GET_OTA_STATUS
        USB->>PROTO: Parse command
        PROTO->>OTA: Get status
        OTA-->>PROTO: Downloading, 45%
        PROTO->>USB: Response
        USB-->>PC: Status=Downloading, progress=45%
    end
    
    HTTP-->>OTA: Download complete
    
    OTA->>OTA: Verify MD5
    alt MD5 valid
        OTA->>FLASH: Finalize OTA
        FLASH-->>OTA: OK
        
        OTA->>BOOT: Set boot partition
        BOOT-->>OTA: OK
        
        OTA-->>PROTO: Complete
        PROTO->>USB: Response
        USB-->>PC: Status=Complete
        
        Note over OTA: Reboot in 3 seconds
        OTA->>OTA: Delay 3s
        OTA->>OTA: esp_restart()
        
        Note over BOOT: Boot from new partition
    else MD5 invalid
        OTA->>FLASH: Abort OTA
        OTA-->>PROTO: Error
        PROTO->>USB: Error response
        USB-->>PC: Status=Error, MD5 mismatch
    end
```

## Работа Display Task

```mermaid
flowchart TB
    START([Display Task Start]) --> WAIT[Wait for update event]
    
    WAIT --> CHECK{Event received?}
    CHECK -->|Timeout| WAIT
    CHECK -->|Update event| PARSE[Parse event]
    
    PARSE --> TYPE{Update type?}
    
    TYPE -->|Single display| SINGLE[Update one display]
    TYPE -->|All displays| ALL[Update all displays]
    TYPE -->|Profile change| PROF[Profile change update]
    
    SINGLE --> SELECT1[Select display via mux]
    SELECT1 --> DECODE1[Get decoded image from cache]
    DECODE1 --> DRAW1[Draw to display via SPI]
    DRAW1 --> WAIT
    
    ALL --> LOOP_START[i = 0]
    LOOP_START --> LOOP_CHECK{i < 10?}
    LOOP_CHECK -->|Yes| SELECT2[Select display i]
    SELECT2 --> DECODE2[Get image from cache]
    DECODE2 --> DRAW2[Draw to display]
    DRAW2 --> INC[i++]
    INC --> LOOP_CHECK
    LOOP_CHECK -->|No| WAIT
    
    PROF --> LOAD[Load new profile images]
    LOAD --> UPDATE[Update all displays]
    UPDATE --> WAIT
```

## Работа Button Task

```mermaid
flowchart TB
    START([Button Task Start]) --> INIT[Initialize button states]
    
    INIT --> WAIT[Wait for button event from queue]
    
    WAIT --> EVENT{Event received?}
    EVENT -->|Timeout| WAIT
    EVENT -->|Button event| DEBOUNCE[Start debounce timer]
    
    DEBOUNCE --> WAIT_DB[Wait 10ms]
    WAIT_DB --> CONFIRM[Read GPIO to confirm]
    CONFIRM --> STILL{Still pressed?}
    
    STILL -->|Yes| PRESSED[Mark as pressed]
    PRESSED --> GET_ACTION[Get action from profile]
    GET_ACTION --> EXEC[Execute action]
    EXEC --> LED_EFFECT[Update LED]
    LED_EFFECT --> WAIT_RELEASE[Wait for release event]
    
    STILL -->|No| BOUNCE[Ignore bounce]
    BOUNCE --> WAIT
    
    WAIT_RELEASE --> RELEASE_EVENT{Release event?}
    RELEASE_EVENT -->|Yes| RELEASED[Mark as released]
    RELEASED --> RELEASE_KEYS[Release keyboard keys]
    RELEASE_KEYS --> LED_RESTORE[Restore LED]
    LED_RESTORE --> WAIT
    
    RELEASE_EVENT -->|Timeout| WAIT
```

**Примечание**: События генерируются GPIO ISR, polling не используется.

## Взаимодействие с кэшем изображений

```mermaid
sequenceDiagram
    participant DISP as Display Manager
    participant CACHE as Image Cache
    participant STOR as Storage
    participant JPEG as JPEG Decoder
    participant PSRAM as PSRAM
    
    DISP->>CACHE: Request image (p0, b5)
    
    CACHE->>CACHE: Check cache
    alt Image in cache
        CACHE->>CACHE: Update last_access time
        CACHE-->>DISP: Return cached data
    else Image not in cache
        CACHE->>CACHE: Check free slots
        
        alt Cache full
            CACHE->>CACHE: Find LRU entry
            CACHE->>PSRAM: Free old image data
            CACHE->>CACHE: Mark slot as free
        end
        
        CACHE->>STOR: Load JPEG file
        STOR-->>CACHE: JPEG data (10KB)
        
        CACHE->>PSRAM: Allocate buffer (51.2KB)
        PSRAM-->>CACHE: Buffer allocated
        
        CACHE->>JPEG: Decode JPEG to RGB565
        JPEG->>JPEG: Hardware decode
        JPEG-->>CACHE: RGB565 data
        
        CACHE->>CACHE: Store in cache entry
        CACHE->>CACHE: Set last_access time
        
        CACHE-->>DISP: Return decoded data
    end
    
    DISP->>DISP: Draw to display
```

## Обработка ошибок и восстановление

```mermaid
flowchart TB
    START([Error Detected]) --> TYPE{Error Type?}
    
    TYPE -->|USB Disconnect| USB_ERR[USB Error Handler]
    USB_ERR --> SAVE_STATE[Save current state]
    SAVE_STATE --> WAIT_USB[Wait for reconnect]
    WAIT_USB --> RECONNECT{Reconnected?}
    RECONNECT -->|Yes| RESTORE[Restore state]
    RECONNECT -->|No| WAIT_USB
    RESTORE --> RESUME[Resume operation]
    
    TYPE -->|Flash Error| FLASH_ERR[Flash Error Handler]
    FLASH_ERR --> RETRY1[Retry operation]
    RETRY1 --> SUCCESS1{Success?}
    SUCCESS1 -->|Yes| RESUME
    SUCCESS1 -->|No| COUNT1{Retry < 3?}
    COUNT1 -->|Yes| RETRY1
    COUNT1 -->|No| FALLBACK[Use default data]
    FALLBACK --> LOG1[Log error]
    LOG1 --> RESUME
    
    TYPE -->|Display Error| DISP_ERR[Display Error Handler]
    DISP_ERR --> SKIP[Skip this display]
    SKIP --> CONTINUE[Continue with others]
    CONTINUE --> LOG2[Log error]
    LOG2 --> RESUME
    
    TYPE -->|Memory Error| MEM_ERR[Memory Error Handler]
    MEM_ERR --> FREE_CACHE[Free image cache]
    FREE_CACHE --> RETRY2[Retry allocation]
    RETRY2 --> SUCCESS2{Success?}
    SUCCESS2 -->|Yes| RESUME
    SUCCESS2 -->|No| DEGRADE[Graceful degradation]
    DEGRADE --> LOG3[Log error]
    LOG3 --> RESUME
    
    TYPE -->|Critical Error| PANIC[Panic Handler]
    PANIC --> SAVE_LOG[Save log to flash]
    SAVE_LOG --> LED_BLINK[Blink red LED]
    LED_BLINK --> DELAY[Wait 5 seconds]
    DELAY --> REBOOT[esp_restart]
    
    RESUME --> END([Continue Operation])
```

## Диаграмма состояний устройства

```mermaid
stateDiagram-v2
    [*] --> Boot
    Boot --> Initializing: Power on
    
    Initializing --> Ready: Init complete
    Initializing --> Error: Init failed
    
    Ready --> Active: User interaction
    Ready --> OTA_Update: OTA command
    Ready --> Idle: No activity (30s)
    
    Active --> Ready: Action complete
    Active --> ProfileSwitch: Encoder rotated
    
    ProfileSwitch --> Loading: Load profile
    Loading --> Rendering: Profile loaded
    Rendering --> Ready: Displays updated
    
    Idle --> Ready: User interaction
    Idle --> Sleep: No activity (5min)
    
    Sleep --> Ready: Button press
    
    OTA_Update --> Connecting: WiFi connect
    Connecting --> Downloading: Connected
    Downloading --> Installing: Download complete
    Installing --> Rebooting: Install complete
    Rebooting --> Boot: Restart
    
    Connecting --> Error: Connection failed
    Downloading --> Error: Download failed
    Installing --> Error: Install failed
    
    Error --> Ready: Error handled
    Error --> [*]: Critical error
```

## Временная диаграмма критического пути

```mermaid
gantt
    title Button Press to Action Execution
    dateFormat SSS
    axisFormat %L ms
    
    section Hardware
    Button pressed           :000, 1ms
    GPIO interrupt           :001, 1ms
    
    section Software
    ISR handler              :002, 1ms
    Queue send               :003, 1ms
    Task wakeup              :004, 1ms
    Debounce delay           :005, 10ms
    Confirm press            :015, 1ms
    
    section Action
    Get action from profile  :016, 1ms
    Execute action           :017, 2ms
    Send USB HID report      :019, 1ms
    
    section Feedback
    LED flash effect         :020, 5ms
    
    Total latency: ~20ms from press to action
```

## Диаграмма использования памяти

```mermaid
pie title Flash Memory (16 MB)
    "Bootloader" : 64
    "Partition Table" : 4
    "NVS" : 16
    "OTA Data" : 8
    "Factory App" : 3072
    "OTA_0" : 3072
    "OTA_1" : 3072
    "Profiles" : 1536
    "Images" : 5120
    "Reserved" : 410
```

```mermaid
pie title SRAM (512 KB)
    "FreeRTOS Kernel" : 50
    "Task Stacks" : 40
    "Heap" : 200
    "Static Data" : 50
    "USB Buffers" : 20
    "Reserved" : 152
```

```mermaid
pie title PSRAM (8 MB)
    "Display Framebuffers" : 512
    "Image Decode Buffer" : 512
    "Image Cache" : 2048
    "Free" : 5120
```
