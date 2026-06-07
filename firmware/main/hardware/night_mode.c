/**
 * @file night_mode.c
 * @brief Night mode toggle implementation
 */

#include "common.h"
#include "night_mode.h"
#include "hardware/leds.h"
#include "hardware/gc9a01.h"
#include "profile/profile_manager.h"

static const char* TAG = "NIGHT";

static bool    night_active      = false;
static uint8_t saved_brightness  = 255;

void night_mode_toggle(void) {
    if (!night_active) {
        saved_brightness = gc9a01_get_brightness();
        led_clear_all();
        led_update();
        gc9a01_set_brightness(0);
        night_active = true;
        ESP_LOGI(TAG, "Night mode ON (saved brightness=%d)", saved_brightness);
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
