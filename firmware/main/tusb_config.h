/**
 * @file tusb_config.h
 * @brief TinyUSB configuration
 */

#ifndef TUSB_CONFIG_H
#define TUSB_CONFIG_H

#ifdef __cplusplus
extern "C" {
#endif

//--------------------------------------------------------------------
// COMMON CONFIGURATION
//--------------------------------------------------------------------

#define CFG_TUSB_MCU                OPT_MCU_ESP32S3
#define CFG_TUSB_OS                 OPT_OS_FREERTOS

// RHPort number used for device
#define BOARD_TUD_RHPORT            0

// RHPort max operational speed
#define BOARD_TUD_MAX_SPEED         OPT_MODE_FULL_SPEED

//--------------------------------------------------------------------
// DEVICE CONFIGURATION
//--------------------------------------------------------------------

#define CFG_TUD_ENABLED             1

// Device mode with rhport and speed defined by board.mk
#define CFG_TUD_ENDPOINT0_SIZE      64

//------------- CLASS -------------//
#define CFG_TUD_HID                 2
#define CFG_TUD_CDC                 0
#define CFG_TUD_MSC                 0
#define CFG_TUD_MIDI                0
#define CFG_TUD_VENDOR              0

// HID buffer size
#define CFG_TUD_HID_EP_BUFSIZE      64

#ifdef __cplusplus
}
#endif

#endif // TUSB_CONFIG_H
