/**
 * @file profile_manager.c
 * @brief Profile manager implementation
 */

#include "common.h"
#include "profile_manager.h"
#include "storage/profile_storage.h"
#include "storage/image_storage.h"
#include "storage/nvs_manager.h"
#include "hardware/leds.h"
#include "hardware/gc9a01.h"
#include "protocol/protocol_handler.h"
#include "protocol/protocol_types.h"
#include "utils/crc.h"
#include "utils/jpeg_decode_util.h"
#include "config.h"

// Embedded firmware assets (compiled into binary via EMBED_FILES in CMakeLists.txt)
extern const uint8_t back_icon_jpg_start[] asm("_binary_back_icon_jpg_start");
extern const uint8_t back_icon_jpg_end[]   asm("_binary_back_icon_jpg_end");

static const char* TAG = "PROFILE";

static profile_t current_profile;
static uint8_t current_profile_id = 0;
static SemaphoreHandle_t profile_mutex = NULL;

// Folder navigation stack
static uint8_t folder_stack[FOLDER_STACK_DEPTH];
static uint8_t folder_stack_depth = 0;
static uint8_t folder_entry_button = 0xFF;  // Button that was used to enter current folder

esp_err_t profile_manager_init(void) {
    ESP_LOGI(TAG, "Initializing profile manager");
    
    profile_mutex = xSemaphoreCreateMutex();
    if (profile_mutex == NULL) {
        return ESP_FAIL;
    }
    
    // Load current profile ID from NVS
    nvs_get_current_profile(&current_profile_id);
    
    if (current_profile_id >= NUM_PROFILES) {
        current_profile_id = 0;
    }
    
    // Try to load profile from storage
    esp_err_t ret = profile_storage_load(current_profile_id, &current_profile);
    if (ret != ESP_OK) {
        ESP_LOGW(TAG, "Failed to load profile %d, using empty profile", current_profile_id);
        // Create empty profile in memory (don't save to avoid watchdog timeout)
        memset(&current_profile, 0, sizeof(profile_t));
        current_profile.profile_id = current_profile_id;
        snprintf(current_profile.name, sizeof(current_profile.name), "Profile %d", current_profile_id + 1);
    }
    
    ESP_LOGI(TAG, "Profile manager initialized, current profile: %d", current_profile_id);
    return ESP_OK;
}

esp_err_t profile_switch(uint8_t profile_id) {
    if (profile_id >= NUM_PROFILES) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (profile_id == current_profile_id) {
        return ESP_OK;
    }
    
    ESP_LOGI(TAG, "Switching profile: %d -> %d", current_profile_id, profile_id);
    
    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    
    // Load new profile
    esp_err_t ret = profile_storage_load(profile_id, &current_profile);
    if (ret != ESP_OK) {
        xSemaphoreGive(profile_mutex);
        ESP_LOGE(TAG, "Failed to load profile %d", profile_id);
        return ret;
    }
    
    current_profile_id = profile_id;
    
    // Update LEDs
    for (int i = 0; i < NUM_BUTTONS; i++) {
        button_config_t* btn = &current_profile.buttons[i];
        led_set_color(i, btn->led_r, btn->led_g, btn->led_b, btn->led_brightness);
    }
    led_update();
    
    // Save to NVS
    nvs_set_current_profile(profile_id);
    
    xSemaphoreGive(profile_mutex);
    
    ESP_LOGI(TAG, "Profile switched to %d", profile_id);
    return ESP_OK;
}

uint8_t profile_get_current_id(void) {
    return current_profile_id;
}

profile_t* profile_get(uint8_t profile_id) {
    if (profile_id == current_profile_id) {
        return &current_profile;
    }
    return NULL;
}

button_config_t* profile_get_button_config(uint8_t button_id) {
    if (button_id >= NUM_BUTTONS) {
        return NULL;
    }
    
    // If inside a folder, return button config from current folder
    if (folder_stack_depth > 0) {
        uint8_t current_folder = folder_stack[folder_stack_depth - 1];
        return &current_profile.folders[current_folder].buttons[button_id];
    }
    
    // Otherwise return from root profile
    return &current_profile.buttons[button_id];
}

esp_err_t profile_set_button_action(uint8_t profile_id, uint8_t button_id,
                                     uint8_t action_type, const uint8_t* action_data,
                                     uint16_t action_len) {
    if (profile_id >= NUM_PROFILES || button_id >= NUM_BUTTONS) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (action_len > ACTION_DATA_MAX_LEN) {
        return ESP_ERR_INVALID_SIZE;
    }
    
    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    
    button_config_t* btn = &current_profile.buttons[button_id];
    btn->action_type = action_type;
    btn->action_data_len = action_len;
    
    if (action_data != NULL && action_len > 0) {
        memcpy(btn->action_data, action_data, action_len);
    }
    
    xSemaphoreGive(profile_mutex);
    
    return ESP_OK;
}

esp_err_t profile_set_led_color(uint8_t profile_id, uint8_t button_id,
                                 uint8_t r, uint8_t g, uint8_t b,
                                 uint8_t brightness, uint8_t effect) {
    if (profile_id >= NUM_PROFILES || button_id >= NUM_BUTTONS) {
        return ESP_ERR_INVALID_ARG;
    }
    
    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    
    button_config_t* btn = &current_profile.buttons[button_id];
    btn->led_r = r;
    btn->led_g = g;
    btn->led_b = b;
    btn->led_brightness = brightness;
    btn->led_effect = effect;
    
    // Update LED immediately if current profile
    if (profile_id == current_profile_id) {
        led_set_color(button_id, r, g, b, brightness);
        led_update();
    }
    
    xSemaphoreGive(profile_mutex);
    
    return ESP_OK;
}

esp_err_t profile_save_to_storage(uint8_t profile_id) {
    if (profile_id >= NUM_PROFILES) {
        return ESP_ERR_INVALID_ARG;
    }
    
    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    
    // Calculate CRC
    current_profile.crc32 = crc32_calculate((uint8_t*)&current_profile,
                                             sizeof(profile_t) - sizeof(uint32_t));
    
    esp_err_t ret = profile_storage_save(profile_id, &current_profile);
    
    xSemaphoreGive(profile_mutex);
    
    return ret;
}

esp_err_t profile_create_defaults(void) {
    ESP_LOGI(TAG, "Creating default profiles");
    
    for (int p = 0; p < NUM_PROFILES; p++) {
        // Feed watchdog to prevent timeout
        vTaskDelay(pdMS_TO_TICKS(100));
        
        profile_t profile;
        memset(&profile, 0, sizeof(profile));
        
        profile.profile_id = p;
        snprintf(profile.name, sizeof(profile.name), "Profile %d", p + 1);
        
        // Configure buttons with default actions
        for (int b = 0; b < NUM_BUTTONS; b++) {
            button_config_t* btn = &profile.buttons[b];
            btn->button_id = b;
            btn->action_type = ACTION_TYPE_KEYBOARD;
            
            // Default: F13-F22 keys
            btn->action_data[0] = 0;  // No modifiers
            btn->action_data[1] = 0x68 + b;  // HID_KEY_F13 = 0x68
            btn->action_data_len = 2;
            
            // Default LED colors (rainbow)
            uint8_t hue = (b * 255) / NUM_BUTTONS;
            // Simple HSV to RGB conversion
            btn->led_r = (hue < 85) ? (255 - hue * 3) : ((hue < 170) ? 0 : ((hue - 170) * 3));
            btn->led_g = (hue < 85) ? (hue * 3) : ((hue < 170) ? (255 - (hue - 85) * 3) : 0);
            btn->led_b = (hue < 85) ? 0 : ((hue < 170) ? ((hue - 85) * 3) : (255 - (hue - 170) * 3));
            btn->led_brightness = LED_DEFAULT_BRIGHTNESS;
            btn->led_effect = LED_EFFECT_STATIC;
        }
        
        // Calculate CRC
        profile.crc32 = crc32_calculate((uint8_t*)&profile,
                                         sizeof(profile_t) - sizeof(uint32_t));
        
        // Save profile
        ESP_LOGI(TAG, "Saving default profile %d", p);
        profile_storage_save(p, &profile);
        
        // Feed watchdog after each profile - longer delay for SPIFFS
        vTaskDelay(pdMS_TO_TICKS(200));
    }
    
    ESP_LOGI(TAG, "Default profiles created");
    return ESP_OK;
}

// ============================================
// Display Helper
// ============================================

/**
 * @brief Update a single button's display with its image or clear to black.
 *        Loads JPEG from storage and renders to the GC9A01 display.
 *        If no image is available, clears the display.
 *        NOTE: JPEG decode is not yet implemented — currently just clears display.
 * @param button_id Button/display ID (0-9)
 * @param btn Button configuration (for image metadata)
 */
static void profile_update_button_display(uint8_t button_id, button_config_t* btn) {
    if (button_id >= NUM_BUTTONS || btn == NULL) return;
    
    if (btn->image_size > 0) {
        // Image exists in config — try to load from storage
        uint8_t* image_data = NULL;
        size_t image_size = 0;
        
        esp_err_t ret = image_storage_load(current_profile_id, button_id, &image_data, &image_size);
        if (ret == ESP_OK && image_data != NULL) {
            // Decode JPEG → RGB565 → display
            uint8_t* rgb565_buf = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_DMA | MALLOC_CAP_SPIRAM);
            if (rgb565_buf != NULL) {
                uint16_t w, h;
                if (jpeg_decode_to_rgb565(image_data, image_size, rgb565_buf, DISPLAY_BUFFER_SIZE, &w, &h) == ESP_OK) {
                    gc9a01_draw_image(button_id, rgb565_buf, w, h);
                    ESP_LOGD(TAG, "Image displayed for button %d (%dx%d)", button_id, w, h);
                } else {
                    ESP_LOGW(TAG, "JPEG decode failed for button %d, clearing display", button_id);
                    gc9a01_clear(button_id, COLOR_BLACK);
                }
                free(rgb565_buf);
            } else {
                ESP_LOGE(TAG, "Failed to allocate RGB565 buffer for button %d", button_id);
                gc9a01_clear(button_id, COLOR_BLACK);
            }
            free(image_data);
        } else {
            gc9a01_clear(button_id, COLOR_BLACK);
        }
    } else {
        // No image — clear display with LED color as fallback
        uint16_t color = ((btn->led_r >> 3) << 11) | ((btn->led_g >> 2) << 5) | (btn->led_b >> 3);
        gc9a01_clear(button_id, color);
    }
}

// ============================================
// Folder Navigation Functions
// ============================================

esp_err_t profile_folder_enter(uint8_t folder_id) {
    if (folder_id >= NUM_FOLDERS) {
        ESP_LOGE(TAG, "Invalid folder ID: %d", folder_id);
        return ESP_ERR_INVALID_ARG;
    }
    
    if (folder_stack_depth >= FOLDER_STACK_DEPTH) {
        ESP_LOGE(TAG, "Folder stack overflow (max depth: %d)", FOLDER_STACK_DEPTH);
        return ESP_ERR_NO_MEM;
    }
    
    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    
    // Push folder to stack
    folder_stack[folder_stack_depth] = folder_id;
    folder_stack_depth++;
    
    // Get folder configuration
    folder_t* folder = &current_profile.folders[folder_id];
    
    ESP_LOGI(TAG, "Entering folder %d ('%s'), depth: %d",
             folder_id, folder->name, folder_stack_depth);
    
    // Update LEDs and displays with folder buttons
    for (int i = 0; i < NUM_BUTTONS; i++) {
        button_config_t* btn = &folder->buttons[i];
        led_set_color(i, btn->led_r, btn->led_g, btn->led_b, btn->led_brightness);
        
        // Update display: try to load button image, otherwise clear to black
        profile_update_button_display(i, btn);
    }
    led_update();
    
    xSemaphoreGive(profile_mutex);
    
    // Send folder entered event to PC
    {
        uint8_t evt_payload[4];
        evt_payload[0] = folder_id;
        evt_payload[1] = folder_stack_depth;
        evt_payload[2] = current_profile_id;
        evt_payload[3] = 0; // reserved
        protocol_send_event(EVENT_FOLDER_ENTERED, evt_payload, 4);
    }
    
    return ESP_OK;
}

esp_err_t profile_folder_exit(void) {
    if (folder_stack_depth == 0) {
        ESP_LOGW(TAG, "Already at root level");
        return ESP_ERR_INVALID_STATE;
    }
    
    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    
    // Pop folder from stack
    folder_stack_depth--;
    uint8_t exited_folder = folder_stack[folder_stack_depth];
    
    ESP_LOGI(TAG, "Exiting folder %d, new depth: %d", exited_folder, folder_stack_depth);
    
    // Restore buttons from parent context
    button_config_t* buttons;
    if (folder_stack_depth == 0) {
        // Back to root profile
        buttons = current_profile.buttons;
        ESP_LOGI(TAG, "Returned to root profile");
    } else {
        // Back to parent folder
        uint8_t parent_folder = folder_stack[folder_stack_depth - 1];
        buttons = current_profile.folders[parent_folder].buttons;
        ESP_LOGI(TAG, "Returned to parent folder %d", parent_folder);
    }
    
    // Update LEDs and displays
    for (int i = 0; i < NUM_BUTTONS; i++) {
        button_config_t* btn = &buttons[i];
        led_set_color(i, btn->led_r, btn->led_g, btn->led_b, btn->led_brightness);
        
        // Update display: try to load button image, otherwise clear to black
        profile_update_button_display(i, btn);
    }
    led_update();
    
    xSemaphoreGive(profile_mutex);
    
    // Send folder exited event to PC
    {
        uint8_t evt_payload[4];
        evt_payload[0] = exited_folder;
        evt_payload[1] = folder_stack_depth;
        evt_payload[2] = current_profile_id;
        evt_payload[3] = (folder_stack_depth > 0) ? folder_stack[folder_stack_depth - 1] : 0xFF;
        protocol_send_event(EVENT_FOLDER_EXITED, evt_payload, 4);
    }
    
    return ESP_OK;
}

uint8_t profile_get_current_folder(void) {
    if (folder_stack_depth == 0) {
        return 0xFF;  // Root level
    }
    return folder_stack[folder_stack_depth - 1];
}

bool profile_is_in_folder(void) {
    return folder_stack_depth > 0;
}

uint8_t profile_get_folder_depth(void) {
    return folder_stack_depth;
}

void profile_show_back_icon(uint8_t button_id) {
    if (button_id >= NUM_BUTTONS) return;
    
    size_t back_icon_size = back_icon_jpg_end - back_icon_jpg_start;
    
    ESP_LOGI(TAG, "Showing back icon on button %d (%d bytes)", button_id, back_icon_size);
    
    // Decode embedded back icon JPEG → RGB565 → display
    uint8_t* rgb565_buf = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_DMA | MALLOC_CAP_SPIRAM);
    if (rgb565_buf != NULL) {
        uint16_t w, h;
        if (jpeg_decode_to_rgb565(back_icon_jpg_start, back_icon_size, rgb565_buf, DISPLAY_BUFFER_SIZE, &w, &h) == ESP_OK) {
            gc9a01_draw_image(button_id, rgb565_buf, w, h);
        } else {
            ESP_LOGW(TAG, "Failed to decode back icon JPEG, showing white");
            gc9a01_clear(button_id, COLOR_WHITE);
        }
        free(rgb565_buf);
    } else {
        ESP_LOGE(TAG, "Failed to allocate RGB565 buffer for back icon");
        gc9a01_clear(button_id, COLOR_WHITE);
    }
}
