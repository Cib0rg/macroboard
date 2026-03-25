# Использование готовых компонентов ESP-IDF

## Обзор

ESP-IDF предоставляет множество готовых компонентов, которые значительно упрощают разработку. Вместо имплементации с нуля, мы используем готовые API.

## 1. Bootloader и OTA - ГОТОВО

### Bootloader
ESP-IDF автоматически создает bootloader. Нам нужно только:

**partitions.csv:**
```csv
# Name,     Type, SubType,  Offset,   Size
nvs,        data, nvs,      0x9000,   0x4000
otadata,    data, ota,      0xd000,   0x2000
phy_init,   data, phy,      0xf000,   0x1000
factory,    app,  factory,  0x10000,  0x300000
ota_0,      app,  ota_0,    0x310000, 0x300000
ota_1,      app,  ota_1,    0x610000, 0x300000
storage,    data, spiffs,   0x910000, 0x6F0000
```

**sdkconfig:**
```ini
CONFIG_BOOTLOADER_APP_ROLLBACK_ENABLE=y
CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK=n
```

Всё! Bootloader готов.

### OTA - Упрощенная версия

**Вместо нашей сложной имплементации:**
```c
// Было: 200+ строк кода с HTTP client, MD5, chunked download...

// Стало: 10 строк!
esp_err_t ota_start_update(const char* url) {
    esp_http_client_config_t config = {
        .url = url,
        .cert_pem = NULL,  // или TLS сертификат
        .timeout_ms = 5000,
        .keep_alive_enable = true,
    };
    
    // Вся магия OTA в одной функции
    esp_err_t ret = esp_https_ota(&config);
    
    if (ret == ESP_OK) {
        ESP_LOGI(TAG, "OTA successful, rebooting...");
        vTaskDelay(pdMS_TO_TICKS(1000));
        esp_restart();
    } else {
        ESP_LOGE(TAG, "OTA failed: %s", esp_err_to_name(ret));
    }
    
    return ret;
}
```

**С прогрессом (чуть сложнее):**
```c
esp_err_t ota_with_progress(const char* url) {
    esp_http_client_config_t config = {
        .url = url,
    };
    
    esp_https_ota_config_t ota_config = {
        .http_config = &config,
    };
    
    esp_https_ota_handle_t ota_handle = NULL;
    esp_err_t ret = esp_https_ota_begin(&ota_config, &ota_handle);
    if (ret != ESP_OK) {
        return ret;
    }
    
    while (1) {
        ret = esp_https_ota_perform(ota_handle);
        if (ret != ESP_ERR_HTTPS_OTA_IN_PROGRESS) {
            break;
        }
        
        // Получаем прогресс
        int total = esp_https_ota_get_image_size(ota_handle);
        int downloaded = esp_https_ota_get_image_len_read(ota_handle);
        int progress = (downloaded * 100) / total;
        
        ESP_LOGI(TAG, "OTA progress: %d%%", progress);
        
        // Обновляем LED или отправляем в USB
        led_ota_progress(progress);
        usb_send_ota_progress(progress);
    }
    
    if (ret == ESP_OK) {
        ret = esp_https_ota_finish(ota_handle);
        if (ret == ESP_OK) {
            esp_restart();
        }
    }
    
    return ret;
}
```

**Rollback автоматический:**
```c
void app_main(void) {
    // Проверка после OTA
    const esp_partition_t *running = esp_ota_get_running_partition();
    esp_ota_img_states_t ota_state;
    
    if (esp_ota_get_state_partition(running, &ota_state) == ESP_OK) {
        if (ota_state == ESP_OTA_IMG_PENDING_VERIFY) {
            // Первый запуск после OTA
            ESP_LOGI(TAG, "First boot after OTA");
            
            // Проверяем работоспособность
            bool system_ok = check_system();
            
            if (system_ok) {
                ESP_LOGI(TAG, "OTA verified, marking as valid");
                esp_ota_mark_app_valid_cancel_rollback();
            } else {
                ESP_LOGE(TAG, "System check failed, rolling back");
                esp_ota_mark_app_invalid_rollback_and_reboot();
            }
        }
    }
    
    // Остальная инициализация...
}
```

## 2. WiFi - ГОТОВО

**Простая версия:**
```c
#include "esp_wifi.h"
#include "esp_event.h"

static void wifi_event_handler(void* arg, esp_event_base_t event_base,
                               int32_t event_id, void* event_data) {
    if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_START) {
        esp_wifi_connect();
    } else if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_DISCONNECTED) {
        esp_wifi_connect();
    } else if (event_base == IP_EVENT && event_id == IP_EVENT_STA_GOT_IP) {
        ip_event_got_ip_t* event = (ip_event_got_ip_t*) event_data;
        ESP_LOGI(TAG, "Got IP: " IPSTR, IP2STR(&event->ip_info.ip));
    }
}

esp_err_t wifi_init_sta(const char* ssid, const char* password) {
    ESP_ERROR_CHECK(esp_netif_init());
    ESP_ERROR_CHECK(esp_event_loop_create_default());
    esp_netif_create_default_wifi_sta();
    
    wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
    ESP_ERROR_CHECK(esp_wifi_init(&cfg));
    
    ESP_ERROR_CHECK(esp_event_handler_register(WIFI_EVENT, ESP_EVENT_ANY_ID, 
                                                &wifi_event_handler, NULL));
    ESP_ERROR_CHECK(esp_event_handler_register(IP_EVENT, IP_EVENT_STA_GOT_IP, 
                                                &wifi_event_handler, NULL));
    
    wifi_config_t wifi_config = {
        .sta = {
            .ssid = "",
            .password = "",
        },
    };
    strncpy((char*)wifi_config.sta.ssid, ssid, sizeof(wifi_config.sta.ssid));
    strncpy((char*)wifi_config.sta.password, password, sizeof(wifi_config.sta.password));
    
    ESP_ERROR_CHECK(esp_wifi_set_mode(WIFI_MODE_STA));
    ESP_ERROR_CHECK(esp_wifi_set_config(WIFI_IF_STA, &wifi_config));
    ESP_ERROR_CHECK(esp_wifi_start());
    
    return ESP_OK;
}
```

## 3. NVS - ГОТОВО

**Простое использование:**
```c
#include "nvs_flash.h"
#include "nvs.h"

// Инициализация
esp_err_t storage_init(void) {
    esp_err_t ret = nvs_flash_init();
    if (ret == ESP_ERR_NVS_NO_FREE_PAGES || ret == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_ERROR_CHECK(nvs_flash_erase());
        ret = nvs_flash_init();
    }
    return ret;
}

// Сохранение
esp_err_t save_current_profile(uint8_t profile_id) {
    nvs_handle_t nvs;
    esp_err_t ret = nvs_open("config", NVS_READWRITE, &nvs);
    if (ret == ESP_OK) {
        ret = nvs_set_u8(nvs, "curr_profile", profile_id);
        nvs_commit(nvs);
        nvs_close(nvs);
    }
    return ret;
}

// Загрузка
esp_err_t load_current_profile(uint8_t* profile_id) {
    nvs_handle_t nvs;
    esp_err_t ret = nvs_open("config", NVS_READONLY, &nvs);
    if (ret == ESP_OK) {
        ret = nvs_get_u8(nvs, "curr_profile", profile_id);
        nvs_close(nvs);
    }
    return ret;
}
```

## 4. SPIFFS - ГОТОВО

**Простое использование:**
```c
#include "esp_spiffs.h"

esp_err_t spiffs_init(void) {
    esp_vfs_spiffs_conf_t conf = {
        .base_path = "/storage",
        .partition_label = "storage",
        .max_files = 10,
        .format_if_mount_failed = true
    };
    
    esp_err_t ret = esp_vfs_spiffs_register(&conf);
    if (ret != ESP_OK) {
        return ret;
    }
    
    size_t total = 0, used = 0;
    ret = esp_spiffs_info("storage", &total, &used);
    ESP_LOGI(TAG, "SPIFFS: %d KB total, %d KB used", total/1024, used/1024);
    
    return ESP_OK;
}

// Дальше работаем как с обычными файлами
FILE* f = fopen("/storage/profile_0.bin", "wb");
fwrite(data, 1, size, f);
fclose(f);
```

## 5. USB TinyUSB - ГОТОВО (с примерами)

**HID Keyboard:**
```c
#include "tinyusb.h"
#include "class/hid/hid_device.h"

// Дескрипторы есть в примерах ESP-IDF
// examples/peripherals/usb/device/tusb_hid/

void send_keyboard_report(uint8_t modifier, uint8_t keycode) {
    uint8_t report[8] = {modifier, 0, keycode, 0, 0, 0, 0, 0};
    tud_hid_keyboard_report(REPORT_ID_KEYBOARD, modifier, &keycode);
}
```

**HID Raw:**
```c
void send_raw_report(uint8_t* data, size_t len) {
    tud_hid_report(REPORT_ID_RAW, data, len);
}

// Callback для приема
uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id,
                                hid_report_type_t report_type, uint8_t* buffer, uint16_t reqlen) {
    // Обработка входящих данных
    protocol_handle_packet(buffer, reqlen);
    return reqlen;
}
```

## 6. JPEG Decoder - ГОТОВО

ESP32-S3 имеет аппаратный JPEG декодер!

```c
#include "esp_jpeg_dec.h"

esp_err_t decode_jpeg_to_rgb565(const uint8_t* jpeg_data, size_t jpeg_size,
                                 uint8_t* rgb565_out, int width, int height) {
    jpeg_dec_config_t config = {
        .output_type = JPEG_RAW_TYPE_RGB565_BE,
    };
    
    jpeg_dec_handle_t jpeg_dec;
    jpeg_dec_open(&config, &jpeg_dec);
    
    jpeg_dec_header_info_t header;
    jpeg_dec_parse_header(jpeg_dec, jpeg_data, jpeg_size, &header);
    
    jpeg_dec_io_t decode_io = {
        .inbuf = jpeg_data,
        .inbuf_len = jpeg_size,
        .outbuf = rgb565_out,
    };
    
    esp_err_t ret = jpeg_dec_process(jpeg_dec, &decode_io);
    
    jpeg_dec_close(jpeg_dec);
    return ret;
}
```

## 7. FreeRTOS - ГОТОВО

Все FreeRTOS API доступны:

```c
// Задачи
xTaskCreate(my_task, "my_task", 4096, NULL, 5, NULL);

// Очереди
QueueHandle_t queue = xQueueCreate(10, sizeof(event_t));
xQueueSend(queue, &event, portMAX_DELAY);

// Семафоры
SemaphoreHandle_t mutex = xSemaphoreCreateMutex();
xSemaphoreTake(mutex, portMAX_DELAY);

// Event groups
EventGroupHandle_t events = xEventGroupCreate();
xEventGroupSetBits(events, BIT_0);
```

## Что РЕАЛЬНО нужно написать

### 1. Display Driver (GC9A01)
Нет готового в ESP-IDF, но есть примеры для похожих дисплеев (ST7789).
Можно адаптировать.

### 2. Display Multiplexer
Специфично для нашей схемы - 4 GPIO для выбора дисплея.

### 3. Protocol Handler
Наш специфичный протокол - парсинг 64-байтовых пакетов.

### 4. Profile Manager
Наша логика профилей и действий кнопок.

### 5. Application Logic
Связь всех компонентов воедино.

## Упрощенная структура проекта

```
firmware/
├── main/
│   ├── main.c                  # app_main() - точка входа
│   ├── config.h                # Конфигурация
│   │
│   ├── hardware/
│   │   ├── gc9a01.c/h         # Драйвер дисплея (адаптируем из примеров)
│   │   ├── display_mux.c/h    # Мультиплексор (наш код)
│   │   ├── buttons.c/h        # GPIO + debounce (простой код)
│   │   ├── encoder.c/h        # Encoder logic (простой код)
│   │   └── leds.c/h           # WS2812 через RMT (есть примеры)
│   │
│   ├── protocol/
│   │   ├── protocol.c/h       # Наш протокол
│   │   └── commands.c/h       # Обработчики команд
│   │
│   ├── profile/
│   │   ├── profile.c/h        # Profile manager
│   │   └── actions.c/h        # Button actions
│   │
│   └── usb/
│       ├── usb_hid.c/h        # HID wrapper (используем TinyUSB)
│       └── descriptors.c      # USB дескрипторы
│
├── components/                 # Внешние компоненты (если нужны)
├── CMakeLists.txt
├── sdkconfig                   # Конфигурация ESP-IDF
└── partitions.csv
```

## Итого: Что используем готовое

✅ **Bootloader** - ESP-IDF  
✅ **OTA** - `esp_https_ota()` API  
✅ **WiFi** - `esp_wifi` API  
✅ **NVS** - `nvs` API  
✅ **SPIFFS** - `esp_spiffs` API  
✅ **USB** - TinyUSB (интегрирован в ESP-IDF)  
✅ **JPEG** - Аппаратный декодер ESP32-S3  
✅ **FreeRTOS** - Встроен в ESP-IDF  
✅ **HTTP Client** - `esp_http_client` API  
✅ **Event Loop** - `esp_event` API  

## Что пишем сами

❌ **GC9A01 driver** - адаптируем из примеров  
❌ **Display multiplexer** - простая логика GPIO  
❌ **Protocol handler** - наш протокол  
❌ **Profile manager** - наша логика  
❌ **Application glue** - связываем всё вместе  

## Вывод

Реально писать нужно **гораздо меньше кода**, чем показано в первоначальном плане. ESP-IDF предоставляет 80% функциональности готовой. Нам нужно только:

1. Написать драйвер GC9A01 (или адаптировать существующий)
2. Реализовать наш протокол обмена
3. Написать логику профилей
4. Связать всё вместе в `app_main()`

Остальное - готовые компоненты ESP-IDF!
