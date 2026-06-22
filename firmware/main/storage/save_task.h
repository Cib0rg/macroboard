/**
 * @file save_task.h
 * @brief Async SPIFFS image save with sync fallback when queue is near capacity.
 *
 * Keeps image_storage_save() off the protocol_task critical path so CMD 0x22
 * (IMAGE_TRANSFER_END) can respond to the host as soon as JPEG decode completes
 * (~50ms) instead of waiting for SPIFFS write (~100ms–2.5s).
 *
 * Safety guarantee: save_task_drain() blocks until all queued writes have
 * completed.  Call it inside handle_save_profile() before profile_save_to_storage()
 * so the profile is never persisted with stale image_size fields.
 *
 * Capacity limit: SAVE_QUEUE_DEPTH = 20.  When fewer than SAVE_QUEUE_SYNC_BELOW
 * slots remain free the code falls back to a synchronous (blocking) write,
 * ensuring images are never silently dropped regardless of SPIFFS GC timing.
 */

#pragma once
#include "esp_err.h"
#include <stdint.h>

/**
 * Initialize queue and synchronization primitives.
 * Must be called once before save_task_fn is created.
 */
esp_err_t save_task_init(void);

/**
 * FreeRTOS task entry point.  Pin to Core 0 (same core as all other SPIFFS callers).
 */
void save_task_fn(void* arg);

/**
 * Block until all pending async saves have completed.
 * Call from protocol_task before handle_save_profile() persists the profile.
 */
void save_task_drain(void);

/**
 * Save an RGB565 image to SPIFFS.
 *
 * Async path (queue has room): copies rgb565_buf internally and returns immediately.
 * Sync path  (queue near full): writes directly from rgb565_buf, blocks until done.
 *
 * In both paths the caller retains ownership of rgb565_buf — it is never consumed.
 * image_size in the in-memory profile is updated only on a successful write.
 *
 * @param profile_id  Profile slot (0).
 * @param folder_id   0xFF for root buttons; folder index for folder buttons.
 * @param button_id   Physical button index (0-9).
 * @param storage_bid Synthetic storage key (button_id for root, offset for folders).
 * @param rgb565_buf  Decoded RGB565 image (DISPLAY_BUFFER_SIZE bytes, PSRAM).
 * @param crc32       CRC32 of the original JPEG (content-address key).
 * @return ESP_OK always (async path cannot report write errors; check logs).
 */
esp_err_t save_task_save_image(uint8_t profile_id,
                               uint8_t folder_id,
                               uint8_t button_id,
                               uint8_t storage_bid,
                               const uint8_t* rgb565_buf,
                               uint32_t crc32);
