/**
 * @file display_mux.c
 * @brief Display multiplexer implementation using two 74HC138 decoders
 * 
 * Hardware scheme:
 *   - Two 74HC138 3-to-8 decoders, each driving 5 displays (outputs 0-4 used)
 *   - Decoder 1 (SEL=1): Displays 0-4 (top row)
 *   - Decoder 2 (SEL=0): Displays 5-9 (bottom row)
 *   - Address lines A0, A1, A2 select which output (0-4) within the decoder
 *   - SEL pin selects which decoder is active
 */

#include "common.h"
#include "display_mux.h"
#include "config.h"
#include "driver/gpio.h"

static const char* TAG = "DISP_MUX";

esp_err_t display_mux_init(void) {
    ESP_LOGI(TAG, "Initializing display multiplexer (2x 74HC138, 5+5 displays)");
    
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
     * Multiplexer scheme (5+5):
     *   - Displays 0-4: First 74HC138 decoder (SEL=1), outputs 0-4
     *   - Displays 5-9: Second 74HC138 decoder (SEL=0), outputs 0-4
     * 
     * Address lines A0, A1, A2 select which output (0-4) within the active decoder.
     * Only outputs 0-4 are wired; outputs 5-7 of each decoder are unused.
     */
    
    uint8_t local_id;  // Output index within the decoder (0-4)
    
    if (display_id < 5) {
        // First decoder (displays 0-4)
        gpio_set_level(PIN_MUX_SEL, 1);
        local_id = display_id;
    } else {
        // Second decoder (displays 5-9)
        gpio_set_level(PIN_MUX_SEL, 0);
        local_id = display_id - 5;
    }
    
    gpio_set_level(PIN_MUX_A0, (local_id >> 0) & 1);
    gpio_set_level(PIN_MUX_A1, (local_id >> 1) & 1);
    gpio_set_level(PIN_MUX_A2, (local_id >> 2) & 1);
    
    // Small delay for signal stabilization
    esp_rom_delay_us(1);
    
    // DEBUG: Read back actual GPIO levels to verify mux addressing
    int actual_a0 = gpio_get_level(PIN_MUX_A0);
    int actual_a1 = gpio_get_level(PIN_MUX_A1);
    int actual_a2 = gpio_get_level(PIN_MUX_A2);
    int actual_sel = gpio_get_level(PIN_MUX_SEL);
    ESP_LOGW(TAG, "MUX SELECT display_id=%d -> local_id=%d, SEL=%d, A2=%d A1=%d A0=%d (readback: SEL=%d A2=%d A1=%d A0=%d)",
             display_id, local_id,
             (display_id < 5) ? 1 : 0, (local_id >> 2) & 1, (local_id >> 1) & 1, (local_id >> 0) & 1,
             actual_sel, actual_a2, actual_a1, actual_a0);
    
    return ESP_OK;
}
