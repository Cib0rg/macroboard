/**
 * @file usb_vendor.c
 * @brief USB Vendor-specific interface implementation
 *
 * Uses TinyUSB Vendor class for bidirectional custom communication
 * with the host PC software. This replaces the previous HID Raw approach
 * to enable a true composite USB device (HID + Vendor).
 */

#include "common.h"
#include "usb_vendor.h"
#include "protocol/protocol_handler.h"
#include "config.h"
#include "tinyusb.h"
#include "tusb.h"

static const char* TAG = "USB_VENDOR";

esp_err_t usb_vendor_init(void) {
    ESP_LOGI(TAG, "USB Vendor interface initialized");
    return ESP_OK;
}

esp_err_t usb_vendor_send(const uint8_t* data, size_t length) {
    if (data == NULL || length == 0 || length > USB_HID_REPORT_SIZE) {
        return ESP_ERR_INVALID_ARG;
    }

    if (!tud_mounted()) {
        ESP_LOGW(TAG, "USB not mounted");
        return ESP_ERR_INVALID_STATE;
    }

    // Send via TinyUSB Vendor class (interface 0 of vendor = first vendor itf)
    uint32_t written = tud_vendor_n_write(0, data, length);
    tud_vendor_n_flush(0);

    return (written > 0) ? ESP_OK : ESP_FAIL;
}

void usb_vendor_rx_task(void* arg) {
    ESP_LOGI(TAG, "USB Vendor RX task started");
    uint8_t rx_buf[64];

    while (1) {
        if (tud_mounted() && tud_vendor_n_available(0)) {
            uint32_t count = tud_vendor_n_read(0, rx_buf, sizeof(rx_buf));
            if (count > 0) {
                ESP_LOGD(TAG, "Vendor received %lu bytes", (unsigned long)count);
                // Forward to protocol handler
                protocol_handle_packet(rx_buf, count);
            }
        }
        vTaskDelay(pdMS_TO_TICKS(1));
    }
}

// TinyUSB Vendor device callback — called when data arrives on vendor OUT endpoint
void tud_vendor_rx_cb(uint8_t itf, uint8_t const* buffer, uint16_t bufsize) {
    // Data is buffered in TinyUSB FIFO, will be read by usb_vendor_rx_task
    (void)itf;
    (void)buffer;
    (void)bufsize;
}
