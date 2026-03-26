/**
 * @file logger.c
 * @brief Logging system implementation
 */

#include "logger.h"
#include "esp_err.h"
#include "esp_log.h"

static const char* TAG = "LOGGER";

esp_err_t logger_init(void) {
    // Set default log level
    esp_log_level_set("*", ESP_LOG_INFO);
    
    // Set specific module log levels if needed
    // esp_log_level_set("BUTTONS", ESP_LOG_DEBUG);
    // esp_log_level_set("ENCODER", ESP_LOG_DEBUG);
    
    ESP_LOGI(TAG, "Logger initialized");
    return ESP_OK;
}
