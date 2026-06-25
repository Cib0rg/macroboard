/**
 * @file text_transfer.c
 * @brief Chunked data transfer for keyboard text and sequence blobs (CMD 0x38/0x39/0x3A)
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

#define TRANSFER_MAX_SIZE 4096

typedef struct {
    bool     active;
    uint8_t  profile_id;
    uint8_t  folder_id;
    uint8_t  button_id;
    uint8_t  action_slot;    // TEXT_ACTION_SHORT / TEXT_ACTION_LONG
    uint8_t  step_index;     // TEXT_STEP_DIRECT / TEXT_STEP_SEQ_BLOB / 0..15
    uint32_t total_size;
    uint32_t received_size;
    uint16_t expected_chunk;
    uint8_t* buffer;
} text_transfer_ctx_t;

static text_transfer_ctx_t ctx = {0};

esp_err_t text_transfer_start(uint8_t profile_id, uint8_t folder_id,
                               uint8_t button_id, uint8_t action_slot,
                               uint8_t step_index, uint32_t data_size) {
    if (ctx.active) {
        ESP_LOGW(TAG, "Previous transfer still active, cancelling");
        if (ctx.buffer) { free(ctx.buffer); ctx.buffer = NULL; }
        ctx.active = false;
    }

    if (data_size == 0 || data_size > TRANSFER_MAX_SIZE) {
        ESP_LOGE(TAG, "Rejected data size %lu (max %d)", data_size, TRANSFER_MAX_SIZE);
        return ESP_ERR_INVALID_ARG;
    }

    ctx.buffer = malloc(data_size);
    if (!ctx.buffer) {
        ESP_LOGE(TAG, "Failed to allocate %lu bytes", data_size);
        return ESP_ERR_NO_MEM;
    }

    ctx.active         = true;
    ctx.profile_id     = profile_id;
    ctx.folder_id      = folder_id;
    ctx.button_id      = button_id;
    ctx.action_slot    = action_slot;
    ctx.step_index     = step_index;
    ctx.total_size     = data_size;
    ctx.received_size  = 0;
    ctx.expected_chunk = 0;

    ESP_LOGI(TAG, "Start: profile=%d folder=0x%02X btn=%d action=%d step=0x%02X size=%lu",
             profile_id, folder_id, button_id, action_slot, step_index, data_size);
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
    if (size > remaining) size = (uint16_t)remaining;

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

    // Persist to SPIFFS
    ret = text_storage_save(ctx.profile_id, ctx.folder_id, ctx.button_id,
                            ctx.action_slot, ctx.step_index,
                            ctx.buffer, ctx.received_size);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "text_storage_save failed: %s", esp_err_to_name(ret));
        goto cleanup;
    }

    // Update in-memory profile so the SPIFFS flag survives without a full reload
    if (ctx.step_index == TEXT_STEP_SEQ_BLOB) {
        // Sequence blob: store [0x01] marker in action_data
        static const uint8_t seq_marker[1] = {0x01};
        if (ctx.action_slot == TEXT_ACTION_SHORT) {
            if (ctx.folder_id == 0xFF)
                ret = profile_set_button_action(ctx.button_id,
                                                ACTION_TYPE_SEQUENCE, seq_marker, 1);
            else
                ret = profile_set_folder_button_action(ctx.folder_id, ctx.button_id,
                                                       ACTION_TYPE_SEQUENCE, seq_marker, 1);
        } else {
            ret = profile_set_button_long_press_action(ctx.button_id, ctx.folder_id,
                                                       ACTION_TYPE_SEQUENCE, seq_marker, 1);
        }
    } else if (ctx.step_index == TEXT_STEP_DIRECT) {
        // Direct KEYBOARD action: store SPIFFS flag [0x00, 0x00, 0x01] in action_data
        static const uint8_t kb_marker[3] = {0x00, 0x00, 0x01};
        if (ctx.action_slot == TEXT_ACTION_SHORT) {
            if (ctx.folder_id == 0xFF)
                ret = profile_set_button_action(ctx.button_id,
                                                ACTION_TYPE_KEYBOARD, kb_marker, 3);
            else
                ret = profile_set_folder_button_action(ctx.folder_id, ctx.button_id,
                                                       ACTION_TYPE_KEYBOARD, kb_marker, 3);
        } else {
            ret = profile_set_button_long_press_action(ctx.button_id, ctx.folder_id,
                                                       ACTION_TYPE_KEYBOARD, kb_marker, 3);
        }
    }
    // step_index 0..15: KEYBOARD text for a sequence step — sequence blob already
    // has the SPIFFS marker baked in, no profile update needed here.

    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "profile update failed: %s", esp_err_to_name(ret));
        goto cleanup;
    }

    // Do NOT call profile_save_to_storage() here: images are saved asynchronously
    // via save_task, so the profile's image_size fields may still be stale (zero).
    // CMD_SAVE_PROFILE (0x50) calls save_task_drain() before profile_save_to_storage(),
    // ensuring a consistent snapshot.  The in-memory marker set above survives until then.
    ESP_LOGI(TAG, "Transfer complete: %lu bytes (btn=%d action=%d step=0x%02X)",
             ctx.received_size, ctx.button_id, ctx.action_slot, ctx.step_index);

cleanup:
    free(ctx.buffer);
    ctx.buffer = NULL;
    ctx.active = false;
    return ret;
}
