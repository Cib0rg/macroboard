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
#include "config.h"

static const char* TAG = "ACTION";

// Track which button was used to enter current folder (for toggle exit)
static uint8_t folder_entry_button_id = 0xFF;

// Forward declarations for internal functions
static esp_err_t execute_single_action(action_type_t type, const uint8_t* data, uint16_t data_len, uint8_t button_id);
static esp_err_t execute_sequence(const action_sequence_t* seq, uint8_t button_id);

/**
 * @brief Execute a single action (used by both direct actions and sequence steps)
 */
static esp_err_t execute_single_action(action_type_t type, const uint8_t* data, uint16_t data_len, uint8_t button_id) {
    switch (type) {
        case ACTION_TYPE_NONE:
            ESP_LOGD(TAG, "No action configured");
            break;
            
        case ACTION_TYPE_KEYBOARD: {
            if (data_len >= 2) {
                uint8_t modifier = data[0];
                uint8_t keycode = data[1];
                
                if (keycode != 0) {
                    usb_hid_keyboard_press(modifier, keycode);
                } else if (data_len > 7) {
                    char text_buf[ACTION_DATA_MAX_LEN - 7 + 1];
                    uint16_t text_len = data_len - 7;
                    if (text_len > sizeof(text_buf) - 1) {
                        text_len = sizeof(text_buf) - 1;
                    }
                    memcpy(text_buf, &data[7], text_len);
                    text_buf[text_len] = '\0';
                    
                    ESP_LOGI(TAG, "Typing text: '%s' (%d chars)", text_buf, text_len);
                    usb_hid_keyboard_type_text(text_buf);
                } else {
                    ESP_LOGW(TAG, "Keyboard action with keycode=0 and no text data");
                }
            }
            break;
        }
        
        case ACTION_TYPE_CUSTOM_HID: {
            uint8_t payload[PROTOCOL_PAYLOAD_SIZE];
            payload[0] = button_id;
            payload[1] = profile_get_current_id();
            payload[2] = type;
            
            uint16_t copy_len = (data_len < 50) ? data_len : 50;
            memcpy(&payload[3], data, copy_len);
            
            protocol_send_event(EVENT_BUTTON_PRESSED, payload, 3 + copy_len);
            break;
        }
        
        case ACTION_TYPE_PROFILE_SWITCH: {
            if (data_len >= 1) {
                uint8_t target_profile = data[0];
                profile_switch(target_profile);
            }
            break;
        }
        
        case ACTION_TYPE_FOLDER: {
            // Note: folder_id should be passed in data[0] for sequence steps
            uint8_t folder_id = (data_len >= 1) ? data[0] : 0;
            
            if (profile_is_in_folder() && folder_entry_button_id == button_id) {
                ESP_LOGI(TAG, "Exiting folder via toggle button %d", button_id);
                profile_folder_exit();
                folder_entry_button_id = 0xFF;
            } else {
                ESP_LOGI(TAG, "Entering folder %d via button %d", folder_id, button_id);
                esp_err_t ret = profile_folder_enter(folder_id, button_id);
                if (ret == ESP_OK) {
                    folder_entry_button_id = button_id;
                }
            }
            break;
        }
        
        case ACTION_TYPE_DELAY: {
            // Delay action - extract delay_ms from data
            if (data_len >= 2) {
                uint16_t delay_ms = data[0] | (data[1] << 8);
                ESP_LOGD(TAG, "Delay: %d ms", delay_ms);
                vTaskDelay(pdMS_TO_TICKS(delay_ms));
            }
            break;
        }
        
        case ACTION_TYPE_NIGHT_MODE:
            night_mode_toggle();
            break;

        case ACTION_TYPE_MEDIA: {
            // Media key action - send Consumer Control HID report
            if (data_len >= 2) {
                uint16_t usage_code = data[0] | (data[1] << 8);
                ESP_LOGI(TAG, "Media key: usage code 0x%04X", usage_code);
                usb_hid_consumer_press(usage_code);
            } else {
                ESP_LOGW(TAG, "Media action with insufficient data (need 2 bytes for usage code)");
            }
            break;
        }
        
        case ACTION_TYPE_SHELL:
            // buttons.c already sent EVENT_BUTTON_PRESSED before calling action_execute();
            // backend's ActionExecutorService handles this via profile lookup — nothing to do here.
            ESP_LOGI(TAG, "Shell action forwarded to PC via generic button event");
            break;

        case ACTION_TYPE_LAUNCH_APP:
            // buttons.c already sent EVENT_BUTTON_PRESSED before calling action_execute();
            // backend's ActionExecutorService handles this via profile lookup — nothing to do here.
            ESP_LOGI(TAG, "LaunchApp action forwarded to PC via generic button event");
            break;

        default:
            ESP_LOGW(TAG, "Unknown action type: %d", type);
            return ESP_ERR_INVALID_ARG;
    }
    
    return ESP_OK;
}

esp_err_t action_execute_raw(action_type_t type, const uint8_t* data, uint16_t data_len) {
    return execute_single_action(type, data, data_len, 0xFF);
}

/**
 * @brief Execute a sequence of actions
 */
static esp_err_t execute_sequence(const action_sequence_t* seq, uint8_t button_id) {
    if (seq == NULL || seq->num_steps == 0) {
        return ESP_ERR_INVALID_ARG;
    }
    
    ESP_LOGI(TAG, "Executing sequence with %d steps", seq->num_steps);
    
    for (int i = 0; i < seq->num_steps && i < MAX_SEQUENCE_STEPS; i++) {
        const sequence_step_t* step = &seq->steps[i];
        
        // Delay before action (if specified)
        if (step->delay_before_ms > 0) {
            ESP_LOGD(TAG, "Step %d: delay %d ms before action", i, step->delay_before_ms);
            vTaskDelay(pdMS_TO_TICKS(step->delay_before_ms));
        }
        
        // Execute the action
        ESP_LOGI(TAG, "Step %d: executing action type %d", i, step->action_type);
        esp_err_t ret = execute_single_action(
            step->action_type,
            step->action_data,
            step->action_data_len,
            button_id
        );
        
        if (ret != ESP_OK) {
            ESP_LOGE(TAG, "Step %d failed: %s", i, esp_err_to_name(ret));
            // Continue with remaining steps even if one fails
        }
    }
    
    ESP_LOGI(TAG, "Sequence completed");
    return ESP_OK;
}

// Sequence parsing is split into its own function so the ~1732-byte
// action_sequence_t is only on the stack when a sequence is actually running,
// not on every button press regardless of action type.
static esp_err_t execute_sequence_action(button_config_t* btn, uint8_t button_id) {
    if (btn->action_data_len < 1) {
        ESP_LOGW(TAG, "Sequence action with no data");
        return ESP_ERR_INVALID_ARG;
    }

    action_sequence_t seq;
    memset(&seq, 0, sizeof(seq));

    const uint8_t* ptr = btn->action_data;
    const uint8_t* end = btn->action_data + btn->action_data_len;

    seq.num_steps = *ptr++;
    if (seq.num_steps > MAX_SEQUENCE_STEPS) {
        seq.num_steps = MAX_SEQUENCE_STEPS;
    }

    for (int i = 0; i < seq.num_steps && ptr < end; i++) {
        sequence_step_t* step = &seq.steps[i];

        // Parse step: [action_type][delay_before_ms(2)][data_len(2)][data...]
        if (ptr + 5 > end) break;

        step->action_type = (action_type_t)*ptr++;
        step->delay_before_ms = ptr[0] | (ptr[1] << 8);
        ptr += 2;
        step->action_data_len = ptr[0] | (ptr[1] << 8);
        ptr += 2;

        if (step->action_data_len > ACTION_DATA_MAX_LEN) {
            step->action_data_len = ACTION_DATA_MAX_LEN;
        }

        if (ptr + step->action_data_len > end) {
            step->action_data_len = end - ptr;
        }

        memcpy(step->action_data, ptr, step->action_data_len);
        ptr += step->action_data_len;
    }

    return execute_sequence(&seq, button_id);
}

esp_err_t action_execute(uint8_t button_id) {
    if (button_id >= NUM_BUTTONS) {
        return ESP_ERR_INVALID_ARG;
    }

    // Exit folder toggle: the button that opened the folder acts as a back button
    // regardless of what action the folder assigns to it (often NONE).
    // Check before reading the folder's button config.
    if (profile_is_in_folder() && folder_entry_button_id == button_id) {
        ESP_LOGI(TAG, "Exiting folder via back button %d", button_id);
        profile_folder_exit();
        folder_entry_button_id = 0xFF;
        return ESP_OK;
    }

    button_config_t* btn = profile_get_button_config(button_id);
    if (btn == NULL) {
        return ESP_FAIL;
    }

    ESP_LOGI(TAG, "Executing action for button %d, type=%d", button_id, btn->action_type);

    if (btn->action_type == ACTION_TYPE_SEQUENCE) {
        return execute_sequence_action(btn, button_id);
    }

    // Folder action needs folder_id from button config, not action_data
    if (btn->action_type == ACTION_TYPE_FOLDER) {
        uint8_t folder_data[1] = { btn->folder_id };
        return execute_single_action(ACTION_TYPE_FOLDER, folder_data, 1, button_id);
    }

    return execute_single_action(btn->action_type, btn->action_data, btn->action_data_len, button_id);
}
