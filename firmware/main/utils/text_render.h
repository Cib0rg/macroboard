/**
 * @file text_render.h
 * @brief Text rendering to GC9D01 displays (160x160 RGB565)
 */

#ifndef TEXT_RENDER_H
#define TEXT_RENDER_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Render text centered on a display.
 *        Allocates an RGB565 buffer, draws text, sends to display, frees buffer.
 *        Chooses scale automatically: 2x (16x16/char) if text is short,
 *        1x (8x8/char) for longer text.
 * @param display_id Display ID (0-9)
 * @param text       Null-terminated string; '\n' forces a line break
 * @param fg_color   Foreground color (RGB565)
 * @param bg_color   Background color (RGB565)
 * @return ESP_OK on success
 */
esp_err_t text_render_to_display(uint8_t display_id, const char* text,
                                  uint16_t fg_color, uint16_t bg_color);

#endif // TEXT_RENDER_H
