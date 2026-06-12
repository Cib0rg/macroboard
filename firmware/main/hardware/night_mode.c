/**
 * @file night_mode.c
 * @brief Night mode toggle implementation
 */

#include "common.h"
#include "night_mode.h"
#include "hardware/leds.h"
#include "hardware/gc9a01.h"
#include "profile/profile_manager.h"
#include "profile/profile_types.h"

static const char* TAG = "NIGHT";

#define NIGHT_INDICATOR_BRIGHTNESS 25  // ~10% of 255

static bool    night_active     = false;
static uint8_t saved_brightness = 255;

void night_mode_toggle(uint8_t button_id) {
    if (!night_active) {
        saved_brightness = gc9a01_get_brightness();

        led_clear_all();

        // Keep the trigger button lit at 10% as a visual indicator
        button_config_t* btn = profile_get_button_config(button_id);
        uint8_t r = 255, g = 255, b = 255;  // fallback: dim white
        if (btn && (btn->led_r || btn->led_g || btn->led_b)) {
            r = btn->led_r;
            g = btn->led_g;
            b = btn->led_b;
        }
        led_set_color(button_id, r, g, b, NIGHT_INDICATOR_BRIGHTNESS);
        led_update();

        gc9a01_set_brightness(0);
        night_active = true;
        ESP_LOGI(TAG, "Night mode ON (button=%d, saved brightness=%d)", button_id, saved_brightness);
    } else {
        gc9a01_set_brightness(saved_brightness);
        profile_restore_leds();
        night_active = false;
        ESP_LOGI(TAG, "Night mode OFF");
    }
}

bool night_mode_is_active(void) {
    return night_active;
}
