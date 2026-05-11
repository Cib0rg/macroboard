/**
 * @file buttons.c
 * @brief Button driver implementation with interrupt-based debouncing
 */

#include "buttons.h"
#include "config.h"
#include "esp_log.h"
#include "driver/gpio.h"
#include "esp_timer.h"
#include "freertos/task.h"
#include "profile/action_executor.h"
#include "profile/profile_manager.h"
#include "protocol/protocol_handler.h"
#include "protocol/protocol_types.h"

static const char* TAG = "BUTTONS";

QueueHandle_t button_event_queue = NULL;

// Button GPIO pins
static const uint8_t button_pins[NUM_BUTTONS] = {
    PIN_BUTTON_0, PIN_BUTTON_1, PIN_BUTTON_2, PIN_BUTTON_3, PIN_BUTTON_4,
    PIN_BUTTON_5, PIN_BUTTON_6, PIN_BUTTON_7, PIN_BUTTON_8, PIN_BUTTON_9
};

// Button state tracking
typedef struct {
    uint64_t press_time;
    bool is_pressed;
    bool long_press_sent;
} button_state_t;

static button_state_t button_states[NUM_BUTTONS] = {0};

// ISR handler for button interrupts
static void IRAM_ATTR button_isr_handler(void* arg) {
    uint8_t button_id = (uint8_t)(uintptr_t)arg;
    BaseType_t xHigherPriorityTaskWoken = pdFALSE;
    
    button_event_t event = {
        .button_id = button_id,
        .timestamp = esp_timer_get_time(),
        .event_type = BUTTON_EVENT_PRESS,
    };
    
    xQueueSendFromISR(button_event_queue, &event, &xHigherPriorityTaskWoken);
    
    if (xHigherPriorityTaskWoken) {
        portYIELD_FROM_ISR();
    }
}

esp_err_t buttons_init(void) {
    ESP_LOGI(TAG, "Initializing buttons");
    
    // Create event queue
    button_event_queue = xQueueCreate(10, sizeof(button_event_t));
    if (button_event_queue == NULL) {
        ESP_LOGE(TAG, "Failed to create button event queue");
        return ESP_FAIL;
    }
    
    // Install GPIO ISR service FIRST
    esp_err_t ret = gpio_install_isr_service(0);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to install GPIO ISR service: %s", esp_err_to_name(ret));
        return ret;
    }
    
    // Configure button pins with interrupts
    for (int i = 0; i < NUM_BUTTONS; i++) {
        gpio_config_t io_conf = {
            .pin_bit_mask = (1ULL << button_pins[i]),
            .mode = GPIO_MODE_INPUT,
            .pull_up_en = GPIO_PULLUP_ENABLE,
            .pull_down_en = GPIO_PULLDOWN_DISABLE,
            .intr_type = GPIO_INTR_ANYEDGE,  // Both press and release
        };
        
        ret = gpio_config(&io_conf);
        if (ret != ESP_OK) {
            ESP_LOGE(TAG, "Failed to configure button %d: %s", i, esp_err_to_name(ret));
            return ret;
        }
        
        // Add ISR handler
        gpio_isr_handler_add(button_pins[i], button_isr_handler, (void*)(uintptr_t)i);
    }
    
    ESP_LOGI(TAG, "Buttons initialized (%d buttons)", NUM_BUTTONS);
    return ESP_OK;
}

void button_task(void* arg) {
    button_event_t event;
    
    ESP_LOGI(TAG, "Button task started");
    
    // Log initial stack high water mark
    ESP_LOGI(TAG, "Button task stack high water mark: %u bytes free",
             (unsigned int)(uxTaskGetStackHighWaterMark(NULL) * sizeof(StackType_t)));
    
    while (1) {
        if (xQueueReceive(button_event_queue, &event, portMAX_DELAY)) {
            uint8_t btn_id = event.button_id;
            
            if (btn_id >= NUM_BUTTONS) {
                continue;
            }
            
            // Read current button state
            int level = gpio_get_level(button_pins[btn_id]);
            
            // Debouncing delay
            vTaskDelay(pdMS_TO_TICKS(BUTTON_DEBOUNCE_MS));
            
            // Verify state after debounce
            int level_after = gpio_get_level(button_pins[btn_id]);
            if (level != level_after) {
                // Bounce detected, ignore
                continue;
            }
            
            if (level == 0) {
                // Button pressed (active low)
                if (!button_states[btn_id].is_pressed) {
                    button_states[btn_id].is_pressed = true;
                    button_states[btn_id].press_time = event.timestamp;
                    button_states[btn_id].long_press_sent = false;
                    
                    ESP_LOGI(TAG, "Button %d pressed", btn_id);
                    
                    // Diagnostic: log stack before action execution
                    ESP_LOGI(TAG, "Stack before action: %u bytes free",
                             (unsigned int)(uxTaskGetStackHighWaterMark(NULL) * sizeof(StackType_t)));
                    
                    // Send button press event to PC
                    ESP_LOGD(TAG, "Sending button press event to PC...");
                    {
                        uint8_t evt_payload[3];
                        evt_payload[0] = btn_id;
                        evt_payload[1] = profile_get_current_id();
                        button_config_t* btn_cfg = profile_get_button_config(btn_id);
                        evt_payload[2] = btn_cfg ? btn_cfg->action_type : 0;
                        protocol_send_event(EVENT_BUTTON_PRESSED, evt_payload, 3);
                    }
                    ESP_LOGD(TAG, "Event sent, executing action...");
                    
                    // Execute button action
                    action_execute(btn_id);
                    
                    // Diagnostic: log stack after action execution
                    ESP_LOGI(TAG, "Stack after action: %u bytes free",
                             (unsigned int)(uxTaskGetStackHighWaterMark(NULL) * sizeof(StackType_t)));
                }
            } else {
                // Button released
                if (button_states[btn_id].is_pressed) {
                    uint64_t press_duration = event.timestamp - button_states[btn_id].press_time;
                    button_states[btn_id].is_pressed = false;
                    
                    ESP_LOGI(TAG, "Button %d released (duration: %llu us)", 
                             btn_id, press_duration);
                    
                    // Check for long press
                    if (press_duration >= (BUTTON_LONG_PRESS_MS * 1000) && 
                        !button_states[btn_id].long_press_sent) {
                        ESP_LOGI(TAG, "Button %d long press detected", btn_id);
                        // Could trigger different action for long press
                    }
                }
            }
        }
    }
}
