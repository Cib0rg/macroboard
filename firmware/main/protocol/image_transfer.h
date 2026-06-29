/**
 * @file image_transfer.h
 * @brief Image transfer management
 */

#ifndef IMAGE_TRANSFER_H
#define IMAGE_TRANSFER_H

#include <stdint.h>
#include "esp_err.h"

// Transfer flags for image_transfer_start() — combined in the flags byte.
// VOLATILE: skip SPIFFS write and profile cache update; display only.
// Used for plugin-driven display updates that must not persist across reboots.
#define TRANSFER_FLAG_VOLATILE  0x01

/**
 * @brief Start image transfer
 * @param profile_id Profile ID
 * @param folder_id  Folder ID (0xFF = root button, 0-N = folder button)
 * @param button_id  Button ID within the profile or folder
 * @param image_size Total image size
 * @param format     Image format (0x01 = JPEG)
 * @param flags      Transfer flags (TRANSFER_FLAG_VOLATILE = skip SPIFFS write)
 * @return ESP_OK on success
 */
esp_err_t image_transfer_start(uint8_t profile_id, uint8_t folder_id,
                                uint8_t button_id, uint32_t image_size,
                                uint8_t format, uint8_t flags);

/**
 * @brief Receive image data chunk
 * @param data Chunk data
 * @param size Chunk size
 * @param chunk_num Chunk number
 * @return ESP_OK on success
 */
esp_err_t image_transfer_chunk(const uint8_t* data, uint16_t size, uint16_t chunk_num);

/**
 * @brief End image transfer and save
 * @param calculated_crc Output calculated CRC32
 * @return ESP_OK on success
 */
esp_err_t image_transfer_end(uint32_t* calculated_crc);

#endif // IMAGE_TRANSFER_H
