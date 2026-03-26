/**
 * @file profile_storage.h
 * @brief Profile storage in SPIFFS
 */

#ifndef PROFILE_STORAGE_H
#define PROFILE_STORAGE_H

#include "common.h"
#include "profile/profile_types.h"

/**
 * @brief Initialize profile storage
 * @return ESP_OK on success
 */
esp_err_t profile_storage_init(void);

/**
 * @brief Save profile to storage
 * @param profile_id Profile ID
 * @param profile Profile data
 * @return ESP_OK on success
 */
esp_err_t profile_storage_save(uint8_t profile_id, const profile_t* profile);

/**
 * @brief Load profile from storage
 * @param profile_id Profile ID
 * @param profile Output profile data
 * @return ESP_OK on success
 */
esp_err_t profile_storage_load(uint8_t profile_id, profile_t* profile);

/**
 * @brief Delete profile from storage
 * @param profile_id Profile ID
 * @return ESP_OK on success
 */
esp_err_t profile_storage_delete(uint8_t profile_id);

/**
 * @brief Check if profile exists
 * @param profile_id Profile ID
 * @return true if exists
 */
bool profile_storage_exists(uint8_t profile_id);

#endif // PROFILE_STORAGE_H
