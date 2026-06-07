/**
 * @file profile_types.h
 * @brief Data types for profile management
 */

#ifndef PROFILE_TYPES_H
#define PROFILE_TYPES_H

#include <stdint.h>
#include "config.h"

// ============================================
// Action Types
// ============================================

typedef enum {
    ACTION_TYPE_NONE = 0x00,
    ACTION_TYPE_KEYBOARD = 0x01,
    ACTION_TYPE_CUSTOM_HID = 0x02,
    ACTION_TYPE_PROFILE_SWITCH = 0x03,
    ACTION_TYPE_FOLDER = 0x04,
    ACTION_TYPE_DELAY = 0x05,        // Задержка (используется в последовательностях)
    ACTION_TYPE_SHELL = 0x06,        // Shell-команда (выполняется на PC через Backend)
    ACTION_TYPE_SEQUENCE = 0x07,     // Последовательность действий
    ACTION_TYPE_LAUNCH_APP = 0x08,   // Запуск приложения (выполняется на PC через Backend)
    ACTION_TYPE_MEDIA = 0x09,        // Медиа-клавиши (Consumer Control: громкость, mute и т.д.)
} action_type_t;

// ============================================
// Sequence Configuration
// ============================================

#define MAX_SEQUENCE_STEPS 16

/**
 * @brief Один шаг в последовательности действий
 */
typedef struct {
    action_type_t action_type;       // Тип действия (не может быть SEQUENCE)
    uint16_t delay_before_ms;        // Задержка ПЕРЕД выполнением (0-65535 ms)
    uint16_t action_data_len;        // Длина данных действия
    uint8_t action_data[ACTION_DATA_MAX_LEN]; // Данные действия
} sequence_step_t;

/**
 * @brief Последовательность действий (макс. 16 шагов)
 */
typedef struct {
    uint8_t num_steps;               // Количество шагов (1-16)
    sequence_step_t steps[MAX_SEQUENCE_STEPS];
} action_sequence_t;

// ============================================
// LED Effects
// ============================================

typedef enum {
    LED_EFFECT_STATIC = 0x00,
    LED_EFFECT_BREATHING = 0x01,
    LED_EFFECT_RAINBOW = 0x02,
    LED_EFFECT_WAVE = 0x03,
} led_effect_t;

// ============================================
// Button Configuration
// ============================================

typedef struct {
    uint8_t button_id;
    action_type_t action_type;
    uint16_t action_data_len;
    uint8_t action_data[ACTION_DATA_MAX_LEN];

    // Folder configuration (for ACTION_TYPE_FOLDER)
    uint8_t folder_id;  // ID of the folder (0-255)

    // LED configuration
    uint8_t led_r;
    uint8_t led_g;
    uint8_t led_b;
    uint8_t led_brightness;
    led_effect_t led_effect;

    // Image metadata
    uint32_t image_offset;
    uint32_t image_size;
    uint8_t image_format;  // 0 = JPEG

    // Display name shown when no image is set (empty = derive from action)
    char name[BUTTON_NAME_MAX_LEN];
} button_config_t;

// ============================================
// Folder Structure
// ============================================

typedef struct {
    uint8_t folder_id;
    char name[PROFILE_NAME_MAX_LEN];
    button_config_t buttons[NUM_BUTTONS];
} folder_t;

// ============================================
// Encoder Configuration
// ============================================

typedef struct {
    action_type_t cw_action_type;           // Action on clockwise rotation
    uint16_t cw_action_data_len;
    uint8_t cw_action_data[ACTION_DATA_MAX_LEN];
    
    action_type_t ccw_action_type;          // Action on counter-clockwise rotation
    uint16_t ccw_action_data_len;
    uint8_t ccw_action_data[ACTION_DATA_MAX_LEN];
    
    action_type_t press_action_type;        // Action on button press
    uint16_t press_action_data_len;
    uint8_t press_action_data[ACTION_DATA_MAX_LEN];
} encoder_config_t;

// ============================================
// Profile Structure
// ============================================

typedef struct {
    uint8_t profile_id;
    char name[PROFILE_NAME_MAX_LEN];
    button_config_t buttons[NUM_BUTTONS];
    folder_t folders[NUM_FOLDERS];
    encoder_config_t encoder;
    uint32_t crc32;
} profile_t;

// ============================================
// Button Events
// ============================================

typedef enum {
    BUTTON_EVENT_PRESS = 0,
    BUTTON_EVENT_RELEASE = 1,
    BUTTON_EVENT_LONG_PRESS = 2,
} button_event_type_t;

typedef struct {
    uint8_t button_id;
    button_event_type_t event_type;
    uint64_t timestamp;
} button_event_t;

// ============================================
// Encoder Events
// ============================================

typedef enum {
    ENCODER_ROTATED = 0,
    ENCODER_BUTTON_PRESSED = 1,
} encoder_event_type_t;

typedef enum {
    ENCODER_CW = 0,   // Clockwise
    ENCODER_CCW = 1,  // Counter-clockwise
} encoder_direction_t;

typedef enum {
    PRESS_SHORT = 0,
    PRESS_LONG = 1,
} press_type_t;

typedef struct {
    encoder_event_type_t type;
    encoder_direction_t direction;
    press_type_t press_type;
    uint64_t timestamp;
} encoder_event_t;

// ============================================
// Display Update
// ============================================

typedef struct {
    uint8_t display_id;
    uint8_t* image_data;
    uint16_t width;
    uint16_t height;
} display_update_t;

#endif // PROFILE_TYPES_H
