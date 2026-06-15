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

typedef struct { uint8_t keycode; uint8_t modifier; } hid_mapping_t;

// US-QWERTY: ASCII 32..126 → (HID keycode, modifier). keycode=0 means unmapped.
static const hid_mapping_t s_ascii_hid[95] = {
    {0x2C, 0x00}, // 32  ' '
    {0x1E, 0x02}, // 33  !
    {0x34, 0x02}, // 34  "
    {0x20, 0x02}, // 35  #
    {0x21, 0x02}, // 36  $
    {0x22, 0x02}, // 37  %
    {0x24, 0x02}, // 38  &
    {0x34, 0x00}, // 39  '
    {0x26, 0x02}, // 40  (
    {0x27, 0x02}, // 41  )
    {0x25, 0x02}, // 42  *
    {0x2E, 0x02}, // 43  +
    {0x36, 0x00}, // 44  ,
    {0x2D, 0x00}, // 45  -
    {0x37, 0x00}, // 46  .
    {0x38, 0x00}, // 47  /
    {0x27, 0x00}, // 48  0
    {0x1E, 0x00}, // 49  1
    {0x1F, 0x00}, // 50  2
    {0x20, 0x00}, // 51  3
    {0x21, 0x00}, // 52  4
    {0x22, 0x00}, // 53  5
    {0x23, 0x00}, // 54  6
    {0x24, 0x00}, // 55  7
    {0x25, 0x00}, // 56  8
    {0x26, 0x00}, // 57  9
    {0x33, 0x02}, // 58  :
    {0x33, 0x00}, // 59  ;
    {0x36, 0x02}, // 60  <
    {0x2E, 0x00}, // 61  =
    {0x37, 0x02}, // 62  >
    {0x38, 0x02}, // 63  ?
    {0x1F, 0x02}, // 64  @
    {0x04, 0x02}, // 65  A
    {0x05, 0x02}, // 66  B
    {0x06, 0x02}, // 67  C
    {0x07, 0x02}, // 68  D
    {0x08, 0x02}, // 69  E
    {0x09, 0x02}, // 70  F
    {0x0A, 0x02}, // 71  G
    {0x0B, 0x02}, // 72  H
    {0x0C, 0x02}, // 73  I
    {0x0D, 0x02}, // 74  J
    {0x0E, 0x02}, // 75  K
    {0x0F, 0x02}, // 76  L
    {0x10, 0x02}, // 77  M
    {0x11, 0x02}, // 78  N
    {0x12, 0x02}, // 79  O
    {0x13, 0x02}, // 80  P
    {0x14, 0x02}, // 81  Q
    {0x15, 0x02}, // 82  R
    {0x16, 0x02}, // 83  S
    {0x17, 0x02}, // 84  T
    {0x18, 0x02}, // 85  U
    {0x19, 0x02}, // 86  V
    {0x1A, 0x02}, // 87  W
    {0x1B, 0x02}, // 88  X
    {0x1C, 0x02}, // 89  Y
    {0x1D, 0x02}, // 90  Z
    {0x2F, 0x00}, // 91  [
    {0x31, 0x00}, // 92  backslash
    {0x30, 0x00}, // 93  ]
    {0x23, 0x02}, // 94  ^
    {0x2D, 0x02}, // 95  _
    {0x35, 0x00}, // 96  `
    {0x04, 0x00}, // 97  a
    {0x05, 0x00}, // 98  b
    {0x06, 0x00}, // 99  c
    {0x07, 0x00}, // 100 d
    {0x08, 0x00}, // 101 e
    {0x09, 0x00}, // 102 f
    {0x0A, 0x00}, // 103 g
    {0x0B, 0x00}, // 104 h
    {0x0C, 0x00}, // 105 i
    {0x0D, 0x00}, // 106 j
    {0x0E, 0x00}, // 107 k
    {0x0F, 0x00}, // 108 l
    {0x10, 0x00}, // 109 m
    {0x11, 0x00}, // 110 n
    {0x12, 0x00}, // 111 o
    {0x13, 0x00}, // 112 p
    {0x14, 0x00}, // 113 q
    {0x15, 0x00}, // 114 r
    {0x16, 0x00}, // 115 s
    {0x17, 0x00}, // 116 t
    {0x18, 0x00}, // 117 u
    {0x19, 0x00}, // 118 v
    {0x1A, 0x00}, // 119 w
    {0x1B, 0x00}, // 120 x
    {0x1C, 0x00}, // 121 y
    {0x1D, 0x00}, // 122 z
    {0x2F, 0x02}, // 123 {
    {0x31, 0x02}, // 124 |
    {0x30, 0x02}, // 125 }
    {0x35, 0x02}, // 126 ~
};

esp_err_t usb_hid_keyboard_type_text(const char* text) {
    if (text == NULL) return ESP_ERR_INVALID_ARG;

    for (size_t i = 0; text[i] != '\0'; i++) {
        uint8_t c = (uint8_t)text[i];
        uint8_t keycode = 0;
        uint8_t modifier = 0;

        if (c == '\n') {
            keycode = 0x28; // Enter
        } else if (c == '\t') {
            keycode = 0x2B; // Tab
        } else if (c >= 32 && c <= 126) {
            keycode  = s_ascii_hid[c - 32].keycode;
            modifier = s_ascii_hid[c - 32].modifier;
        }

        if (keycode != 0) {
            usb_hid_keyboard_press(modifier, keycode);
            vTaskDelay(pdMS_TO_TICKS(20));
        }
    }

    return ESP_OK;
}
