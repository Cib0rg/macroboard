/**
 * @file usb_hid_keyboard.c
 * @brief USB HID Keyboard implementation using TinyUSB
 */

#include "common.h"
#include "usb_hid_keyboard.h"
#include "config.h"
#include "tinyusb.h"
#include "class/hid/hid_device.h"

static const char* TAG = "USB_KBD";

esp_err_t usb_hid_keyboard_init(void) {
    ESP_LOGI(TAG, "USB HID Keyboard initialized");
    return ESP_OK;
}

esp_err_t usb_hid_keyboard_send(uint8_t modifier, const uint8_t* keycodes, uint8_t num_keys) {
    if (num_keys > 6) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (!tud_mounted()) {
        ESP_LOGW(TAG, "USB not mounted");
        return ESP_ERR_INVALID_STATE;
    }
    
    uint8_t keycode_array[6] = {0};
    
    if (keycodes != NULL && num_keys > 0) {
        memcpy(keycode_array, keycodes, num_keys);
    }
    
    return tud_hid_keyboard_report(1, modifier, keycode_array) ? ESP_OK : ESP_FAIL;
}

esp_err_t usb_hid_keyboard_press(uint8_t modifier, uint8_t keycode) {
    esp_err_t ret = usb_hid_keyboard_send(modifier, &keycode, 1);
    if (ret != ESP_OK) {
        return ret;
    }
    
    vTaskDelay(pdMS_TO_TICKS(10));
    
    return usb_hid_keyboard_release_all();
}

esp_err_t usb_hid_keyboard_release_all(void) {
    return usb_hid_keyboard_send(0, NULL, 0);
}

esp_err_t usb_hid_consumer_press(uint16_t usage_code) {
    if (!tud_mounted()) {
        ESP_LOGW(TAG, "USB not mounted");
        return ESP_ERR_INVALID_STATE;
    }
    
    // Send consumer control report (Report ID 2)
    bool ok = tud_hid_report(2, &usage_code, sizeof(usage_code));
    if (!ok) {
        ESP_LOGE(TAG, "Failed to send consumer report");
        return ESP_FAIL;
    }
    
    vTaskDelay(pdMS_TO_TICKS(10));
    
    // Release
    return usb_hid_consumer_release();
}

esp_err_t usb_hid_consumer_release(void) {
    if (!tud_mounted()) {
        return ESP_ERR_INVALID_STATE;
    }
    
    uint16_t zero = 0;
    return tud_hid_report(2, &zero, sizeof(zero)) ? ESP_OK : ESP_FAIL;
}

esp_err_t usb_hid_send_raw_report(const uint8_t* data, uint16_t len) {
    if (!tud_mounted()) {
        ESP_LOGW(TAG, "USB not mounted");
        return ESP_ERR_INVALID_STATE;
    }

    uint8_t buf[USB_HID_REPORT_SIZE - 1];
    uint16_t copy_len = len < sizeof(buf) ? len : sizeof(buf);
    memcpy(buf, data, copy_len);
    if (copy_len < sizeof(buf))
        memset(buf + copy_len, 0, sizeof(buf) - copy_len);

    return tud_hid_report(3, buf, sizeof(buf)) ? ESP_OK : ESP_FAIL;
}

esp_err_t usb_hid_keyboard_type_text(const char* text) {
    if (text == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    
    // Simple ASCII typing implementation
    // Full UTF-8 support would require character mapping
    
    for (size_t i = 0; text[i] != '\0'; i++) {
        char c = text[i];
        uint8_t keycode = 0;
        uint8_t modifier = 0;
        
        // Simple ASCII to HID keycode mapping
        if (c >= 'a' && c <= 'z') {
            keycode = 0x04 + (c - 'a'); // HID_KEY_A = 0x04
        } else if (c >= 'A' && c <= 'Z') {
            keycode = 0x04 + (c - 'A');
            modifier = 0x02; // Left Shift
        } else if (c >= '1' && c <= '9') {
            keycode = 0x1E + (c - '1'); // HID_KEY_1 = 0x1E
        } else if (c == '0') {
            keycode = 0x27; // HID_KEY_0
        } else if (c == ' ') {
            keycode = 0x2C; // HID_KEY_SPACE
        } else if (c == '\n') {
            keycode = 0x28; // HID_KEY_ENTER
        }
        // Add more character mappings as needed
        
        if (keycode != 0) {
            usb_hid_keyboard_press(modifier, keycode);
            vTaskDelay(pdMS_TO_TICKS(20));
        }
    }
    
    return ESP_OK;
}
