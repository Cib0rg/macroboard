/**
 * @file profile_manager.c
 * @brief Profile manager implementation
 */

#include "common.h"
#include "profile_manager.h"
#include "storage/profile_storage.h"
#include "storage/nvs_manager.h"
#include "hardware/leds.h"
#include "utils/crc.h"
#include "config.h"

static const char* TAG = "PROFILE";

static profile_t current_profile;
static uint8_t current_profile_id = 0;
static SemaphoreHandle_t profile_mutex = NULL;

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
        ESP_LOGW(TAG, "Failed to load profile %d, creating defaults", current_profile_id);
        profile_create_defaults();
        profile_storage_load(current_profile_id, &current_profile);
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
        profile_storage_save(p, &profile);
    }
    
    ESP_LOGI(TAG, "Default profiles created");
    return ESP_OK;
}
