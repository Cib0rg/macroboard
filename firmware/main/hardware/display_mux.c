/**
 * @file display_mux.c
 * @brief Display multiplexer implementation using 74HC138 decoders
 */

#include "common.h"
#include "display_mux.h"
#include "config.h"
#include "driver/gpio.h"

static const char* TAG = "DISP_MUX";

esp_err_t display_mux_init(void) {
    ESP_LOGI(TAG, "Initializing display multiplexer");
    
    // Configure multiplexer control pins
    gpio_config_t io_conf = {
        .pin_bit_mask = (1ULL << PIN_MUX_A0) | (1ULL << PIN_MUX_A1) | 
                        (1ULL << PIN_MUX_A2) | (1ULL << PIN_MUX_SEL),
        .mode = GPIO_MODE_OUTPUT,
        .pull_up_en = GPIO_PULLUP_DISABLE,
        .pull_down_en = GPIO_PULLDOWN_DISABLE,
        .intr_type = GPIO_INTR_DISABLE,
    };
    
    esp_err_t ret = gpio_config(&io_conf);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to configure GPIO: %s", esp_err_to_name(ret));
        return ret;
    }
    
    // Deselect all displays initially
    gpio_set_level(PIN_MUX_SEL, 0);
    gpio_set_level(PIN_MUX_A0, 0);
    gpio_set_level(PIN_MUX_A1, 0);
    gpio_set_level(PIN_MUX_A2, 0);
    
    ESP_LOGI(TAG, "Display multiplexer initialized");
    return ESP_OK;
}

esp_err_t display_mux_select(uint8_t display_id) {
    if (display_id >= NUM_DISPLAYS) {
        ESP_LOGE(TAG, "Invalid display ID: %d", display_id);
        return ESP_ERR_INVALID_ARG;
    }
    
    /*
     * Multiplexer scheme:
     * - Displays 0-7: First 74HC138 (SEL=1)
     * - Displays 8-9: Second 74HC138 (SEL=0)
     * 
     * Address lines A0, A1, A2 select which output (0-7)
     */
    
    if (display_id < 8) {
        // First decoder (displays 0-7)
        gpio_set_level(PIN_MUX_SEL, 1);
        gpio_set_level(PIN_MUX_A0, (display_id >> 0) & 1);
        gpio_set_level(PIN_MUX_A1, (display_id >> 1) & 1);
        gpio_set_level(PIN_MUX_A2, (display_id >> 2) & 1);
    } else {
        // Second decoder (displays 8-9)
        gpio_set_level(PIN_MUX_SEL, 0);
        gpio_set_level(PIN_MUX_A0, (display_id - 8) & 1);
        gpio_set_level(PIN_MUX_A1, 0);
        gpio_set_level(PIN_MUX_A2, 0);
    }
    
    // Small delay for signal stabilization
    esp_rom_delay_us(1);
    
    return ESP_OK;
}
