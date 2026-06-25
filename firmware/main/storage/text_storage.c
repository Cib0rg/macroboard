/**
 * @file text_storage.c
 * @brief SPIFFS-based storage for keyboard text and sequence blobs
 *
 * Path format: /storage/txt_{P}_{F}_{B}_{A}_{S}.bin
 *   P = profile_id, F = folder_id (255=root), B = button_id,
 *   A = action_slot (0=short, 1=long), S = step_index (0xFF=direct, 0xFE=seq blob)
 */

#include "common.h"
#include "text_storage.h"

#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

static const char* TAG = "TEXT_STORAGE";

static void build_path(uint8_t profile_id, uint8_t folder_id, uint8_t button_id,
                        uint8_t action_slot, uint8_t step_index,
                        char* path, size_t path_size) {
    snprintf(path, path_size, "/storage/txt_%d_%d_%d_%d_%d.bin",
             (int)profile_id, (int)folder_id, (int)button_id,
             (int)action_slot, (int)step_index);
}

esp_err_t text_storage_save(uint8_t profile_id, uint8_t folder_id, uint8_t button_id,
                             uint8_t action_slot, uint8_t step_index,
                             const uint8_t* data, size_t data_len) {
    char path[64];
    build_path(profile_id, folder_id, button_id, action_slot, step_index, path, sizeof(path));

    FILE* f = fopen(path, "wb");
    if (f == NULL) {
        ESP_LOGE(TAG, "Failed to open %s for writing", path);
        return ESP_FAIL;
    }

    size_t written = fwrite(data, 1, data_len, f);
    fclose(f);

    if (written != data_len) {
        ESP_LOGE(TAG, "Write incomplete: %d/%d bytes to %s", (int)written, (int)data_len, path);
        return ESP_FAIL;
    }

    ESP_LOGI(TAG, "Saved %d bytes to %s", (int)data_len, path);
    return ESP_OK;
}

FILE* text_storage_open_for_read(uint8_t profile_id, uint8_t folder_id, uint8_t button_id,
                                  uint8_t action_slot, uint8_t step_index) {
    char path[64];
    build_path(profile_id, folder_id, button_id, action_slot, step_index, path, sizeof(path));
    FILE* f = fopen(path, "rb");
    if (!f) {
        ESP_LOGD(TAG, "Not found: %s", path);
    }
    return f;
}

bool text_storage_exists(uint8_t profile_id, uint8_t folder_id, uint8_t button_id,
                          uint8_t action_slot, uint8_t step_index) {
    char path[64];
    build_path(profile_id, folder_id, button_id, action_slot, step_index, path, sizeof(path));
    struct stat st;
    return (stat(path, &st) == 0);
}

esp_err_t text_storage_delete(uint8_t profile_id, uint8_t folder_id, uint8_t button_id,
                               uint8_t action_slot, uint8_t step_index) {
    char path[64];
    build_path(profile_id, folder_id, button_id, action_slot, step_index, path, sizeof(path));
    if (unlink(path) != 0) {
        ESP_LOGW(TAG, "Delete failed or not found: %s", path);
    }
    return ESP_OK;
}
