/**
 * @file encoder.c
 * @brief Rotary encoder driver implementation
 */

#include "encoder.h"
#include "config.h"
#include "esp_log.h"
#include "driver/gpio.h"
#include "esp_timer.h"
#include "profile/action_executor.h"
#include "profile/profile_manager.h"

static const char* TAG = "ENCODER";

QueueHandle_t encoder_event_queue = NULL;

static volatile uint32_t s_isr_fire_count = 0;  // incremented in ISR, logged in task

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
    
    s_isr_fire_count++;

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
    
    // Create event queue — 32 slots so burst-rotation during button hold doesn't overflow
    encoder_event_queue = xQueueCreate(32, sizeof(encoder_event_t));
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


void encoder_task(void* arg) {
    encoder_event_t event;
    int16_t step_accumulator = 0;
    uint32_t last_logged_isr_count = 0;

    ESP_LOGI(TAG, "Encoder task started (pins A=%d B=%d BTN=%d, threshold=%d)",
             PIN_ENCODER_A, PIN_ENCODER_B, PIN_ENCODER_BTN, ENCODER_STEPS_PER_PROFILE);
    ESP_LOGI(TAG, "Initial pin states: A=%d B=%d",
             gpio_get_level(PIN_ENCODER_A), gpio_get_level(PIN_ENCODER_B));

    while (1) {
        if (xQueueReceive(encoder_event_queue, &event, pdMS_TO_TICKS(2000))) {

            // Log ISR fire count whenever it changed — confirms interrupts are arriving
            uint32_t cur = s_isr_fire_count;
            if (cur != last_logged_isr_count) {
                ESP_LOGI(TAG, "ISR fired %lu times total", (unsigned long)cur);
                last_logged_isr_count = cur;
            }

            profile_t* current_profile = profile_get(0);
            encoder_config_t* enc_cfg = current_profile ? &current_profile->encoder : NULL;

            if (event.type == ENCODER_ROTATED) {
                if (event.direction == ENCODER_CW) {
                    step_accumulator++;
                } else {
                    step_accumulator--;
                }

                ESP_LOGI(TAG, "Rotation event: %s, accumulator=%d",
                         event.direction == ENCODER_CW ? "CW" : "CCW", step_accumulator);

                if (abs(step_accumulator) >= ENCODER_STEPS_PER_PROFILE) {
                    bool clockwise = step_accumulator > 0;
                    ESP_LOGI(TAG, "Threshold reached → firing %s action", clockwise ? "CW" : "CCW");

                    if (enc_cfg != NULL && clockwise && enc_cfg->cw_action_type != ACTION_TYPE_NONE) {
                        action_execute_raw(enc_cfg->cw_action_type, enc_cfg->cw_action_data, enc_cfg->cw_action_data_len);
                    } else if (enc_cfg != NULL && !clockwise && enc_cfg->ccw_action_type != ACTION_TYPE_NONE) {
                        action_execute_raw(enc_cfg->ccw_action_type, enc_cfg->ccw_action_data, enc_cfg->ccw_action_data_len);
                    } else {
                        ESP_LOGW(TAG, "No action configured for %s rotation", clockwise ? "CW" : "CCW");
                    }

                    step_accumulator = 0;
                }
            }
            else if (event.type == ENCODER_BUTTON_PRESSED) {
                vTaskDelay(pdMS_TO_TICKS(50));  // debounce

                int level = gpio_get_level(PIN_ENCODER_BTN);
                if (level == 0) {
                    uint64_t press_start = event.timestamp;

                    while (gpio_get_level(PIN_ENCODER_BTN) == 0) {
                        vTaskDelay(pdMS_TO_TICKS(10));
                        if ((esp_timer_get_time() - press_start) >= (BUTTON_LONG_PRESS_MS * 1000ULL)) {
                            break;
                        }
                    }

                    uint64_t press_duration = esp_timer_get_time() - press_start;
                    if (press_duration < (BUTTON_LONG_PRESS_MS * 1000ULL)) {
                        ESP_LOGI(TAG, "Encoder button short press");
                        if (enc_cfg != NULL && enc_cfg->press_action_type != ACTION_TYPE_NONE) {
                            action_execute_raw(enc_cfg->press_action_type, enc_cfg->press_action_data, enc_cfg->press_action_data_len);
                        }
                    } else {
                        ESP_LOGI(TAG, "Encoder button long press");
                        if (enc_cfg != NULL && enc_cfg->long_press_action_type != ACTION_TYPE_NONE) {
                            action_execute_raw(enc_cfg->long_press_action_type, enc_cfg->long_press_action_data, enc_cfg->long_press_action_data_len);
                        }
                    }
                }
            }
        } else {
            // 2-second timeout — log ISR count to confirm ISR is (or isn't) firing
            uint32_t cur = s_isr_fire_count;
            if (cur != last_logged_isr_count) {
                ESP_LOGI(TAG, "ISR fired %lu times total (idle flush)", (unsigned long)cur);
                last_logged_isr_count = cur;
            }
        }
    }
}
