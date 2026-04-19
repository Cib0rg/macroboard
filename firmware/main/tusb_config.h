/**
 * @file tusb_config.h
 * @brief TinyUSB configuration for composite USB device (HID + Vendor)
 */

#ifndef TUSB_CONFIG_H
#define TUSB_CONFIG_H

#ifdef __cplusplus
extern "C" {
#endif

//--------------------------------------------------------------------
// COMMON CONFIGURATION
//--------------------------------------------------------------------

#ifndef CFG_TUSB_MCU
#define CFG_TUSB_MCU                OPT_MCU_ESP32S3
#endif

#ifndef CFG_TUSB_OS
#define CFG_TUSB_OS                 OPT_OS_FREERTOS
#endif

#ifndef CFG_TUSB_DEBUG
#define CFG_TUSB_DEBUG              0
#endif

// Enable Device stack
#define CFG_TUD_ENABLED             1

// Default is max speed that hardware controller could support with on-chip PHY
#define CFG_TUD_MAX_SPEED           OPT_MODE_FULL_SPEED

//--------------------------------------------------------------------
// DEVICE CONFIGURATION
//--------------------------------------------------------------------

#ifndef CFG_TUD_ENDPOINT0_SIZE
#define CFG_TUD_ENDPOINT0_SIZE      64
#endif

//------------- CLASS -------------//
// Composite device: 1x HID (keyboard) + 1x Vendor (custom communication)
#define CFG_TUD_HID                 1
#define CFG_TUD_VENDOR              1
#define CFG_TUD_CDC                 0
#define CFG_TUD_MSC                 0
#define CFG_TUD_MIDI                0

// HID buffer size - should be sufficient to hold ID (if any) + Data
#define CFG_TUD_HID_EP_BUFSIZE      16

// Vendor FIFO size of TX and RX
#define CFG_TUD_VENDOR_RX_BUFSIZE   64
#define CFG_TUD_VENDOR_TX_BUFSIZE   64

#ifdef __cplusplus
}
#endif

#endif // TUSB_CONFIG_H
