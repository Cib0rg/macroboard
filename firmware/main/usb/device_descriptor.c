/**
 * @file device_descriptor.c
 * @brief Custom USB device descriptors for HID composite device
 */

#include "tusb.h"

#define USB_VID  0x303A  // Espressif
#define USB_PID  0x4001  // Custom HID device

//--------------------------------------------------------------------+
// Device Descriptor
//--------------------------------------------------------------------+
tusb_desc_device_t const desc_device = {
    .bLength            = sizeof(tusb_desc_device_t),
    .bDescriptorType    = TUSB_DESC_DEVICE,
    .bcdUSB             = 0x0200,
    .bDeviceClass       = 0x00,
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

// Invoked when received GET DEVICE DESCRIPTOR
uint8_t const * tud_descriptor_device_cb(void) {
    return (uint8_t const *) &desc_device;
}

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

uint8_t const desc_configuration[] = {
    // Config: self powered, max 500mA
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL, 0, CONFIG_TOTAL_LEN, TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP, 500),

    // Interface 0: HID Keyboard
    TUD_HID_DESCRIPTOR(ITF_NUM_HID_KEYBOARD, 0, HID_ITF_PROTOCOL_KEYBOARD, sizeof(uint8_t) * 33, EPNUM_HID_KEYBOARD, CFG_TUD_HID_EP_BUFSIZE, 10),

    // Interface 1: HID Raw (bidirectional)
    TUD_HID_INOUT_DESCRIPTOR(ITF_NUM_HID_RAW, 0, HID_ITF_PROTOCOL_NONE, sizeof(uint8_t) * 33, EPNUM_HID_RAW_OUT, EPNUM_HID_RAW_IN, CFG_TUD_HID_EP_BUFSIZE, 10)
};

// Invoked when received GET CONFIGURATION DESCRIPTOR
uint8_t const * tud_descriptor_configuration_cb(uint8_t index) {
    (void) index;
    return desc_configuration;
}

//--------------------------------------------------------------------+
// String Descriptors
//--------------------------------------------------------------------+

char const* string_desc_arr[] = {
    (const char[]) { 0x09, 0x04 },  // 0: Language (English)
    "Espressif",                     // 1: Manufacturer
    "ESP32-S3 Macro Keyboard",       // 2: Product
    "123456",                        // 3: Serial (should use chip ID)
    "HID Keyboard",                  // 4: HID Keyboard Interface
    "HID Raw",                       // 5: HID Raw Interface
};

static uint16_t _desc_str[32];

// Invoked when received GET STRING DESCRIPTOR request
uint16_t const* tud_descriptor_string_cb(uint8_t index, uint16_t langid) {
    (void) langid;

    uint8_t chr_count;

    if (index == 0) {
        memcpy(&_desc_str[1], string_desc_arr[0], 2);
        chr_count = 1;
    } else {
        if (index >= sizeof(string_desc_arr) / sizeof(string_desc_arr[0])) {
            return NULL;
        }

        const char* str = string_desc_arr[index];
        chr_count = strlen(str);
        if (chr_count > 31) chr_count = 31;

        for (uint8_t i = 0; i < chr_count; i++) {
            _desc_str[1 + i] = str[i];
        }
    }

    // first byte is length (including header), second byte is string type
    _desc_str[0] = (TUSB_DESC_STRING << 8) | (2 * chr_count + 2);

    return _desc_str;
}
