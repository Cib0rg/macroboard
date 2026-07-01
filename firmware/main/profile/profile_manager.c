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
#include "utils/text_render.h"
#include "utils/jpeg_decode_util.h"
#include "config.h"

// Embedded firmware assets (compiled into binary via EMBED_FILES in CMakeLists.txt)
extern const uint8_t back_icon_jpg_start[] asm("_binary_back_icon_jpg_start");
extern const uint8_t back_icon_jpg_end[]   asm("_binary_back_icon_jpg_end");

static const char* TAG = "PROFILE";

static profile_t current_profile;
static SemaphoreHandle_t profile_mutex = NULL;

// Folder navigation stack
// Forward declarations — defined in the Display Helper section below
static void profile_update_button_display(uint8_t button_id, button_config_t* btn);
static void profile_show_back_icon(uint8_t button_id);
static uint8_t folder_stack[FOLDER_STACK_DEPTH];
static uint8_t folder_stack_depth = 0;

// Unassigned buttons (ACTION_TYPE_NONE) have their LED suppressed to avoid burning eyes.
// Stored color/brightness is preserved so it lights up as soon as an action is assigned.
#define BTN_LED_BRIGHTNESS(btn) \
    ((btn)->action_type == ACTION_TYPE_NONE ? 0 : (btn)->led_brightness)

// PSRAM display cache for root buttons (one per physical button slot).
static uint8_t* s_display_cache[NUM_BUTTONS];

// PSRAM display cache for folder buttons.
// Survives folder exit so that re-entry into the same folder is instant.
// Invalidated only when entering a DIFFERENT folder.
static uint8_t* s_folder_display_cache[NUM_BUTTONS];
static uint8_t  s_cached_folder_id = 0xFF; // 0xFF = no folder cached yet

static void folder_display_cache_clear(void) {
    for (int i = 0; i < NUM_BUTTONS; i++) {
        if (s_folder_display_cache[i]) { free(s_folder_display_cache[i]); s_folder_display_cache[i] = NULL; }
    }
}

void profile_image_cache_invalidate(uint8_t button_id, bool folder) {
    if (button_id >= NUM_BUTTONS) return;
    uint8_t **cache = folder ? s_folder_display_cache : s_display_cache;
    if (cache[button_id]) { free(cache[button_id]); cache[button_id] = NULL; }
}

void profile_image_cache_update(uint8_t button_id, bool folder, uint8_t* buf) {
    if (button_id >= NUM_BUTTONS) { free(buf); return; }
    uint8_t **cache = folder ? s_folder_display_cache : s_display_cache;
    free(cache[button_id]);
    cache[button_id] = buf;
}

void profile_refresh_button_display(uint8_t button_id) {
    if (button_id >= NUM_BUTTONS) return;
    // When inside a folder, use the folder button config so the display reflects
    // the folder content, not the root-level config at the same physical position.
    if (profile_is_in_folder()) {
        uint8_t folder_id = profile_get_current_folder();
        if (folder_id < NUM_FOLDERS) {
            profile_update_button_display(button_id, &current_profile.folders[folder_id].buttons[button_id]);
            return;
        }
    }
    profile_update_button_display(button_id, &current_profile.buttons[button_id]);
}

static uint8_t folder_entry_button = 0xFF;  // Button that was used to enter current folder

esp_err_t profile_manager_init(void) {
    ESP_LOGI(TAG, "Initializing profile manager");
    
    profile_mutex = xSemaphoreCreateMutex();
    if (profile_mutex == NULL) {
        return ESP_FAIL;
    }
    
    // Try to load profile from storage
    esp_err_t ret = profile_storage_load(0, &current_profile);
    if (ret != ESP_OK) {
        ESP_LOGW(TAG, "Failed to load profile, using empty profile");
        memset(&current_profile, 0, sizeof(profile_t));
        current_profile.profile_id = 0;
        snprintf(current_profile.name, sizeof(current_profile.name), "Profile 1");
    }

    ESP_LOGI(TAG, "Boot: btn[0] image_size=%lu action_type=0x%02X lp_action_type=0x%02X",
             (unsigned long)current_profile.buttons[0].image_size,
             current_profile.buttons[0].action_type,
             current_profile.buttons[0].long_press_action_type);

    // Defensive: if image_size is 0 for a button but image_storage still holds a
    // valid mapping+blob, the profile was saved before save_task could update
    // image_size (e.g. async SPIFFS write failed).  Correct the in-memory value so
    // the boot render loads the image from SPIFFS instead of falling back to text.
    // Note: image_storage_init (Phase 1.4) already ran and its GC now keeps mappings
    // alive when the blob exists, so image_storage_has_image() is reliable here.
    for (int b = 0; b < NUM_BUTTONS; b++) {
        if (current_profile.buttons[b].image_size == 0 &&
            image_storage_has_image(0, (uint8_t)b)) {
            current_profile.buttons[b].image_size = DISPLAY_BUFFER_SIZE;
            ESP_LOGW(TAG, "Boot fix: btn[%d] image_size corrected (mapping+blob exists)", b);
        }
    }
    for (int f = 0; f < NUM_FOLDERS; f++) {
        for (int b = 0; b < NUM_BUTTONS; b++) {
            uint8_t sbid = (uint8_t)(NUM_BUTTONS + (uint8_t)f * NUM_BUTTONS + (uint8_t)b);
            if (current_profile.folders[f].buttons[b].image_size == 0 &&
                image_storage_has_image(0, sbid)) {
                current_profile.folders[f].buttons[b].image_size = DISPLAY_BUFFER_SIZE;
                ESP_LOGW(TAG, "Boot fix: folder[%d].btn[%d] image_size corrected", f, b);
            }
        }
    }

    ESP_LOGI(TAG, "Profile manager initialized");
    return ESP_OK;
}

profile_t* profile_get(void) {
    return &current_profile;
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

esp_err_t profile_set_button_action(uint8_t button_id,
                                     uint8_t action_type, const uint8_t* action_data,
                                     uint16_t action_len) {
    if (button_id >= NUM_BUTTONS) {
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

esp_err_t profile_set_button_long_press_action(uint8_t button_id, uint8_t folder_id,
                                                uint8_t action_type, const uint8_t* action_data,
                                                uint16_t action_len) {
    if (button_id >= NUM_BUTTONS) return ESP_ERR_INVALID_ARG;
    if (folder_id != 0xFF && folder_id >= NUM_FOLDERS) return ESP_ERR_INVALID_ARG;
    if (action_len > ACTION_DATA_MAX_LEN) action_len = ACTION_DATA_MAX_LEN;

    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    button_config_t* btn = (folder_id == 0xFF)
        ? &current_profile.buttons[button_id]
        : &current_profile.folders[folder_id].buttons[button_id];
    btn->long_press_action_type = action_type;
    btn->long_press_action_data_len = action_len;
    if (action_data != NULL && action_len > 0)
        memcpy(btn->long_press_action_data, action_data, action_len);
    xSemaphoreGive(profile_mutex);

    ESP_LOGD(TAG, "Button %d (folder=%d) long press stored: type=0x%02X len=%d",
             button_id, folder_id, action_type, action_len);
    return ESP_OK;
}

esp_err_t profile_set_button_long_press_name(uint8_t button_id, uint8_t folder_id, const char* name) {
    if (button_id >= NUM_BUTTONS || name == NULL) return ESP_ERR_INVALID_ARG;
    if (folder_id != 0xFF && folder_id >= NUM_FOLDERS) return ESP_ERR_INVALID_ARG;

    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    button_config_t* btn = (folder_id == 0xFF)
        ? &current_profile.buttons[button_id]
        : &current_profile.folders[folder_id].buttons[button_id];
    memset(btn->long_press_name, 0, BUTTON_NAME_MAX_LEN);
    strncpy(btn->long_press_name, name, BUTTON_NAME_MAX_LEN - 1);
    xSemaphoreGive(profile_mutex);
    return ESP_OK;
}

// ---- Folder button setters -----------------------------------------------

esp_err_t profile_set_folder_button_action(uint8_t folder_id,
                                            uint8_t button_id, uint8_t action_type,
                                            const uint8_t* action_data, uint16_t action_len) {
    if (folder_id >= NUM_FOLDERS || button_id >= NUM_BUTTONS)
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

esp_err_t profile_set_folder_button_name(uint8_t folder_id,
                                          uint8_t button_id, const char* name) {
    if (folder_id >= NUM_FOLDERS || button_id >= NUM_BUTTONS || name == NULL)
        return ESP_ERR_INVALID_ARG;

    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    button_config_t* btn = &current_profile.folders[folder_id].buttons[button_id];
    memset(btn->name, 0, BUTTON_NAME_MAX_LEN);
    strncpy(btn->name, name, BUTTON_NAME_MAX_LEN - 1);
    xSemaphoreGive(profile_mutex);
    return ESP_OK;
}

esp_err_t profile_set_folder_button_led(uint8_t folder_id,
                                         uint8_t button_id,
                                         uint8_t r, uint8_t g, uint8_t b,
                                         uint8_t brightness, uint8_t effect) {
    if (folder_id >= NUM_FOLDERS || button_id >= NUM_BUTTONS)
        return ESP_ERR_INVALID_ARG;

    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    button_config_t* btn = &current_profile.folders[folder_id].buttons[button_id];
    btn->led_r = r; btn->led_g = g; btn->led_b = b;
    btn->led_brightness = brightness; btn->led_effect = effect;
    xSemaphoreGive(profile_mutex);
    return ESP_OK;
}


esp_err_t profile_set_button_name(uint8_t button_id, const char* name) {
    if (button_id >= NUM_BUTTONS || name == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    xSemaphoreTake(profile_mutex, portMAX_DELAY);

    button_config_t* btn = &current_profile.buttons[button_id];
    memset(btn->name, 0, BUTTON_NAME_MAX_LEN);
    strncpy(btn->name, name, BUTTON_NAME_MAX_LEN - 1);

    // Only do a text refresh when there is genuinely no image:
    //   image_size > 0  → image is in SPIFFS; name change only affects text/split rendering
    //   image_size == 0 AND cache non-NULL → image was just uploaded and is still being
    //     written to SPIFFS by save_task (async); the display already shows the correct
    //     decoded image drawn by display_post_draw in image_transfer_end.  Triggering a
    //     refresh here would call render_full_display with image_size==0, which takes the
    //     text path and overwrites the correct image with a black/text render.
    bool should_refresh = (btn->image_size == 0) && (s_display_cache[button_id] == NULL);

    xSemaphoreGive(profile_mutex);

    // Refresh display after releasing the mutex so SPI operations don't run under profile_mutex.
    if (should_refresh) {
        profile_update_button_display(button_id, &current_profile.buttons[button_id]);
    }

    return ESP_OK;
}

esp_err_t profile_set_led_color(uint8_t button_id,
                                 uint8_t r, uint8_t g, uint8_t b,
                                 uint8_t brightness, uint8_t effect) {
    if (button_id >= NUM_BUTTONS) {
        return ESP_ERR_INVALID_ARG;
    }

    xSemaphoreTake(profile_mutex, portMAX_DELAY);

    button_config_t* btn = &current_profile.buttons[button_id];
    btn->led_r = r;
    btn->led_g = g;
    btn->led_b = b;
    btn->led_brightness = brightness;
    btn->led_effect = effect;

    // Update LED immediately, but not while night mode is active
    // (night mode exit will call profile_restore_leds() to pick up the new color)
    if (!night_mode_is_active() && !profile_is_in_folder()) {
        uint8_t hw_brightness = (btn->action_type == ACTION_TYPE_NONE) ? 0 : brightness;
        led_set_color(button_id, r, g, b, hw_brightness);
        led_update();
    }

    xSemaphoreGive(profile_mutex);
    
    return ESP_OK;
}

esp_err_t profile_save_to_storage(void) {
    xSemaphoreTake(profile_mutex, portMAX_DELAY);

    // Calculate CRC
    current_profile.crc32 = crc32_calculate((uint8_t*)&current_profile,
                                             sizeof(profile_t) - sizeof(uint32_t));

    esp_err_t ret = profile_storage_save(0, &current_profile);

    xSemaphoreGive(profile_mutex);

    return ret;
}

esp_err_t profile_create_defaults(void) {
    ESP_LOGI(TAG, "Creating default profile");

    vTaskDelay(pdMS_TO_TICKS(100));

    profile_t profile;
    memset(&profile, 0, sizeof(profile));

    profile.profile_id = 0;
    snprintf(profile.name, sizeof(profile.name), "Profile 1");

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
        btn->led_r = (hue < 85) ? (255 - hue * 3) : ((hue < 170) ? 0 : ((hue - 170) * 3));
        btn->led_g = (hue < 85) ? (hue * 3) : ((hue < 170) ? (255 - (hue - 85) * 3) : 0);
        btn->led_b = (hue < 85) ? 0 : ((hue < 170) ? ((hue - 85) * 3) : (255 - (hue - 170) * 3));
        btn->led_brightness = LED_DEFAULT_BRIGHTNESS;
        btn->led_effect = LED_EFFECT_STATIC;
    }

    // Calculate CRC
    profile.crc32 = crc32_calculate((uint8_t*)&profile,
                                     sizeof(profile_t) - sizeof(uint32_t));

    ESP_LOGI(TAG, "Saving default profile");
    profile_storage_save(0, &profile);

    vTaskDelay(pdMS_TO_TICKS(200));

    ESP_LOGI(TAG, "Default profile created");
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
// Core action-type → display string mapping shared by short and long press.
// folder_id is used only for ACTION_TYPE_FOLDER; pass 0xFF when not applicable
// (renders "Folder" without a number). Leaves out[0] unchanged for unknown types
// so the caller can set a fallback.
static void build_action_label(uint8_t action_type, const uint8_t* data, uint16_t data_len,
                                uint8_t folder_id, char* out, size_t out_size)
{
    switch (action_type) {
        case ACTION_TYPE_KEYBOARD:
            if (data_len >= 2) {
                uint8_t modifier = data[0];
                uint8_t keycode  = data[1];
                if (keycode != 0) {
                    char mod_str[16] = {0};
                    if (modifier & 0x01) strncat(mod_str, "Ctrl+", sizeof(mod_str) - strlen(mod_str) - 1);
                    if (modifier & 0x02) strncat(mod_str, "Sft+",  sizeof(mod_str) - strlen(mod_str) - 1);
                    if (modifier & 0x04) strncat(mod_str, "Alt+",  sizeof(mod_str) - strlen(mod_str) - 1);
                    if (modifier & 0x08) strncat(mod_str, "GUI+",  sizeof(mod_str) - strlen(mod_str) - 1);
                    if (mod_str[0])
                        snprintf(out, out_size, "%s\n%s", mod_str, hid_keycode_name(keycode));
                    else
                        snprintf(out, out_size, "%s", hid_keycode_name(keycode));
                } else if (data_len > 7) {
                    int show = data_len - 7;
                    if (show > 10) show = 10;
                    char preview[11];
                    memcpy(preview, data + 7, show);
                    preview[show] = '\0';
                    snprintf(out, out_size, "Type\n%s", preview);
                } else {
                    snprintf(out, out_size, "Key");
                }
            } else {
                snprintf(out, out_size, "Key");
            }
            break;

        case ACTION_TYPE_MEDIA:
            if (data_len >= 2) {
                uint16_t usage = (uint16_t)data[0] | ((uint16_t)data[1] << 8);
                snprintf(out, out_size, "%s", media_key_name(usage));
            } else {
                snprintf(out, out_size, "Media");
            }
            break;

        case ACTION_TYPE_FOLDER:
            if (folder_id != 0xFF)
                snprintf(out, out_size, "Folder\n%d", folder_id + 1);
            else
                snprintf(out, out_size, "Folder");
            break;

        case ACTION_TYPE_DELAY:
            if (data_len >= 2) {
                uint16_t ms = (uint16_t)data[0] | ((uint16_t)data[1] << 8);
                snprintf(out, out_size, "Delay\n%dms", ms);
            } else {
                snprintf(out, out_size, "Delay");
            }
            break;

        case ACTION_TYPE_SHELL: {
            const char* cmd = (data_len > 1) ? (const char*)(data + 1) : (const char*)data;
            int show = 0;
            while (show < 12 && cmd[show]) show++;
            char preview[13];
            memcpy(preview, cmd, show);
            preview[show] = '\0';
            snprintf(out, out_size, "$\n%s", preview);
            break;
        }

        case ACTION_TYPE_LAUNCH_APP: {
            const char* path = (const char*)data;
            const char* base = path;
            for (const char* p = path; *p; p++)
                if (*p == '/' || *p == '\\') base = p + 1;
            int show = 0;
            while (show < 12 && base[show]) show++;
            char preview[13];
            memcpy(preview, base, show);
            preview[show] = '\0';
            snprintf(out, out_size, "App\n%s", preview);
            break;
        }

        case ACTION_TYPE_SEQUENCE:
            snprintf(out, out_size, data_len >= 1 ? "Seq\n%d steps" : "Sequence",
                     data_len >= 1 ? data[0] : 0);
            break;

        case ACTION_TYPE_CUSTOM_HID:   snprintf(out, out_size, "HID");    break;
        case ACTION_TYPE_NIGHT_MODE:   snprintf(out, out_size, "Night");  break;
        case ACTION_TYPE_PLUGIN:       snprintf(out, out_size, "Plugin"); break;

        default: break;  // caller sets fallback
    }
}

static void generate_action_label(const button_config_t* btn, char* label, size_t label_size) {
    if (btn->action_type == ACTION_TYPE_NONE) {
        snprintf(label, label_size, "Empty");
        return;
    }
    label[0] = '\0';
    build_action_label(btn->action_type, btn->action_data, btn->action_data_len,
                       btn->folder_id, label, label_size);
    if (label[0] == '\0')
        snprintf(label, label_size, "0x%02X", btn->action_type);
}

// Build a short display label for the long press action.
static void generate_long_press_label(const button_config_t* btn, char* label, size_t label_size) {
    label[0] = '\0';
    if (btn->long_press_action_type == ACTION_TYPE_NONE) return;
    build_action_label(btn->long_press_action_type, btn->long_press_action_data,
                       btn->long_press_action_data_len, 0xFF, label, label_size);
    if (label[0] == '\0')
        snprintf(label, label_size, "Hold");
}

void profile_restore_leds(void) {
    xSemaphoreTake(profile_mutex, portMAX_DELAY);
    for (int i = 0; i < NUM_BUTTONS; i++) {
        button_config_t* btn = &current_profile.buttons[i];
        led_set_color(i, btn->led_r, btn->led_g, btn->led_b, BTN_LED_BRIGHTNESS(btn));
    }
    led_update();
    xSemaphoreGive(profile_mutex);
}

void profile_refresh_displays(void) {
    if (folder_stack_depth > 0) {
        // Inside a folder: refresh using folder buttons and restore folder LEDs.
        // profile_update_button_display already handles the back icon for folder_entry_button.
        uint8_t current_folder_id = folder_stack[folder_stack_depth - 1];
        folder_t* folder = &current_profile.folders[current_folder_id];
        // Phase 1: update all LEDs at once so they change instantly.
        for (int i = 0; i < NUM_BUTTONS; i++) {
            button_config_t* btn = &folder->buttons[i];
            led_set_color(i, btn->led_r, btn->led_g, btn->led_b, BTN_LED_BRIGHTNESS(btn));
        }
        led_update();
        // Phase 2: render displays one by one.
        for (int i = 0; i < NUM_BUTTONS; i++) {
            button_config_t* btn = &folder->buttons[i];
            profile_update_button_display(i, btn);
            vTaskDelay(pdMS_TO_TICKS(2));
        }
        return;
    }

    for (int i = 0; i < NUM_BUTTONS; i++) {
        profile_update_button_display(i, &current_profile.buttons[i]);
        vTaskDelay(pdMS_TO_TICKS(2));
    }
}

// ── Resolve storage_bid and cache_slot for the current folder context ─────────
static uint8_t resolve_storage_bid(uint8_t button_id) {
    uint8_t cur_folder = profile_get_current_folder();
    return (cur_folder == 0xFF)
        ? button_id
        : (uint8_t)(NUM_BUTTONS + cur_folder * NUM_BUTTONS + button_id);
}

static uint8_t** resolve_cache_slot(uint8_t button_id) {
    uint8_t cur_folder = profile_get_current_folder();
    return (cur_folder == 0xFF)
        ? &s_display_cache[button_id]
        : &s_folder_display_cache[button_id];
}

// ── Load image into cache if needed, return pointer or NULL ───────────────────
static uint8_t* load_image_cached(uint8_t button_id) {
    uint8_t** cache_slot = resolve_cache_slot(button_id);
    if (*cache_slot != NULL) return *cache_slot;

    uint8_t* jpeg_data = NULL;
    size_t jpeg_size = 0;
    if (image_storage_load(0, resolve_storage_bid(button_id), &jpeg_data, &jpeg_size) != ESP_OK
        || jpeg_data == NULL) {
        free(jpeg_data);
        return NULL;
    }

    // Stale RGB565 guard: JPEG always starts with 0xFF 0xD8
    if (jpeg_size < 2 || jpeg_data[0] != 0xFF || jpeg_data[1] != 0xD8) {
        ESP_LOGW(TAG, "button %u: stored data is not JPEG (stale RGB565?), skipping", button_id);
        free(jpeg_data);
        return NULL;
    }

    uint8_t* rgb565 = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_SPIRAM);
    if (!rgb565) { free(jpeg_data); return NULL; }

    uint16_t w = 0, h = 0;
    esp_err_t ret = jpeg_decode_to_rgb565(jpeg_data, jpeg_size, rgb565, DISPLAY_BUFFER_SIZE, &w, &h);
    free(jpeg_data);

    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "JPEG decode failed for button %u: %s", button_id, esp_err_to_name(ret));
        free(rgb565);
        return NULL;
    }

    *cache_slot = rgb565;
    return rgb565;
}

// ── Split display: top = short-press content, bottom = long-press label ───────
static void render_split_display(uint8_t button_id, button_config_t* btn) {
    if (button_id == 0) {
        ESP_LOGI(TAG, "render_split[0]: action=0x%02X lp=0x%02X cache=%s image_size=%lu",
                 btn->action_type, btn->long_press_action_type,
                 (resolve_cache_slot(button_id) && *resolve_cache_slot(button_id)) ? "yes" : "no",
                 (unsigned long)btn->image_size);
    }
    char lp_label[64] = {0};
    if (btn->long_press_name[0] != '\0')
        strncpy(lp_label, btn->long_press_name, sizeof(lp_label) - 1);
    else
        generate_long_press_label(btn, lp_label, sizeof(lp_label));

    uint8_t* frame = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_DMA | MALLOC_CAP_SPIRAM);
    if (!frame) { gc9a01_clear(button_id, COLOR_BLACK); return; }

    // Top SPLIT_SHORT_H px: image (from cache or SPIFFS) or text label.
    // load_image_cached checks the PSRAM cache first so a freshly uploaded image
    // (cache populated, image_size still 0 while save_task is running) is shown
    // correctly without waiting for the async SPIFFS write to finish.
    uint8_t* img = load_image_cached(button_id);
    if (img != NULL) {
        memcpy(frame, img, (size_t)DISPLAY_WIDTH * SPLIT_SHORT_H * 2);
    } else {
        char label[64] = {0};
        if (btn->name[0] != '\0')
            strncpy(label, btn->name, sizeof(label) - 1);
        else if (btn->action_type != ACTION_TYPE_NONE)
            generate_action_label(btn, label, sizeof(label));
        text_render_fill_region(frame, DISPLAY_WIDTH, 0, SPLIT_SHORT_H,
                                label[0] ? label : NULL, COLOR_WHITE, COLOR_BLACK);
    }

    // Bottom SPLIT_LONG_H px: long-press label on gray background
    text_render_fill_region(frame, DISPLAY_WIDTH, SPLIT_SHORT_H, SPLIT_LONG_H,
                            lp_label, COLOR_WHITE, COLOR_GRAY_LP);

    // 2-px white divider
    uint8_t div_hi = COLOR_DIVIDER_LP >> 8, div_lo = COLOR_DIVIDER_LP & 0xFF;
    uint8_t* div_ptr = frame + (size_t)DISPLAY_WIDTH * (SPLIT_SHORT_H - 2) * 2;
    for (size_t i = 0; i < (size_t)DISPLAY_WIDTH * 2 * 2; i += 2) {
        div_ptr[i] = div_hi; div_ptr[i + 1] = div_lo;
    }

    gc9a01_draw_image(button_id, frame, DISPLAY_WIDTH, DISPLAY_HEIGHT);
    free(frame);
}

// ── Full-screen display: image or text label ──────────────────────────────────
static void render_full_display(uint8_t button_id, button_config_t* btn) {
    if (button_id == 0) {
        ESP_LOGI(TAG, "render_full[0]: action=0x%02X lp=0x%02X cache=%s image_size=%lu",
                 btn->action_type, btn->long_press_action_type,
                 (resolve_cache_slot(button_id) && *resolve_cache_slot(button_id)) ? "yes" : "no",
                 (unsigned long)btn->image_size);
    }

    // Unassigned buttons have no image and no label — just black.
    // Skip cache/SPIFFS entirely, mirrors BTN_LED_BRIGHTNESS returning 0 for NONE.
    if (btn->action_type == ACTION_TYPE_NONE) {
        gc9a01_clear(button_id, COLOR_BLACK);
        return;
    }

    // A non-NULL cache entry is authoritative: it holds a freshly decoded image that
    // may not yet be reflected in image_size (save_task writes SPIFFS asynchronously).
    // Check cache before image_size so we never fall through to the text path while a
    // real image is sitting in the cache waiting for save_task to finish.
    uint8_t** cache_slot = resolve_cache_slot(button_id);
    if (*cache_slot != NULL) {
        gc9a01_draw_image(button_id, *cache_slot, DISPLAY_WIDTH, DISPLAY_HEIGHT);
        return;
    }

    if (btn->image_size > 0) {
        uint8_t* jpeg_data = NULL;
        size_t jpeg_size = 0;
        bool loaded = (image_storage_load(0, resolve_storage_bid(button_id), &jpeg_data, &jpeg_size) == ESP_OK
                       && jpeg_data != NULL);

        // Stale RGB565 guard: JPEG always starts with 0xFF 0xD8
        if (loaded && (jpeg_size < 2 || jpeg_data[0] != 0xFF || jpeg_data[1] != 0xD8)) {
            ESP_LOGW(TAG, "render_full[%u]: stored data not JPEG (stale RGB565?)", button_id);
            loaded = false;
        }

        if (loaded) {
            uint8_t* rgb565 = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_SPIRAM);
            if (rgb565) {
                uint16_t w = 0, h = 0;
                if (jpeg_decode_to_rgb565(jpeg_data, jpeg_size, rgb565, DISPLAY_BUFFER_SIZE, &w, &h) == ESP_OK) {
                    gc9a01_draw_image(button_id, rgb565, DISPLAY_WIDTH, DISPLAY_HEIGHT);
                    *cache_slot = rgb565;   // cache takes ownership
                } else {
                    ESP_LOGE(TAG, "render_full[%u]: JPEG decode failed", button_id);
                    free(rgb565);
                    gc9a01_clear(button_id, COLOR_BLACK);
                }
            } else {
                gc9a01_clear(button_id, COLOR_BLACK);
            }
        } else {
            gc9a01_clear(button_id, COLOR_BLACK);
        }
        free(jpeg_data);
        return;
    }

    char label[64] = {0};
    if (btn->name[0] != '\0')
        strncpy(label, btn->name, sizeof(label) - 1);
    else if (btn->action_type != ACTION_TYPE_NONE)
        generate_action_label(btn, label, sizeof(label));

    if (label[0] != '\0') {
        if (text_render_to_display(button_id, label, COLOR_WHITE, COLOR_BLACK) != ESP_OK)
            gc9a01_clear(button_id, COLOR_BLACK);
    } else {
        gc9a01_clear(button_id, COLOR_BLACK);
    }
}

// ── Dispatcher ────────────────────────────────────────────────────────────────
static void profile_update_button_display(uint8_t button_id, button_config_t* btn) {
    if (button_id >= NUM_BUTTONS || btn == NULL) return;
    // Entry button always shows back icon while inside a folder.
    // (folder_stack_depth is decremented before the render loop in profile_folder_exit,
    //  so this check is false on exit and the button renders its root content normally.)
    if (profile_is_in_folder() && button_id == folder_entry_button) {
        profile_show_back_icon(button_id);
        return;
    }
    if (btn->long_press_action_type != ACTION_TYPE_NONE)
        render_split_display(button_id, btn);
    else
        render_full_display(button_id, btn);
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

    // Clear folder image cache only when switching between two PREVIOUSLY VISITED folders.
    // When s_cached_folder_id is 0xFF (never entered any folder this session), the cache
    // was just populated by image_transfer_end during the profile send — clearing it would
    // force expensive SPIFFS re-reads (~300 ms/image) on first folder entry.
    if (s_cached_folder_id != 0xFF && s_cached_folder_id != folder_id) {
        folder_display_cache_clear();
    }
    s_cached_folder_id = folder_id;

    // Phase 1: set all LED colours and flush to hardware immediately.
    // Doing this before any display SPI work means the LEDs visually respond
    // to the button press in ~1 ms instead of after all images are rendered.
    button_config_t* root_back_btn = &current_profile.buttons[entry_button_id];
    for (int i = 0; i < NUM_BUTTONS; i++) {
        button_config_t* btn = &folder->buttons[i];
        // Back button slot: keep the root button's LED colour on the exit key.
        if (i == entry_button_id) {
            led_set_color(i, root_back_btn->led_r, root_back_btn->led_g, root_back_btn->led_b,
                          BTN_LED_BRIGHTNESS(root_back_btn));
        } else {
            led_set_color(i, btn->led_r, btn->led_g, btn->led_b, BTN_LED_BRIGHTNESS(btn));
        }
    }
    led_update();

    // Phase 2: render displays one by one (slower — may involve SPIFFS or SPI).
    for (int i = 0; i < NUM_BUTTONS; i++) {
        button_config_t* btn = &folder->buttons[i];
        profile_update_button_display(i, btn);
    }
    
    xSemaphoreGive(profile_mutex);
    
    // Send folder entered event to PC
    {
        uint8_t evt_payload[4];
        evt_payload[0] = folder_id;
        evt_payload[1] = folder_stack_depth;
        evt_payload[2] = 0; // profile_id (always 0)
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

    // Keep s_folder_display_cache alive — re-entry into the same folder will be instant.

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
    
    // Phase 1: flush all LED colours before any SPI display work.
    for (int i = 0; i < NUM_BUTTONS; i++) {
        button_config_t* btn = &buttons[i];
        led_set_color(i, btn->led_r, btn->led_g, btn->led_b, BTN_LED_BRIGHTNESS(btn));
    }
    led_update();

    // Phase 2: render displays one by one.
    for (int i = 0; i < NUM_BUTTONS; i++) {
        profile_update_button_display(i, &buttons[i]);
    }
    
    xSemaphoreGive(profile_mutex);
    
    // Send folder exited event to PC
    {
        uint8_t evt_payload[4];
        evt_payload[0] = exited_folder;
        evt_payload[1] = folder_stack_depth;
        evt_payload[2] = 0; // profile_id (always 0)
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
