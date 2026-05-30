/**
 * @file encoder.c
 * @brief Rotary encoder driver implementation
 */

#include "encoder.h"
#include "config.h"
#include "esp_log.h"
#include "driver/gpio.h"
#include "esp_timer.h"
#include "profile/profile_manager.h"
#include "profile/action_executor.h"

static const char* TAG = "ENCODER";

QueueHandle_t encoder_event_queue = NULL;

// Encoder state
static volatile int8_t encoder_state = 0;
static volatile uint64_t last_rotation_time = 0;
static volatile uint64_t button_press_time = 0;
static volatile bool button_pressed = false;

// Encoder state machine lookup table
static const int8_t encoder_table[16] = {
    0,  // 0000
    1,  // 0001 CW step 1
    -1, // 0010 CCW step 1
    0,  // 0011
    -1, // 0100 CCW step 2
    0,  // 0101
    0,  // 0110
    1,  // 0111 CW step 2
    1,  // 1000 CW step 3
    0,  // 1001
    0,  // 1010
    -1, // 1011 CCW step 3
    0,  // 1100
    -1, // 1101 CCW step 4
    1,  // 1110 CW step 4
    0   // 1111
};

// ISR handler for encoder rotation
static void IRAM_ATTR encoder_rotation_isr(void* arg) {
    static uint8_t prev_state = 0;
    
    uint8_t a = gpio_get_level(PIN_ENCODER_A);
    uint8_t b = gpio_get_level(PIN_ENCODER_B);
    
    uint8_t current_state = (a << 1) | b;
    uint8_t combined = (prev_state << 2) | current_state;
    
    int8_t direction = encoder_table[combined];
    
    if (direction != 0) {
        BaseType_t xHigherPriorityTaskWoken = pdFALSE;
        
        encoder_event_t event = {
            .type = ENCODER_ROTATED,
            .direction = (direction > 0) ? ENCODER_CW : ENCODER_CCW,
            .timestamp = esp_timer_get_time(),
        };
        
        xQueueSendFromISR(encoder_event_queue, &event, &xHigherPriorityTaskWoken);
        
        if (xHigherPriorityTaskWoken) {
            portYIELD_FROM_ISR();
        }
    }
    
    prev_state = current_state;
}

// ISR handler for encoder button
static void IRAM_ATTR encoder_button_isr(void* arg) {
    BaseType_t xHigherPriorityTaskWoken = pdFALSE;
    
    encoder_event_t event = {
        .type = ENCODER_BUTTON_PRESSED,
        .timestamp = esp_timer_get_time(),
    };
    
    xQueueSendFromISR(encoder_event_queue, &event, &xHigherPriorityTaskWoken);
    
    if (xHigherPriorityTaskWoken) {
        portYIELD_FROM_ISR();
    }
}

esp_err_t encoder_init(void) {
    ESP_LOGI(TAG, "Initializing encoder");
    
    // Create event queue
    encoder_event_queue = xQueueCreate(5, sizeof(encoder_event_t));
    if (encoder_event_queue == NULL) {
        ESP_LOGE(TAG, "Failed to create encoder event queue");
        return ESP_FAIL;
    }
    
    // Configure encoder A and B pins
    gpio_config_t io_conf = {
        .pin_bit_mask = (1ULL << PIN_ENCODER_A) | (1ULL << PIN_ENCODER_B),
        .mode = GPIO_MODE_INPUT,
        .pull_up_en = GPIO_PULLUP_ENABLE,
        .pull_down_en = GPIO_PULLDOWN_DISABLE,
        .intr_type = GPIO_INTR_ANYEDGE,
    };
    
    esp_err_t ret = gpio_config(&io_conf);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to configure encoder pins: %s", esp_err_to_name(ret));
        return ret;
    }
    
    // Configure encoder button pin
    io_conf.pin_bit_mask = (1ULL << PIN_ENCODER_BTN);
    io_conf.intr_type = GPIO_INTR_NEGEDGE;
    
    ret = gpio_config(&io_conf);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to configure encoder button: %s", esp_err_to_name(ret));
        return ret;
    }
    
    // ISR service already installed by buttons_init(), just add handlers
    // gpio_install_isr_service(0);  // Already installed
    
    // Add ISR handlers
    gpio_isr_handler_add(PIN_ENCODER_A, encoder_rotation_isr, NULL);
    gpio_isr_handler_add(PIN_ENCODER_B, encoder_rotation_isr, NULL);
    gpio_isr_handler_add(PIN_ENCODER_BTN, encoder_button_isr, NULL);
    
    ESP_LOGI(TAG, "Encoder initialized");
    return ESP_OK;
}

/**
 * @brief Default encoder behavior: switch to next/previous profile
 */
static void encoder_default_profile_switch(bool clockwise) {
    uint8_t current = profile_get_current_id();
    uint8_t next;
    
    if (clockwise) {
        next = (current + 1) % NUM_PROFILES;
    } else {
        next = (current == 0) ? (NUM_PROFILES - 1) : (current - 1);
    }
    
    ESP_LOGI(TAG, "Default: switching profile %d -> %d", current, next);
    profile_switch(next);
}

/**
 * @brief Execute encoder action from profile config, or fall back to default
 */
static void encoder_execute_action(action_type_t type, const uint8_t* data, uint16_t data_len, bool is_rotation, bool clockwise) {
    if (type == ACTION_TYPE_NONE) {
        // No action configured — use default behavior
        if (is_rotation) {
            encoder_default_profile_switch(clockwise);
        } else {
            // Default button press: reset to profile 0
            ESP_LOGI(TAG, "Default: encoder press -> profile 0");
            profile_switch(0);
        }
    } else {
        // Execute configured action
        action_execute_raw(type, data, data_len);
    }
}

void encoder_task(void* arg) {
    encoder_event_t event;
    int16_t step_accumulator = 0;
    
    ESP_LOGI(TAG, "Encoder task started");
    
    while (1) {
        if (xQueueReceive(encoder_event_queue, &event, portMAX_DELAY)) {
            
            // Get current profile's encoder config
            profile_t* current_profile = profile_get(profile_get_current_id());
            encoder_config_t* enc_cfg = current_profile ? &current_profile->encoder : NULL;
            
            if (event.type == ENCODER_ROTATED) {
                // Accumulate steps
                if (event.direction == ENCODER_CW) {
                    step_accumulator++;
                } else {
                    step_accumulator--;
                }
                
                ESP_LOGD(TAG, "Encoder steps: %d", step_accumulator);
                
                // Check if threshold reached
                if (abs(step_accumulator) >= ENCODER_STEPS_PER_PROFILE) {
                    bool clockwise = step_accumulator > 0;
                    
                    if (enc_cfg != NULL && clockwise && enc_cfg->cw_action_type != ACTION_TYPE_NONE) {
                        encoder_execute_action(enc_cfg->cw_action_type, enc_cfg->cw_action_data, enc_cfg->cw_action_data_len, true, true);
                    } else if (enc_cfg != NULL && !clockwise && enc_cfg->ccw_action_type != ACTION_TYPE_NONE) {
                        encoder_execute_action(enc_cfg->ccw_action_type, enc_cfg->ccw_action_data, enc_cfg->ccw_action_data_len, true, false);
                    } else {
                        // Default: profile switching
                        encoder_default_profile_switch(clockwise);
                    }
                    
                    step_accumulator = 0;
                }
            }
            else if (event.type == ENCODER_BUTTON_PRESSED) {
                // Debounce
                vTaskDelay(pdMS_TO_TICKS(50));
                
                int level = gpio_get_level(PIN_ENCODER_BTN);
                if (level == 0) {
                    // Button still pressed after debounce
                    uint64_t press_start = event.timestamp;
                    
                    // Wait for release or long press timeout
                    while (gpio_get_level(PIN_ENCODER_BTN) == 0) {
                        vTaskDelay(pdMS_TO_TICKS(10));
                        
                        uint64_t press_duration = esp_timer_get_time() - press_start;
                        if (press_duration >= (BUTTON_LONG_PRESS_MS * 1000)) {
                            ESP_LOGI(TAG, "Encoder button long press");
                            break;
                        }
                    }
                    
                    uint64_t press_duration = esp_timer_get_time() - press_start;
                    if (press_duration < (BUTTON_LONG_PRESS_MS * 1000)) {
                        ESP_LOGI(TAG, "Encoder button short press");
                        if (enc_cfg != NULL) {
                            encoder_execute_action(enc_cfg->press_action_type, enc_cfg->press_action_data, enc_cfg->press_action_data_len, false, false);
                        } else {
                            // Default: reset to profile 0
                            profile_switch(0);
                        }
                    }
                }
            }
        }
    }
}
