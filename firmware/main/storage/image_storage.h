/**
 * @file image_storage.h
 * @brief Content-addressable image storage in SPIFFS with deduplication
 *
 * Images are stored by their CRC32 hash. Multiple buttons can reference
 * the same image without duplicating data on flash. A mapping table
 * tracks which (profile, button) pairs point to which image hash,
 * and a reference counter ensures images are only deleted when no
 * buttons reference them.
 */

#ifndef IMAGE_STORAGE_H
#define IMAGE_STORAGE_H

#include <stdint.h>
#include <stdbool.h>
#include "esp_err.h"
#include "config.h"

/**
 * @brief Initialize image storage (load mapping table from flash)
 * @return ESP_OK on success
 */
esp_err_t image_storage_init(void);

/**
 * @brief Save image to storage with deduplication
 *
 * If an image with the same CRC32 already exists on flash, no new file
 * is written — only the mapping is updated. The old image referenced by
 * this (profile, button) slot (if any) has its refcount decremented and
 * is deleted from flash when refcount reaches zero.
 *
 * @param profile_id Profile ID
 * @param button_id Button ID
 * @param image_data Image data (JPEG)
 * @param image_size Image size in bytes
 * @param crc32 Pre-computed CRC32 of image_data
 * @return ESP_OK on success
 */
esp_err_t image_storage_save(uint8_t profile_id, uint8_t button_id,
                              const uint8_t* image_data, size_t image_size,
                              uint32_t crc32);

/**
 * @brief Load image from storage
 * @param profile_id Profile ID
 * @param button_id Button ID
 * @param image_data Output buffer (must be freed by caller)
 * @param image_size Output image size
 * @return ESP_OK on success
 */
esp_err_t image_storage_load(uint8_t profile_id, uint8_t button_id,
                              uint8_t** image_data, size_t* image_size);

/**
 * @brief Delete image mapping for a button
 *
 * Decrements the reference count for the underlying image file.
 * If refcount reaches zero, the image file is deleted from flash.
 *
 * @param profile_id Profile ID
 * @param button_id Button ID
 * @return ESP_OK on success
 */
esp_err_t image_storage_delete(uint8_t profile_id, uint8_t button_id);

/**
 * @brief Check if a button has an image assigned
 * @param profile_id Profile ID
 * @param button_id Button ID
 * @return true if an image is mapped to this button
 */
bool image_storage_has_image(uint8_t profile_id, uint8_t button_id);

/**
 * @brief Get storage statistics
 * @param total_images Output: total unique images on flash
 * @param total_mappings Output: total (profile,button)->image mappings
 * @param saved_bytes Output: estimated bytes saved by deduplication
 */
void image_storage_get_stats(uint16_t* total_images, uint16_t* total_mappings,
                              uint32_t* saved_bytes);

/**
 * @brief Persist the mapping table to flash
 *
 * Called automatically by save/delete, but can be called explicitly
 * to ensure consistency.
 *
 * @return ESP_OK on success
 */
esp_err_t image_storage_flush(void);

#endif // IMAGE_STORAGE_H
