/**
 * @file image_storage.c
 * @brief Content-addressable image storage with deduplication
 *
 * Storage layout on SPIFFS:
 *   /storage/img_{CRC32_HEX}.jpg   — actual image blobs (content-addressed)
 *   /storage/img_map.bin            — mapping table persisted to flash
 *
 * In-memory structures:
 *   - mapping table: (profile_id, button_id) → crc32
 *   - image registry: crc32 → { refcount, size }
 *
 * When saving an image:
 *   1. Check if an image with the same CRC32 already exists on flash.
 *   2. If not, write the new blob file.
 *   3. Update the mapping for (profile, button) → crc32.
 *   4. If the button previously pointed to a different image, decrement
 *      that image's refcount and delete the blob if refcount == 0.
 *   5. Persist the mapping table.
 */

#include "common.h"
#include "image_storage.h"
#include "config.h"
#include "utils/crc.h"
#include <sys/stat.h>
#include <unistd.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char* TAG = "IMG_STOR";

// ============================================
// Constants
// ============================================

#define IMAGE_BLOB_FMT          "/storage/img_%08lx.jpg"
#define IMAGE_MAP_FILE          "/storage/img_map.bin"
#define IMAGE_MAP_MAGIC         0x494D4150  // "IMAP"
#define IMAGE_MAP_VERSION       1

// Maximum unique images we can track (NUM_PROFILES * NUM_BUTTONS = 50 max,
// but with folders it could be more; 64 is a safe upper bound for unique blobs)
#define MAX_UNIQUE_IMAGES       64
#define MAX_MAPPINGS            (NUM_PROFILES * NUM_BUTTONS)

// ============================================
// In-memory data structures
// ============================================

/** Mapping entry: (profile, button) → image CRC32 */
typedef struct {
    uint8_t  profile_id;
    uint8_t  button_id;
    uint32_t crc32;
} image_mapping_t;

/** Image registry entry: tracks refcount and size for a unique image */
typedef struct {
    uint32_t crc32;
    uint32_t size;       // file size in bytes
    uint16_t refcount;   // how many (profile, button) pairs reference this
} image_entry_t;

/** Persistent mapping file header */
typedef struct __attribute__((packed)) {
    uint32_t magic;
    uint8_t  version;
    uint8_t  num_mappings;
    uint8_t  num_images;
    uint8_t  reserved;
} image_map_header_t;

// In-memory state
static image_mapping_t s_mappings[MAX_MAPPINGS];
static uint8_t         s_num_mappings = 0;

static image_entry_t   s_images[MAX_UNIQUE_IMAGES];
static uint8_t         s_num_images = 0;

static bool            s_initialized = false;

// ============================================
// Internal helpers
// ============================================

/** Find mapping index for (profile, button). Returns -1 if not found. */
static int find_mapping(uint8_t profile_id, uint8_t button_id) {
    for (int i = 0; i < s_num_mappings; i++) {
        if (s_mappings[i].profile_id == profile_id &&
            s_mappings[i].button_id == button_id) {
            return i;
        }
    }
    return -1;
}

/** Find image entry index by CRC32. Returns -1 if not found. */
static int find_image(uint32_t crc32) {
    for (int i = 0; i < s_num_images; i++) {
        if (s_images[i].crc32 == crc32) {
            return i;
        }
    }
    return -1;
}

/** Build the blob file path for a given CRC32 */
static void build_blob_path(uint32_t crc32, char* path, size_t path_size) {
    snprintf(path, path_size, IMAGE_BLOB_FMT, (unsigned long)crc32);
}

/** Remove a mapping entry by index (swap with last) */
static void remove_mapping(int idx) {
    if (idx < 0 || idx >= s_num_mappings) return;
    s_num_mappings--;
    if (idx < s_num_mappings) {
        s_mappings[idx] = s_mappings[s_num_mappings];
    }
}

/** Remove an image entry by index (swap with last) */
static void remove_image(int idx) {
    if (idx < 0 || idx >= s_num_images) return;
    s_num_images--;
    if (idx < s_num_images) {
        s_images[idx] = s_images[s_num_images];
    }
}

/** Decrement refcount for an image; delete blob if refcount reaches 0 */
static void release_image(uint32_t crc32) {
    int img_idx = find_image(crc32);
    if (img_idx < 0) return;

    if (s_images[img_idx].refcount > 0) {
        s_images[img_idx].refcount--;
    }

    if (s_images[img_idx].refcount == 0) {
        // Delete the blob file from flash
        char path[48];
        build_blob_path(crc32, path, sizeof(path));
        if (unlink(path) == 0) {
            ESP_LOGI(TAG, "Deleted unreferenced image: CRC32=0x%08lx", (unsigned long)crc32);
        } else {
            ESP_LOGW(TAG, "Failed to delete blob: %s", path);
        }
        remove_image(img_idx);
    }
}


// ============================================
// Persistence: save/load mapping table
// ============================================

static esp_err_t save_map_to_flash(void) {
    FILE* f = fopen(IMAGE_MAP_FILE, "wb");
    if (f == NULL) {
        ESP_LOGE(TAG, "Failed to open map file for writing");
        return ESP_FAIL;
    }

    image_map_header_t hdr = {
        .magic = IMAGE_MAP_MAGIC,
        .version = IMAGE_MAP_VERSION,
        .num_mappings = s_num_mappings,
        .num_images = s_num_images,
        .reserved = 0,
    };

    fwrite(&hdr, 1, sizeof(hdr), f);

    // Write mappings
    for (int i = 0; i < s_num_mappings; i++) {
        fwrite(&s_mappings[i].profile_id, 1, 1, f);
        fwrite(&s_mappings[i].button_id, 1, 1, f);
        fwrite(&s_mappings[i].crc32, 1, 4, f);
    }

    // Write image entries
    for (int i = 0; i < s_num_images; i++) {
        fwrite(&s_images[i].crc32, 1, 4, f);
        fwrite(&s_images[i].size, 1, 4, f);
        fwrite(&s_images[i].refcount, 1, 2, f);
    }

    fflush(f);
    fclose(f);

    ESP_LOGD(TAG, "Map saved: %d mappings, %d unique images", s_num_mappings, s_num_images);
    return ESP_OK;
}

static esp_err_t load_map_from_flash(void) {
    FILE* f = fopen(IMAGE_MAP_FILE, "rb");
    if (f == NULL) {
        ESP_LOGI(TAG, "No mapping file found, starting fresh");
        s_num_mappings = 0;
        s_num_images = 0;
        return ESP_OK;
    }

    image_map_header_t hdr;
    if (fread(&hdr, 1, sizeof(hdr), f) != sizeof(hdr)) {
        ESP_LOGW(TAG, "Map file too short");
        fclose(f);
        return ESP_FAIL;
    }

    if (hdr.magic != IMAGE_MAP_MAGIC || hdr.version != IMAGE_MAP_VERSION) {
        ESP_LOGW(TAG, "Map file invalid (magic=0x%08lx, ver=%d)", 
                 (unsigned long)hdr.magic, hdr.version);
        fclose(f);
        return ESP_FAIL;
    }

    if (hdr.num_mappings > MAX_MAPPINGS || hdr.num_images > MAX_UNIQUE_IMAGES) {
        ESP_LOGW(TAG, "Map file counts out of range");
        fclose(f);
        return ESP_FAIL;
    }

    // Read mappings
    s_num_mappings = 0;
    for (int i = 0; i < hdr.num_mappings; i++) {
        uint8_t pid, bid;
        uint32_t crc;
        if (fread(&pid, 1, 1, f) != 1 ||
            fread(&bid, 1, 1, f) != 1 ||
            fread(&crc, 1, 4, f) != 4) {
            ESP_LOGW(TAG, "Map file truncated at mapping %d", i);
            break;
        }
        s_mappings[s_num_mappings].profile_id = pid;
        s_mappings[s_num_mappings].button_id = bid;
        s_mappings[s_num_mappings].crc32 = crc;
        s_num_mappings++;
    }

    // Read image entries
    s_num_images = 0;
    for (int i = 0; i < hdr.num_images; i++) {
        uint32_t crc, size;
        uint16_t refcount;
        if (fread(&crc, 1, 4, f) != 4 ||
            fread(&size, 1, 4, f) != 4 ||
            fread(&refcount, 1, 2, f) != 2) {
            ESP_LOGW(TAG, "Map file truncated at image %d", i);
            break;
        }
        s_images[s_num_images].crc32 = crc;
        s_images[s_num_images].size = size;
        s_images[s_num_images].refcount = refcount;
        s_num_images++;
    }

    fclose(f);

    ESP_LOGI(TAG, "Map loaded: %d mappings, %d unique images", s_num_mappings, s_num_images);
    return ESP_OK;
}

/**
 * @brief Migrate legacy per-button image files to content-addressed storage.
 *
 * Scans for files matching the old naming pattern /storage/img_{P}_{B}.jpg
 * and migrates them into the new CRC32-based scheme.
 */
static void migrate_legacy_images(void) {
    int migrated = 0;

    for (uint8_t p = 0; p < NUM_PROFILES; p++) {
        for (uint8_t b = 0; b < NUM_BUTTONS; b++) {
            // Yield between iterations so the IDLE task can run and reset the WDT.
            // SPIFFS stat() can be slow enough on its own to starve the idle task.
            vTaskDelay(pdMS_TO_TICKS(1));
            char old_path[64];
            snprintf(old_path, sizeof(old_path), IMAGE_FILE_FMT, p, b);

            struct stat st;
            if (stat(old_path, &st) != 0) {
                continue;  // No legacy file for this slot
            }

            ESP_LOGI(TAG, "Migrating legacy image: %s (%ld bytes)", old_path, st.st_size);

            // Read the file
            FILE* f = fopen(old_path, "rb");
            if (f == NULL) continue;

            uint8_t* buf = heap_caps_malloc(st.st_size, MALLOC_CAP_SPIRAM);
            if (buf == NULL) {
                fclose(f);
                continue;
            }

            size_t read = fread(buf, 1, st.st_size, f);
            fclose(f);

            if (read != (size_t)st.st_size) {
                free(buf);
                continue;
            }

            // Compute CRC32
            uint32_t crc = crc32_calculate(buf, read);

            // Save through the new dedup path
            image_storage_save(p, b, buf, read, crc);
            free(buf);

            // Delete the legacy file
            unlink(old_path);
            migrated++;
        }
    }

    if (migrated > 0) {
        ESP_LOGI(TAG, "Migrated %d legacy images to content-addressed storage", migrated);
    }
}

// ============================================
// Public API
// ============================================

esp_err_t image_storage_init(void) {
    if (s_initialized) return ESP_OK;

    esp_err_t ret = load_map_from_flash();
    bool map_was_empty = false;
    if (ret != ESP_OK) {
        // Start fresh if map is corrupted
        s_num_mappings = 0;
        s_num_images = 0;
        map_was_empty = true;
    } else if (s_num_mappings == 0 && s_num_images == 0) {
        map_was_empty = true;
    }

    s_initialized = true;

    // Only scan for legacy per-button files when the map is fresh (first boot or
    // after a firmware format change). On subsequent boots the map already contains
    // the migrated data, so skipping avoids 50+ slow SPIFFS stat() calls that
    // would trigger the task watchdog.
    if (map_was_empty) {
        migrate_legacy_images();
    }

    ESP_LOGI(TAG, "Image storage initialized: %d unique images, %d mappings",
             s_num_images, s_num_mappings);
    return ESP_OK;
}

esp_err_t image_storage_save(uint8_t profile_id, uint8_t button_id,
                              const uint8_t* image_data, size_t image_size,
                              uint32_t crc32) {
    if (profile_id >= NUM_PROFILES || button_id >= NUM_BUTTONS ||
        image_data == NULL || image_size == 0) {
        return ESP_ERR_INVALID_ARG;
    }

    // 1. Check if this (profile, button) already maps to the same CRC32
    int map_idx = find_mapping(profile_id, button_id);
    if (map_idx >= 0 && s_mappings[map_idx].crc32 == crc32) {
        ESP_LOGI(TAG, "Image unchanged for profile=%d button=%d (CRC32=0x%08lx)",
                 profile_id, button_id, (unsigned long)crc32);
        return ESP_OK;  // Nothing to do — same image already assigned
    }

    // 2. If this button previously had a different image, release it
    uint32_t old_crc = 0;
    bool had_old = false;
    if (map_idx >= 0) {
        old_crc = s_mappings[map_idx].crc32;
        had_old = true;
    }

    // 3. Check if the new image blob already exists (deduplication!)
    int img_idx = find_image(crc32);
    if (img_idx >= 0) {
        // Image already on flash — just bump refcount
        s_images[img_idx].refcount++;
        ESP_LOGI(TAG, "Dedup hit! CRC32=0x%08lx refcount=%d (saved %lu bytes)",
                 (unsigned long)crc32, s_images[img_idx].refcount, (unsigned long)image_size);
    } else {
        // New image — write blob to flash
        char path[48];
        build_blob_path(crc32, path, sizeof(path));

        FILE* f = fopen(path, "wb");
        if (f == NULL) {
            ESP_LOGE(TAG, "Failed to open file for writing: %s", path);
            return ESP_FAIL;
        }

        size_t written = fwrite(image_data, 1, image_size, f);
        fflush(f);
        fclose(f);

        if (written != image_size) {
            ESP_LOGE(TAG, "Failed to write image blob");
            unlink(path);
            return ESP_FAIL;
        }

        // Add to image registry
        if (s_num_images >= MAX_UNIQUE_IMAGES) {
            ESP_LOGE(TAG, "Image registry full (%d)", MAX_UNIQUE_IMAGES);
            unlink(path);
            return ESP_ERR_NO_MEM;
        }

        s_images[s_num_images].crc32 = crc32;
        s_images[s_num_images].size = image_size;
        s_images[s_num_images].refcount = 1;
        s_num_images++;

        ESP_LOGI(TAG, "New image saved: CRC32=0x%08lx, size=%lu",
                 (unsigned long)crc32, (unsigned long)image_size);
    }

    // 4. Update or create the mapping
    if (map_idx >= 0) {
        s_mappings[map_idx].crc32 = crc32;
    } else {
        if (s_num_mappings >= MAX_MAPPINGS) {
            ESP_LOGE(TAG, "Mapping table full (%d)", MAX_MAPPINGS);
            // Rollback: decrement refcount of the new image
            release_image(crc32);
            return ESP_ERR_NO_MEM;
        }
        s_mappings[s_num_mappings].profile_id = profile_id;
        s_mappings[s_num_mappings].button_id = button_id;
        s_mappings[s_num_mappings].crc32 = crc32;
        s_num_mappings++;
    }

    // 5. Release the old image (if any)
    if (had_old && old_crc != crc32) {
        release_image(old_crc);
    }

    // 6. Persist mapping table
    save_map_to_flash();

    ESP_LOGI(TAG, "Image mapped: profile=%d, button=%d -> CRC32=0x%08lx",
             profile_id, button_id, (unsigned long)crc32);
    return ESP_OK;
}

esp_err_t image_storage_load(uint8_t profile_id, uint8_t button_id,
                              uint8_t** image_data, size_t* image_size) {
    if (profile_id >= NUM_PROFILES || button_id >= NUM_BUTTONS ||
        image_data == NULL || image_size == NULL) {
        return ESP_ERR_INVALID_ARG;
    }

    // Find mapping
    int map_idx = find_mapping(profile_id, button_id);
    if (map_idx < 0) {
        ESP_LOGW(TAG, "No image for profile=%d, button=%d", profile_id, button_id);
        return ESP_ERR_NOT_FOUND;
    }

    uint32_t crc32 = s_mappings[map_idx].crc32;

    // Build blob path and read
    char path[48];
    build_blob_path(crc32, path, sizeof(path));

    struct stat st;
    if (stat(path, &st) != 0) {
        ESP_LOGW(TAG, "Blob file missing: %s", path);
        return ESP_ERR_NOT_FOUND;
    }

    *image_size = st.st_size;

    // Allocate buffer in PSRAM
    *image_data = heap_caps_malloc(*image_size, MALLOC_CAP_SPIRAM);
    if (*image_data == NULL) {
        ESP_LOGE(TAG, "Failed to allocate memory for image");
        return ESP_ERR_NO_MEM;
    }

    FILE* f = fopen(path, "rb");
    if (f == NULL) {
        free(*image_data);
        *image_data = NULL;
        return ESP_FAIL;
    }

    size_t read = fread(*image_data, 1, *image_size, f);
    fclose(f);

    if (read != *image_size) {
        free(*image_data);
        *image_data = NULL;
        ESP_LOGE(TAG, "Failed to read image blob");
        return ESP_FAIL;
    }

    ESP_LOGI(TAG, "Image loaded: profile=%d, button=%d, CRC32=0x%08lx, size=%lu",
             profile_id, button_id, (unsigned long)crc32, (unsigned long)*image_size);
    return ESP_OK;
}

esp_err_t image_storage_delete(uint8_t profile_id, uint8_t button_id) {
    if (profile_id >= NUM_PROFILES || button_id >= NUM_BUTTONS) {
        return ESP_ERR_INVALID_ARG;
    }

    int map_idx = find_mapping(profile_id, button_id);
    if (map_idx < 0) {
        ESP_LOGW(TAG, "No image to delete for profile=%d, button=%d",
                 profile_id, button_id);
        return ESP_OK;  // Nothing to delete
    }

    uint32_t crc32 = s_mappings[map_idx].crc32;

    // Remove the mapping
    remove_mapping(map_idx);

    // Release the image (decrements refcount, deletes blob if 0)
    release_image(crc32);

    // Persist
    save_map_to_flash();

    ESP_LOGI(TAG, "Image deleted: profile=%d, button=%d", profile_id, button_id);
    return ESP_OK;
}

bool image_storage_has_image(uint8_t profile_id, uint8_t button_id) {
    return find_mapping(profile_id, button_id) >= 0;
}

void image_storage_get_stats(uint16_t* total_images, uint16_t* total_mappings,
                              uint32_t* saved_bytes) {
    if (total_images)  *total_images = s_num_images;
    if (total_mappings) *total_mappings = s_num_mappings;

    if (saved_bytes) {
        // Calculate bytes saved: for each image with refcount > 1,
        // we saved (refcount - 1) * size bytes
        uint32_t saved = 0;
        for (int i = 0; i < s_num_images; i++) {
            if (s_images[i].refcount > 1) {
                saved += (uint32_t)(s_images[i].refcount - 1) * s_images[i].size;
            }
        }
        *saved_bytes = saved;
    }
}

esp_err_t image_storage_flush(void) {
    return save_map_to_flash();
}
