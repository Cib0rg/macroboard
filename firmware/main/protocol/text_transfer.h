/**
 * @file text_transfer.h
 * @brief Chunked data transfer for keyboard text and sequence blobs (CMD 0x38/0x39/0x3A)
 */

#ifndef TEXT_TRANSFER_H
#define TEXT_TRANSFER_H

#include <stdint.h>
#include "esp_err.h"
#include "storage/text_storage.h"  // TEXT_ACTION_* / TEXT_STEP_* constants

/**
 * @brief Begin a transfer session.
 *
 * @param profile_id  Profile ID (always 0)
 * @param folder_id   0xFF = root, 0..15 = folder
 * @param button_id   0..9 = button
 * @param action_slot TEXT_ACTION_SHORT or TEXT_ACTION_LONG
 * @param step_index  TEXT_STEP_DIRECT / TEXT_STEP_SEQ_BLOB / 0..15
 * @param data_size   Total bytes to expect (max 4096)
 */
esp_err_t text_transfer_start(uint8_t profile_id, uint8_t folder_id,
                               uint8_t button_id, uint8_t action_slot,
                               uint8_t step_index, uint32_t data_size);

/**
 * @brief Append one chunk to the in-progress transfer buffer.
 * @param data      Raw bytes for this chunk
 * @param size      Chunk byte count
 * @param chunk_num Zero-based, sequential
 */
esp_err_t text_transfer_chunk(const uint8_t* data, uint16_t size, uint16_t chunk_num);

/**
 * @brief Finish the transfer: persist to SPIFFS and update profile action data.
 */
esp_err_t text_transfer_end(void);

#endif // TEXT_TRANSFER_H
