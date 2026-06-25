/**
 * @file text_storage.h
 * @brief SPIFFS-based storage for keyboard text and sequence blobs
 */

#ifndef TEXT_STORAGE_H
#define TEXT_STORAGE_H

#include <stdint.h>
#include <stdbool.h>
#include <stdio.h>
#include "esp_err.h"

// action_slot values
#define TEXT_ACTION_SHORT   0
#define TEXT_ACTION_LONG    1

// step_index sentinels
#define TEXT_STEP_DIRECT    0xFF  // direct KEYBOARD action (not inside a sequence)
#define TEXT_STEP_SEQ_BLOB  0xFE  // the sequence blob itself (not a step's text)
// 0x00..0x0F: KEYBOARD text for sequence step N

/**
 * @brief Save data for a given action slot to SPIFFS.
 *
 * Used for both keyboard text (step_index = TEXT_STEP_DIRECT or 0..15)
 * and sequence blobs (step_index = TEXT_STEP_SEQ_BLOB).
 *
 * @param folder_id   0xFF = root, 0..15 = folder
 * @param button_id   0..9 = button, 0x40..0x43 = encoder slot
 * @param action_slot TEXT_ACTION_SHORT or TEXT_ACTION_LONG
 * @param step_index  TEXT_STEP_DIRECT / TEXT_STEP_SEQ_BLOB / 0..15
 */
esp_err_t text_storage_save(uint8_t profile_id, uint8_t folder_id, uint8_t button_id,
                             uint8_t action_slot, uint8_t step_index,
                             const uint8_t* data, size_t data_len);

/**
 * @brief Open a SPIFFS file for streaming read.  Caller must fclose().
 * Returns NULL if the file does not exist.
 */
FILE* text_storage_open_for_read(uint8_t profile_id, uint8_t folder_id, uint8_t button_id,
                                  uint8_t action_slot, uint8_t step_index);

/**
 * @brief Check whether a file exists for the given key.
 */
bool text_storage_exists(uint8_t profile_id, uint8_t folder_id, uint8_t button_id,
                          uint8_t action_slot, uint8_t step_index);

/**
 * @brief Delete the file for the given key (no-op if it does not exist).
 */
esp_err_t text_storage_delete(uint8_t profile_id, uint8_t folder_id, uint8_t button_id,
                               uint8_t action_slot, uint8_t step_index);

#endif // TEXT_STORAGE_H
