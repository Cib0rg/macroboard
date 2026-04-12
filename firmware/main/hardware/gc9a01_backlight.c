/**
 * @file gc9a01_backlight.c
 * @brief Display backlight control implementation
 */

#include "common.h"
#include "gc9a01.h"
#include "config.h"
#include "driver/gpio.h"
#include "driver/ledc.h"

static const char* TAG = "BACKLIGHT";
static bool backlight_enabled = true;
static uint8_t current_brightness = 255;

esp_err_t gc9a01_set_backlight(bool enabled) {
    backlight_enabled = enabled;
    
    if (enabled) {
        gpio_set_level(PIN_DISPLAY_BACKLIGHT, 1);
        ESP_LOGI(TAG, "Backlight enabled");
    } else {
        gpio_set_level(PIN_DISPLAY_BACKLIGHT, 0);
        ESP_LOGI(TAG, "Backlight disabled");
    }
    
    return ESP_OK;
}

esp_err_t gc9a01_set_brightness(uint8_t brightness) {
    current_brightness = brightness;
    
    // For simple on/off control via GPIO
    // If brightness > 127, turn on, else turn off
    if (brightness > 127) {
        return gc9a01_set_backlight(true);
    } else if (brightness == 0) {
        return gc9a01_set_backlight(false);
    }
    
    // For PWM control, would need to configure LEDC:
    // ledc_set_duty(LEDC_LOW_SPEED_MODE, LEDC_CHANNEL_0, brightness);
    // ledc_update_duty(LEDC_LOW_SPEED_MODE, LEDC_CHANNEL_0);
    
    return ESP_OK;
}
