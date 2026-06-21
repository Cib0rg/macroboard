/**
 * @file buttons.c
 * @brief Button driver implementation with interrupt-based debouncing
 */

#include "buttons.h"
#include "leds.h"
#include "config.h"
#include "esp_log.h"
#include "esp_system.h"
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
    bool combo_consumed; // action suppressed — button is part of the active reboot combo
} button_state_t;

static button_state_t button_states[NUM_BUTTONS] = {0};

// ── Soft-reboot combo ─────────────────────────────────────────────────────────
// Hold buttons 0+1+2 simultaneously for REBOOT_HOLD_MS → esp_restart().
//
// To avoid firing individual button actions when the combo is pressed:
//   • Combo-button press events are buffered for COMBO_WINDOW_US.
//   • If all three are pressed within that window → combo confirmed, buffer
//     discarded, actions suppressed.
//   • If the window expires before all three are pressed → buffered events
//     are sent normally (behaves like regular button presses).
// ─────────────────────────────────────────────────────────────────────────────
#define REBOOT_HOLD_MS    3000
#define COMBO_WINDOW_US   150000ULL   // 150 ms to press all three buttons
static const uint8_t REBOOT_COMBO[]  = {0, 1, 2};
#define REBOOT_COMBO_SIZE 3

static bool     reboot_combo_active   = false;
static uint64_t reboot_combo_start_us = 0;

// Pending press events for combo buttons not yet sent to the PC
typedef struct { bool pending; uint8_t payload[3]; } combo_evt_t;
static combo_evt_t  combo_pending[REBOOT_COMBO_SIZE] = {0};
static uint64_t     combo_window_start_us             = 0; // 0 = no pending window

// Returns the combo index of btn_id, or -1 if not a combo button.
static int get_combo_idx(uint8_t btn_id) {
    for (int i = 0; i < REBOOT_COMBO_SIZE; i++)
        if (REBOOT_COMBO[i] == btn_id) return i;
    return -1;
}

// Send all buffered combo events (combo did not form in time).
static void flush_combo_pending(void) {
    for (int i = 0; i < REBOOT_COMBO_SIZE; i++) {
        if (combo_pending[i].pending) {
            protocol_send_event(EVENT_BUTTON_PRESSED, combo_pending[i].payload, 3);
            combo_pending[i].pending = false;
        }
    }
    combo_window_start_us = 0;
}

// Discard all buffered combo events (combo formed — actions are suppressed).
static void cancel_combo_pending(void) {
    for (int i = 0; i < REBOOT_COMBO_SIZE; i++)
        combo_pending[i].pending = false;
    combo_window_start_us = 0;
}

// Call after any change to button_states[].is_pressed.
static void update_reboot_combo(void) {
    bool all_pressed = true;
    for (int i = 0; i < REBOOT_COMBO_SIZE; i++)
        if (!button_states[REBOOT_COMBO[i]].is_pressed) { all_pressed = false; break; }

    if (all_pressed && !reboot_combo_active) {
        reboot_combo_active   = true;
        reboot_combo_start_us = esp_timer_get_time();
        // Discard buffered press events — this is a reboot gesture, not individual presses
        cancel_combo_pending();
        // Prevent long-press timers and short-press actions for all combo buttons
        for (int i = 0; i < REBOOT_COMBO_SIZE; i++) {
            uint8_t b = REBOOT_COMBO[i];
            button_states[b].long_press_sent = true;
            button_states[b].combo_consumed  = true;
        }
        ESP_LOGW(TAG, "Reboot combo formed — hold for %d s to reboot", REBOOT_HOLD_MS / 1000);
    } else if (!all_pressed && reboot_combo_active) {
        reboot_combo_active = false;
        ESP_LOGI(TAG, "Reboot combo cancelled");
    }
}

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

// Brief LED flash to confirm long press fired: off 100 ms, then restore.
static void led_blink_confirm(uint8_t btn_id) {
    button_config_t* cfg = profile_get_button_config(btn_id);

    led_set_color(btn_id, 0, 0, 0, 0);
    led_update();

    vTaskDelay(pdMS_TO_TICKS(100));

    if (cfg != NULL) {
        led_set_color(btn_id, cfg->led_r, cfg->led_g, cfg->led_b, cfg->led_brightness);
    }
    led_update();
}

// Calculate how many ticks until the next threshold crossing (long press, reboot hold, or combo window).
// Returns portMAX_DELAY when nothing is pending.
static TickType_t calculate_timeout(void) {
    TickType_t min_ticks = portMAX_DELAY;
    uint64_t now = esp_timer_get_time();

    for (int i = 0; i < NUM_BUTTONS; i++) {
        if (!button_states[i].is_pressed || button_states[i].long_press_sent) continue;

        uint64_t held_us      = now - button_states[i].press_time;
        uint64_t threshold_us = (uint64_t)BUTTON_LONG_PRESS_MS * 1000ULL;

        if (held_us >= threshold_us) return 0;

        uint64_t remaining_us = threshold_us - held_us;
        uint32_t remaining_ms = (uint32_t)((remaining_us + 999ULL) / 1000ULL);
        TickType_t ticks = pdMS_TO_TICKS(remaining_ms);
        if (ticks == 0) ticks = 1;
        if (ticks < min_ticks) min_ticks = ticks;
    }

    // Wake to flush the combo detection window if it hasn't formed yet
    if (combo_window_start_us != 0) {
        uint64_t elapsed   = now - combo_window_start_us;
        if (elapsed >= COMBO_WINDOW_US) return 0;
        uint64_t remaining = COMBO_WINDOW_US - elapsed;
        uint32_t ms        = (uint32_t)((remaining + 999ULL) / 1000ULL);
        TickType_t ticks   = pdMS_TO_TICKS(ms);
        if (ticks == 0) ticks = 1;
        if (ticks < min_ticks) min_ticks = ticks;
    }

    // Wake to fire the reboot once the hold threshold is reached
    if (reboot_combo_active) {
        uint64_t held_us = now - reboot_combo_start_us;
        uint64_t threshold_us = (uint64_t)REBOOT_HOLD_MS * 1000ULL;
        if (held_us >= threshold_us) return 0;
        uint64_t remaining_us = threshold_us - held_us;
        uint32_t remaining_ms = (uint32_t)((remaining_us + 999ULL) / 1000ULL);
        TickType_t ticks = pdMS_TO_TICKS(remaining_ms);
        if (ticks == 0) ticks = 1;
        if (ticks < min_ticks) min_ticks = ticks;
    }

    return min_ticks;
}

void button_task(void* arg) {
    button_event_t event;

    ESP_LOGI(TAG, "Button task started");
    ESP_LOGI(TAG, "Button task stack high water mark: %u bytes free",
             (unsigned int)(uxTaskGetStackHighWaterMark(NULL) * sizeof(StackType_t)));

    while (1) {
        TickType_t timeout = calculate_timeout();

        if (xQueueReceive(button_event_queue, &event, timeout) == pdFALSE) {
            // Timeout — check every pending deadline.
            uint64_t now = esp_timer_get_time();

            // Flush combo window if it expired before all three buttons were pressed
            if (combo_window_start_us != 0 && (now - combo_window_start_us) >= COMBO_WINDOW_US) {
                ESP_LOGI(TAG, "Combo window expired — flushing buffered events");
                flush_combo_pending();
            }

            // Fire long press for any button that crossed its threshold
            for (int i = 0; i < NUM_BUTTONS; i++) {
                if (!button_states[i].is_pressed || button_states[i].long_press_sent) continue;
                uint64_t held_us = now - button_states[i].press_time;
                if (held_us >= (uint64_t)BUTTON_LONG_PRESS_MS * 1000ULL) {
                    ESP_LOGI(TAG, "Button %d long press (threshold)", i);
                    button_states[i].long_press_sent = true;
                    action_execute_long_press((uint8_t)i);
                    led_blink_confirm((uint8_t)i);
                }
            }

            // Reboot if the combo has been held long enough
            if (reboot_combo_active) {
                uint64_t held_us = now - reboot_combo_start_us;
                if (held_us >= (uint64_t)REBOOT_HOLD_MS * 1000ULL) {
                    ESP_LOGW(TAG, "Reboot combo held for %llu ms — restarting!", held_us / 1000ULL);
                    vTaskDelay(pdMS_TO_TICKS(50));
                    esp_restart();
                }
            }
            continue;
        }

        // Got an edge event from ISR.
        uint8_t btn_id = event.button_id;
        if (btn_id >= NUM_BUTTONS) continue;

        // Debounce
        int level = gpio_get_level(button_pins[btn_id]);
        vTaskDelay(pdMS_TO_TICKS(BUTTON_DEBOUNCE_MS));
        int level_after = gpio_get_level(button_pins[btn_id]);
        if (level != level_after) continue;  // bounce

        if (level == 0) {
            // ── Button pressed (active low) ──────────────────────────────────
            if (!button_states[btn_id].is_pressed) {
                button_states[btn_id].is_pressed      = true;
                button_states[btn_id].press_time      = event.timestamp;
                button_states[btn_id].long_press_sent = false;
                button_states[btn_id].combo_consumed  = false;

                ESP_LOGI(TAG, "Button %d pressed", btn_id);

                button_config_t* btn_cfg = profile_get_button_config(btn_id);
                uint8_t evt_payload[3] = { btn_id, 0, btn_cfg ? btn_cfg->action_type : 0 };

                int cidx = get_combo_idx(btn_id);
                if (cidx >= 0) {
                    // Combo button: buffer the event — send only if combo doesn't form in time.
                    // If there are already non-combo events that flushed the window, this combo
                    // attempt is invalid; treat it as a regular button.
                    if (combo_window_start_us == 0) {
                        // Start or extend the detection window only on first combo-button press.
                        combo_window_start_us = esp_timer_get_time();
                    }
                    combo_pending[cidx].pending    = true;
                    combo_pending[cidx].payload[0] = evt_payload[0];
                    combo_pending[cidx].payload[1] = evt_payload[1];
                    combo_pending[cidx].payload[2] = evt_payload[2];
                    ESP_LOGI(TAG, "Button %d buffered (combo candidate)", btn_id);
                } else {
                    // Non-combo button pressed — flush any pending combo window first so
                    // those buttons behave normally.
                    if (combo_window_start_us != 0) {
                        ESP_LOGI(TAG, "Non-combo press — flushing buffered combo events");
                        flush_combo_pending();
                    }
                    protocol_send_event(EVENT_BUTTON_PRESSED, evt_payload, 3);
                }

                update_reboot_combo();
            }
        } else {
            // ── Button released ──────────────────────────────────────────────
            if (button_states[btn_id].is_pressed) {
                uint64_t press_duration = esp_timer_get_time() - button_states[btn_id].press_time;
                bool already_fired    = button_states[btn_id].long_press_sent;
                bool was_consumed     = button_states[btn_id].combo_consumed;
                button_states[btn_id].is_pressed      = false;
                button_states[btn_id].long_press_sent = false;
                button_states[btn_id].combo_consumed  = false;

                ESP_LOGI(TAG, "Button %d released (duration: %llu us, already_fired: %d, consumed: %d)",
                         btn_id, press_duration, already_fired, was_consumed);

                int cidx = get_combo_idx(btn_id);
                if (cidx >= 0 && combo_pending[cidx].pending) {
                    // Released before window closed and combo didn't form — send now.
                    protocol_send_event(EVENT_BUTTON_PRESSED, combo_pending[cidx].payload, 3);
                    combo_pending[cidx].pending = false;
                    // Check if the window is now empty
                    bool any_pending = false;
                    for (int i = 0; i < REBOOT_COMBO_SIZE; i++)
                        if (combo_pending[i].pending) { any_pending = true; break; }
                    if (!any_pending) combo_window_start_us = 0;
                }

                update_reboot_combo();

                // Suppress actions entirely if button was consumed by the reboot combo.
                if (was_consumed) {
                    ESP_LOGI(TAG, "Button %d release skipped — consumed by reboot combo", btn_id);
                } else if (already_fired) {
                    // Long press already executed at threshold crossing — nothing on release.
                } else if (press_duration >= (uint64_t)BUTTON_LONG_PRESS_MS * 1000ULL) {
                    ESP_LOGI(TAG, "Button %d long press (on release)", btn_id);
                    action_execute_long_press(btn_id);
                    led_blink_confirm(btn_id);
                } else {
                    ESP_LOGI(TAG, "Button %d short press", btn_id);
                    action_execute(btn_id);
                }

                ESP_LOGI(TAG, "Stack after action: %u bytes free",
                         (unsigned int)(uxTaskGetStackHighWaterMark(NULL) * sizeof(StackType_t)));
            }
        }
    }
}
