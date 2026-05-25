/**
 * @file usb_descriptors.c
 * @brief USB descriptors for composite device (HID Keyboard + Vendor)
 *
 * This is the single source of all USB descriptor data. The device presents
 * itself as a composite USB device with two interfaces:
 *   - Interface 0: HID Keyboard (for key press emulation)
 *   - Interface 1: Vendor-specific (for custom bidirectional communication)
 *
 * NOTE: The esp_tinyusb library provides tud_descriptor_device_cb,
 * tud_descriptor_configuration_cb, and tud_descriptor_string_cb.
 * We pass our descriptors via tinyusb_config_t in main.c.
 * Only tud_hid_descriptor_report_cb and HID callbacks are defined here.
 */

#include <string.h>
#include "tusb.h"
#include "config.h"

//--------------------------------------------------------------------+
// Device Descriptor
//--------------------------------------------------------------------+
const tusb_desc_device_t desc_device = {
    .bLength            = sizeof(tusb_desc_device_t),
    .bDescriptorType    = TUSB_DESC_DEVICE,
    .bcdUSB             = 0x0200,
    .bDeviceClass       = 0x00,      // Composite: class defined per-interface
    .bDeviceSubClass    = 0x00,
    .bDeviceProtocol    = 0x00,
    .bMaxPacketSize0    = CFG_TUD_ENDPOINT0_SIZE,

    .idVendor           = USB_VID,
    .idProduct          = USB_PID,
    .bcdDevice          = 0x0100,

    .iManufacturer      = 0x01,
    .iProduct           = 0x02,
    .iSerialNumber      = 0x03,

    .bNumConfigurations = 0x01
};

//--------------------------------------------------------------------+
// HID Report Descriptor (Keyboard + Consumer Control)
//--------------------------------------------------------------------+
static const uint8_t desc_hid_report[] = {
    TUD_HID_REPORT_DESC_KEYBOARD(HID_REPORT_ID(1)),
    TUD_HID_REPORT_DESC_CONSUMER(HID_REPORT_ID(2))
};

// Invoked when received GET HID REPORT DESCRIPTOR
// This callback is NOT provided by esp_tinyusb, so we must define it.
uint8_t const *tud_hid_descriptor_report_cb(uint8_t instance) {
    (void)instance;
    return desc_hid_report;
}

//--------------------------------------------------------------------+
// Configuration Descriptor
//--------------------------------------------------------------------+
enum {
    ITF_NUM_HID = 0,
    ITF_NUM_VENDOR,
    ITF_NUM_TOTAL
};

#define CONFIG_TOTAL_LEN  (TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN + TUD_VENDOR_DESC_LEN)

#define EPNUM_HID           0x81
#define EPNUM_VENDOR_OUT    0x02
#define EPNUM_VENDOR_IN     0x82

const uint8_t desc_configuration[] = {
    // Config: self powered, max 500mA
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL, 0, CONFIG_TOTAL_LEN, TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP, 500),

    // Interface 0: HID Keyboard (string index 4 = "HID Keyboard")
    TUD_HID_DESCRIPTOR(ITF_NUM_HID, 4, HID_ITF_PROTOCOL_NONE, sizeof(desc_hid_report), EPNUM_HID, CFG_TUD_HID_EP_BUFSIZE, 10),

    // Interface 1: Vendor-specific (string index 5 = "Vendor Interface")
    TUD_VENDOR_DESCRIPTOR(ITF_NUM_VENDOR, 5, EPNUM_VENDOR_OUT, EPNUM_VENDOR_IN, 64)
};

//--------------------------------------------------------------------+
// String Descriptors
// Passed to esp_tinyusb via tinyusb_config_t.descriptor.string
//--------------------------------------------------------------------+
const char *desc_string_arr[] = {
    (const char[]){0x09, 0x04},   // 0: Language (English)
    USB_MANUFACTURER,              // 1: Manufacturer
    USB_PRODUCT,                   // 2: Product
    USB_SERIAL,                    // 3: Serial Number
    "HID Keyboard",                // 4: HID Keyboard Interface
    USB_PRODUCT,                   // 5: Vendor Interface
};

const int desc_string_count = sizeof(desc_string_arr) / sizeof(desc_string_arr[0]);

//--------------------------------------------------------------------+
// TinyUSB HID Callbacks
// These are NOT provided by esp_tinyusb, so we must define them.
//--------------------------------------------------------------------+

// Invoked when received GET_REPORT control request
uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id,
                                hid_report_type_t report_type,
                                uint8_t *buffer, uint16_t reqlen) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)reqlen;
    return 0;
}

// Invoked when received SET_REPORT control request or
// received data on OUT endpoint (Report ID = 0, Type = 0)
void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id,
                            hid_report_type_t report_type,
                            uint8_t const *buffer, uint16_t bufsize) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)bufsize;
    // HID keyboard interface does not receive data from host in our design.
    // All custom communication goes through the Vendor interface.
}
