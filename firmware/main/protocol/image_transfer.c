/**
 * @file image_transfer.c
 * @brief Image transfer management implementation
 */

#include "common.h"
#include "image_transfer.h"
#include "storage/image_storage.h"
#include "hardware/gc9a01.h"
#include "utils/crc.h"
#include "utils/jpeg_decode_util.h"
#include "profile/profile_manager.h"
#include "config.h"

static const char* TAG = "IMG_XFER";

typedef struct {
    bool active;
    uint8_t profile_id;
    uint8_t button_id;
    uint32_t total_size;
    uint32_t received_size;
    uint8_t format;
    uint16_t expected_chunk;
    uint8_t* buffer;
} image_transfer_ctx_t;

static image_transfer_ctx_t transfer_ctx = {0};

esp_err_t image_transfer_start(uint8_t profile_id, uint8_t button_id,
                                uint32_t image_size, uint8_t format) {
    if (transfer_ctx.active) {
        ESP_LOGW(TAG, "Transfer already in progress, cancelling previous");
        // Cancel previous transfer and free buffer
        if (transfer_ctx.buffer != NULL) {
            free(transfer_ctx.buffer);
            transfer_ctx.buffer = NULL;
        }
        transfer_ctx.active = false;
    }
    
    if (profile_id >= NUM_PROFILES || button_id >= NUM_BUTTONS) {
        return ESP_ERR_INVALID_ARG;
    }
    
    ESP_LOGI(TAG, "Starting image transfer: profile=%d, button=%d, size=%lu",
             profile_id, button_id, image_size);
    
    // Allocate buffer in PSRAM
    transfer_ctx.buffer = heap_caps_malloc(image_size, MALLOC_CAP_SPIRAM);
    if (transfer_ctx.buffer == NULL) {
        ESP_LOGE(TAG, "Failed to allocate buffer for image");
        return ESP_ERR_NO_MEM;
    }
    
    transfer_ctx.active = true;
    transfer_ctx.profile_id = profile_id;
    transfer_ctx.button_id = button_id;
    transfer_ctx.total_size = image_size;
    transfer_ctx.received_size = 0;
    transfer_ctx.format = format;
    transfer_ctx.expected_chunk = 0;
    
    return ESP_OK;
}

esp_err_t image_transfer_chunk(const uint8_t* data, uint16_t size, uint16_t chunk_num) {
    if (!transfer_ctx.active) {
        ESP_LOGW(TAG, "No active transfer");
        return ESP_ERR_INVALID_STATE;
    }
    
    if (chunk_num != transfer_ctx.expected_chunk) {
        ESP_LOGW(TAG, "Unexpected chunk: got %d, expected %d", 
                 chunk_num, transfer_ctx.expected_chunk);
        return ESP_ERR_INVALID_ARG;
    }
    
    if (transfer_ctx.received_size + size > transfer_ctx.total_size) {
        ESP_LOGE(TAG, "Chunk would exceed total size");
        return ESP_ERR_INVALID_SIZE;
    }
    
    // Copy chunk to buffer
    memcpy(transfer_ctx.buffer + transfer_ctx.received_size, data, size);
    transfer_ctx.received_size += size;
    transfer_ctx.expected_chunk++;
    
    ESP_LOGD(TAG, "Received chunk %d: %d bytes (%lu/%lu total)",
             chunk_num, size, transfer_ctx.received_size, transfer_ctx.total_size);
    
    return ESP_OK;
}

esp_err_t image_transfer_end(uint32_t* calculated_crc) {
    if (!transfer_ctx.active) {
        return ESP_ERR_INVALID_STATE;
    }
    
    if (transfer_ctx.received_size != transfer_ctx.total_size) {
        ESP_LOGE(TAG, "Incomplete transfer: %lu/%lu bytes",
                 transfer_ctx.received_size, transfer_ctx.total_size);
        free(transfer_ctx.buffer);
        transfer_ctx.active = false;
        return ESP_ERR_INVALID_SIZE;
    }
    
    // Calculate CRC32
    *calculated_crc = crc32_calculate(transfer_ctx.buffer, transfer_ctx.total_size);
    
    ESP_LOGI(TAG, "Transfer complete: %lu bytes, CRC32=0x%08lX",
             transfer_ctx.total_size, *calculated_crc);
    
    // Save image to storage (content-addressed with deduplication)
    esp_err_t ret = image_storage_save(transfer_ctx.profile_id, transfer_ctx.button_id,
                                        transfer_ctx.buffer, transfer_ctx.total_size,
                                        *calculated_crc);
    
    // If save succeeded and this is the current profile, decode and display immediately
    if (ret == ESP_OK && transfer_ctx.profile_id == profile_get_current_id()) {
        uint8_t* rgb565_buf = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_DMA | MALLOC_CAP_SPIRAM);
        if (rgb565_buf != NULL) {
            uint16_t w, h;
            if (jpeg_decode_to_rgb565(transfer_ctx.buffer, transfer_ctx.total_size,
                                       rgb565_buf, DISPLAY_BUFFER_SIZE, &w, &h) == ESP_OK) {
                gc9a01_draw_image(transfer_ctx.button_id, rgb565_buf, w, h);
                ESP_LOGI(TAG, "Image displayed on button %d (%dx%d)", transfer_ctx.button_id, w, h);
            } else {
                ESP_LOGW(TAG, "JPEG decode failed for button %d after transfer", transfer_ctx.button_id);
            }
            free(rgb565_buf);
        }
    }
    
    // Cleanup
    free(transfer_ctx.buffer);
    transfer_ctx.active = false;
    
    return ret;
}
