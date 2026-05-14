/**
 * @file jpeg_decode_util.h
 * @brief JPEG to RGB565 decoder utility using esp_jpeg component
 *
 * NOTE: This file is intentionally NOT named "jpeg_decoder.h" to avoid
 * a naming collision with the esp_jpeg component's own "jpeg_decoder.h" header.
 */

#ifndef JPEG_DECODE_UTIL_H
#define JPEG_DECODE_UTIL_H

#include <stdint.h>
#include <stddef.h>
#include "esp_err.h"

/**
 * @brief Decode JPEG data to RGB565 big-endian (ready for SPI display)
 *
 * Uses the esp_jpeg component to decode JPEG → RGB888, then converts
 * each pixel to RGB565 in big-endian byte order suitable for direct
 * SPI transfer to GC9D01 displays.
 *
 * @param jpeg_data     Pointer to JPEG compressed data
 * @param jpeg_size     Size of JPEG data in bytes
 * @param rgb565_output Caller-allocated output buffer for RGB565 data
 * @param output_size   Size of output buffer in bytes (must be >= width*height*2)
 * @param out_width     Output: decoded image width
 * @param out_height    Output: decoded image height
 * @return ESP_OK on success, error code on failure
 */
esp_err_t jpeg_decode_to_rgb565(const uint8_t* jpeg_data, size_t jpeg_size,
                                 uint8_t* rgb565_output, size_t output_size,
                                 uint16_t* out_width, uint16_t* out_height);

#endif // JPEG_DECODE_UTIL_H
