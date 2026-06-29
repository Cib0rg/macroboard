/**
 * @file display_task.c
 * @brief Dedicated FreeRTOS task for SPI display writes (Core 1).
 *
 * Keeps gc9a01_draw_image off the protocol_task critical path (Core 0).
 * Each draw command carries an already-decoded RGB565 buffer; the task owns
 * the buffer and frees it after the SPI write completes.
 */

#include "display_task.h"
#include "gc9a01.h"
#include "config.h"
#include "common.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "esp_log.h"

#define DISPLAY_QUEUE_DEPTH  16

static const char* TAG = "DISP_TASK";

typedef struct {
    uint8_t  button_id;
    uint8_t* rgb565_buf;
} display_cmd_t;

static QueueHandle_t display_queue = NULL;

esp_err_t display_task_init(void) {
    display_queue = xQueueCreate(DISPLAY_QUEUE_DEPTH, sizeof(display_cmd_t));
    if (display_queue == NULL) {
        ESP_LOGE(TAG, "Failed to create display queue");
        return ESP_ERR_NO_MEM;
    }
    return ESP_OK;
}

esp_err_t display_post_draw(uint8_t button_id, uint8_t* rgb565_buf) {
    if (display_queue == NULL || rgb565_buf == NULL) return ESP_ERR_INVALID_ARG;

    display_cmd_t cmd = { .button_id = button_id, .rgb565_buf = rgb565_buf };

    if (xQueueSend(display_queue, &cmd, 0) != pdTRUE) {
        ESP_LOGW(TAG, "Queue full — dropping draw for button %d", button_id);
        free(rgb565_buf);  // caller transferred ownership; must free if we can't queue
        return ESP_FAIL;
    }
    return ESP_OK;
}

void display_task(void* arg) {
    (void)arg;
    ESP_LOGI(TAG, "Display task started (Core %d)", xPortGetCoreID());

    display_cmd_t cmd;
    while (1) {
        if (xQueueReceive(display_queue, &cmd, portMAX_DELAY) == pdTRUE) {
            // Log ring pixel before draw so we can verify buffer content independently
            // of gc9a01_draw_image (row 80, x=2 is on the ring for 160×160 images).
            const size_t ring_off = (size_t)80 * DISPLAY_WIDTH * 2 + 2 * 2;
            ESP_LOGI(TAG, "draw button=%u ring_px=0x%02X%02X",
                     cmd.button_id,
                     cmd.rgb565_buf[ring_off], cmd.rgb565_buf[ring_off + 1]);
            esp_err_t ret = gc9a01_draw_image(cmd.button_id, cmd.rgb565_buf,
                                              DISPLAY_WIDTH, DISPLAY_HEIGHT);
            if (ret != ESP_OK) {
                ESP_LOGW(TAG, "draw_image failed button=%u: %s",
                         cmd.button_id, esp_err_to_name(ret));
            }
            free(cmd.rgb565_buf);
        }
    }
}
