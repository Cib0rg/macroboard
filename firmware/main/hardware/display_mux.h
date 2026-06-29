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

/**
 * @brief Deselect all displays (all CS lines HIGH).
 *
 * Points the address lines to output 7 (unused on both decoders).
 * Call at the end of every SPI frame so the next display_mux_select() produces
 * the CS HIGH→LOW transition the GC9D01 requires to start a new frame.
 */
void display_mux_deselect(void);

#endif // DISPLAY_MUX_H
