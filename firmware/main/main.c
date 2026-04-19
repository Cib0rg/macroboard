/**
 * @file main.c
 * @brief Main entry point for ESP32-S3 Macro Keyboard firmware
 */

#include "common.h"
#include "config.h"
#include "tinyusb.h"
#include "utils/logger.h"
#include "hardware/gc9a01.h"
#include "hardware/display_mux.h"
#include "hardware/buttons.h"
#include "hardware/encoder.h"
#include "hardware/leds.h"
#include "usb/usb_hid_keyboard.h"
#include "usb/usb_vendor.h"
#include "protocol/protocol_handler.h"
#include "storage/nvs_manager.h"
#include "storage/profile_storage.h"
#include "storage/image_storage.h"
#include "profile/profile_manager.h"

static const char* TAG = "MAIN";

// External USB descriptors (defined in usb_descriptors.c)
extern const tusb_desc_device_t desc_device;
extern const uint8_t desc_configuration[];
extern const char *desc_string_arr[];
extern const int desc_string_count;

void app_main(void) {
    esp_err_t ret;
    
    ESP_LOGI(TAG, "===========================================");
    ESP_LOGI(TAG, "  ESP32-S3 Macro Keyboard Firmware v%s", FIRMWARE_VERSION);
    ESP_LOGI(TAG, "===========================================");
    
    // ============================================
    // PHASE 1: Core System Initialization
    // ============================================
    
    ESP_LOGI(TAG, "Phase 1: Core System Init");
    
    // 1.1 Initialize logger
    logger_init();
    ESP_LOGI(TAG, "✓ Logger initialized");
    
    // 1.2 Initialize NVS
    ret = nvs_manager_init();
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to initialize NVS: %s", esp_err_to_name(ret));
        return;
    }
    ESP_LOGI(TAG, "✓ NVS initialized");
    
    // 1.3 Initialize SPIFFS
    ret = profile_storage_init();
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to initialize SPIFFS: %s", esp_err_to_name(ret));
        return;
    }
    ESP_LOGI(TAG, "✓ SPIFFS initialized");
    
    // ============================================
    // PHASE 2: Hardware Initialization
    // ============================================
    
    ESP_LOGI(TAG, "Phase 2: Hardware Init");
    
    // 2.1 Initialize display multiplexer
    ret = display_mux_init();
    ESP_ERROR_CHECK(ret);
    ESP_LOGI(TAG, "✓ Display multiplexer initialized");
    
    // 2.2 Initialize SPI and GC9A01 displays
    ret = gc9a01_init();
    ESP_ERROR_CHECK(ret);
    ESP_LOGI(TAG, "✓ SPI and display driver initialized");
    
    // 2.2.1 Enable display backlight
    gc9a01_set_backlight(true);
    ESP_LOGI(TAG, "✓ Display backlight enabled");
    
    // 2.3 Initialize all displays
    for (int i = 0; i < NUM_DISPLAYS; i++) {
        ret = gc9a01_init_display(i);
        if (ret == ESP_OK) {
            gc9a01_clear(i, COLOR_BLACK);
            ESP_LOGI(TAG, "✓ Display %d initialized", i);
        }
        vTaskDelay(pdMS_TO_TICKS(10));
    }
    
    // 2.4 Initialize buttons
    ret = buttons_init();
    ESP_ERROR_CHECK(ret);
    ESP_LOGI(TAG, "✓ Buttons initialized (%d buttons)", NUM_BUTTONS);
    
    // 2.5 Initialize rotary encoder
    ret = encoder_init();
    ESP_ERROR_CHECK(ret);
    ESP_LOGI(TAG, "✓ Encoder initialized");
    
    // 2.6 Initialize WS2812 LEDs
    ret = leds_init();
    ESP_ERROR_CHECK(ret);
    ESP_LOGI(TAG, "✓ WS2812 LEDs initialized (%d LEDs)", NUM_LEDS);
    
    // ============================================
    // PHASE 3: USB Initialization
    // ============================================
    
    ESP_LOGI(TAG, "Phase 3: USB Init");
    
    // 3.1 Initialize TinyUSB — composite device (HID + Vendor)
    // Device descriptor, config descriptor, and string descriptors are
    // provided via TinyUSB callbacks in usb_descriptors.c.
    // We also pass them here for the esp_tinyusb wrapper.
    tinyusb_config_t tusb_cfg = {
        .port = TINYUSB_PORT_FULL_SPEED_0,
        .task = {
            .size = 4096,
            .priority = 5,
            .xCoreID = 0,
        },
        .descriptor = {
            .device = &desc_device,
            .string = desc_string_arr,
            .string_count = desc_string_count,
            .full_speed_config = desc_configuration,
            .high_speed_config = NULL,
        },
    };

    ret = tinyusb_driver_install(&tusb_cfg);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to initialize TinyUSB: %s", esp_err_to_name(ret));
        ESP_LOGI(TAG, "Continuing without USB...");
    } else {
        ESP_LOGI(TAG, "✓ TinyUSB driver installed (composite: HID + Vendor)");
    }
    
    // 3.2 Initialize USB HID Keyboard interface
    usb_hid_keyboard_init();
    ESP_LOGI(TAG, "✓ USB HID Keyboard ready");
    
    // 3.3 Initialize USB Vendor interface
    usb_vendor_init();
    ESP_LOGI(TAG, "✓ USB Vendor interface ready");
    
    // Wait for USB enumeration
    ESP_LOGI(TAG, "Waiting for USB enumeration...");
    int timeout = 50; // 5 seconds
    while (!tud_mounted() && timeout > 0) {
        vTaskDelay(pdMS_TO_TICKS(100));
        timeout--;
    }
    
    if (tud_mounted()) {
        ESP_LOGI(TAG, "✓ USB enumerated");
    } else {
        ESP_LOGW(TAG, "USB enumeration timeout (device may still work)");
    }
    
    // ============================================
    // PHASE 4: Protocol Initialization
    // ============================================
    
    ESP_LOGI(TAG, "Phase 4: Protocol Init");
    
    ret = protocol_handler_init();
    ESP_ERROR_CHECK(ret);
    ESP_LOGI(TAG, "✓ Protocol handler initialized");
    
    // ============================================
    // PHASE 5: Profile Loading
    // ============================================
    
    ESP_LOGI(TAG, "Phase 5: Profile Loading");
    
    // 5.1 Initialize profile manager
    ret = profile_manager_init();
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to initialize profile manager: %s", esp_err_to_name(ret));
        return;
    }
    
    uint8_t current_profile = profile_get_current_id();
    ESP_LOGI(TAG, "✓ Profile %d loaded", current_profile);
    
    // 5.2 Update LEDs from profile
    for (int i = 0; i < NUM_BUTTONS; i++) {
        button_config_t* btn = profile_get_button_config(i);
        if (btn != NULL) {
            led_set_color(i, btn->led_r, btn->led_g, btn->led_b, btn->led_brightness);
        }
    }
    led_update();
    ESP_LOGI(TAG, "✓ LEDs configured from profile");
    
    // 5.3 Load and display images (if available)
    ESP_LOGI(TAG, "Loading images for profile %d...", current_profile);
    for (int i = 0; i < NUM_BUTTONS; i++) {
        uint8_t* image_data = NULL;
        size_t image_size = 0;
        
        ret = image_storage_load(current_profile, i, &image_data, &image_size);
        if (ret == ESP_OK && image_data != NULL) {
            // NOTE: JPEG decode and display would require:
            // - JPEG decoder library (esp_jpeg or TJpgDec)
            // - RGB565 conversion
            // - GC9A01 bitmap rendering
            // Current implementation: shows that image exists in storage
            ESP_LOGI(TAG, "✓ Image loaded for button %d (%d bytes)", i, image_size);
            free(image_data);
        } else {
            // Show default color
            gc9a01_clear(i, COLOR_BLACK);
        }
    }
    
    // ============================================
    // PHASE 6: Task Creation
    // ============================================
    
    ESP_LOGI(TAG, "Phase 6: Creating FreeRTOS Tasks");
    
    // 6.1 Create USB Vendor RX task
    xTaskCreatePinnedToCore(usb_vendor_rx_task, "usb_vendor_rx", STACK_SIZE_USB_RX, NULL,
                            TASK_PRIORITY_USB_RX, NULL, 0);
    ESP_LOGI(TAG, "✓ USB Vendor RX task created");
    
    // 6.2 Create button task
    xTaskCreatePinnedToCore(button_task, "button", STACK_SIZE_BUTTON, NULL,
                            TASK_PRIORITY_BUTTON, NULL, 1);
    ESP_LOGI(TAG, "✓ Button task created");
    
    // 6.3 Create encoder task
    xTaskCreatePinnedToCore(encoder_task, "encoder", STACK_SIZE_ENCODER, NULL,
                            TASK_PRIORITY_ENCODER, NULL, 1);
    ESP_LOGI(TAG, "✓ Encoder task created");
    
    // 6.4 Create protocol task
    xTaskCreatePinnedToCore(protocol_task, "protocol", STACK_SIZE_PROTOCOL, NULL,
                            TASK_PRIORITY_PROTOCOL, NULL, 0);
    ESP_LOGI(TAG, "✓ Protocol task created");
    
    // 6.5 Create LED task
    xTaskCreatePinnedToCore(led_task, "led", STACK_SIZE_LED, NULL,
                            TASK_PRIORITY_LED, NULL, 1);
    ESP_LOGI(TAG, "✓ LED task created");
    
    // ============================================
    // PHASE 7: System Ready
    // ============================================
    
    ESP_LOGI(TAG, "===========================================");
    ESP_LOGI(TAG, "  System Ready!");
    ESP_LOGI(TAG, "===========================================");
    ESP_LOGI(TAG, "Firmware Version: %s", FIRMWARE_VERSION);
    ESP_LOGI(TAG, "Current Profile: %d", current_profile);
    ESP_LOGI(TAG, "Free heap: %u bytes", (unsigned int)esp_get_free_heap_size());
    ESP_LOGI(TAG, "Free PSRAM: %u bytes", (unsigned int)heap_caps_get_free_size(MALLOC_CAP_SPIRAM));
    ESP_LOGI(TAG, "===========================================");
    
    // Send device ready event to PC (defined in protocol_types.h)
    protocol_send_event(0xF4, NULL, 0);  // EVENT_DEVICE_READY = 0xF4
    
    // Main task can now delete itself
    vTaskDelete(NULL);
}
