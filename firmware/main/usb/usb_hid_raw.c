/**
 * @file usb_hid_raw.c
 * @brief USB HID Raw implementation
 */

#include "common.h"
#include "usb_hid_raw.h"
#include "protocol/protocol_handler.h"
#include "config.h"
#include "tinyusb.h"
#include "class/hid/hid_device.h"

static const char* TAG = "USB_RAW";

esp_err_t usb_hid_raw_init(void) {
    ESP_LOGI(TAG, "USB HID Raw initialized");
    return ESP_OK;
}

esp_err_t usb_hid_raw_send(const uint8_t* data, size_t length) {
    if (data == NULL || length == 0 || length > USB_HID_REPORT_SIZE) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (!tud_mounted()) {
        ESP_LOGW(TAG, "USB not mounted");
        return ESP_ERR_INVALID_STATE;
    }
    
    // Send via TinyUSB HID
    bool sent = tud_hid_report(0x01, data, length);
    
    return sent ? ESP_OK : ESP_FAIL;
}

void usb_rx_task(void* arg) {
    ESP_LOGI(TAG, "USB RX task started");
    
    while (1) {
        // Wait for USB data
        // TinyUSB will call tud_hid_set_report_cb when data is received
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}

// Note: TinyUSB callbacks are now in usb_descriptors.c to avoid multiple definitions
