/**
 * @file jpeg_decoder.c
 * @brief JPEG to RGB565 decoder utility using esp_jpeg component
 *
 * The espressif/esp_jpeg component provides <jpeg_decoder.h> with:
 *   - esp_jpeg_image_cfg_t
 *   - esp_jpeg_image_output_t
 *   - esp_jpeg_decode()
 *   - JPEG_IMAGE_FORMAT_RGB888
 *   - JPEG_IMAGE_SCALE_0
 */

#include "utils/jpeg_decode_util.h"
#include "esp_log.h"
#include "esp_heap_caps.h"

// esp_jpeg component header — provides the JPEG decode API.
// This is the component's own header, NOT our jpeg_decode_util.h.
#include "jpeg_decoder.h"

static const char* TAG = "JPEG_DEC";

esp_err_t jpeg_decode_to_rgb565(const uint8_t* jpeg_data, size_t jpeg_size,
                                 uint8_t* rgb565_output, size_t output_size,
                                 uint16_t* out_width, uint16_t* out_height)
{
    if (jpeg_data == NULL || rgb565_output == NULL || out_width == NULL || out_height == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    if (jpeg_size == 0) {
        return ESP_ERR_INVALID_SIZE;
    }

    ESP_LOGD(TAG, "Decoding JPEG: %u bytes input, %u bytes output buffer",
             (unsigned)jpeg_size, (unsigned)output_size);

    // Allocate temporary RGB888 buffer in PSRAM for the decoded image.
    // Maximum expected size: 160x160x3 = 76800 bytes
    const size_t max_pixels = output_size / 2;  // output_size is for RGB565 (2 bytes/pixel)
    const size_t rgb888_buf_size = max_pixels * 3;

    uint8_t* rgb888_buf = heap_caps_malloc(rgb888_buf_size, MALLOC_CAP_SPIRAM);
    if (rgb888_buf == NULL) {
        ESP_LOGE(TAG, "Failed to allocate RGB888 buffer (%u bytes)", (unsigned)rgb888_buf_size);
        return ESP_ERR_NO_MEM;
    }

    // Configure the JPEG decoder (esp_jpeg component API)
    esp_jpeg_image_cfg_t jpeg_cfg = {
        .indata = (uint8_t*)jpeg_data,
        .indata_size = jpeg_size,
        .outbuf = rgb888_buf,
        .outbuf_size = rgb888_buf_size,
        .out_format = JPEG_IMAGE_FORMAT_RGB888,
        .out_scale = JPEG_IMAGE_SCALE_0,  // No downscaling
        .flags = {
            .swap_color_bytes = false,
        },
    };

    esp_jpeg_image_output_t jpeg_out = {0};

    // Decode JPEG → RGB888
    esp_err_t ret = esp_jpeg_decode(&jpeg_cfg, &jpeg_out);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "JPEG decode failed: %s", esp_err_to_name(ret));
        free(rgb888_buf);
        return ret;
    }

    uint16_t width = jpeg_out.width;
    uint16_t height = jpeg_out.height;

    ESP_LOGD(TAG, "JPEG decoded: %dx%d", width, height);

    // Verify output buffer is large enough for RGB565
    size_t required_size = (size_t)width * height * 2;
    if (required_size > output_size) {
        ESP_LOGE(TAG, "Output buffer too small: need %u, have %u",
                 (unsigned)required_size, (unsigned)output_size);
        free(rgb888_buf);
        return ESP_ERR_INVALID_SIZE;
    }

    // Convert RGB888 → RGB565 big-endian (MSB first for SPI)
    const uint8_t* src = rgb888_buf;
    uint8_t* dst = rgb565_output;
    size_t pixel_count = (size_t)width * height;

    for (size_t i = 0; i < pixel_count; i++) {
        uint8_t r = src[0];
        uint8_t g = src[1];
        uint8_t b = src[2];
        src += 3;

        // RGB565: RRRRR GGGGGG BBBBB
        uint16_t pixel = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);

        // Big-endian byte order for SPI (MSB first)
        dst[0] = (uint8_t)(pixel >> 8);
        dst[1] = (uint8_t)(pixel & 0xFF);
        dst += 2;
    }

    free(rgb888_buf);

    *out_width = width;
    *out_height = height;

    ESP_LOGD(TAG, "RGB565 conversion complete: %dx%d (%u bytes)",
             width, height, (unsigned)required_size);

    return ESP_OK;
}
