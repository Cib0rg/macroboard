/**
 * @file image_storage.h
 * @brief Image storage in SPIFFS
 */

#ifndef IMAGE_STORAGE_H
#define IMAGE_STORAGE_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Save image to storage
 * @param profile_id Profile ID
 * @param button_id Button ID
 * @param image_data Image data (JPEG)
 * @param image_size Image size
 * @return ESP_OK on success
 */
esp_err_t image_storage_save(uint8_t profile_id, uint8_t button_id,
                              const uint8_t* image_data, size_t image_size);

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
 * @brief Delete image from storage
 * @param profile_id Profile ID
 * @param button_id Button ID
 * @return ESP_OK on success
 */
esp_err_t image_storage_delete(uint8_t profile_id, uint8_t button_id);

#endif // IMAGE_STORAGE_H
