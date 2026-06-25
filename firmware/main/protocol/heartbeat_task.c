/**
 * @file heartbeat_task.c
 * @brief Periodic liveness heartbeat sent to the host.
 */

#include "heartbeat_task.h"
#include "protocol_handler.h"
#include "protocol_types.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char* TAG = "HEARTBEAT";

void heartbeat_task(void* arg) {
    (void)arg;
    ESP_LOGI(TAG, "Heartbeat task started (every 2 s)");

    while (1) {
        vTaskDelay(pdMS_TO_TICKS(2000));

        // Send heartbeat directly (bypasses protocol_cmd_queue so it works even
        // if protocol_task is blocked on a long SPIFFS operation).
        esp_err_t ret = protocol_send_event(EVENT_HEARTBEAT, NULL, 0);
        if (ret != ESP_OK) {
            // Normal when host is not connected; silently ignore.
            ESP_LOGV(TAG, "Heartbeat not sent (err=0x%x)", ret);
        }
    }
}
