/**
 * @file image_transfer.c
 * @brief Image transfer management implementation
 */

#include "common.h"
#include "image_transfer.h"
#include "storage/image_storage.h"
#include "storage/save_task.h"
#include "hardware/gc9a01.h"
#include "hardware/display_task.h"
#include "utils/crc.h"
#include "utils/jpeg_decode_util.h"
#include "profile/profile_manager.h"
#include "config.h"

static const char* TAG = "IMG_XFER";

typedef struct {
    bool active;
    uint8_t profile_id;
    uint8_t folder_id;     // 0xFF = root button; 0..N = folder button
    uint8_t button_id;     // physical button position
    uint8_t storage_bid;   // synthetic button_id used as storage key
    uint32_t total_size;
    uint32_t received_size;
    uint8_t format;
    uint16_t expected_chunk;
    uint8_t* buffer;
} image_transfer_ctx_t;

static image_transfer_ctx_t transfer_ctx = {0};

esp_err_t image_transfer_start(uint8_t profile_id, uint8_t folder_id,
                                uint8_t button_id, uint32_t image_size,
                                uint8_t format) {
    if (transfer_ctx.active) {
        ESP_LOGW(TAG, "Transfer already in progress, cancelling previous");
        if (transfer_ctx.buffer != NULL) {
            free(transfer_ctx.buffer);
            transfer_ctx.buffer = NULL;
        }
        transfer_ctx.active = false;
    }

    if (button_id >= NUM_BUTTONS) {
        return ESP_ERR_INVALID_ARG;
    }
    if (folder_id != 0xFF && folder_id >= NUM_FOLDERS) {
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
    
    // Compute the synthetic storage key: root buttons keep their id;
    // folder buttons are offset by NUM_BUTTONS + folder_id * NUM_BUTTONS.
    uint8_t storage_bid = (folder_id == 0xFF)
        ? button_id
        : (uint8_t)(NUM_BUTTONS + folder_id * NUM_BUTTONS + button_id);

    transfer_ctx.active = true;
    transfer_ctx.profile_id = profile_id;
    transfer_ctx.folder_id = folder_id;
    transfer_ctx.button_id = button_id;
    transfer_ctx.storage_bid = storage_bid;
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
    *calculated_crc = 0;  // initialise so the caller can safely use it on any return path

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

    // CRC over JPEG bytes — transport integrity check and content-address key
    *calculated_crc = crc32_calculate(transfer_ctx.buffer, transfer_ctx.total_size);

    ESP_LOGI(TAG, "Transfer complete: %lu bytes JPEG, CRC32=0x%08lX",
             transfer_ctx.total_size, *calculated_crc);

    // Decode JPEG → raw RGB565 big-endian (one-time cost at upload, ~20-50ms)
    uint8_t* rgb565_buf = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_SPIRAM);
    if (rgb565_buf == NULL) {
        ESP_LOGE(TAG, "Failed to allocate RGB565 decode buffer");
        free(transfer_ctx.buffer);
        transfer_ctx.active = false;
        return ESP_ERR_NO_MEM;
    }

    uint16_t decoded_w = 0, decoded_h = 0;
    esp_err_t decode_ret = jpeg_decode_to_rgb565(
        transfer_ctx.buffer, transfer_ctx.total_size,
        rgb565_buf, DISPLAY_BUFFER_SIZE, &decoded_w, &decoded_h);

    free(transfer_ctx.buffer);
    transfer_ctx.buffer = NULL;

    if (decode_ret != ESP_OK) {
        ESP_LOGE(TAG, "JPEG decode failed: %s", esp_err_to_name(decode_ret));
        free(rgb565_buf);
        transfer_ctx.active = false;
        return decode_ret;
    }

    ESP_LOGI(TAG, "JPEG decoded to %dx%d", decoded_w, decoded_h);

    // Queue the SPIFFS write asynchronously so CMD 0x22 can respond to the host
    // as soon as decode finishes (~50ms) rather than waiting for SPIFFS (~2.5s).
    // Falls back to a blocking write if the queue is near capacity (see save_task.h).
    // save_task updates image_size in the profile on success; save_task_drain() in
    // handle_save_profile() ensures all writes complete before the profile is persisted.
    save_task_save_image(transfer_ctx.profile_id, transfer_ctx.folder_id,
                         transfer_ctx.button_id, transfer_ctx.storage_bid,
                         rgb565_buf, *calculated_crc);

    // Always draw decoded image to display (CRC verified, decode succeeded).
    // A SPIFFS save failure must NOT prevent the screen from showing the new image.
    // Buffer ownership is transferred to the display_task (Core 1); it frees it
    // after the SPI write.  If draw is not required we free it here instead.
    bool draw_posted = false;
    {
        bool should_draw = false;
        if (transfer_ctx.folder_id == 0xFF) {
            profile_image_cache_invalidate(transfer_ctx.button_id, false);
            // Only draw if the device is currently at root level.  When inside a
            // folder, root-button images must not overwrite the folder-button displays
            // at the same physical positions.  The cache is already invalidated above,
            // so the image will be drawn correctly when the user exits the folder.
            should_draw = (profile_get_current_folder() == 0xFF);
        } else {
            profile_image_cache_invalidate(transfer_ctx.button_id, true);
            if (profile_get_current_folder() == transfer_ctx.folder_id) {
                should_draw = true;
            }
        }
        if (should_draw) {
            display_post_draw(transfer_ctx.button_id, rgb565_buf);
            rgb565_buf = NULL;  // ownership always transfers: task frees on success, display_post_draw frees on queue-full
            draw_posted = true;
            ESP_LOGI(TAG, "Draw queued for button %d (folder=%d)",
                     transfer_ctx.button_id, transfer_ctx.folder_id);
        }
    }
    if (!draw_posted) {
        free(rgb565_buf);  // only reached when should_draw was false; rgb565_buf is still valid here
    }
    transfer_ctx.active = false;

    // Return OK regardless of SPIFFS outcome: the image was decoded and displayed.
    // The protocol handler will send STATUS_OK + the correct CRC to the host.
    return ESP_OK;
}
