#include "logger.h"
#include "esp_log.h"

static const char *TAG = "LOGGER";

esp_err_t logger_init(void)
{
    // ESP-IDF уже инициализирует систему логирования
    // Здесь можно настроить уровни логирования для разных компонентов
    
    esp_log_level_set("*", ESP_LOG_INFO);
    esp_log_level_set("MAIN", ESP_LOG_DEBUG);
    esp_log_level_set("PROTOCOL", ESP_LOG_DEBUG);
    esp_log_level_set("USB_HID", ESP_LOG_DEBUG);
    esp_log_level_set("PROFILE", ESP_LOG_DEBUG);
    
    ESP_LOGI(TAG, "Logger initialized with custom levels");
    
    return ESP_OK;
}
