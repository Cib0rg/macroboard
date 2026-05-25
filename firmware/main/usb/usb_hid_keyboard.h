/**
 * @file usb_hid_keyboard.h
 * @brief USB HID Keyboard interface
 */

#ifndef USB_HID_KEYBOARD_H
#define USB_HID_KEYBOARD_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Initialize USB HID Keyboard
 * @return ESP_OK on success
 */
esp_err_t usb_hid_keyboard_init(void);

/**
 * @brief Send keyboard report
 * @param modifier Modifier keys (Ctrl, Shift, Alt, GUI)
 * @param keycodes Array of up to 6 keycodes
 * @param num_keys Number of keys (0-6)
 * @return ESP_OK on success
 */
esp_err_t usb_hid_keyboard_send(uint8_t modifier, const uint8_t* keycodes, uint8_t num_keys);

/**
 * @brief Type text string
 * @param text UTF-8 text to type
 * @return ESP_OK on success
 */
esp_err_t usb_hid_keyboard_type_text(const char* text);

/**
 * @brief Press key
 * @param modifier Modifier keys
 * @param keycode Key code
 * @return ESP_OK on success
 */
esp_err_t usb_hid_keyboard_press(uint8_t modifier, uint8_t keycode);

/**
 * @brief Release all keys
 * @return ESP_OK on success
 */
esp_err_t usb_hid_keyboard_release_all(void);

/**
 * @brief Send consumer control key (media keys: volume, mute, etc.)
 * @param usage_code HID Consumer Control usage code (e.g., 0x00E9 = Volume Up)
 * @return ESP_OK on success
 */
esp_err_t usb_hid_consumer_press(uint16_t usage_code);

/**
 * @brief Release consumer control key
 * @return ESP_OK on success
 */
esp_err_t usb_hid_consumer_release(void);

// Common Consumer Control usage codes
#define HID_USAGE_CONSUMER_VOLUME_INCREMENT  0x00E9
#define HID_USAGE_CONSUMER_VOLUME_DECREMENT  0x00EA
#define HID_USAGE_CONSUMER_MUTE              0x00E2
#define HID_USAGE_CONSUMER_PLAY_PAUSE        0x00CD
#define HID_USAGE_CONSUMER_SCAN_NEXT         0x00B5
#define HID_USAGE_CONSUMER_SCAN_PREVIOUS     0x00B6
#define HID_USAGE_CONSUMER_STOP              0x00B7

#endif // USB_HID_KEYBOARD_H
