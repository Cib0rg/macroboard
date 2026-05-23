/**
 * @file gc9a01_backlight.c
 * @brief Display backlight control implementation with PWM brightness
 * 
 * Uses ESP32 LEDC peripheral for PWM-based brightness control.
 * The backlight pin is shared across all displays.
 */

#include "common.h"
#include "gc9a01.h"
#include "config.h"
#include "driver/gpio.h"
#include "driver/ledc.h"

static const char* TAG = "BACKLIGHT";
static bool backlight_initialized = false;
static bool backlight_enabled = true;
static uint8_t current_brightness = 255;

// LEDC configuration for backlight PWM
#define BACKLIGHT_LEDC_TIMER       LEDC_TIMER_1
#define BACKLIGHT_LEDC_MODE        LEDC_LOW_SPEED_MODE
#define BACKLIGHT_LEDC_CHANNEL     LEDC_CHANNEL_0
#define BACKLIGHT_LEDC_DUTY_RES    LEDC_TIMER_8_BIT   // 0-255
#define BACKLIGHT_LEDC_FREQUENCY   5000                // 5 kHz PWM

/**
 * @brief Initialize LEDC PWM for backlight control
 */
static esp_err_t backlight_pwm_init(void) {
    if (backlight_initialized) {
        return ESP_OK;
    }

    // Configure LEDC timer
    ledc_timer_config_t timer_config = {
        .speed_mode      = BACKLIGHT_LEDC_MODE,
        .timer_num        = BACKLIGHT_LEDC_TIMER,
        .duty_resolution  = BACKLIGHT_LEDC_DUTY_RES,
        .freq_hz          = BACKLIGHT_LEDC_FREQUENCY,
        .clk_cfg          = LEDC_AUTO_CLK,
    };
    esp_err_t ret = ledc_timer_config(&timer_config);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to configure LEDC timer: %s", esp_err_to_name(ret));
        return ret;
    }

    // Configure LEDC channel
    ledc_channel_config_t channel_config = {
        .speed_mode = BACKLIGHT_LEDC_MODE,
        .channel    = BACKLIGHT_LEDC_CHANNEL,
        .timer_sel  = BACKLIGHT_LEDC_TIMER,
        .intr_type  = LEDC_INTR_DISABLE,
        .gpio_num   = PIN_DISPLAY_BACKLIGHT,
        .duty       = current_brightness,  // Start at current brightness
        .hpoint     = 0,
    };
    ret = ledc_channel_config(&channel_config);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to configure LEDC channel: %s", esp_err_to_name(ret));
        return ret;
    }

    backlight_initialized = true;
    ESP_LOGI(TAG, "Backlight PWM initialized (GPIO %d, %d Hz)", 
             PIN_DISPLAY_BACKLIGHT, BACKLIGHT_LEDC_FREQUENCY);
    return ESP_OK;
}

esp_err_t gc9a01_set_backlight(bool enabled) {
    backlight_enabled = enabled;

    // Initialize PWM on first use
    esp_err_t ret = backlight_pwm_init();
    if (ret != ESP_OK) {
        // Fallback to simple GPIO
        gpio_set_level(PIN_DISPLAY_BACKLIGHT, enabled ? 1 : 0);
        ESP_LOGW(TAG, "PWM init failed, using GPIO fallback");
        return ESP_OK;
    }

    if (enabled) {
        ledc_set_duty(BACKLIGHT_LEDC_MODE, BACKLIGHT_LEDC_CHANNEL, current_brightness);
        ledc_update_duty(BACKLIGHT_LEDC_MODE, BACKLIGHT_LEDC_CHANNEL);
        ESP_LOGI(TAG, "Backlight enabled (brightness: %d)", current_brightness);
    } else {
        ledc_set_duty(BACKLIGHT_LEDC_MODE, BACKLIGHT_LEDC_CHANNEL, 0);
        ledc_update_duty(BACKLIGHT_LEDC_MODE, BACKLIGHT_LEDC_CHANNEL);
        ESP_LOGI(TAG, "Backlight disabled");
    }

    return ESP_OK;
}

esp_err_t gc9a01_set_brightness(uint8_t brightness) {
    current_brightness = brightness;

    // Initialize PWM on first use
    esp_err_t ret = backlight_pwm_init();
    if (ret != ESP_OK) {
        // Fallback to simple GPIO on/off
        gpio_set_level(PIN_DISPLAY_BACKLIGHT, brightness > 0 ? 1 : 0);
        ESP_LOGW(TAG, "PWM init failed, using GPIO fallback");
        return ESP_OK;
    }

    if (!backlight_enabled && brightness > 0) {
        backlight_enabled = true;
    }

    if (brightness == 0) {
        backlight_enabled = false;
    }

    ledc_set_duty(BACKLIGHT_LEDC_MODE, BACKLIGHT_LEDC_CHANNEL, brightness);
    ledc_update_duty(BACKLIGHT_LEDC_MODE, BACKLIGHT_LEDC_CHANNEL);

    ESP_LOGI(TAG, "Backlight brightness set to %d", brightness);
    return ESP_OK;
}

uint8_t gc9a01_get_brightness(void) {
    return current_brightness;
}
