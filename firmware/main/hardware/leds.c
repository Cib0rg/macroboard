/**
 * @file leds.c
 * @brief WS2812 RGB LED driver implementation using led_strip component
 */

#include "common.h"
#include "leds.h"
#include "config.h"
#include "led_strip.h"

static const char* TAG = "LEDS";

// LED color buffer (RGB format with brightness applied)
typedef struct {
    uint8_t r;
    uint8_t g;
    uint8_t b;
} led_color_t;

static led_color_t led_colors[NUM_LEDS] = {0};
static SemaphoreHandle_t led_mutex = NULL;
static led_strip_handle_t led_strip = NULL;

esp_err_t leds_init(void) {
    ESP_LOGI(TAG, "Initializing WS2812 LEDs on GPIO %d", PIN_LED_DATA);
    
    // Create mutex
    led_mutex = xSemaphoreCreateMutex();
    if (led_mutex == NULL) {
        ESP_LOGE(TAG, "Failed to create LED mutex");
        return ESP_FAIL;
    }
    
    // Configure LED strip using led_strip component
    led_strip_config_t strip_config = {
        .strip_gpio_num = PIN_LED_DATA,
        .max_leds = NUM_LEDS,
        .led_model = LED_MODEL_WS2812,
        .color_component_format = LED_STRIP_COLOR_COMPONENT_FMT_GRB,
    };
    
    led_strip_rmt_config_t rmt_config = {
        .resolution_hz = 10 * 1000 * 1000, // 10 MHz
        .flags.with_dma = false,
    };
    
    esp_err_t ret = led_strip_new_rmt_device(&strip_config, &rmt_config, &led_strip);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to create LED strip: %s", esp_err_to_name(ret));
        return ret;
    }
    
    // Clear all LEDs on startup
    memset(led_colors, 0, sizeof(led_colors));
    led_strip_clear(led_strip);
    
    ESP_LOGI(TAG, "WS2812 LEDs initialized (%d LEDs)", NUM_LEDS);
    return ESP_OK;
}

esp_err_t led_set_color(uint8_t led_id, uint8_t r, uint8_t g, uint8_t b, uint8_t brightness) {
    if (led_id >= NUM_LEDS) {
        return ESP_ERR_INVALID_ARG;
    }
    
    xSemaphoreTake(led_mutex, portMAX_DELAY);
    
    // Apply brightness (0-100 scale)
    led_colors[led_id].r = (r * brightness) / 100;
    led_colors[led_id].g = (g * brightness) / 100;
    led_colors[led_id].b = (b * brightness) / 100;
    
    xSemaphoreGive(led_mutex);
    
    return ESP_OK;
}

esp_err_t led_update(void) {
    if (led_strip == NULL) {
        return ESP_ERR_INVALID_STATE;
    }
    
    xSemaphoreTake(led_mutex, portMAX_DELAY);
    
    // Set all pixel colors
    for (int i = 0; i < NUM_LEDS; i++) {
        led_strip_set_pixel(led_strip, i, led_colors[i].r, led_colors[i].g, led_colors[i].b);
    }
    
    // Send data to the strip
    esp_err_t ret = led_strip_refresh(led_strip);
    
    xSemaphoreGive(led_mutex);
    
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to refresh LED strip: %s", esp_err_to_name(ret));
    }
    
    return ret;
}

esp_err_t led_clear_all(void) {
    xSemaphoreTake(led_mutex, portMAX_DELAY);
    
    memset(led_colors, 0, sizeof(led_colors));
    
    esp_err_t ret = ESP_OK;
    if (led_strip != NULL) {
        ret = led_strip_clear(led_strip);
    }
    
    xSemaphoreGive(led_mutex);
    
    return ret;
}

void led_task(void* arg) {
    ESP_LOGI(TAG, "LED task started");
    
    // This task can handle LED effects in the future
    // For now, it just waits
    
    while (1) {
        vTaskDelay(pdMS_TO_TICKS(100));
        
        // LED effects could be implemented here
        // For example: breathing, rainbow, wave, etc.
    }
}
