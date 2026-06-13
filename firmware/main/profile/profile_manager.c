/**
 * @file profile_manager.c
 * @brief Profile manager implementation
 */

#include "common.h"
#include "profile_manager.h"
#include "storage/profile_storage.h"
#include "storage/image_storage.h"
#include "hardware/leds.h"
#include "hardware/gc9a01.h"
#include "hardware/night_mode.h"
#include "protocol/protocol_handler.h"
#include "protocol/protocol_types.h"
#include "utils/crc.h"
#include "utils/jpeg_decode_util.h"
#include "utils/text_render.h"
#include "config.h"

// Embedded firmware assets (compiled into binary via EMBED_FILES in CMakeLists.txt)
extern const uint8_t back_icon_jpg_start[] asm("_binary_back_icon_jpg_start");
extern const uint8_t back_icon_jpg_end[]   asm("_binary_back_icon_jpg_end");

static const char* TAG = "PROFILE";

static profile_t current_profile;
static uint8_t current_profile_id = 0;
static SemaphoreHandle_t profile_mutex = NULL;

// Folder navigation stack
// Forward declarations — defined in the Display Helper section below
static void profile_update_button_display(uint8_t button_id, button_config_t* btn);
static void profile_show_back_icon(uint8_t button_id);
static uint8_t folder_stack[FOLDER_STACK_DEPTH];
static uint8_t folder_stack_depth = 0;
static uint8_t folder_entry_button = 0xFF;  // Button that was used to enter current folder

esp_err_t profile_manager_init(void) {
    ESP_LOGI(TAG, "Initializing profile manager");
    
    profile_mutex = xSemaphoreCreateMutex();
    if (profile_mutex == NULL) {
        return ESP_FAIL;
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
    
    // Load new profile; initialise empty if not found or corrupted (allows first-time send from PC)
    esp_err_t ret = profile_storage_load(profile_id, &current_profile);
    if (ret != ESP_OK) {
        if (ret == ESP_ERR_NOT_FOUND || ret == ESP_ERR_INVALID_CRC) {
            ESP_LOGW(TAG, "Profile %d not on disk, starting empty", profile_id);
            memset(&current_profile, 0, sizeof(profile_t));
            current_profile.profile_id = profile_id;
            snprintf(current_profile.name, sizeof(current_profile.name), "Profile %d", profile_id + 1);
            for (int i = 0; i < NUM_BUTTONS; i++) current_profile.buttons[i].button_id = i;
            for (int f = 0; f < NUM_FOLDERS; f++)
                for (int i = 0; i < NUM_BUTTONS; i++)
                    current_profile.folders[f].buttons[i].button_id = i;
        } else {
            xSemaphoreGive(profile_mutex);
            ESP_LOGE(TAG, "Failed to load profile %d: %s", profile_id, esp_err_to_name(ret));
            return ret;
        }
    }
    
    current_profile_id = profile_id;
    
    // Update LEDs
    for (int i = 0; i < NUM_BUTTONS; i++) {
        button_config_t* btn = &current_profile.buttons[i];
        led_set_color(i, btn->led_r, btn->led_g, btn->led_b, btn->led_brightness);
    }
    led_update();

    // Refresh all button displays so stale images from the previous profile
    // don't remain on screen after a switch.
    for (int i = 0; i < NUM_BUTTONS; i++) {
        profile_update_button_display(i, &current_profile.buttons[i]);
    }

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

esp_err_t profile_set_encoder_action(uint8_t slot, uint8_t action_type,
                                      const uint8_t* action_data, uint16_t action_len) {
    if (slot > 3) return ESP_ERR_INVALID_ARG;
    if (action_len > ACTION_DATA_MAX_LEN) action_len = ACTION_DATA_MAX_LEN;

    xSemaphoreTake(profile_mutex, portMAX_DELAY);

    switch (slot) {
        case 0:
            current_profile.encoder.cw_action_type = action_type;
            current_profile.encoder.cw_action_data_len = action_len;
            if (action_data && action_len > 0) memcpy(current_profile.encoder.cw_action_data, action_data, action_len);
            break;
        case 1:
            current_profile.encoder.ccw_action_type = action_type;
            current_profile.encoder.ccw_action_data_len = action_len;
            if (action_data && action_len > 0) memcpy(current_profile.encoder.ccw_action_data, action_data, action_len);
            break;
        case 2:
            current_profile.encoder.press_action_type = action_type;
            current_profile.encoder.press_action_data_len = action_len;
            if (action_data && action_len > 0) memcpy(current_profile.encoder.press_action_data, action_data, action_len);
            break;
        case 3:
            current_profile.encoder.long_press_action_type = action_type;
            current_profile.encoder.long_press_action_data_len = action_len;
            if (action_data && action_len > 0) memcpy(current_profile.encoder.long_press_action_data, action_data, action_len);
            break;
    }

    xSemaphoreGive(profile_mutex);
    return ESP_OK;
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

    // Folder action: also populate the dedicated folder_id field used by the executor
    if (action_type == ACTION_TYPE_FOLDER && action_len >= 1 && action_data != NULL) {
        btn->folder_id = action_data[0];
    }

    xSemaphoreGive(profile_mutex);

    return ESP_OK;
}

esp_err_t profile_set_button_long_press_action(uint8_t button_id,
                                                uint8_t action_type, const uint8_t* action_data,
                                                uint16_t action_len) {
    if (button_id >= NUM_BUTTONS) return ESP_ERR_INVALID_ARG;
    if (action_len > ACTION_DATA_MAX_LEN) action_len = ACTION_DATA_MAX_LEN;

    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    button_config_t* btn = &current_profile.buttons[button_id];
    btn->long_press_action_type = action_type;
    btn->long_press_action_data_len = action_len;
    if (action_data != NULL && action_len > 0) {
        memcpy(btn->long_press_action_data, action_data, action_len);
    }
    xSemaphoreGive(profile_mutex);

    ESP_LOGD(TAG, "Button %d long press stored: type=0x%02X len=%d",
             button_id, action_type, action_len);

    // Display is refreshed by CMD_REFRESH_DISPLAYS after the full save sequence,
    // once both the action and long-press data are fully committed.

    return ESP_OK;
}

esp_err_t profile_set_button_long_press_name(uint8_t button_id, const char* name) {
    if (button_id >= NUM_BUTTONS || name == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    xSemaphoreTake(profile_mutex, portMAX_DELAY);

    button_config_t* btn = &current_profile.buttons[button_id];
    memset(btn->long_press_name, 0, BUTTON_NAME_MAX_LEN);
    strncpy(btn->long_press_name, name, BUTTON_NAME_MAX_LEN - 1);

    xSemaphoreGive(profile_mutex);

    return ESP_OK;
}

// ---- Folder button setters -----------------------------------------------

esp_err_t profile_set_folder_button_action(uint8_t profile_id, uint8_t folder_id,
                                            uint8_t button_id, uint8_t action_type,
                                            const uint8_t* action_data, uint16_t action_len) {
    if (profile_id >= NUM_PROFILES || folder_id >= NUM_FOLDERS || button_id >= NUM_BUTTONS)
        return ESP_ERR_INVALID_ARG;
    if (action_len > ACTION_DATA_MAX_LEN)
        return ESP_ERR_INVALID_SIZE;

    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    button_config_t* btn = &current_profile.folders[folder_id].buttons[button_id];
    btn->button_id   = button_id;
    btn->action_type = action_type;
    btn->action_data_len = action_len;
    if (action_data != NULL && action_len > 0)
        memcpy(btn->action_data, action_data, action_len);
    if (action_type == ACTION_TYPE_FOLDER && action_len >= 1 && action_data != NULL)
        btn->folder_id = action_data[0];
    xSemaphoreGive(profile_mutex);
    return ESP_OK;
}

esp_err_t profile_set_folder_button_name(uint8_t profile_id, uint8_t folder_id,
                                          uint8_t button_id, const char* name) {
    if (profile_id >= NUM_PROFILES || folder_id >= NUM_FOLDERS || button_id >= NUM_BUTTONS || name == NULL)
        return ESP_ERR_INVALID_ARG;

    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    button_config_t* btn = &current_profile.folders[folder_id].buttons[button_id];
    memset(btn->name, 0, BUTTON_NAME_MAX_LEN);
    strncpy(btn->name, name, BUTTON_NAME_MAX_LEN - 1);
    xSemaphoreGive(profile_mutex);
    return ESP_OK;
}

esp_err_t profile_set_folder_button_led(uint8_t profile_id, uint8_t folder_id,
                                         uint8_t button_id,
                                         uint8_t r, uint8_t g, uint8_t b,
                                         uint8_t brightness, uint8_t effect) {
    if (profile_id >= NUM_PROFILES || folder_id >= NUM_FOLDERS || button_id >= NUM_BUTTONS)
        return ESP_ERR_INVALID_ARG;

    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    button_config_t* btn = &current_profile.folders[folder_id].buttons[button_id];
    btn->led_r = r; btn->led_g = g; btn->led_b = b;
    btn->led_brightness = brightness; btn->led_effect = effect;
    xSemaphoreGive(profile_mutex);
    return ESP_OK;
}

esp_err_t profile_set_button_name(uint8_t profile_id, uint8_t button_id, const char* name) {
    if (profile_id >= NUM_PROFILES || button_id >= NUM_BUTTONS || name == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    xSemaphoreTake(profile_mutex, portMAX_DELAY);

    button_config_t* btn = &current_profile.buttons[button_id];
    memset(btn->name, 0, BUTTON_NAME_MAX_LEN);
    strncpy(btn->name, name, BUTTON_NAME_MAX_LEN - 1);

    bool should_refresh = (profile_id == current_profile_id && btn->image_size == 0);

    xSemaphoreGive(profile_mutex);

    // Refresh display after releasing the mutex so SPI operations don't run under profile_mutex.
    if (should_refresh) {
        profile_update_button_display(button_id, &current_profile.buttons[button_id]);
    }

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
    
    // Update LED immediately if current profile, but not while night mode is active
    // (night mode exit will call profile_restore_leds() to pick up the new color)
    if (profile_id == current_profile_id && !night_mode_is_active()) {
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

// HID keycode → short key name (covers the most common codes)
static const char* hid_keycode_name(uint8_t kc) {
    if (kc >= 0x04 && kc <= 0x1D) {
        static char buf[2] = {0, 0};
        buf[0] = 'A' + (kc - 0x04);
        return buf;
    }
    if (kc >= 0x1E && kc <= 0x27) {
        static char buf[2] = {0, 0};
        buf[0] = (kc == 0x27) ? '0' : ('1' + (kc - 0x1E));
        return buf;
    }
    if (kc >= 0x3A && kc <= 0x45) {
        static char buf[4];
        snprintf(buf, sizeof(buf), "F%d", kc - 0x3A + 1);
        return buf;
    }
    if (kc >= 0x68 && kc <= 0x73) {
        static char buf[5];
        snprintf(buf, sizeof(buf), "F%d", kc - 0x68 + 13);
        return buf;
    }
    switch (kc) {
        case 0x28: return "Enter";
        case 0x29: return "Esc";
        case 0x2A: return "Bksp";
        case 0x2B: return "Tab";
        case 0x2C: return "Space";
        case 0x4F: return "Right";
        case 0x50: return "Left";
        case 0x51: return "Down";
        case 0x52: return "Up";
        case 0x4A: return "Home";
        case 0x4D: return "End";
        case 0x4B: return "PgUp";
        case 0x4E: return "PgDn";
        case 0x46: return "Print";
        case 0x47: return "Scrl";
        case 0x48: return "Pause";
        case 0x49: return "Ins";
        case 0x4C: return "Del";
        default: {
            static char buf[7];
            snprintf(buf, sizeof(buf), "0x%02X", kc);
            return buf;
        }
    }
}

// HID Consumer Control usage code → short name
static const char* media_key_name(uint16_t usage) {
    switch (usage) {
        case 0x00E2: return "Mute";
        case 0x00E9: return "Vol+";
        case 0x00EA: return "Vol-";
        case 0x00B5: return "Next";
        case 0x00B6: return "Prev";
        case 0x00CD: return "Play";
        case 0x00B3: return "FFwd";
        case 0x00B4: return "Rwd";
        default: {
            static char buf[8];
            snprintf(buf, sizeof(buf), "0x%04X", usage);
            return buf;
        }
    }
}

// Split-display layout constants (160px total height)
#define SPLIT_SHORT_H    96  // 60% — short press region (top)
#define SPLIT_LONG_H     64  // 40% — long press region (bottom)
#define COLOR_GRAY_LP   0x528A  // RGB(80,80,80) — visible gray for long press area
#define COLOR_DIVIDER_LP 0xFFFF // white 2-px separator between the two regions

// Build a text label from the button's action when no name is set.
// Uses '\n' as line separator for the text renderer.
static void generate_action_label(const button_config_t* btn, char* label, size_t label_size) {
    switch (btn->action_type) {
        case ACTION_TYPE_NONE:
            snprintf(label, label_size, "Empty");
            break;

        case ACTION_TYPE_KEYBOARD:
            if (btn->action_data_len >= 2) {
                uint8_t modifier = btn->action_data[0];
                uint8_t keycode  = btn->action_data[1];
                if (keycode != 0) {
                    // Single key press
                    char mod_str[16] = {0};
                    if (modifier & 0x01) strncat(mod_str, "Ctrl+", sizeof(mod_str) - strlen(mod_str) - 1);
                    if (modifier & 0x02) strncat(mod_str, "Sft+",  sizeof(mod_str) - strlen(mod_str) - 1);
                    if (modifier & 0x04) strncat(mod_str, "Alt+",  sizeof(mod_str) - strlen(mod_str) - 1);
                    if (modifier & 0x08) strncat(mod_str, "GUI+",  sizeof(mod_str) - strlen(mod_str) - 1);
                    if (mod_str[0]) {
                        snprintf(label, label_size, "%s\n%s", mod_str, hid_keycode_name(keycode));
                    } else {
                        snprintf(label, label_size, "%s", hid_keycode_name(keycode));
                    }
                } else if (btn->action_data_len > 7) {
                    // Text string to type — show first chars
                    int text_len = btn->action_data_len - 7;
                    int show = (text_len <= 10) ? text_len : 10;
                    char preview[11];
                    memcpy(preview, btn->action_data + 7, show);
                    preview[show] = '\0';
                    snprintf(label, label_size, "Type\n%s", preview);
                } else {
                    snprintf(label, label_size, "Key");
                }
            } else {
                snprintf(label, label_size, "Key");
            }
            break;

        case ACTION_TYPE_MEDIA:
            if (btn->action_data_len >= 2) {
                uint16_t usage = (uint16_t)btn->action_data[0] | ((uint16_t)btn->action_data[1] << 8);
                snprintf(label, label_size, "%s", media_key_name(usage));
            } else {
                snprintf(label, label_size, "Media");
            }
            break;

        case ACTION_TYPE_PROFILE_SWITCH:
            if (btn->action_data_len >= 1) {
                snprintf(label, label_size, "Profile\n%d", btn->action_data[0] + 1);
            } else {
                snprintf(label, label_size, "Profile");
            }
            break;

        case ACTION_TYPE_FOLDER:
            snprintf(label, label_size, "Folder\n%d", btn->folder_id + 1);
            break;

        case ACTION_TYPE_DELAY:
            if (btn->action_data_len >= 2) {
                uint16_t ms = (uint16_t)btn->action_data[0] | ((uint16_t)btn->action_data[1] << 8);
                snprintf(label, label_size, "Delay\n%dms", ms);
            } else {
                snprintf(label, label_size, "Delay");
            }
            break;

        case ACTION_TYPE_SHELL: {
            // action_data contains flags byte + null-terminated command
            const char* cmd = (btn->action_data_len > 1)
                              ? (const char*)(btn->action_data + 1)
                              : (const char*)btn->action_data;
            int show = 0;
            while (show < 12 && cmd[show] && cmd[show] != '\0') show++;
            char preview[13];
            memcpy(preview, cmd, show);
            preview[show] = '\0';
            snprintf(label, label_size, "$\n%s", preview);
            break;
        }

        case ACTION_TYPE_LAUNCH_APP: {
            // Extract the filename from a path for brevity
            const char* path = (const char*)btn->action_data;
            const char* base = path;
            for (const char* p = path; *p; p++) {
                if (*p == '/' || *p == '\\') base = p + 1;
            }
            int show = 0;
            while (show < 12 && base[show] && base[show] != '\0') show++;
            char preview[13];
            memcpy(preview, base, show);
            preview[show] = '\0';
            snprintf(label, label_size, "App\n%s", preview);
            break;
        }

        case ACTION_TYPE_SEQUENCE:
            if (btn->action_data_len >= 1) {
                snprintf(label, label_size, "Seq\n%d steps", btn->action_data[0]);
            } else {
                snprintf(label, label_size, "Sequence");
            }
            break;

        case ACTION_TYPE_CUSTOM_HID:
            snprintf(label, label_size, "HID");
            break;

        case ACTION_TYPE_NIGHT_MODE:
            snprintf(label, label_size, "Night");
            break;

        case ACTION_TYPE_PLUGIN:
            snprintf(label, label_size, "Plugin");
            break;

        default:
            snprintf(label, label_size, "0x%02X", btn->action_type);
            break;
    }
}

// Build a short display label for the long press action.
static void generate_long_press_label(const button_config_t* btn, char* label, size_t label_size) {
    label[0] = '\0';
    switch (btn->long_press_action_type) {
        case ACTION_TYPE_NONE:
            break;  // no label — caller handles empty string

        case ACTION_TYPE_KEYBOARD:
            if (btn->long_press_action_data_len >= 2) {
                uint8_t mod = btn->long_press_action_data[0];
                uint8_t key = btn->long_press_action_data[1];
                if (key != 0) {
                    char mod_str[16] = {0};
                    if (mod & 0x01) strncat(mod_str, "Ctrl+", sizeof(mod_str) - strlen(mod_str) - 1);
                    if (mod & 0x02) strncat(mod_str, "Sft+",  sizeof(mod_str) - strlen(mod_str) - 1);
                    if (mod & 0x04) strncat(mod_str, "Alt+",  sizeof(mod_str) - strlen(mod_str) - 1);
                    if (mod & 0x08) strncat(mod_str, "GUI+",  sizeof(mod_str) - strlen(mod_str) - 1);
                    if (mod_str[0])
                        snprintf(label, label_size, "%s\n%s", mod_str, hid_keycode_name(key));
                    else
                        snprintf(label, label_size, "%s", hid_keycode_name(key));
                } else if (btn->long_press_action_data_len > 7) {
                    int show = btn->long_press_action_data_len - 7;
                    if (show > 10) show = 10;
                    char preview[11];
                    memcpy(preview, btn->long_press_action_data + 7, show);
                    preview[show] = '\0';
                    snprintf(label, label_size, "Type\n%s", preview);
                } else {
                    snprintf(label, label_size, "Key");
                }
            } else {
                snprintf(label, label_size, "Key");
            }
            break;

        case ACTION_TYPE_MEDIA:
            if (btn->long_press_action_data_len >= 2) {
                uint16_t usage = (uint16_t)btn->long_press_action_data[0]
                               | ((uint16_t)btn->long_press_action_data[1] << 8);
                snprintf(label, label_size, "%s", media_key_name(usage));
            } else {
                snprintf(label, label_size, "Media");
            }
            break;

        case ACTION_TYPE_SHELL: {
            const char* cmd = (btn->long_press_action_data_len > 1)
                              ? (const char*)(btn->long_press_action_data + 1)
                              : (const char*)btn->long_press_action_data;
            int show = 0;
            while (show < 12 && cmd[show]) show++;
            char preview[13];
            memcpy(preview, cmd, show);
            preview[show] = '\0';
            snprintf(label, label_size, "$\n%s", preview);
            break;
        }

        case ACTION_TYPE_LAUNCH_APP: {
            const char* path = (const char*)btn->long_press_action_data;
            const char* base = path;
            for (const char* p = path; *p; p++)
                if (*p == '/' || *p == '\\') base = p + 1;
            int show = 0;
            while (show < 12 && base[show]) show++;
            char preview[13];
            memcpy(preview, base, show);
            preview[show] = '\0';
            snprintf(label, label_size, "App\n%s", preview);
            break;
        }

        case ACTION_TYPE_SEQUENCE:
            if (btn->long_press_action_data_len >= 1)
                snprintf(label, label_size, "Seq\n%d", btn->long_press_action_data[0]);
            else
                snprintf(label, label_size, "Seq");
            break;

        case ACTION_TYPE_FOLDER:
            snprintf(label, label_size, "Folder");
            break;

        case ACTION_TYPE_NIGHT_MODE:
            snprintf(label, label_size, "Night");
            break;

        case ACTION_TYPE_CUSTOM_HID:
            snprintf(label, label_size, "HID");
            break;

        case ACTION_TYPE_PLUGIN:
            snprintf(label, label_size, "Plugin");
            break;

        default:
            snprintf(label, label_size, "Hold");
            break;
    }
}

void profile_restore_leds(void) {
    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    for (int i = 0; i < NUM_BUTTONS; i++) {
        button_config_t* btn = &current_profile.buttons[i];
        led_set_color(i, btn->led_r, btn->led_g, btn->led_b, btn->led_brightness);
    }
    led_update();
    xSemaphoreGive(profile_mutex);
}

void profile_refresh_displays(void) {
    for (int i = 0; i < NUM_BUTTONS; i++) {
        profile_update_button_display(i, &current_profile.buttons[i]);
        // Yield between buttons so the USB task and watchdog feed can run.
        // Without this, rendering 10 displays in a tight loop can starve other tasks.
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}

/**
 * @brief Update a single button's display with its image, text label, or solid color.
 *        Priority: JPEG image > button name > action-derived label > solid color.
 * @param button_id Button/display ID (0-9)
 * @param btn Button configuration
 */
static void profile_update_button_display(uint8_t button_id, button_config_t* btn) {
    if (button_id >= NUM_BUTTONS || btn == NULL) return;

    // While inside a folder, the entry button always shows the embedded back icon
    // regardless of what action/image the folder assigns to it.
    // (folder_stack_depth is already decremented before the render loop in profile_folder_exit,
    //  so this check is false on exit and the button renders its root content normally.)
    if (profile_is_in_folder() && button_id == folder_entry_button) {
        profile_show_back_icon(button_id);
        return;
    }

    bool has_long_press = (btn->long_press_action_type != ACTION_TYPE_NONE);

    if (has_long_press) {
        ESP_LOGD(TAG, "Split display btn %d: img=%lu lp_type=0x%02X name='%s'",
                 button_id, (unsigned long)btn->image_size,
                 btn->long_press_action_type, btn->name);

        char lp_label[64] = {0};
        if (btn->long_press_name[0] != '\0')
            strncpy(lp_label, btn->long_press_name, sizeof(lp_label) - 1);
        else
            generate_long_press_label(btn, lp_label, sizeof(lp_label));
        ESP_LOGD(TAG, "  lp_label='%s'", lp_label);

        // Compose the split frame in a single DISPLAY_BUFFER_SIZE buffer, then draw
        // once via gc9a01_draw_image.  GC9D01 partial-window writes (CASET/RASET on a
        // sub-region) are unreliable on this panel; a full 160x160 write always works.
        uint8_t* frame = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_DMA | MALLOC_CAP_SPIRAM);
        if (!frame) {
            gc9a01_clear(button_id, COLOR_BLACK);
            return;
        }

        // ── Top SPLIT_SHORT_H px: short press content ──────────────────────
        if (btn->image_size > 0) {
            uint8_t* image_data = NULL;
            size_t image_size = 0;
            if (image_storage_load(current_profile_id, button_id, &image_data, &image_size) == ESP_OK &&
                image_data) {
                uint16_t w, h;
                if (jpeg_decode_to_rgb565(image_data, image_size, frame, DISPLAY_BUFFER_SIZE, &w, &h) != ESP_OK)
                    text_render_fill_region(frame, DISPLAY_WIDTH, 0, SPLIT_SHORT_H, NULL, COLOR_WHITE, COLOR_BLACK);
                // On success: frame rows 0..SPLIT_SHORT_H-1 hold the top of the image
                free(image_data);
            } else {
                text_render_fill_region(frame, DISPLAY_WIDTH, 0, SPLIT_SHORT_H, NULL, COLOR_WHITE, COLOR_BLACK);
            }
        } else {
            char label[64] = {0};
            if (btn->name[0] != '\0')
                strncpy(label, btn->name, sizeof(label) - 1);
            else if (btn->action_type != ACTION_TYPE_NONE)
                generate_action_label(btn, label, sizeof(label));
            text_render_fill_region(frame, DISPLAY_WIDTH, 0, SPLIT_SHORT_H,
                                    label[0] ? label : NULL, COLOR_WHITE, COLOR_BLACK);
        }

        // ── Bottom SPLIT_LONG_H px: long press content (gray background) ───
        text_render_fill_region(frame, DISPLAY_WIDTH, SPLIT_SHORT_H, SPLIT_LONG_H,
                                lp_label, COLOR_WHITE, COLOR_GRAY_LP);

        // ── 2-px white divider at rows SPLIT_SHORT_H-2 .. SPLIT_SHORT_H-1 ─
        {
            uint8_t div_hi = COLOR_DIVIDER_LP >> 8, div_lo = COLOR_DIVIDER_LP & 0xFF;
            uint8_t* div_ptr = frame + (size_t)DISPLAY_WIDTH * (SPLIT_SHORT_H - 2) * 2;
            for (size_t i = 0; i < (size_t)DISPLAY_WIDTH * 2 * 2; i += 2) {
                div_ptr[i]     = div_hi;
                div_ptr[i + 1] = div_lo;
            }
        }

        gc9a01_draw_image(button_id, frame, DISPLAY_WIDTH, DISPLAY_HEIGHT);
        free(frame);
        return;
    }

    // ── Full-screen (no long press assigned) ──────────────────────────────
    if (btn->image_size > 0) {
        uint8_t* image_data = NULL;
        size_t image_size = 0;

        esp_err_t ret = image_storage_load(current_profile_id, button_id, &image_data, &image_size);
        if (ret == ESP_OK && image_data != NULL) {
            uint8_t* rgb565_buf = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_DMA | MALLOC_CAP_SPIRAM);
            if (rgb565_buf != NULL) {
                uint16_t w, h;
                if (jpeg_decode_to_rgb565(image_data, image_size, rgb565_buf, DISPLAY_BUFFER_SIZE, &w, &h) == ESP_OK) {
                    gc9a01_draw_image(button_id, rgb565_buf, w, h);
                    ESP_LOGD(TAG, "Image displayed for button %d (%dx%d)", button_id, w, h);
                } else {
                    ESP_LOGW(TAG, "JPEG decode failed for button %d", button_id);
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
        return;
    }

    // No image — build a text label and render it
    char label[64] = {0};

    if (btn->name[0] != '\0') {
        strncpy(label, btn->name, sizeof(label) - 1);
    } else if (btn->action_type != ACTION_TYPE_NONE) {
        generate_action_label(btn, label, sizeof(label));
    }

    if (label[0] != '\0') {
        ESP_LOGD(TAG, "Rendering text label for button %d: '%s'", button_id, label);
        if (text_render_to_display(button_id, label, COLOR_WHITE, COLOR_BLACK) != ESP_OK)
            gc9a01_clear(button_id, COLOR_BLACK);
    } else {
        gc9a01_clear(button_id, COLOR_BLACK);
    }
}

// ============================================
// Folder Navigation Functions
// ============================================

esp_err_t profile_folder_enter(uint8_t folder_id, uint8_t entry_button_id) {
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
    // Record which button entered the folder so profile_update_button_display
    // can substitute the back icon for it inside the normal render loop.
    folder_entry_button = entry_button_id;

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

static void profile_show_back_icon(uint8_t button_id) {
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
