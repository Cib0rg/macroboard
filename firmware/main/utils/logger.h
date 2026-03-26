/**
 * @file logger.h
 * @brief Logging system wrapper
 */

#ifndef LOGGER_H
#define LOGGER_H

#include "esp_err.h"
#include "esp_log.h"

/**
 * @brief Initialize logger
 * @return ESP_OK on success
 */
esp_err_t logger_init(void);

// Use ESP-IDF logging macros directly
// ESP_LOGE, ESP_LOGW, ESP_LOGI, ESP_LOGD, ESP_LOGV

#endif // LOGGER_H
