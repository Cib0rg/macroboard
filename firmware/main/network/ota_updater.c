/**
 * @file ota_updater.c
 * @brief OTA firmware updater implementation using ESP-IDF API
 */

#include "ota_updater.h"
#include "wifi_manager.h"
#include "esp_https_ota.h"
#include "esp_log.h"

static const char* TAG = "OTA";
static uint8_t ota_progress = 0;

esp_err_t ota_start_update(const char* url) {
    if (url == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (!wifi_is_connected()) {
        ESP_LOGE(TAG, "WiFi not connected");
        return ESP_ERR_INVALID_STATE;
    }
    
    ESP_LOGI(TAG, "Starting OTA update from: %s", url);
    
    esp_http_client_config_t config = {
        .url = url,
        .timeout_ms = 5000,
        .keep_alive_enable = true,
    };
    
    esp_https_ota_config_t ota_config = {
        .http_config = &config,
    };
    
    esp_https_ota_handle_t ota_handle = NULL;
    esp_err_t ret = esp_https_ota_begin(&ota_config, &ota_handle);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "OTA begin failed: %s", esp_err_to_name(ret));
        return ret;
    }
    
    ota_progress = 0;
    
    while (1) {
        ret = esp_https_ota_perform(ota_handle);
        if (ret != ESP_ERR_HTTPS_OTA_IN_PROGRESS) {
            break;
        }
        
        // Update progress
        int total = esp_https_ota_get_image_size(ota_handle);
        int downloaded = esp_https_ota_get_image_len_read(ota_handle);
        if (total > 0) {
            ota_progress = (downloaded * 100) / total;
            ESP_LOGI(TAG, "OTA progress: %d%%", ota_progress);
        }
    }
    
    if (ret == ESP_OK) {
        ret = esp_https_ota_finish(ota_handle);
        if (ret == ESP_OK) {
            ESP_LOGI(TAG, "OTA successful, rebooting...");
            vTaskDelay(pdMS_TO_TICKS(1000));
            esp_restart();
        }
    } else {
        ESP_LOGE(TAG, "OTA failed: %s", esp_err_to_name(ret));
        esp_https_ota_abort(ota_handle);
    }
    
    return ret;
}

uint8_t ota_get_progress(void) {
    return ota_progress;
}
