/**
 * @file gc9a01.c
 * @brief GC9A01 display driver implementation
 */

#include "gc9a01.h"
#include "display_mux.h"
#include "config.h"
#include "esp_log.h"
#include "driver/gpio.h"
#include "driver/spi_master.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include <string.h>

static const char* TAG = "GC9A01";

static spi_device_handle_t spi_device = NULL;
static SemaphoreHandle_t spi_mutex = NULL;
static bool backlight_enabled = true;

// Initialization sequence for GC9A01
static const uint8_t gc9a01_init_sequence[] = {
    0xEF, 0,
    0xEB, 1, 0x14,
    
    0xFE, 0,
    0xEF, 0,
    
    0xEB, 1, 0x14,
    0x84, 1, 0x40,
    0x85, 1, 0xFF,
    0x86, 1, 0xFF,
    0x87, 1, 0xFF,
    0x88, 1, 0x0A,
    0x89, 1, 0x21,
    0x8A, 1, 0x00,
    0x8B, 1, 0x80,
    0x8C, 1, 0x01,
    0x8D, 1, 0x01,
    0x8E, 1, 0xFF,
    0x8F, 1, 0xFF,
    
    0xB6, 2, 0x00, 0x00,
    
    0x36, 1, 0x48,  // MADCTL: RGB order
    
    0x3A, 1, 0x05,  // COLMOD: 16-bit color
    
    0x90, 4, 0x08, 0x08, 0x08, 0x08,
    0xBD, 1, 0x06,
    0xBC, 1, 0x00,
    0xFF, 3, 0x60, 0x01, 0x04,
    
    0xC3, 1, 0x13,
    0xC4, 1, 0x13,
    0xC9, 1, 0x22,
    
    0xBE, 1, 0x11,
    0xE1, 2, 0x10, 0x0E,
    
    0xDF, 3, 0x21, 0x0C, 0x02,
    
    0xF0, 6, 0x45, 0x09, 0x08, 0x08, 0x26, 0x2A,
    0xF1, 6, 0x43, 0x70, 0x72, 0x36, 0x37, 0x6F,
    0xF2, 6, 0x45, 0x09, 0x08, 0x08, 0x26, 0x2A,
    0xF3, 6, 0x43, 0x70, 0x72, 0x36, 0x37, 0x6F,
    
    0xED, 2, 0x1B, 0x0B,
    0xAE, 1, 0x77,
    0xCD, 1, 0x63,
    
    0x70, 9, 0x07, 0x07, 0x04, 0x0E, 0x0F, 0x09, 0x07, 0x08, 0x03,
    
    0xE8, 1, 0x34,
    
    0x62, 12, 0x18, 0x0D, 0x71, 0xED, 0x70, 0x70, 0x18, 0x0F, 0x71, 0xEF, 0x70, 0x70,
    0x63, 12, 0x18, 0x11, 0x71, 0xF1, 0x70, 0x70, 0x18, 0x13, 0x71, 0xF3, 0x70, 0x70,
    0x64, 7, 0x28, 0x29, 0xF1, 0x01, 0xF1, 0x00, 0x07,
    
    0x66, 10, 0x3C, 0x00, 0xCD, 0x67, 0x45, 0x45, 0x10, 0x00, 0x00, 0x00,
    0x67, 10, 0x00, 0x3C, 0x00, 0x00, 0x00, 0x01, 0x54, 0x10, 0x32, 0x98,
    0x74, 7, 0x10, 0x85, 0x80, 0x00, 0x00, 0x4E, 0x00,
    
    0x98, 2, 0x3E, 0x07,
    
    0x35, 0,  // Tearing effect line on
    0x21, 0,  // Display inversion on
    
    0x11, 0,  // Sleep out
    0xFF, 0xFF, 120,  // Delay 120ms
    
    0x29, 0,  // Display on
    0xFF, 0xFF, 20,   // Delay 20ms
    
    0x00  // End of sequence
};

esp_err_t gc9a01_send_command(uint8_t cmd) {
    gpio_set_level(PIN_SPI_DC, 0);  // Command mode
    
    spi_transaction_t trans = {
        .length = 8,
        .tx_buffer = &cmd,
        .flags = 0,
    };
    
    return spi_device_transmit(spi_device, &trans);
}

esp_err_t gc9a01_write_data(const uint8_t* data, size_t len) {
    if (len == 0) return ESP_OK;
    
    gpio_set_level(PIN_SPI_DC, 1);  // Data mode
    
    spi_transaction_t trans = {
        .length = len * 8,
        .tx_buffer = data,
        .flags = 0,
    };
    
    return spi_device_transmit(spi_device, &trans);
}

esp_err_t gc9a01_init(void) {
    ESP_LOGI(TAG, "Initializing GC9A01 driver");
    
    // Create mutex for SPI access
    spi_mutex = xSemaphoreCreateMutex();
    if (spi_mutex == NULL) {
        ESP_LOGE(TAG, "Failed to create SPI mutex");
        return ESP_FAIL;
    }
    
    // Configure DC and RST pins
    gpio_config_t io_conf = {
        .pin_bit_mask = (1ULL << PIN_SPI_DC) | (1ULL << PIN_SPI_RST),
        .mode = GPIO_MODE_OUTPUT,
        .pull_up_en = GPIO_PULLUP_DISABLE,
        .pull_down_en = GPIO_PULLDOWN_DISABLE,
        .intr_type = GPIO_INTR_DISABLE,
    };
    gpio_config(&io_conf);
    
    // Initialize SPI bus
    spi_bus_config_t bus_config = {
        .mosi_io_num = PIN_SPI_MOSI,
        .miso_io_num = -1,
        .sclk_io_num = PIN_SPI_CLK,
        .quadwp_io_num = -1,
        .quadhd_io_num = -1,
        .max_transfer_sz = DISPLAY_BUFFER_SIZE + 8,
    };
    
    esp_err_t ret = spi_bus_initialize(SPI_HOST, &bus_config, SPI_DMA_CHAN);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to initialize SPI bus: %s", esp_err_to_name(ret));
        return ret;
    }
    
    // Add SPI device
    spi_device_interface_config_t dev_config = {
        .clock_speed_hz = SPI_CLOCK_SPEED_HZ,
        .mode = 0,
        .spics_io_num = -1,  // CS controlled by multiplexer
        .queue_size = 7,
        .flags = 0,
    };
    
    ret = spi_bus_add_device(SPI_HOST, &dev_config, &spi_device);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to add SPI device: %s", esp_err_to_name(ret));
        return ret;
    }
    
    ESP_LOGI(TAG, "GC9A01 driver initialized");
    return ESP_OK;
}

esp_err_t gc9a01_set_backlight(bool enabled) {
    backlight_enabled = enabled;
    
    if (enabled) {
        gpio_set_level(PIN_DISPLAY_BACKLIGHT, 1);
        ESP_LOGI(TAG, "Backlight enabled");
    } else {
        gpio_set_level(PIN_DISPLAY_BACKLIGHT, 0);
        ESP_LOGI(TAG, "Backlight disabled");
    }
    
    return ESP_OK;
}

esp_err_t gc9a01_set_brightness(uint8_t brightness) {
    // Simple on/off control via GPIO
    // For full PWM control, would need LEDC configuration
    if (brightness > 127) {
        return gc9a01_set_backlight(true);
    } else if (brightness == 0) {
        return gc9a01_set_backlight(false);
    }
    
    return ESP_OK;
}

esp_err_t gc9a01_init_display(uint8_t display_id) {
    if (display_id >= NUM_DISPLAYS) {
        return ESP_ERR_INVALID_ARG;
    }
    
    xSemaphoreTake(spi_mutex, portMAX_DELAY);
    
    // Select display
    display_mux_select(display_id);
    
    // Hardware reset
    gpio_set_level(PIN_SPI_RST, 0);
    vTaskDelay(pdMS_TO_TICKS(10));
    gpio_set_level(PIN_SPI_RST, 1);
    vTaskDelay(pdMS_TO_TICKS(120));
    
    // Send initialization sequence
    const uint8_t* cmd = gc9a01_init_sequence;
    while (*cmd != 0x00) {
        uint8_t command = *cmd++;
        uint8_t num_args = *cmd++;
        
        if (command == 0xFF && num_args == 0xFF) {
            // Delay command
            uint8_t delay_ms = *cmd++;
            vTaskDelay(pdMS_TO_TICKS(delay_ms));
        } else {
            gc9a01_send_command(command);
            if (num_args > 0) {
                gc9a01_write_data(cmd, num_args);
                cmd += num_args;
            }
        }
    }
    
    xSemaphoreGive(spi_mutex);
    
    ESP_LOGI(TAG, "Display %d initialized", display_id);
    return ESP_OK;
}

esp_err_t gc9a01_set_window(uint16_t x0, uint16_t y0, uint16_t x1, uint16_t y1) {
    uint8_t data[4];
    
    // Column address set
    gc9a01_send_command(GC9A01_CASET);
    data[0] = x0 >> 8;
    data[1] = x0 & 0xFF;
    data[2] = x1 >> 8;
    data[3] = x1 & 0xFF;
    gc9a01_write_data(data, 4);
    
    // Row address set
    gc9a01_send_command(GC9A01_RASET);
    data[0] = y0 >> 8;
    data[1] = y0 & 0xFF;
    data[2] = y1 >> 8;
    data[3] = y1 & 0xFF;
    gc9a01_write_data(data, 4);
    
    // Write to RAM
    gc9a01_send_command(GC9A01_RAMWR);
    
    return ESP_OK;
}

esp_err_t gc9a01_clear(uint8_t display_id, uint16_t color) {
    if (display_id >= NUM_DISPLAYS) {
        return ESP_ERR_INVALID_ARG;
    }
    
    xSemaphoreTake(spi_mutex, portMAX_DELAY);
    
    display_mux_select(display_id);
    gc9a01_set_window(0, 0, DISPLAY_WIDTH - 1, DISPLAY_HEIGHT - 1);
    
    // Prepare color buffer
    uint16_t* buffer = heap_caps_malloc(DISPLAY_WIDTH * 2, MALLOC_CAP_DMA);
    if (buffer == NULL) {
        xSemaphoreGive(spi_mutex);
        return ESP_ERR_NO_MEM;
    }
    
    for (int i = 0; i < DISPLAY_WIDTH; i++) {
        buffer[i] = __builtin_bswap16(color);  // Swap bytes for RGB565
    }
    
    // Write rows
    for (int y = 0; y < DISPLAY_HEIGHT; y++) {
        gc9a01_write_data((uint8_t*)buffer, DISPLAY_WIDTH * 2);
    }
    
    free(buffer);
    xSemaphoreGive(spi_mutex);
    
    return ESP_OK;
}

esp_err_t gc9a01_draw_image(uint8_t display_id, const uint8_t* image_data,
                             uint16_t width, uint16_t height) {
    if (display_id >= NUM_DISPLAYS || image_data == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (width > DISPLAY_WIDTH || height > DISPLAY_HEIGHT) {
        return ESP_ERR_INVALID_SIZE;
    }
    
    xSemaphoreTake(spi_mutex, portMAX_DELAY);
    
    display_mux_select(display_id);
    
    uint16_t x0 = (DISPLAY_WIDTH - width) / 2;
    uint16_t y0 = (DISPLAY_HEIGHT - height) / 2;
    
    gc9a01_set_window(x0, y0, x0 + width - 1, y0 + height - 1);
    
    // Write image data
    gc9a01_write_data(image_data, width * height * 2);
    
    xSemaphoreGive(spi_mutex);
    
    return ESP_OK;
}
