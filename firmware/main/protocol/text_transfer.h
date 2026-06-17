/**
 * @file text_transfer.h
 * @brief Chunked long keyboard-text transfer (CMD 0x38/0x39/0x3A)
 */

#ifndef TEXT_TRANSFER_H
#define TEXT_TRANSFER_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Begin a text transfer session.
 * @param profile_id Profile ID (always 0 on device)
 * @param folder_id  0xFF = root button; 0..N = folder button
 * @param button_id  Physical button position within root / folder
 * @param text_size  Total text size in bytes (max 4096)
 */
esp_err_t text_transfer_start(uint8_t profile_id, uint8_t folder_id,
                               uint8_t button_id, uint32_t text_size);

/**
 * @brief Append one chunk to the in-progress transfer buffer.
 * @param data      Raw text bytes for this chunk
 * @param size      Chunk byte count
 * @param chunk_num Zero-based, sequential
 */
esp_err_t text_transfer_chunk(const uint8_t* data, uint16_t size, uint16_t chunk_num);

/**
 * @brief Finish the transfer: persist to SPIFFS and update profile action data.
 */
esp_err_t text_transfer_end(void);

#endif // TEXT_TRANSFER_H
