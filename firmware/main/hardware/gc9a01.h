/**
 * @file gc9a01.h
 * @brief GC9D01 display driver for 160x160 round LCD
 * 
 * NOTE: Despite the filename "gc9a01", the actual display IC is GC9D01 (160x160).
 * The API names are kept as gc9a01_* for backward compatibility with the rest
 * of the firmware codebase.
 */

#ifndef GC9A01_H
#define GC9A01_H

#include <stdint.h>
#include <stdbool.h>
#include "esp_err.h"
#include "driver/spi_master.h"

// GC9D01 Commands (compatible with GC9A01 standard commands)
#define GC9A01_SLPIN    0x10
#define GC9A01_SLPOUT   0x11
#define GC9A01_INVOFF   0x20
#define GC9A01_INVON    0x21
#define GC9A01_DISPOFF  0x28
#define GC9A01_DISPON   0x29
#define GC9A01_CASET    0x2A
#define GC9A01_RASET    0x2B
#define GC9A01_RAMWR    0x2C
#define GC9A01_COLMOD   0x3A
#define GC9A01_MADCTL   0x36

// Color definitions (RGB565)
#define COLOR_BLACK     0x0000
#define COLOR_WHITE     0xFFFF
#define COLOR_RED       0xF800
#define COLOR_GREEN     0x07E0
#define COLOR_BLUE      0x001F

/**
 * @brief Initialize GC9D01 display driver (SPI bus and GPIO)
 * @return ESP_OK on success
 */
esp_err_t gc9a01_init(void);

/**
 * @brief Initialize a specific display with GC9D01 init sequence
 * @param display_id Display ID (0-9)
 * @return ESP_OK on success
 */
esp_err_t gc9a01_init_display(uint8_t display_id);

/**
 * @brief Clear display with color
 * @param display_id Display ID
 * @param color RGB565 color
 * @return ESP_OK on success
 */
esp_err_t gc9a01_clear(uint8_t display_id, uint16_t color);

/**
 * @brief Draw image on display (centered)
 * @param display_id Display ID
 * @param image_data RGB565 image data (big-endian byte order for SPI)
 * @param width Image width
 * @param height Image height
 * @return ESP_OK on success
 */
esp_err_t gc9a01_draw_image(uint8_t display_id, const uint8_t* image_data,
                             uint16_t width, uint16_t height);

/**
 * @brief Draw image centered within a horizontal band of the display.
 *        If image is taller than region_h, it is center-cropped.
 *        Does NOT fill background pixels outside the image.
 * @param display_id  Display ID
 * @param image_data  RGB565 image data (big-endian)
 * @param img_w       Image width (must be <= DISPLAY_WIDTH)
 * @param img_h       Image height
 * @param dst_y       Top pixel of the destination region on the display
 * @param region_h    Height of the destination region in pixels
 * @return ESP_OK on success
 */
esp_err_t gc9a01_draw_image_in_region(uint8_t display_id, const uint8_t* image_data,
                                       uint16_t img_w, uint16_t img_h,
                                       uint16_t dst_y, uint16_t region_h);

/**
 * @brief Fill a horizontal band with a solid color.
 * @param display_id Display ID
 * @param y0         First row to fill
 * @param h          Number of rows to fill
 * @param color      RGB565 color (native byte order, same as COLOR_* constants)
 * @return ESP_OK on success
 */
esp_err_t gc9a01_fill_band(uint8_t display_id, uint16_t y0, uint16_t h, uint16_t color);

/**
 * @brief Set display address window for pixel writes
 * @param x0 Start X coordinate
 * @param y0 Start Y coordinate
 * @param x1 End X coordinate
 * @param y1 End Y coordinate
 * @return ESP_OK on success
 */
esp_err_t gc9a01_set_window(uint16_t x0, uint16_t y0, uint16_t x1, uint16_t y1);

/**
 * @brief Write data to display (data mode)
 * @param data Data buffer
 * @param len Data length in bytes
 * @return ESP_OK on success
 */
esp_err_t gc9a01_write_data(const uint8_t* data, size_t len);

/**
 * @brief Send command to display (command mode)
 * @param cmd Command byte
 * @return ESP_OK on success
 */
esp_err_t gc9a01_send_command(uint8_t cmd);

/**
 * @brief Set backlight state for all displays
 * @param enabled true to enable, false to disable
 * @return ESP_OK on success
 */
esp_err_t gc9a01_set_backlight(bool enabled);

/**
 * @brief Set backlight brightness (PWM)
 * @param brightness Brightness level (0-255)
 * @return ESP_OK on success
 */
esp_err_t gc9a01_set_brightness(uint8_t brightness);

/**
 * @brief Get current backlight brightness
 * @return Current brightness level (0-255)
 */
uint8_t gc9a01_get_brightness(void);

#endif // GC9A01_H
