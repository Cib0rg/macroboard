/**
 * @file text_storage.c
 * @brief SPIFFS-based long keyboard text storage implementation
 */

#include "common.h"
#include "text_storage.h"

#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

static const char* TAG = "TEXT_STORAGE";

#define TEXT_FILE_FMT "/storage/txt_%d_%d.bin"

static void build_path(uint8_t profile_id, uint8_t storage_bid,
                        char* path, size_t path_size) {
    snprintf(path, path_size, TEXT_FILE_FMT, (int)profile_id, (int)storage_bid);
}

esp_err_t text_storage_save(uint8_t profile_id, uint8_t storage_bid,
                             const char* text, size_t text_len) {
    char path[64];
    build_path(profile_id, storage_bid, path, sizeof(path));

    FILE* f = fopen(path, "wb");
    if (f == NULL) {
        ESP_LOGE(TAG, "Failed to open %s for writing", path);
        return ESP_FAIL;
    }

    size_t written = fwrite(text, 1, text_len, f);
    fclose(f);

    if (written != text_len) {
        ESP_LOGE(TAG, "Write incomplete: %d/%d bytes", (int)written, (int)text_len);
        return ESP_FAIL;
    }

    ESP_LOGI(TAG, "Saved %d bytes to %s", (int)text_len, path);
    return ESP_OK;
}

esp_err_t text_storage_load(uint8_t profile_id, uint8_t storage_bid,
                             char* buf, size_t buf_size, size_t* out_len) {
    char path[64];
    build_path(profile_id, storage_bid, path, sizeof(path));

    struct stat st;
    if (stat(path, &st) != 0) {
        return ESP_ERR_NOT_FOUND;
    }

    FILE* f = fopen(path, "rb");
    if (f == NULL) {
        ESP_LOGE(TAG, "Failed to open %s for reading", path);
        return ESP_FAIL;
    }

    size_t to_read = (size_t)st.st_size;
    if (to_read >= buf_size) {
        to_read = buf_size - 1;
    }

    size_t actual = fread(buf, 1, to_read, f);
    fclose(f);

    *out_len = actual;
    return ESP_OK;
}

bool text_storage_exists(uint8_t profile_id, uint8_t storage_bid) {
    char path[64];
    build_path(profile_id, storage_bid, path, sizeof(path));
    struct stat st;
    return (stat(path, &st) == 0);
}

esp_err_t text_storage_delete(uint8_t profile_id, uint8_t storage_bid) {
    char path[64];
    build_path(profile_id, storage_bid, path, sizeof(path));
    if (unlink(path) != 0) {
        ESP_LOGW(TAG, "Delete failed or not found: %s", path);
    }
    return ESP_OK;
}
