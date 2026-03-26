/**
 * @file leds.h
 * @brief WS2812 RGB LED driver
 */

#ifndef LEDS_H
#define LEDS_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Initialize LED driver
 * @return ESP_OK on success
 */
esp_err_t leds_init(void);

/**
 * @brief Set LED color
 * @param led_id LED ID (0-9)
 * @param r Red component (0-255)
 * @param g Green component (0-255)
 * @param b Blue component (0-255)
 * @param brightness Brightness (0-255)
 * @return ESP_OK on success
 */
esp_err_t led_set_color(uint8_t led_id, uint8_t r, uint8_t g, uint8_t b, uint8_t brightness);

/**
 * @brief Update all LEDs (send data to strip)
 * @return ESP_OK on success
 */
esp_err_t led_update(void);

/**
 * @brief Clear all LEDs (turn off)
 * @return ESP_OK on success
 */
esp_err_t led_clear_all(void);

/**
 * @brief LED task for effects
 * @param arg Task argument (unused)
 */
void led_task(void* arg);

#endif // LEDS_H
