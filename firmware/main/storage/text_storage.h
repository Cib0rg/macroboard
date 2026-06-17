/**
 * @file text_storage.h
 * @brief SPIFFS-based long keyboard text storage
 */

#ifndef TEXT_STORAGE_H
#define TEXT_STORAGE_H

#include <stdint.h>
#include <stdbool.h>
#include "esp_err.h"

/**
 * @brief Save text for a button to SPIFFS
 * @param profile_id  Profile ID
 * @param storage_bid Synthetic button ID (root: button_id; folder: NUM_BUTTONS + fid*NUM_BUTTONS + bid)
 * @param text        Text bytes (need not be null-terminated)
 * @param text_len    Number of bytes to write
 */
esp_err_t text_storage_save(uint8_t profile_id, uint8_t storage_bid,
                             const char* text, size_t text_len);

/**
 * @brief Load text for a button from SPIFFS
 * @param profile_id  Profile ID
 * @param storage_bid Synthetic button ID
 * @param buf         Caller-provided buffer (will NOT be null-terminated — caller must do it)
 * @param buf_size    Buffer capacity
 * @param out_len     Number of bytes actually read
 */
esp_err_t text_storage_load(uint8_t profile_id, uint8_t storage_bid,
                             char* buf, size_t buf_size, size_t* out_len);

/**
 * @brief Check whether a text file exists for the given button
 */
bool text_storage_exists(uint8_t profile_id, uint8_t storage_bid);

/**
 * @brief Delete the text file for a button (no-op if it does not exist)
 */
esp_err_t text_storage_delete(uint8_t profile_id, uint8_t storage_bid);

#endif // TEXT_STORAGE_H
