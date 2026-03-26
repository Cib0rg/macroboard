/**
 * @file display_mux.h
 * @brief Display multiplexer for selecting one of 10 displays
 */

#ifndef DISPLAY_MUX_H
#define DISPLAY_MUX_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Initialize display multiplexer
 * @return ESP_OK on success
 */
esp_err_t display_mux_init(void);

/**
 * @brief Select active display
 * @param display_id Display ID (0-9)
 * @return ESP_OK on success
 */
esp_err_t display_mux_select(uint8_t display_id);

#endif // DISPLAY_MUX_H
