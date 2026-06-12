/**
 * @file text_render.h
 * @brief Text rendering to GC9D01 displays (160x160 RGB565)
 */

#ifndef TEXT_RENDER_H
#define TEXT_RENDER_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Render text centered on a display (full 160x160).
 * @param display_id Display ID (0-9)
 * @param text       Null-terminated string; '\n' forces a line break
 * @param fg_color   Foreground color (RGB565)
 * @param bg_color   Background color (RGB565)
 * @return ESP_OK on success
 */
esp_err_t text_render_to_display(uint8_t display_id, const char* text,
                                  uint16_t fg_color, uint16_t bg_color);

/**
 * @brief Render text centered within a horizontal band of the display.
 *        Background outside the text glyphs is filled with bg_color.
 * @param display_id Display ID (0-9)
 * @param text       Null-terminated string; '\n' forces a line break
 * @param fg_color   Foreground color (RGB565)
 * @param bg_color   Background color (RGB565)
 * @param y_offset   Top row of the region on the display
 * @param region_h   Height of the region in pixels
 * @return ESP_OK on success
 */
esp_err_t text_render_to_region(uint8_t display_id, const char* text,
                                 uint16_t fg_color, uint16_t bg_color,
                                 uint16_t y_offset, uint16_t region_h);

/**
 * @brief Fill a horizontal region of a pre-allocated frame buffer with text.
 *        Writes bg_color across all pixels, then draws text centered in the region.
 *        No memory allocation or SPI transfer — caller owns frame_buf and drives the draw.
 * @param frame_buf  Full-frame RGB565 buffer (caller-allocated, frame_w × frame_h × 2 bytes)
 * @param frame_w    Width of the full frame in pixels (typically DISPLAY_WIDTH = 160)
 * @param region_y   Top row of the region within frame_buf (pixels from top)
 * @param region_h   Height of the region in pixels
 * @param text       Text to render (NULL or empty → fill bg only)
 * @param fg         Foreground color (RGB565, big-endian for SPI)
 * @param bg         Background color (RGB565, big-endian for SPI)
 */
void text_render_fill_region(uint8_t* frame_buf, int frame_w,
                              int region_y, int region_h,
                              const char* text, uint16_t fg, uint16_t bg);

#endif // TEXT_RENDER_H
