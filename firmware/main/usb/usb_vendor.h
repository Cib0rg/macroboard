/**
 * @file usb_vendor.h
 * @brief USB Vendor-specific interface for custom bidirectional communication
 */

#ifndef USB_VENDOR_H
#define USB_VENDOR_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Initialize USB Vendor interface
 * @return ESP_OK on success
 */
esp_err_t usb_vendor_init(void);

/**
 * @brief Send data via USB Vendor interface
 * @param data Data to send
 * @param length Data length (max 64 bytes)
 * @return ESP_OK on success
 */
esp_err_t usb_vendor_send(const uint8_t* data, size_t length);

/**
 * @brief USB Vendor RX task — polls for incoming data
 * @param arg Task argument (unused)
 */
void usb_vendor_rx_task(void* arg);

#endif // USB_VENDOR_H
