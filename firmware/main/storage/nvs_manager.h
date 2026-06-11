/**
 * @file nvs_manager.h
 * @brief NVS (Non-Volatile Storage) manager
 */

#ifndef NVS_MANAGER_H
#define NVS_MANAGER_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Initialize NVS
 * @return ESP_OK on success
 */
esp_err_t nvs_manager_init(void);

/**
 * @brief Get WiFi credentials
 * @param ssid Output SSID buffer (min 32 bytes)
 * @param password Output password buffer (min 64 bytes)
 * @return ESP_OK on success
 */
esp_err_t nvs_get_wifi_credentials(char* ssid, char* password);

/**
 * @brief Set WiFi credentials
 * @param ssid SSID string
 * @param password Password string
 * @return ESP_OK on success
 */
esp_err_t nvs_set_wifi_credentials(const char* ssid, const char* password);

#endif // NVS_MANAGER_H
