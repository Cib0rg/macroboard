/**
 * @file text_transfer.c
 * @brief Chunked long keyboard-text transfer implementation
 */

#include "common.h"
#include "text_transfer.h"
#include "storage/text_storage.h"
#include "profile/profile_manager.h"
#include "profile/profile_types.h"
#include "config.h"

#include <stdlib.h>
#include <string.h>

static const char* TAG = "TXT_XFER";

#define TEXT_MAX_SIZE 4096

typedef struct {
    bool     active;
    uint8_t  profile_id;
    uint8_t  folder_id;
    uint8_t  button_id;
    uint8_t  storage_bid;   // synthetic key: root→bid, folder→NUM_BUTTONS+fid*NUM_BUTTONS+bid
    uint32_t total_size;
    uint32_t received_size;
    uint16_t expected_chunk;
    char*    buffer;
} text_transfer_ctx_t;

static text_transfer_ctx_t ctx = {0};

esp_err_t text_transfer_start(uint8_t profile_id, uint8_t folder_id,
                               uint8_t button_id, uint32_t text_size) {
    if (ctx.active) {
        ESP_LOGW(TAG, "Previous transfer still active, cancelling");
        if (ctx.buffer) { free(ctx.buffer); ctx.buffer = NULL; }
        ctx.active = false;
    }

    if (text_size == 0 || text_size > TEXT_MAX_SIZE) {
        ESP_LOGE(TAG, "Rejected text size %lu (max %d)", text_size, TEXT_MAX_SIZE);
        return ESP_ERR_INVALID_ARG;
    }

    uint8_t storage_bid = (folder_id == 0xFF)
        ? button_id
        : (uint8_t)(NUM_BUTTONS + folder_id * NUM_BUTTONS + button_id);

    ctx.buffer = malloc(text_size + 1);
    if (!ctx.buffer) {
        ESP_LOGE(TAG, "Failed to allocate %lu bytes", text_size);
        return ESP_ERR_NO_MEM;
    }

    ctx.active         = true;
    ctx.profile_id     = profile_id;
    ctx.folder_id      = folder_id;
    ctx.button_id      = button_id;
    ctx.storage_bid    = storage_bid;
    ctx.total_size     = text_size;
    ctx.received_size  = 0;
    ctx.expected_chunk = 0;

    ESP_LOGI(TAG, "Start: profile=%d folder=0x%02X button=%d size=%lu storage_bid=%d",
             profile_id, folder_id, button_id, text_size, storage_bid);
    return ESP_OK;
}

esp_err_t text_transfer_chunk(const uint8_t* data, uint16_t size, uint16_t chunk_num) {
    if (!ctx.active) {
        ESP_LOGE(TAG, "Chunk received but no active transfer");
        return ESP_ERR_INVALID_STATE;
    }
    if (chunk_num != ctx.expected_chunk) {
        ESP_LOGW(TAG, "Out-of-order chunk %d (expected %d)", chunk_num, ctx.expected_chunk);
        return ESP_ERR_INVALID_ARG;
    }

    uint32_t remaining = ctx.total_size - ctx.received_size;
    if (size > remaining) {
        size = (uint16_t)remaining;
    }

    memcpy(ctx.buffer + ctx.received_size, data, size);
    ctx.received_size += size;
    ctx.expected_chunk++;

    return ESP_OK;
}

esp_err_t text_transfer_end(void) {
    if (!ctx.active) {
        ESP_LOGE(TAG, "End received but no active transfer");
        return ESP_ERR_INVALID_STATE;
    }

    esp_err_t ret = ESP_OK;

    // Persist raw bytes to SPIFFS
    ret = text_storage_save(ctx.profile_id, ctx.storage_bid,
                            ctx.buffer, ctx.received_size);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "text_storage_save failed: %s", esp_err_to_name(ret));
        goto cleanup;
    }

    // Update in-memory profile: KEYBOARD action with SPIFFS flag in data[2]
    {
        static const uint8_t spiffs_data[3] = {0x00, 0x00, 0x01};
        if (ctx.folder_id == 0xFF) {
            ret = profile_set_button_action(ctx.profile_id, ctx.button_id,
                                            ACTION_TYPE_KEYBOARD, spiffs_data, 3);
        } else {
            ret = profile_set_folder_button_action(ctx.profile_id, ctx.folder_id,
                                                   ctx.button_id, ACTION_TYPE_KEYBOARD,
                                                   spiffs_data, 3);
        }
        if (ret != ESP_OK) {
            ESP_LOGE(TAG, "profile_set_button_action failed: %s", esp_err_to_name(ret));
            goto cleanup;
        }
    }

    // Flush profile to flash so the SPIFFS flag survives a reboot
    profile_save_to_storage(ctx.profile_id);

    ESP_LOGI(TAG, "Text transfer complete: %lu bytes for storage_bid=%d",
             ctx.received_size, ctx.storage_bid);

cleanup:
    free(ctx.buffer);
    ctx.buffer = NULL;
    ctx.active = false;
    return ret;
}
