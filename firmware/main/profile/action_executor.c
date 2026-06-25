/**
 * @file action_executor.c
 * @brief Button action executor implementation
 */

#include "common.h"
#include "action_executor.h"
#include "profile_manager.h"
#include "usb/usb_hid_keyboard.h"
#include "protocol/protocol_handler.h"
#include "protocol/protocol_types.h"
#include "hardware/night_mode.h"
#include "storage/text_storage.h"
#include "config.h"

static const char* TAG = "ACTION";

// Track which button was used to enter current folder (for toggle exit)
static uint8_t folder_entry_button_id = 0xFF;

// SPIFFS lookup key — passed through the call chain so every action context
// (direct press, long press, sequence step) uses the right file slot.
typedef struct {
    uint8_t folder_id;    // 0xFF = root
    uint8_t button_id;    // 0xFF = encoder (SPIFFS unsupported)
    uint8_t action_slot;  // TEXT_ACTION_SHORT / TEXT_ACTION_LONG
    uint8_t step_index;   // TEXT_STEP_DIRECT / TEXT_STEP_SEQ_BLOB / 0..15
} text_key_t;

// Forward declarations
static esp_err_t execute_single_action(action_type_t type, const uint8_t* data,
                                        uint16_t data_len, uint8_t button_id,
                                        const text_key_t* tkey);
static esp_err_t execute_sequence(const action_sequence_t* seq,
                                   uint8_t button_id, uint8_t action_slot);
static esp_err_t execute_sequence_from_blob(const uint8_t* data, uint16_t data_len,
                                             uint8_t button_id, uint8_t action_slot);
static esp_err_t execute_sequence_from_spiffs(const text_key_t* tkey);

// Execute a single action — the single dispatcher for all action types and all call sites.
static esp_err_t execute_single_action(action_type_t type, const uint8_t* data,
                                        uint16_t data_len, uint8_t button_id,
                                        const text_key_t* tkey) {
    switch (type) {
        case ACTION_TYPE_NONE:
            ESP_LOGD(TAG, "No action configured");
            break;

        case ACTION_TYPE_KEYBOARD: {
            if (data_len >= 2) {
                uint8_t modifier = data[0];
                uint8_t keycode  = data[1];

                if (keycode != 0) {
                    usb_hid_keyboard_press(modifier, keycode);
                } else if (data_len >= 3 && data[2] == 0x01) {
                    // Long text in SPIFFS — stream directly without a heap buffer
                    if (tkey->button_id == 0xFF) {
                        ESP_LOGE(TAG, "SPIFFS text not supported for encoder");
                        return ESP_ERR_NOT_SUPPORTED;
                    }
                    FILE* f = text_storage_open_for_read(0, tkey->folder_id, tkey->button_id,
                                                          tkey->action_slot, tkey->step_index);
                    if (f) {
                        char chunk[64];
                        size_t n;
                        while ((n = fread(chunk, 1, sizeof(chunk) - 1, f)) > 0) {
                            chunk[n] = '\0';
                            usb_hid_keyboard_type_text(chunk);
                        }
                        fclose(f);
                    } else {
                        ESP_LOGE(TAG, "SPIFFS text not found (btn=%d slot=%d step=0x%02X)",
                                 tkey->button_id, tkey->action_slot, tkey->step_index);
                    }
                } else if (data_len > 7) {
                    // Inline text (up to 44 bytes after the 7-byte keyboard header)
                    char text_buf[ACTION_DATA_MAX_LEN - 7 + 1];
                    uint16_t text_len = data_len - 7;
                    if (text_len > sizeof(text_buf) - 1) text_len = sizeof(text_buf) - 1;
                    memcpy(text_buf, &data[7], text_len);
                    text_buf[text_len] = '\0';
                    ESP_LOGI(TAG, "Typing inline text: '%s' (%d chars)", text_buf, text_len);
                    usb_hid_keyboard_type_text(text_buf);
                } else {
                    ESP_LOGW(TAG, "Keyboard action with keycode=0 and no text data");
                }
            }
            break;
        }

        case ACTION_TYPE_CUSTOM_HID:
            if (data_len > 0)
                usb_hid_send_raw_report(data, data_len);
            break;

        case ACTION_TYPE_FOLDER: {
            uint8_t folder_id = (data_len >= 1) ? data[0] : 0;
            if (profile_is_in_folder() && folder_entry_button_id == button_id) {
                ESP_LOGI(TAG, "Exiting folder via toggle button %d", button_id);
                profile_folder_exit();
                folder_entry_button_id = 0xFF;
            } else {
                ESP_LOGI(TAG, "Entering folder %d via button %d", folder_id, button_id);
                esp_err_t ret = profile_folder_enter(folder_id, button_id);
                if (ret == ESP_OK) folder_entry_button_id = button_id;
            }
            break;
        }

        case ACTION_TYPE_DELAY:
            if (data_len >= 2) {
                uint16_t delay_ms = data[0] | (data[1] << 8);
                ESP_LOGD(TAG, "Delay: %d ms", delay_ms);
                vTaskDelay(pdMS_TO_TICKS(delay_ms));
            }
            break;

        case ACTION_TYPE_NIGHT_MODE:
            night_mode_toggle(button_id);
            break;

        case ACTION_TYPE_MEDIA:
            if (data_len >= 2) {
                uint16_t usage_code = data[0] | (data[1] << 8);
                ESP_LOGI(TAG, "Media key: usage code 0x%04X", usage_code);
                usb_hid_consumer_press(usage_code);
            } else {
                ESP_LOGW(TAG, "Media action: need 2 bytes for usage code");
            }
            break;

        case ACTION_TYPE_SHELL:
            ESP_LOGI(TAG, "Shell action forwarded to PC via button event");
            break;

        case ACTION_TYPE_LAUNCH_APP:
            ESP_LOGI(TAG, "LaunchApp action forwarded to PC via button event");
            break;

        case ACTION_TYPE_PLUGIN:
            ESP_LOGD(TAG, "Plugin action forwarded to PC via button event");
            break;

        case ACTION_TYPE_SEQUENCE:
            // data[0]==0x01 and data_len==1 → blob stored in SPIFFS
            if (data_len == 1 && data[0] == 0x01)
                return execute_sequence_from_spiffs(tkey);
            return execute_sequence_from_blob(data, data_len, button_id, tkey->action_slot);

        default:
            ESP_LOGW(TAG, "Unknown action type: %d", type);
            return ESP_ERR_INVALID_ARG;
    }

    return ESP_OK;
}

esp_err_t action_execute_raw(action_type_t type, const uint8_t* data, uint16_t data_len) {
    const text_key_t tkey = {
        .folder_id   = 0xFF,
        .button_id   = 0xFF,
        .action_slot = TEXT_ACTION_SHORT,
        .step_index  = TEXT_STEP_DIRECT
    };
    return execute_single_action(type, data, data_len, 0xFF, &tkey);
}

// Run a deserialized sequence — each step gets its own text_key with the correct step_index.
static esp_err_t execute_sequence(const action_sequence_t* seq,
                                   uint8_t button_id, uint8_t action_slot) {
    if (seq == NULL || seq->num_steps == 0) return ESP_ERR_INVALID_ARG;

    ESP_LOGI(TAG, "Executing sequence with %d steps", seq->num_steps);

    for (int i = 0; i < seq->num_steps && i < MAX_SEQUENCE_STEPS; i++) {
        const sequence_step_t* step = &seq->steps[i];

        if (step->delay_before_ms > 0) {
            ESP_LOGD(TAG, "Step %d: delay %d ms", i, step->delay_before_ms);
            vTaskDelay(pdMS_TO_TICKS(step->delay_before_ms));
        }

        text_key_t tkey = {
            .folder_id   = profile_get_current_folder(),
            .button_id   = button_id,
            .action_slot = action_slot,
            .step_index  = (uint8_t)i
        };

        ESP_LOGI(TAG, "Step %d: type=%d", i, step->action_type);
        esp_err_t ret = execute_single_action(step->action_type, step->action_data,
                                               step->action_data_len, button_id, &tkey);
        if (ret != ESP_OK)
            ESP_LOGE(TAG, "Step %d failed: %s", i, esp_err_to_name(ret));
    }

    ESP_LOGI(TAG, "Sequence completed");
    return ESP_OK;
}

// Read a sequence blob from SPIFFS into a stack buffer and execute it.
// Used when action_data == [0x01] (blob too large for inline storage).
static esp_err_t execute_sequence_from_spiffs(const text_key_t* tkey) {
    FILE* f = text_storage_open_for_read(0, tkey->folder_id, tkey->button_id,
                                          tkey->action_slot, TEXT_STEP_SEQ_BLOB);
    if (!f) {
        ESP_LOGE(TAG, "Sequence blob not found in SPIFFS (btn=%d slot=%d)",
                 tkey->button_id, tkey->action_slot);
        return ESP_ERR_NOT_FOUND;
    }

    // Max blob: 1 + 16*(1+2+2+51) = 897 bytes — 256 covers practical cases with SPIFFS-only steps
    uint8_t blob[256];
    size_t blob_len = fread(blob, 1, sizeof(blob), f);
    fclose(f);

    return execute_sequence_from_blob(blob, (uint16_t)blob_len,
                                      tkey->button_id, tkey->action_slot);
}

// Parse a sequence blob and execute it.
static esp_err_t execute_sequence_from_blob(const uint8_t* data, uint16_t data_len,
                                             uint8_t button_id, uint8_t action_slot) {
    if (data_len < 1) {
        ESP_LOGW(TAG, "Sequence blob is empty");
        return ESP_ERR_INVALID_ARG;
    }

    action_sequence_t seq;
    memset(&seq, 0, sizeof(seq));

    const uint8_t* ptr = data;
    const uint8_t* end = data + data_len;

    seq.num_steps = *ptr++;
    if (seq.num_steps > MAX_SEQUENCE_STEPS) seq.num_steps = MAX_SEQUENCE_STEPS;

    for (int i = 0; i < seq.num_steps && ptr < end; i++) {
        sequence_step_t* step = &seq.steps[i];

        if (ptr + 5 > end) break;

        step->action_type     = (action_type_t)*ptr++;
        step->delay_before_ms = ptr[0] | (ptr[1] << 8); ptr += 2;
        step->action_data_len = ptr[0] | (ptr[1] << 8); ptr += 2;

        if (step->action_data_len > ACTION_DATA_MAX_LEN)
            step->action_data_len = ACTION_DATA_MAX_LEN;
        if (ptr + step->action_data_len > end)
            step->action_data_len = (uint16_t)(end - ptr);

        memcpy(step->action_data, ptr, step->action_data_len);
        ptr += step->action_data_len;
    }

    return execute_sequence(&seq, button_id, action_slot);
}

esp_err_t action_execute_long_press(uint8_t button_id) {
    if (button_id >= NUM_BUTTONS) return ESP_ERR_INVALID_ARG;
    button_config_t* btn = profile_get_button_config(button_id);
    if (btn == NULL) return ESP_FAIL;

    if (btn->long_press_action_type == ACTION_TYPE_NONE) {
        ESP_LOGD(TAG, "No long press action for button %d", button_id);
        return ESP_OK;
    }

    ESP_LOGI(TAG, "Executing long press for button %d, type=%d",
             button_id, btn->long_press_action_type);

    text_key_t tkey = {
        .folder_id   = profile_get_current_folder(),
        .button_id   = button_id,
        .action_slot = TEXT_ACTION_LONG,
        .step_index  = TEXT_STEP_DIRECT
    };
    return execute_single_action(btn->long_press_action_type, btn->long_press_action_data,
                                 btn->long_press_action_data_len, button_id, &tkey);
}

esp_err_t action_execute(uint8_t button_id) {
    if (button_id >= NUM_BUTTONS) return ESP_ERR_INVALID_ARG;

    // The button that opened the folder exits it on short press, regardless of its config.
    if (profile_is_in_folder() && folder_entry_button_id == button_id) {
        ESP_LOGI(TAG, "Exiting folder via back button %d", button_id);
        profile_folder_exit();
        folder_entry_button_id = 0xFF;
        return ESP_OK;
    }

    button_config_t* btn = profile_get_button_config(button_id);
    if (btn == NULL) return ESP_FAIL;

    ESP_LOGI(TAG, "Executing action for button %d, type=%d", button_id, btn->action_type);

    text_key_t tkey = {
        .folder_id   = profile_get_current_folder(),
        .button_id   = button_id,
        .action_slot = TEXT_ACTION_SHORT,
        .step_index  = TEXT_STEP_DIRECT
    };
    return execute_single_action(btn->action_type, btn->action_data,
                                 btn->action_data_len, button_id, &tkey);
}
