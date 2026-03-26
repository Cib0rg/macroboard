/**
 * @file wifi_manager.h
 * @brief WiFi manager for OTA updates
 */

#ifndef WIFI_MANAGER_H
#define WIFI_MANAGER_H

#include "common.h"

/**
 * @brief Initialize WiFi manager
 * @return ESP_OK on success
 */
esp_err_t wifi_manager_init(void);

/**
 * @brief Connect to WiFi
 * @param ssid WiFi SSID
 * @param password WiFi password
 * @return ESP_OK on success
 */
esp_err_t wifi_connect(const char* ssid, const char* password);

/**
 * @brief Disconnect from WiFi
 * @return ESP_OK on success
 */
esp_err_t wifi_disconnect(void);

/**
 * @brief Check if WiFi is connected
 * @return true if connected
 */
bool wifi_is_connected(void);

#endif // WIFI_MANAGER_H
