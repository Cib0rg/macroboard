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

#endif // USB_HID_KEYBOARD_H
