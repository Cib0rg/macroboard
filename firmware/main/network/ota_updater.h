/**
 * @file ota_updater.h
 * @brief OTA firmware updater
 */

#ifndef OTA_UPDATER_H
#define OTA_UPDATER_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Start OTA update
 * @param url Firmware URL
 * @return ESP_OK on success
 */
esp_err_t ota_start_update(const char* url);

/**
 * @brief Get OTA update progress
 * @return Progress percentage (0-100)
 */
uint8_t ota_get_progress(void);

#endif // OTA_UPDATER_H
