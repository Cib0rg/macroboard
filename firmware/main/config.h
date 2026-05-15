/**
 * @file config.h
 * @brief Configuration constants for ESP32-S3 Macro Keyboard
 */

#ifndef CONFIG_H
#define CONFIG_H

#include <stdint.h>

// ============================================
// Firmware Version
// ============================================
#define FIRMWARE_VERSION_MAJOR  1
#define FIRMWARE_VERSION_MINOR  0
#define FIRMWARE_VERSION_PATCH  0
#define FIRMWARE_VERSION        "1.0.0"

// ============================================
// Hardware Configuration
// ============================================

// Number of buttons and displays
#define NUM_BUTTONS             10
#define NUM_DISPLAYS            10
#define NUM_LEDS                10

// Display specifications
#define DISPLAY_WIDTH           160
#define DISPLAY_HEIGHT          160
#define DISPLAY_PIXEL_COUNT     (DISPLAY_WIDTH * DISPLAY_HEIGHT)
#define DISPLAY_BUFFER_SIZE     (DISPLAY_PIXEL_COUNT * 2)  // RGB565

// ============================================
// GPIO Pin Definitions
// ============================================

// SPI for displays
#define PIN_SPI_MOSI            11
#define PIN_SPI_CLK             12
#define PIN_SPI_DC              13
#define PIN_SPI_RST             14
#define PIN_DISPLAY_BACKLIGHT   15  // Common backlight control for all displays

// Display multiplexer (74HC138 decoders)
#define PIN_MUX_A0              16
#define PIN_MUX_A1              17
#define PIN_MUX_A2              18
#define PIN_MUX_SEL             21

// Buttons (GPIO pins)
#define PIN_BUTTON_0            2
#define PIN_BUTTON_1            1
#define PIN_BUTTON_2            8
#define PIN_BUTTON_3            9
#define PIN_BUTTON_4            7
#define PIN_BUTTON_5            6
#define PIN_BUTTON_6            5
#define PIN_BUTTON_7            4
#define PIN_BUTTON_8            38
#define PIN_BUTTON_9            39

// Rotary encoder
#define PIN_ENCODER_A           40
#define PIN_ENCODER_B           41
#define PIN_ENCODER_BTN         42

// WS2812 RGB LEDs
#define PIN_LED_DATA            10

// ============================================
// SPI Configuration
// ============================================
#define SPI_HOST                SPI2_HOST
#define SPI_CLOCK_SPEED_HZ      (20 * 1000 * 1000)  // 40 MHz
#define SPI_DMA_CHAN            SPI_DMA_CH_AUTO

// ============================================
// Profile Configuration
// ============================================
#define NUM_PROFILES            5
#define PROFILE_NAME_MAX_LEN    32
#define ACTION_DATA_MAX_LEN     100

// Folder Configuration
#define NUM_FOLDERS             16  // Maximum number of folders per profile
#define FOLDER_STACK_DEPTH      4   // Maximum nesting depth for folders

// ============================================
// Storage Paths
// ============================================
#define STORAGE_BASE_PATH       "/storage"
#define PROFILE_FILE_FMT        "/storage/profile_%d.bin"
#define IMAGE_FILE_FMT          "/storage/img_%d_%d.jpg"

// ============================================
// Protocol Configuration
// ============================================
#define PROTOCOL_PACKET_SIZE    64
#define PROTOCOL_MAGIC          0xEA  // 'EA' for Elgato
#define PROTOCOL_VERSION        0x01

// ============================================
// USB Configuration
// ============================================
#define USB_VID                 0x1209  // pid.codes (Open Source VID)
#define USB_PID                 0x0001  // MacroKeyboard PID
#define USB_MANUFACTURER        "Elgato"
#define USB_PRODUCT             "Stream Deck"
#define USB_SERIAL              "123456"

#define USB_HID_REPORT_SIZE     64

// ============================================
// Task Priorities
// ============================================
#define TASK_PRIORITY_USB_RX    20
#define TASK_PRIORITY_BUTTON    18
#define TASK_PRIORITY_ENCODER   18
#define TASK_PRIORITY_PROTOCOL  15
#define TASK_PRIORITY_DISPLAY   12
#define TASK_PRIORITY_LED       10
#define TASK_PRIORITY_MONITOR   3

// ============================================
// Task Stack Sizes
// ============================================
#define STACK_SIZE_USB_RX       4096
#define STACK_SIZE_BUTTON       4096
#define STACK_SIZE_ENCODER      4096
#define STACK_SIZE_PROTOCOL     8192
#define STACK_SIZE_DISPLAY      4096
#define STACK_SIZE_LED          2048

// ============================================
// Timing Configuration
// ============================================
#define BUTTON_DEBOUNCE_MS      50
#define BUTTON_LONG_PRESS_MS    1000
#define ENCODER_STEPS_PER_PROFILE 4

// ============================================
// LED Configuration
// ============================================
#define LED_DEFAULT_BRIGHTNESS  128
#define LED_MAX_BRIGHTNESS      255

// ============================================
// System Events (FreeRTOS Event Group bits)
// ============================================
#define EVENT_USB_CONFIGURED    (1 << 0)
#define EVENT_PROFILE_CHANGED_BIT   (1 << 1)

#endif // CONFIG_H
