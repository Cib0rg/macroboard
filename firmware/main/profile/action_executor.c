/**
 * @file action_executor.c
 * @brief Button action executor implementation
 */

#include "common.h"
#include "action_executor.h"
#include "profile_manager.h"
#include "usb/usb_hid_keyboard.h"
#include "usb/usb_hid_raw.h"
#include "protocol/protocol_handler.h"
#include "protocol/protocol_types.h"
#include "config.h"

static const char* TAG = "ACTION";

esp_err_t action_execute(uint8_t button_id) {
    if (button_id >= NUM_BUTTONS) {
        return ESP_ERR_INVALID_ARG;
    }
    
    button_config_t* btn = profile_get_button_config(button_id);
    if (btn == NULL) {
        return ESP_FAIL;
    }
    
    ESP_LOGI(TAG, "Executing action for button %d, type=%d", button_id, btn->action_type);
    
    switch (btn->action_type) {
        case ACTION_TYPE_KEYBOARD: {
            // Keyboard action
            if (btn->action_data_len >= 2) {
                uint8_t modifier = btn->action_data[0];
                uint8_t keycode = btn->action_data[1];
                
                usb_hid_keyboard_press(modifier, keycode);
            }
            break;
        }
        
        case ACTION_TYPE_CUSTOM_HID: {
            // Custom HID action - send event to PC
            uint8_t payload[PROTOCOL_PAYLOAD_SIZE];
            payload[0] = button_id;
            payload[1] = profile_get_current_id();
            payload[2] = btn->action_type;
            
            // Copy custom data
            uint16_t copy_len = (btn->action_data_len < 50) ? btn->action_data_len : 50;
            memcpy(&payload[3], btn->action_data, copy_len);
            
            protocol_send_event(EVENT_BUTTON_PRESSED, payload, 3 + copy_len);
            break;
        }
        
        case ACTION_TYPE_PROFILE_SWITCH: {
            // Profile switch action
            if (btn->action_data_len >= 1) {
                uint8_t target_profile = btn->action_data[0];
                profile_switch(target_profile);
            }
            break;
        }
        
        default:
            ESP_LOGW(TAG, "Unknown action type: %d", btn->action_type);
            break;
    }
    
    return ESP_OK;
}
