/**
 * @file usb_descriptors.c
 * @brief USB descriptors for composite device (simplified)
 */

#include "common.h"
#include "config.h"
#include "tinyusb.h"
#include "class/hid/hid_device.h"

// This file provides USB descriptors for composite HID device

// HID Report Descriptor - Keyboard
static const uint8_t desc_hid_report_keyboard[] = {
    TUD_HID_REPORT_DESC_KEYBOARD(HID_REPORT_ID(1))
};

// HID Report Descriptor - Raw
static const uint8_t desc_hid_report_raw[] = {
    TUD_HID_REPORT_DESC_GENERIC_INOUT(64, HID_REPORT_ID(2))
};

//--------------------------------------------------------------------+
// Configuration Descriptor
//--------------------------------------------------------------------+

enum {
    ITF_NUM_HID_KEYBOARD = 0,
    ITF_NUM_HID_RAW,
    ITF_NUM_TOTAL
};

#define CONFIG_TOTAL_LEN  (TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN + TUD_HID_INOUT_DESC_LEN)

#define EPNUM_HID_KEYBOARD  0x81
#define EPNUM_HID_RAW_OUT   0x02
#define EPNUM_HID_RAW_IN    0x82

const uint8_t desc_configuration[] = {
    // Config: self powered, max 500mA
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL, 0, CONFIG_TOTAL_LEN, TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP, 500),

    // Interface 0: HID Keyboard
    TUD_HID_DESCRIPTOR(ITF_NUM_HID_KEYBOARD, 0, HID_ITF_PROTOCOL_KEYBOARD, sizeof(desc_hid_report_keyboard), EPNUM_HID_KEYBOARD, CFG_TUD_HID_EP_BUFSIZE, 10),

    // Interface 1: HID Raw (bidirectional)
    TUD_HID_INOUT_DESCRIPTOR(ITF_NUM_HID_RAW, 0, HID_ITF_PROTOCOL_NONE, sizeof(desc_hid_report_raw), EPNUM_HID_RAW_OUT, EPNUM_HID_RAW_IN, CFG_TUD_HID_EP_BUFSIZE, 10)
};

// Invoked when received GET HID REPORT DESCRIPTOR
__attribute__((used))
uint8_t const* tud_hid_descriptor_report_cb(uint8_t instance) {
    if (instance == 0) {
        return desc_hid_report_keyboard;
    } else if (instance == 1) {
        return desc_hid_report_raw;
    }
    return NULL;
}

// Invoked when received GET_REPORT control request
__attribute__((used))
uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id,
                                hid_report_type_t report_type,
                                uint8_t* buffer, uint16_t reqlen) {
    // Not used in our implementation
    return 0;
}

// Invoked when received SET_REPORT control request or
// received data on OUT endpoint ( Report ID = 0, Type = 0 )
__attribute__((used))
void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id,
                            hid_report_type_t report_type,
                            uint8_t const* buffer, uint16_t bufsize) {
    // Handle incoming HID data
    if (instance == 1 && report_type == HID_REPORT_TYPE_OUTPUT) {
        // This is for HID Raw interface
        // Forward to protocol handler
        extern esp_err_t protocol_handle_packet(const uint8_t* data, size_t length);
        protocol_handle_packet(buffer, bufsize);
    }
}

// Note: String descriptors are provided by esp_tinyusb component
