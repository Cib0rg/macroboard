/**
 * @file nvs_manager.c
 * @brief NVS manager implementation
 */

#include "nvs_manager.h"
#include "nvs_flash.h"
#include "nvs.h"
#include "esp_log.h"
#include <string.h>

static const char* TAG = "NVS";
static const char* NVS_NAMESPACE = "config";

esp_err_t nvs_manager_init(void) {
    ESP_LOGI(TAG, "Initializing NVS");
    
    esp_err_t ret = nvs_flash_init();
    if (ret == ESP_ERR_NVS_NO_FREE_PAGES || ret == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_LOGW(TAG, "NVS partition was truncated, erasing...");
        ESP_ERROR_CHECK(nvs_flash_erase());
        ret = nvs_flash_init();
    }
    
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to initialize NVS: %s", esp_err_to_name(ret));
        return ret;
    }
    
    ESP_LOGI(TAG, "NVS initialized");
    return ESP_OK;
}

esp_err_t nvs_get_current_profile(uint8_t* profile_id) {
    nvs_handle_t nvs;
    esp_err_t ret = nvs_open(NVS_NAMESPACE, NVS_READONLY, &nvs);
    if (ret != ESP_OK) {
        *profile_id = 0; // Default
        return ret;
    }
    
    ret = nvs_get_u8(nvs, "curr_profile", profile_id);
    if (ret == ESP_ERR_NVS_NOT_FOUND) {
        *profile_id = 0; // Default
        ret = ESP_OK;
    }
    
    nvs_close(nvs);
    return ret;
}

esp_err_t nvs_set_current_profile(uint8_t profile_id) {
    nvs_handle_t nvs;
    esp_err_t ret = nvs_open(NVS_NAMESPACE, NVS_READWRITE, &nvs);
    if (ret != ESP_OK) {
        return ret;
    }
    
    ret = nvs_set_u8(nvs, "curr_profile", profile_id);
    if (ret == ESP_OK) {
        ret = nvs_commit(nvs);
    }
    
    nvs_close(nvs);
    return ret;
}

esp_err_t nvs_get_wifi_credentials(char* ssid, char* password) {
    nvs_handle_t nvs;
    esp_err_t ret = nvs_open("wifi", NVS_READONLY, &nvs);
    if (ret != ESP_OK) {
        return ret;
    }
    
    size_t len = 32;
    ret = nvs_get_str(nvs, "ssid", ssid, &len);
    if (ret != ESP_OK) {
        nvs_close(nvs);
        return ret;
    }
    
    len = 64;
    ret = nvs_get_str(nvs, "password", password, &len);
    
    nvs_close(nvs);
    return ret;
}

esp_err_t nvs_set_wifi_credentials(const char* ssid, const char* password) {
    nvs_handle_t nvs;
    esp_err_t ret = nvs_open("wifi", NVS_READWRITE, &nvs);
    if (ret != ESP_OK) {
        return ret;
    }
    
    ret = nvs_set_str(nvs, "ssid", ssid);
    if (ret == ESP_OK) {
        ret = nvs_set_str(nvs, "password", password);
    }
    
    if (ret == ESP_OK) {
        ret = nvs_commit(nvs);
    }
    
    nvs_close(nvs);
    return ret;
}
