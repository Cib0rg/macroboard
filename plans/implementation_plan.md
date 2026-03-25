# План имплементации прошивки

## Обзор

Этот документ содержит детальный план реализации прошивки для ESP32-S3 макроклавиатуры, включая процесс инициализации, работу с профилями, OTA обновления и план тестирования.

## Процесс инициализации устройства

### Последовательность загрузки

#### 1. Bootloader Stage (ROM Bootloader)

```c
// Выполняется ROM кодом ESP32-S3
// - Инициализация базовых периферийных устройств
// - Загрузка второго уровня bootloader из flash
// - Передача управления второму bootloader
```

#### 2. Second Stage Bootloader

```c
// Выполняется кодом из flash (0x0000)
// - Инициализация flash и PSRAM
// - Чтение partition table
// - Определение активного app partition (OTA)
// - Загрузка и проверка app
// - Передача управления app_main()
```

#### 3. Application Initialization (app_main)

```c
void app_main(void) {
    esp_err_t ret;
    
    // ============================================
    // PHASE 1: Core System Initialization
    // ============================================
    
    ESP_LOGI(TAG, "=== Firmware Version %s ===", FIRMWARE_VERSION);
    ESP_LOGI(TAG, "Phase 1: Core System Init");
    
    // 1.1 Initialize NVS
    ret = nvs_flash_init();
    if (ret == ESP_ERR_NVS_NO_FREE_PAGES || 
        ret == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_LOGW(TAG, "NVS partition was truncated, erasing...");
        ESP_ERROR_CHECK(nvs_flash_erase());
        ret = nvs_flash_init();
    }
    ESP_ERROR_CHECK(ret);
    ESP_LOGI(TAG, "✓ NVS initialized");
    
    // 1.2 Check if first boot
    bool first_boot = false;
    nvs_handle_t nvs;
    ret = nvs_open("config", NVS_READONLY, &nvs);
    if (ret == ESP_OK) {
        uint8_t flag;
        ret = nvs_get_u8(nvs, "first_boot", &flag);
        if (ret == ESP_ERR_NVS_NOT_FOUND) {
            first_boot = true;
        }
        nvs_close(nvs);
    }
    
    // 1.3 Initialize SPIFFS
    esp_vfs_spiffs_conf_t spiffs_conf = {
        .base_path = "/storage",
        .partition_label = "storage",
        .max_files = 10,
        .format_if_mount_failed = true
    };
    ret = esp_vfs_spiffs_register(&spiffs_conf);
    ESP_ERROR_CHECK(ret);
    
    size_t total = 0, used = 0;
    ret = esp_spiffs_info("storage", &total, &used);
    ESP_LOGI(TAG, "✓ SPIFFS: Total=%dKB, Used=%dKB, Free=%dKB", 
             total/1024, used/1024, (total-used)/1024);
    
    // 1.4 Initialize logging system
    logger_init();
    ESP_LOGI(TAG, "✓ Logger initialized");
    
    // ============================================
    // PHASE 2: Hardware Initialization
    // ============================================
    
    ESP_LOGI(TAG, "Phase 2: Hardware Init");
    
    // 2.1 Initialize SPI bus for displays
    spi_bus_config_t spi_config = {
        .mosi_io_num = PIN_SPI_MOSI,
        .miso_io_num = -1,  // No MISO for displays
        .sclk_io_num = PIN_SPI_CLK,
        .quadwp_io_num = -1,
        .quadhd_io_num = -1,
        .max_transfer_sz = 160 * 160 * 2 + 8,  // Full screen + command
    };
    ret = spi_bus_initialize(SPI2_HOST, &spi_config, SPI_DMA_CH_AUTO);
    ESP_ERROR_CHECK(ret);
    ESP_LOGI(TAG, "✓ SPI bus initialized");
    
    // 2.2 Initialize display multiplexer
    display_mux_init();
    ESP_LOGI(TAG, "✓ Display multiplexer initialized");
    
    // 2.3 Initialize buttons
    button_driver_init();
    ESP_LOGI(TAG, "✓ Buttons initialized (10 buttons)");
    
    // 2.4 Initialize rotary encoder
    encoder_driver_init();
    ESP_LOGI(TAG, "✓ Encoder initialized");
    
    // 2.5 Initialize WS2812 LEDs
    led_driver_init();
    ESP_LOGI(TAG, "✓ WS2812 LEDs initialized (10 LEDs)");
    
    // ============================================
    // PHASE 3: USB Initialization
    // ============================================
    
    ESP_LOGI(TAG, "Phase 3: USB Init");
    
    // 3.1 Initialize TinyUSB
    tinyusb_config_t tusb_cfg = {
        .device_descriptor = &device_descriptor,
        .string_descriptor = string_descriptors,
        .external_phy = false,
    };
    ESP_ERROR_CHECK(tinyusb_driver_install(&tusb_cfg));
    ESP_LOGI(TAG, "✓ TinyUSB initialized");
    
    // 3.2 Initialize HID Keyboard
    usb_hid_keyboard_init();
    ESP_LOGI(TAG, "✓ USB HID Keyboard ready");
    
    // 3.3 Initialize HID Raw
    usb_hid_raw_init();
    ESP_LOGI(TAG, "✓ USB HID Raw ready");
    
    // 3.4 Initialize CDC (optional logging)
    usb_cdc_init();
    ESP_LOGI(TAG, "✓ USB CDC ready");
    
    // Wait for USB enumeration
    ESP_LOGI(TAG, "Waiting for USB enumeration...");
    while (!tud_mounted()) {
        vTaskDelay(pdMS_TO_TICKS(100));
    }
    ESP_LOGI(TAG, "✓ USB enumerated");
    
    // ============================================
    // PHASE 4: Profile Loading
    // ============================================
    
    ESP_LOGI(TAG, "Phase 4: Profile Loading");
    
    // 4.1 Initialize profile manager
    profile_manager_init();
    
    // 4.2 Get current profile from NVS
    uint8_t current_profile = 0;
    nvs_open("config", NVS_READONLY, &nvs);
    nvs_get_u8(nvs, "curr_profile", &current_profile);
    nvs_close(nvs);
    
    if (current_profile > 4) {
        ESP_LOGW(TAG, "Invalid profile %d, using 0", current_profile);
        current_profile = 0;
    }
    
    // 4.3 Load profile
    if (first_boot) {
        ESP_LOGI(TAG, "First boot detected, creating default profiles");
        create_default_profiles();
    }
    
    ret = profile_load(current_profile);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to load profile %d, using defaults", current_profile);
        create_default_profile(current_profile);
        profile_load(current_profile);
    }
    ESP_LOGI(TAG, "✓ Profile %d loaded", current_profile);
    
    // ============================================
    // PHASE 5: Display Initialization
    // ============================================
    
    ESP_LOGI(TAG, "Phase 5: Display Init");
    
    // 5.1 Initialize all displays
    for (int i = 0; i < 10; i++) {
        display_mux_select(i);
        display_driver_init(i);
        display_driver_clear(i, COLOR_BLACK);
        ESP_LOGI(TAG, "✓ Display %d initialized", i);
        vTaskDelay(pdMS_TO_TICKS(10));
    }
    
    // 5.2 Load and display images for current profile
    ESP_LOGI(TAG, "Loading images for profile %d", current_profile);
    for (int i = 0; i < 10; i++) {
        uint8_t* image_data;
        size_t image_size;
        
        ret = image_load_decoded(current_profile, i, &image_data, &image_size);
        if (ret == ESP_OK) {
            display_mux_select(i);
            display_driver_draw_image(i, image_data, 160, 160);
            ESP_LOGI(TAG, "✓ Display %d image loaded", i);
        } else {
            ESP_LOGW(TAG, "No image for button %d, showing default", i);
            display_show_default(i);
        }
        vTaskDelay(pdMS_TO_TICKS(50));
    }
    
    // 5.3 Set LED colors from profile
    for (int i = 0; i < 10; i++) {
        button_config_t* btn = profile_get_button_config(i);
        led_set_color(i, btn->led_r, btn->led_g, btn->led_b, btn->led_brightness);
    }
    led_update();
    ESP_LOGI(TAG, "✓ LEDs configured");
    
    // ============================================
    // PHASE 6: Task Creation
    // ============================================
    
    ESP_LOGI(TAG, "Phase 6: Creating FreeRTOS Tasks");
    
    // 6.1 Create queues and synchronization objects
    button_event_queue = xQueueCreate(10, sizeof(button_event_t));
    protocol_cmd_queue = xQueueCreate(5, sizeof(protocol_cmd_t));
    encoder_event_queue = xQueueCreate(5, sizeof(encoder_event_t));
    display_update_queue = xQueueCreate(10, sizeof(display_update_t));
    
    spi_mutex = xSemaphoreCreateMutex();
    flash_mutex = xSemaphoreCreateMutex();
    profile_mutex = xSemaphoreCreateMutex();
    usb_tx_ready = xSemaphoreCreateBinary();
    
    system_events = xEventGroupCreate();
    xEventGroupSetBits(system_events, EVENT_USB_CONFIGURED);
    
    // 6.2 Create tasks
    xTaskCreatePinnedToCore(usb_rx_task, "usb_rx", 4096, NULL, 
                            TASK_PRIORITY_USB_RX, NULL, 0);
    ESP_LOGI(TAG, "✓ USB RX task created");
    
    xTaskCreatePinnedToCore(button_task, "button", 3072, NULL, 
                            TASK_PRIORITY_BUTTON, NULL, 1);
    ESP_LOGI(TAG, "✓ Button task created");
    
    xTaskCreatePinnedToCore(encoder_task, "encoder", 2048, NULL, 
                            TASK_PRIORITY_ENCODER, NULL, 1);
    ESP_LOGI(TAG, "✓ Encoder task created");
    
    xTaskCreatePinnedToCore(protocol_task, "protocol", 8192, NULL, 
                            TASK_PRIORITY_PROTOCOL, NULL, 0);
    ESP_LOGI(TAG, "✓ Protocol task created");
    
    xTaskCreatePinnedToCore(display_task, "display", 4096, NULL, 
                            TASK_PRIORITY_DISPLAY, NULL, 0);
    ESP_LOGI(TAG, "✓ Display task created");
    
    xTaskCreatePinnedToCore(led_task, "led", 2048, NULL, 
                            TASK_PRIORITY_LED, NULL, 1);
    ESP_LOGI(TAG, "✓ LED task created");
    
    // Monitor task (optional, for debugging)
    #ifdef CONFIG_ENABLE_TASK_MONITOR
    xTaskCreatePinnedToCore(task_monitor, "monitor", 2048, NULL, 
                            TASK_PRIORITY_MONITOR, NULL, 0);
    ESP_LOGI(TAG, "✓ Monitor task created");
    #endif
    
    // ============================================
    // PHASE 7: Final Setup
    // ============================================
    
    ESP_LOGI(TAG, "Phase 7: Final Setup");
    
    // 7.1 Mark first boot as complete
    if (first_boot) {
        nvs_open("config", NVS_READWRITE, &nvs);
        nvs_set_u8(nvs, "first_boot", 0);
        nvs_commit(nvs);
        nvs_close(nvs);
    }
    
    // 7.2 Send device ready event
    usb_hid_raw_send_event(EVENT_DEVICE_READY, NULL, 0);
    
    ESP_LOGI(TAG, "=== System Ready ===");
    ESP_LOGI(TAG, "Free heap: %d bytes", esp_get_free_heap_size());
    ESP_LOGI(TAG, "Free PSRAM: %d bytes", heap_caps_get_free_size(MALLOC_CAP_SPIRAM));
    
    // Main task can now delete itself or enter idle loop
    vTaskDelete(NULL);
}
```

### Создание профилей по умолчанию

```c
void create_default_profiles(void) {
    for (int p = 0; p < 5; p++) {
        profile_t profile;
        memset(&profile, 0, sizeof(profile));
        
        profile.profile_id = p;
        snprintf(profile.name, sizeof(profile.name), "Profile %d", p + 1);
        
        // Configure buttons with default actions
        for (int b = 0; b < 10; b++) {
            button_config_t* btn = &profile.buttons[b];
            btn->button_id = b;
            btn->action_type = ACTION_TYPE_KEYBOARD;
            
            // Default: F13-F22 keys
            btn->action_data[0] = 0;  // No modifiers
            btn->action_data[1] = HID_KEY_F13 + b;
            btn->action_data_len = 2;
            
            // Default LED colors (rainbow)
            uint8_t hue = (b * 255) / 10;
            hsv_to_rgb(hue, 255, 255, &btn->led_r, &btn->led_g, &btn->led_b);
            btn->led_brightness = 128;
            btn->led_effect = LED_EFFECT_STATIC;
        }
        
        // Save profile
        profile_save(p, &profile);
        
        // Create default images (solid colors or icons)
        for (int b = 0; b < 10; b++) {
            create_default_image(p, b);
        }
    }
}
```

## Работа с профилями

### Структура данных профиля

```c
typedef enum {
    ACTION_TYPE_NONE = 0x00,
    ACTION_TYPE_KEYBOARD = 0x01,
    ACTION_TYPE_CUSTOM_HID = 0x02,
    ACTION_TYPE_PROFILE_SWITCH = 0x03,
} action_type_t;

typedef struct {
    uint8_t button_id;
    action_type_t action_type;
    uint16_t action_data_len;
    uint8_t action_data[100];
    
    // LED configuration
    uint8_t led_r;
    uint8_t led_g;
    uint8_t led_b;
    uint8_t led_brightness;
    uint8_t led_effect;
    
    // Image metadata
    uint32_t image_offset;
    uint32_t image_size;
    uint8_t image_format;
} button_config_t;

typedef struct {
    uint8_t profile_id;
    char name[32];
    button_config_t buttons[10];
    uint32_t crc32;
} profile_t;
```

### Profile Manager

```c
// Глобальное состояние
static profile_t current_profile;
static uint8_t current_profile_id = 0;
static SemaphoreHandle_t profile_mutex;

esp_err_t profile_manager_init(void) {
    profile_mutex = xSemaphoreCreateMutex();
    return ESP_OK;
}

esp_err_t profile_switch(uint8_t new_profile_id) {
    if (new_profile_id > 4) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (new_profile_id == current_profile_id) {
        return ESP_OK;  // Already on this profile
    }
    
    ESP_LOGI(TAG, "Switching from profile %d to %d", 
             current_profile_id, new_profile_id);
    
    // Lock profile
    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    
    uint8_t old_profile_id = current_profile_id;
    
    // Load new profile
    esp_err_t ret = profile_load(new_profile_id);
    if (ret != ESP_OK) {
        xSemaphoreGive(profile_mutex);
        return ret;
    }
    
    current_profile_id = new_profile_id;
    
    // Update displays
    for (int i = 0; i < 10; i++) {
        uint8_t* image_data;
        size_t image_size;
        
        ret = image_load_decoded(new_profile_id, i, &image_data, &image_size);
        if (ret == ESP_OK) {
            display_update_t update = {
                .display_id = i,
                .image_data = image_data,
                .width = 160,
                .height = 160,
            };
            xQueueSend(display_update_queue, &update, portMAX_DELAY);
        }
    }
    
    // Update LEDs
    for (int i = 0; i < 10; i++) {
        button_config_t* btn = &current_profile.buttons[i];
        led_set_color(i, btn->led_r, btn->led_g, btn->led_b, btn->led_brightness);
    }
    led_update();
    
    // Save current profile to NVS
    nvs_handle_t nvs;
    nvs_open("config", NVS_READWRITE, &nvs);
    nvs_set_u8(nvs, "curr_profile", new_profile_id);
    nvs_commit(nvs);
    nvs_close(nvs);
    
    xSemaphoreGive(profile_mutex);
    
    // Send profile changed event
    usb_hid_raw_send_profile_changed(old_profile_id, new_profile_id, 
                                      CHANGE_REASON_ENCODER);
    
    ESP_LOGI(TAG, "Profile switched successfully");
    
    return ESP_OK;
}

button_config_t* profile_get_button_config(uint8_t button_id) {
    if (button_id >= 10) {
        return NULL;
    }
    return &current_profile.buttons[button_id];
}

uint8_t profile_get_current_id(void) {
    return current_profile_id;
}
```

### Переключение через энкодер

```c
void encoder_task(void* arg) {
    encoder_event_t event;
    int16_t step_accumulator = 0;
    const int16_t STEPS_PER_PROFILE = 4;  // 4 шага энкодера = 1 профиль
    
    while (1) {
        if (xQueueReceive(encoder_event_queue, &event, portMAX_DELAY)) {
            
            if (event.type == ENCODER_ROTATED) {
                // Накопление шагов
                if (event.direction == ENCODER_CW) {
                    step_accumulator++;
                } else {
                    step_accumulator--;
                }
                
                ESP_LOGD(TAG, "Encoder steps: %d", step_accumulator);
                
                // Проверка достижения порога
                if (abs(step_accumulator) >= STEPS_PER_PROFILE) {
                    uint8_t current = profile_get_current_id();
                    uint8_t next;
                    
                    if (step_accumulator > 0) {
                        // По часовой стрелке - следующий профиль
                        next = (current + 1) % 5;
                    } else {
                        // Против часовой - предыдущий профиль
                        next = (current == 0) ? 4 : (current - 1);
                    }
                    
                    // Переключение профиля
                    esp_err_t ret = profile_switch(next);
                    if (ret == ESP_OK) {
                        // Сброс аккумулятора
                        step_accumulator = 0;
                        
                        // Визуальная обратная связь через LED
                        // (цвета уже обновлены в profile_switch)
                    }
                }
            }
            else if (event.type == ENCODER_BUTTON_PRESSED) {
                // Кнопка энкодера - дополнительная функция
                if (event.press_type == PRESS_SHORT) {
                    // Короткое нажатие - сброс в профиль 0
                    profile_switch(0);
                }
                else if (event.press_type == PRESS_LONG) {
                    // Длинное нажатие - специальная функция
                    // Например, включение/выключение WiFi
                    wifi_toggle();
                }
            }
        }
    }
}
```

## WiFi OTA обновление

### WiFi Manager

```c
static bool wifi_enabled = false;
static bool wifi_connected = false;
static char wifi_ssid[32] = {0};
static char wifi_password[64] = {0};

esp_err_t wifi_manager_init(void) {
    // Load credentials from NVS
    nvs_handle_t nvs;
    esp_err_t ret = nvs_open("wifi", NVS_READONLY, &nvs);
    if (ret == ESP_OK) {
        size_t len;
        
        len = sizeof(wifi_ssid);
        nvs_get_str(nvs, "ssid", wifi_ssid, &len);
        
        len = sizeof(wifi_password);
        nvs_get_str(nvs, "password", wifi_password, &len);
        
        uint8_t enabled;
        if (nvs_get_u8(nvs, "wifi_enabled", &enabled) == ESP_OK) {
            wifi_enabled = enabled;
        }
        
        nvs_close(nvs);
    }
    
    // Initialize WiFi
    ESP_ERROR_CHECK(esp_netif_init());
    ESP_ERROR_CHECK(esp_event_loop_create_default());
    esp_netif_create_default_wifi_sta();
    
    wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
    ESP_ERROR_CHECK(esp_wifi_init(&cfg));
    
    // Register event handlers
    ESP_ERROR_CHECK(esp_event_handler_register(WIFI_EVENT, ESP_EVENT_ANY_ID, 
                                                &wifi_event_handler, NULL));
    ESP_ERROR_CHECK(esp_event_handler_register(IP_EVENT, IP_EVENT_STA_GOT_IP, 
                                                &wifi_event_handler, NULL));
    
    return ESP_OK;
}

esp_err_t wifi_connect(void) {
    if (strlen(wifi_ssid) == 0) {
        ESP_LOGE(TAG, "WiFi SSID not configured");
        return ESP_ERR_INVALID_STATE;
    }
    
    wifi_config_t wifi_config = {
        .sta = {
            .threshold.authmode = WIFI_AUTH_WPA2_PSK,
        },
    };
    
    strncpy((char*)wifi_config.sta.ssid, wifi_ssid, sizeof(wifi_config.sta.ssid));
    strncpy((char*)wifi_config.sta.password, wifi_password, sizeof(wifi_config.sta.password));
    
    ESP_ERROR_CHECK(esp_wifi_set_mode(WIFI_MODE_STA));
    ESP_ERROR_CHECK(esp_wifi_set_config(WIFI_IF_STA, &wifi_config));
    ESP_ERROR_CHECK(esp_wifi_start());
    
    ESP_LOGI(TAG, "Connecting to WiFi SSID: %s", wifi_ssid);
    
    esp_err_t ret = esp_wifi_connect();
    
    return ret;
}

static void wifi_event_handler(void* arg, esp_event_base_t event_base,
                               int32_t event_id, void* event_data) {
    if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_START) {
        esp_wifi_connect();
    }
    else if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_DISCONNECTED) {
        wifi_connected = false;
        xEventGroupClearBits(system_events, EVENT_WIFI_CONNECTED);
        ESP_LOGI(TAG, "WiFi disconnected, retrying...");
        esp_wifi_connect();
    }
    else if (event_base == IP_EVENT && event_id == IP_EVENT_STA_GOT_IP) {
        ip_event_got_ip_t* event = (ip_event_got_ip_t*) event_data;
        ESP_LOGI(TAG, "WiFi connected, IP: " IPSTR, IP2STR(&event->ip_info.ip));
        wifi_connected = true;
        xEventGroupSetBits(system_events, EVENT_WIFI_CONNECTED);
    }
}
```

### OTA Updater

> **Примечание**: Код ниже показывает детальную имплементацию для понимания процесса.
> В реальности рекомендуется использовать готовый `esp_https_ota()` API из ESP-IDF,
> который делает всё это автоматически. См. [`esp_idf_components.md`](esp_idf_components.md).

```c
typedef struct {
    char url[128];
    uint32_t firmware_size;
    uint8_t md5[16];
    uint8_t progress;
    ota_status_t status;
} ota_context_t;

static ota_context_t ota_ctx;

esp_err_t ota_start_update(const char* url, uint32_t size, const uint8_t* md5) {
    // Check if WiFi is connected
    if (!wifi_connected) {
        ESP_LOGE(TAG, "WiFi not connected");
        return ESP_ERR_INVALID_STATE;
    }
    
    // Check if OTA already in progress
    if (ota_ctx.status == OTA_STATUS_DOWNLOADING || 
        ota_ctx.status == OTA_STATUS_INSTALLING) {
        ESP_LOGE(TAG, "OTA already in progress");
        return ESP_ERR_INVALID_STATE;
    }
    
    // Initialize OTA context
    memset(&ota_ctx, 0, sizeof(ota_ctx));
    strncpy(ota_ctx.url, url, sizeof(ota_ctx.url) - 1);
    ota_ctx.firmware_size = size;
    memcpy(ota_ctx.md5, md5, 16);
    ota_ctx.status = OTA_STATUS_IDLE;
    
    // Create OTA task
    xTaskCreate(ota_task, "ota", 8192, NULL, TASK_PRIORITY_OTA, NULL);
    
    return ESP_OK;
}

void ota_task(void* arg) {
    esp_err_t ret;
    esp_ota_handle_t ota_handle = 0;
    const esp_partition_t* update_partition = NULL;
    
    ESP_LOGI(TAG, "Starting OTA update from: %s", ota_ctx.url);
    ota_ctx.status = OTA_STATUS_DOWNLOADING;
    xEventGroupSetBits(system_events, EVENT_OTA_IN_PROGRESS);
    
    // Get next OTA partition
    update_partition = esp_ota_get_next_update_partition(NULL);
    if (update_partition == NULL) {
        ESP_LOGE(TAG, "No OTA partition found");
        goto ota_error;
    }
    
    ESP_LOGI(TAG, "Writing to partition: %s at 0x%x", 
             update_partition->label, update_partition->address);
    
    // Begin OTA
    ret = esp_ota_begin(update_partition, OTA_SIZE_UNKNOWN, &ota_handle);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "esp_ota_begin failed: %s", esp_err_to_name(ret));
        goto ota_error;
    }
    
    // HTTP client configuration
    esp_http_client_config_t http_config = {
        .url = ota_ctx.url,
        .timeout_ms = 5000,
        .buffer_size = 4096,
    };
    
    esp_http_client_handle_t client = esp_http_client_init(&http_config);
    if (client == NULL) {
        ESP_LOGE(TAG, "Failed to initialize HTTP client");
        goto ota_error;
    }
    
    ret = esp_http_client_open(client, 0);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to open HTTP connection");
        esp_http_client_cleanup(client);
        goto ota_error;
    }
    
    int content_length = esp_http_client_fetch_headers(client);
    ESP_LOGI(TAG, "Content length: %d bytes", content_length);
    
    // Download and write firmware
    uint8_t buffer[4096];
    uint32_t bytes_downloaded = 0;
    struct MD5Context md5_ctx;
    MD5Init(&md5_ctx);
    
    while (1) {
        int data_read = esp_http_client_read(client, (char*)buffer, sizeof(buffer));
        if (data_read < 0) {
            ESP_LOGE(TAG, "HTTP read error");
            break;
        }
        else if (data_read == 0) {
            ESP_LOGI(TAG, "Download complete");
            break;
        }
        
        // Write to OTA partition
        ret = esp_