/**
 * @file save_task.c
 * @brief Async SPIFFS image save task with sync fallback near capacity.
 */

#include "save_task.h"
#include "image_storage.h"
#include "profile/profile_manager.h"
#include "config.h"
#include "esp_log.h"
#include "esp_heap_caps.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "freertos/semphr.h"
#include <string.h>

static const char* TAG = "SAVE_TASK";

// Hard limit: 20 async saves in flight simultaneously.
// At 50 KB per buffer that is 1 MB of PSRAM — well within the 8 MB available.
#define SAVE_QUEUE_DEPTH     20

// When fewer than this many slots are free, fall back to synchronous writes.
#define SAVE_QUEUE_SYNC_BELOW  3

typedef struct {
    uint8_t  profile_id;
    uint8_t  folder_id;    // 0xFF = root
    uint8_t  button_id;
    uint8_t  storage_bid;
    uint8_t* rgb565_buf;   // heap_caps_malloc'd copy; task frees after write
    uint32_t crc32;
} save_cmd_t;

static QueueHandle_t save_queue = NULL;

// pending_count tracks items that have been queued but not yet fully written
// AND whose image_size has not yet been updated.  It is incremented BEFORE an
// item enters the queue and decremented only AFTER the write + update completes.
// This closes the race in save_task_drain: the queue can appear empty while the
// task is still writing the last item; pending_count stays > 0 until that write
// finishes, so drain correctly waits for it.
static portMUX_TYPE         s_mux     = portMUX_INITIALIZER_UNLOCKED;
static volatile int         s_pending = 0;

// ── Internal helpers ──────────────────────────────────────────────────────────

static void update_image_size(uint8_t profile_id, uint8_t folder_id, uint8_t button_id)
{
    (void)profile_id;
    profile_t* prof = profile_get();

    if (folder_id == 0xFF) {
        prof->buttons[button_id].image_size = DISPLAY_BUFFER_SIZE;
    } else if (folder_id < NUM_FOLDERS) {
        prof->folders[folder_id].buttons[button_id].image_size = DISPLAY_BUFFER_SIZE;
    }
}

static inline void pending_inc(void)
{
    portENTER_CRITICAL(&s_mux);
    s_pending++;
    portEXIT_CRITICAL(&s_mux);
}

static inline void pending_dec(void)
{
    portENTER_CRITICAL(&s_mux);
    s_pending--;
    portEXIT_CRITICAL(&s_mux);
}

static inline int pending_get(void)
{
    portENTER_CRITICAL(&s_mux);
    int v = s_pending;
    portEXIT_CRITICAL(&s_mux);
    return v;
}

// ── Public API ────────────────────────────────────────────────────────────────

esp_err_t save_task_init(void)
{
    save_queue = xQueueCreate(SAVE_QUEUE_DEPTH, sizeof(save_cmd_t));
    if (save_queue == NULL) {
        ESP_LOGE(TAG, "Failed to create save queue");
        return ESP_ERR_NO_MEM;
    }

    ESP_LOGI(TAG, "Save task init: queue depth %d, sync threshold %d free slots",
             SAVE_QUEUE_DEPTH, SAVE_QUEUE_SYNC_BELOW);
    return ESP_OK;
}

esp_err_t save_task_save_image(uint8_t profile_id,
                               uint8_t folder_id,
                               uint8_t button_id,
                               uint8_t storage_bid,
                               const uint8_t* rgb565_buf,
                               uint32_t crc32)
{
    UBaseType_t free_slots = uxQueueSpacesAvailable(save_queue);

    if (free_slots >= SAVE_QUEUE_SYNC_BELOW) {
        uint8_t* buf_copy = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_SPIRAM);
        if (buf_copy != NULL) {
            memcpy(buf_copy, rgb565_buf, DISPLAY_BUFFER_SIZE);

            save_cmd_t cmd = {
                .profile_id  = profile_id,
                .folder_id   = folder_id,
                .button_id   = button_id,
                .storage_bid = storage_bid,
                .rgb565_buf  = buf_copy,
                .crc32       = crc32,
            };

            // Increment BEFORE sending to the queue so that save_task_drain
            // never sees a misleadingly-low count between the send and the task
            // picking the item up.
            pending_inc();

            if (xQueueSend(save_queue, &cmd, 0) == pdTRUE) {
                ESP_LOGD(TAG, "Queued async save: storage_bid=%d (pending=%d, free=%lu)",
                         storage_bid, pending_get(),
                         (unsigned long)(free_slots - 1));
                return ESP_OK;
            }

            // Race: queue filled between the check and the send.
            pending_dec();
            free(buf_copy);
            ESP_LOGW(TAG, "Queue race on storage_bid=%d — falling back to sync", storage_bid);
        } else {
            ESP_LOGW(TAG, "PSRAM alloc failed for async save of storage_bid=%d — falling back to sync",
                     storage_bid);
        }
    } else {
        ESP_LOGW(TAG, "Queue near full (%lu free) — sync write for storage_bid=%d",
                 (unsigned long)free_slots, storage_bid);
    }

    // Sync fallback: block here until SPIFFS write completes.
    esp_err_t ret = image_storage_save(profile_id, storage_bid,
                                       rgb565_buf, DISPLAY_BUFFER_SIZE, crc32);
    if (ret == ESP_OK) {
        update_image_size(profile_id, folder_id, button_id);
    } else {
        ESP_LOGE(TAG, "Sync save failed for storage_bid=%d: %s",
                 storage_bid, esp_err_to_name(ret));
    }
    return ret;
}

void save_task_drain(void)
{
    // Poll until every queued save has been fully written and image_size updated.
    // s_pending is incremented before the item enters the queue and decremented
    // only after write + update, so reaching 0 here guarantees completeness.
    while (pending_get() > 0) {
        vTaskDelay(pdMS_TO_TICKS(20));
    }
    ESP_LOGI(TAG, "Drain complete — all SPIFFS writes finished");
}

// ── FreeRTOS task ─────────────────────────────────────────────────────────────

void save_task_fn(void* arg)
{
    (void)arg;
    ESP_LOGI(TAG, "Save task started (Core %d)", xPortGetCoreID());

    save_cmd_t cmd;
    while (1) {
        if (xQueueReceive(save_queue, &cmd, portMAX_DELAY) == pdTRUE) {
            esp_err_t ret = image_storage_save(cmd.profile_id, cmd.storage_bid,
                                               cmd.rgb565_buf, DISPLAY_BUFFER_SIZE,
                                               cmd.crc32);

            // SPIFFS write can fail transiently (GC, fragmentation, stale blob from
            // a previous incomplete session).  The first attempt already calls unlink()
            // on the failed blob, so the retry writes to a clean slate.
            if (ret != ESP_OK) {
                ESP_LOGW(TAG, "SPIFFS write failed for storage_bid=%d — retrying in 200 ms",
                         cmd.storage_bid);
                vTaskDelay(pdMS_TO_TICKS(200));
                ret = image_storage_save(cmd.profile_id, cmd.storage_bid,
                                         cmd.rgb565_buf, DISPLAY_BUFFER_SIZE,
                                         cmd.crc32);
            }

            if (ret == ESP_OK) {
                update_image_size(cmd.profile_id, cmd.folder_id, cmd.button_id);
                ESP_LOGD(TAG, "Saved storage_bid=%d (queue remaining: %lu)",
                         cmd.storage_bid,
                         (unsigned long)uxQueueMessagesWaiting(save_queue));
            } else {
                ESP_LOGE(TAG, "Async save PERMANENTLY failed for storage_bid=%d: %s",
                         cmd.storage_bid, esp_err_to_name(ret));
            }

            free(cmd.rgb565_buf);

            // Decrement only after write + update (and free) are fully complete.
            pending_dec();
        }
    }
}
