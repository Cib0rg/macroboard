/**
 * @file profile_storage.c
 * @brief Profile storage implementation
 */

#include "common.h"
#include "profile_storage.h"
#include "config.h"
#include "utils/crc.h"
#include "esp_spiffs.h"
#include <sys/stat.h>
#include <unistd.h>

static const char* TAG = "PROF_STOR";

esp_err_t profile_storage_init(void) {
    ESP_LOGI(TAG, "Initializing profile storage");
    
    esp_vfs_spiffs_conf_t conf = {
        .base_path = STORAGE_BASE_PATH,
        .partition_label = "storage",
        .max_files = 10,
        .format_if_mount_failed = true
    };
    
    esp_err_t ret = esp_vfs_spiffs_register(&conf);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to initialize SPIFFS: %s", esp_err_to_name(ret));
        return ret;
    }
    
    size_t total = 0, used = 0;
    ret = esp_spiffs_info("storage", &total, &used);
    if (ret == ESP_OK) {
        ESP_LOGI(TAG, "SPIFFS: Total=%dKB, Used=%dKB, Free=%dKB",
                 total/1024, used/1024, (total-used)/1024);
    }
    
    return ESP_OK;
}

esp_err_t profile_storage_save(uint8_t profile_id, const profile_t* profile) {
    if (profile_id >= NUM_PROFILES || profile == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    
    char path[64];
    snprintf(path, sizeof(path), PROFILE_FILE_FMT, profile_id);
    
    FILE* f = fopen(path, "wb");
    if (f == NULL) {
        ESP_LOGE(TAG, "Failed to open file for writing: %s", path);
        return ESP_FAIL;
    }
    
    size_t written = fwrite(profile, 1, sizeof(profile_t), f);
    fflush(f);  // Flush to ensure data is written
    fclose(f);
    
    if (written != sizeof(profile_t)) {
        ESP_LOGE(TAG, "Failed to write profile");
        return ESP_FAIL;
    }
    
    ESP_LOGI(TAG, "Profile %d saved (%d bytes)", profile_id, written);
    return ESP_OK;
}

esp_err_t profile_storage_load(uint8_t profile_id, profile_t* profile) {
    if (profile_id >= NUM_PROFILES || profile == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    
    char path[64];
    snprintf(path, sizeof(path), PROFILE_FILE_FMT, profile_id);
    
    FILE* f = fopen(path, "rb");
    if (f == NULL) {
        ESP_LOGW(TAG, "Profile %d not found", profile_id);
        return ESP_ERR_NOT_FOUND;
    }
    
    size_t read = fread(profile, 1, sizeof(profile_t), f);
    fclose(f);
    
    if (read != sizeof(profile_t)) {
        ESP_LOGE(TAG, "Failed to read profile");
        return ESP_FAIL;
    }
    
    // Verify CRC
    uint32_t calculated_crc = crc32_calculate((uint8_t*)profile, 
                                               sizeof(profile_t) - sizeof(uint32_t));
    if (calculated_crc != profile->crc32) {
        ESP_LOGE(TAG, "Profile CRC mismatch");
        return ESP_ERR_INVALID_CRC;
    }
    
    ESP_LOGI(TAG, "Profile %d loaded", profile_id);
    return ESP_OK;
}

esp_err_t profile_storage_delete(uint8_t profile_id) {
    if (profile_id >= NUM_PROFILES) {
        return ESP_ERR_INVALID_ARG;
    }
    
    char path[64];
    snprintf(path, sizeof(path), PROFILE_FILE_FMT, profile_id);
    
    if (unlink(path) != 0) {
        ESP_LOGW(TAG, "Failed to delete profile %d", profile_id);
        return ESP_FAIL;
    }
    
    ESP_LOGI(TAG, "Profile %d deleted", profile_id);
    return ESP_OK;
}

bool profile_storage_exists(uint8_t profile_id) {
    if (profile_id >= NUM_PROFILES) {
        return false;
    }
    
    char path[64];
    snprintf(path, sizeof(path), PROFILE_FILE_FMT, profile_id);
    
    struct stat st;
    return (stat(path, &st) == 0);
}
