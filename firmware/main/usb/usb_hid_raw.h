/**
 * @file usb_hid_raw.h
 * @brief USB HID Raw interface for custom communication
 */

#ifndef USB_HID_RAW_H
#define USB_HID_RAW_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Initialize USB HID Raw interface
 * @return ESP_OK on success
 */
esp_err_t usb_hid_raw_init(void);

/**
 * @brief Send raw HID report
 * @param data Data to send
 * @param length Data length (max 64 bytes)
 * @return ESP_OK on success
 */
esp_err_t usb_hid_raw_send(const uint8_t* data, size_t length);

/**
 * @brief USB RX task
 * @param arg Task argument (unused)
 */
void usb_rx_task(void* arg);

#endif // USB_HID_RAW_H
