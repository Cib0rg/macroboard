/**
 * @file image_storage.c
 * @brief Image storage implementation
 */

#include "common.h"
#include "image_storage.h"
#include "config.h"
#include <sys/stat.h>
#include <unistd.h>

static const char* TAG = "IMG_STOR";

esp_err_t image_storage_save(uint8_t profile_id, uint8_t button_id,
                              const uint8_t* image_data, size_t image_size) {
    if (profile_id >= NUM_PROFILES || button_id >= NUM_BUTTONS || 
        image_data == NULL || image_size == 0) {
        return ESP_ERR_INVALID_ARG;
    }
    
    char path[64];
    snprintf(path, sizeof(path), IMAGE_FILE_FMT, profile_id, button_id);
    
    FILE* f = fopen(path, "wb");
    if (f == NULL) {
        ESP_LOGE(TAG, "Failed to open file for writing: %s", path);
        return ESP_FAIL;
    }
    
    size_t written = fwrite(image_data, 1, image_size, f);
    fclose(f);
    
    if (written != image_size) {
        ESP_LOGE(TAG, "Failed to write image");
        return ESP_FAIL;
    }
    
    ESP_LOGI(TAG, "Image saved: profile=%d, button=%d, size=%d", 
             profile_id, button_id, image_size);
    return ESP_OK;
}

esp_err_t image_storage_load(uint8_t profile_id, uint8_t button_id,
                              uint8_t** image_data, size_t* image_size) {
    if (profile_id >= NUM_PROFILES || button_id >= NUM_BUTTONS ||
        image_data == NULL || image_size == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    
    char path[64];
    snprintf(path, sizeof(path), IMAGE_FILE_FMT, profile_id, button_id);
    
    // Get file size
    struct stat st;
    if (stat(path, &st) != 0) {
        ESP_LOGW(TAG, "Image not found: profile=%d, button=%d", profile_id, button_id);
        return ESP_ERR_NOT_FOUND;
    }
    
    *image_size = st.st_size;
    
    // Allocate buffer in PSRAM
    *image_data = heap_caps_malloc(*image_size, MALLOC_CAP_SPIRAM);
    if (*image_data == NULL) {
        ESP_LOGE(TAG, "Failed to allocate memory for image");
        return ESP_ERR_NO_MEM;
    }
    
    // Read file
    FILE* f = fopen(path, "rb");
    if (f == NULL) {
        free(*image_data);
        return ESP_FAIL;
    }
    
    size_t read = fread(*image_data, 1, *image_size, f);
    fclose(f);
    
    if (read != *image_size) {
        free(*image_data);
        ESP_LOGE(TAG, "Failed to read image");
        return ESP_FAIL;
    }
    
    ESP_LOGI(TAG, "Image loaded: profile=%d, button=%d, size=%d",
             profile_id, button_id, *image_size);
    return ESP_OK;
}

esp_err_t image_storage_delete(uint8_t profile_id, uint8_t button_id) {
    if (profile_id >= NUM_PROFILES || button_id >= NUM_BUTTONS) {
        return ESP_ERR_INVALID_ARG;
    }
    
    char path[64];
    snprintf(path, sizeof(path), IMAGE_FILE_FMT, profile_id, button_id);
    
    if (unlink(path) != 0) {
        ESP_LOGW(TAG, "Failed to delete image");
        return ESP_FAIL;
    }
    
    ESP_LOGI(TAG, "Image deleted: profile=%d, button=%d", profile_id, button_id);
    return ESP_OK;
}
