/**
 * @file gc9a01.c
 * @brief GC9D01 display driver implementation (160x160 round TFT)
 * 
 * NOTE: Despite the filename "gc9a01", the actual display IC is GC9D01.
 * The initialization sequence is from the TZT manufacturer reference code,
 * verified working in arduino_gc9d01_test.
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

static const char* TAG = "GC9D01";

static spi_device_handle_t spi_device = NULL;
static SemaphoreHandle_t spi_mutex = NULL;

// ==================== Low-level SPI ====================

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

static void gc9d01_send_data_byte(uint8_t data) {
    gc9a01_write_data(&data, 1);
}

// ==================== GC9D01 Initialization ====================

/**
 * @brief Send the GC9D01 initialization sequence
 * 
 * This sequence is from the TZT manufacturer reference code.
 * It configures the display for 160x160 RGB565 operation.
 */
static void gc9d01_send_init_sequence(void) {
    // Enter inter-register command mode
    gc9a01_send_command(0xFE);
    gc9a01_send_command(0xEF);

    // Power/voltage registers (manufacturer-specific)
    gc9a01_send_command(0x80); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x81); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x82); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x83); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x84); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x85); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x86); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x87); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x88); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x89); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x8A); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x8B); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x8C); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x8D); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x8E); gc9d01_send_data_byte(0xFF);
    gc9a01_send_command(0x8F); gc9d01_send_data_byte(0xFF);

    // Pixel format: RGB565 (16-bit color)
    gc9a01_send_command(0x3A); gc9d01_send_data_byte(0x05);

    gc9a01_send_command(0xEC); gc9d01_send_data_byte(0x01);

    // Timing/gate registers
    {
        uint8_t data_74[] = {0x02, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00};
        gc9a01_send_command(0x74);
        gc9a01_write_data(data_74, sizeof(data_74));
    }

    gc9a01_send_command(0x98); gc9d01_send_data_byte(0x3E);
    gc9a01_send_command(0x99); gc9d01_send_data_byte(0x3E);

    {
        uint8_t data_b5[] = {0x0D, 0x0D};
        gc9a01_send_command(0xB5);
        gc9a01_write_data(data_b5, sizeof(data_b5));
    }

    // Source/gate timing
    {
        uint8_t data_60[] = {0x38, 0x0F, 0x79, 0x67};
        gc9a01_send_command(0x60);
        gc9a01_write_data(data_60, sizeof(data_60));
    }

    {
        uint8_t data_61[] = {0x38, 0x11, 0x79, 0x67};
        gc9a01_send_command(0x61);
        gc9a01_write_data(data_61, sizeof(data_61));
    }

    {
        uint8_t data_64[] = {0x38, 0x17, 0x71, 0x5F, 0x79, 0x67};
        gc9a01_send_command(0x64);
        gc9a01_write_data(data_64, sizeof(data_64));
    }

    {
        uint8_t data_65[] = {0x38, 0x13, 0x71, 0x5B, 0x79, 0x67};
        gc9a01_send_command(0x65);
        gc9a01_write_data(data_65, sizeof(data_65));
    }

    {
        uint8_t data_6a[] = {0x00, 0x00};
        gc9a01_send_command(0x6A);
        gc9a01_write_data(data_6a, sizeof(data_6a));
    }

    {
        uint8_t data_6c[] = {0x22, 0x02, 0x22, 0x02, 0x22, 0x22, 0x50};
        gc9a01_send_command(0x6C);
        gc9a01_write_data(data_6c, sizeof(data_6c));
    }

    {
        uint8_t data_6e[] = {
            0x03, 0x03, 0x01, 0x01, 0x00, 0x00, 0x0F, 0x0F,
            0x0D, 0x0D, 0x0B, 0x0B, 0x09, 0x09, 0x00, 0x00,
            0x00, 0x00, 0x0A, 0x0A, 0x0C, 0x0C, 0x0E, 0x0E,
            0x10, 0x10, 0x00, 0x00, 0x02, 0x02, 0x04, 0x04
        };
        gc9a01_send_command(0x6E);
        gc9a01_write_data(data_6e, sizeof(data_6e));
    }

    gc9a01_send_command(0xBF); gc9d01_send_data_byte(0x01);
    gc9a01_send_command(0xF9); gc9d01_send_data_byte(0x40);
    gc9a01_send_command(0x9B); gc9d01_send_data_byte(0x3B);

    {
        uint8_t data_93[] = {0x33, 0x7F, 0x00};
        gc9a01_send_command(0x93);
        gc9a01_write_data(data_93, sizeof(data_93));
    }

    gc9a01_send_command(0x7E); gc9d01_send_data_byte(0x30);

    {
        uint8_t data_70[] = {0x0D, 0x02, 0x08, 0x0D, 0x02, 0x08};
        gc9a01_send_command(0x70);
        gc9a01_write_data(data_70, sizeof(data_70));
    }

    {
        uint8_t data_71[] = {0x0D, 0x02, 0x08};
        gc9a01_send_command(0x71);
        gc9a01_write_data(data_71, sizeof(data_71));
    }

    {
        uint8_t data_91[] = {0x0E, 0x09};
        gc9a01_send_command(0x91);
        gc9a01_write_data(data_91, sizeof(data_91));
    }

    // VREG voltage control
    gc9a01_send_command(0xC3); gc9d01_send_data_byte(0x18);
    gc9a01_send_command(0xC4); gc9d01_send_data_byte(0x18);
    gc9a01_send_command(0xC9); gc9d01_send_data_byte(0x3C);

    // Gamma correction
    {
        uint8_t data_f0[] = {0x13, 0x15, 0x04, 0x05, 0x01, 0x38};
        gc9a01_send_command(0xF0);
        gc9a01_write_data(data_f0, sizeof(data_f0));
    }

    {
        uint8_t data_f2[] = {0x13, 0x15, 0x04, 0x05, 0x01, 0x34};
        gc9a01_send_command(0xF2);
        gc9a01_write_data(data_f2, sizeof(data_f2));
    }

    {
        uint8_t data_f1[] = {0x4B, 0xB8, 0x7B, 0x34, 0x35, 0xEF};
        gc9a01_send_command(0xF1);
        gc9a01_write_data(data_f1, sizeof(data_f1));
    }

    {
        uint8_t data_f3[] = {0x47, 0xB4, 0x72, 0x34, 0x35, 0xDA};
        gc9a01_send_command(0xF3);
        gc9a01_write_data(data_f3, sizeof(data_f3));
    }

    // MADCTL: 180° rotation, RGB order
    // Bit 7 (MY) = 1, Bit 6 (MX) = 1 → 180° rotation
    gc9a01_send_command(0x36); gc9d01_send_data_byte(0xC0);

    // Sleep Out
    gc9a01_send_command(0x11);
    vTaskDelay(pdMS_TO_TICKS(200));

    // Display ON
    gc9a01_send_command(0x29);

    // Memory Write (prepare for pixel data)
    gc9a01_send_command(0x2C);
}

// ==================== Public API ====================

esp_err_t gc9a01_init(void) {
    ESP_LOGI(TAG, "Initializing GC9D01 display driver");
    
    // Create mutex for SPI access
    spi_mutex = xSemaphoreCreateMutex();
    if (spi_mutex == NULL) {
        ESP_LOGE(TAG, "Failed to create SPI mutex");
        return ESP_FAIL;
    }
    
    // Configure DC, RST, and Backlight pins
    gpio_config_t io_conf = {
        .pin_bit_mask = (1ULL << PIN_SPI_DC) | (1ULL << PIN_SPI_RST) | (1ULL << PIN_DISPLAY_BACKLIGHT),
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
    
    // Add SPI device (CS controlled by multiplexer, not SPI peripheral)
    spi_device_interface_config_t dev_config = {
        .clock_speed_hz = SPI_CLOCK_SPEED_HZ,
        .mode = 0,                  // SPI Mode 0 (CPOL=0, CPHA=0)
        .spics_io_num = -1,         // CS controlled by multiplexer
        .queue_size = 7,
        .flags = 0,
    };
    
    ret = spi_bus_add_device(SPI_HOST, &dev_config, &spi_device);
    if (ret != ESP_OK) {
        ESP_LOGE(TAG, "Failed to add SPI device: %s", esp_err_to_name(ret));
        return ret;
    }
    
    // Perform a single hardware reset for all displays (RST is shared)
    ESP_LOGI(TAG, "Hardware reset (shared RST for all displays)");
    gpio_set_level(PIN_SPI_RST, 0);
    vTaskDelay(pdMS_TO_TICKS(100));
    gpio_set_level(PIN_SPI_RST, 1);
    vTaskDelay(pdMS_TO_TICKS(100));
    
    ESP_LOGI(TAG, "GC9D01 driver initialized (SPI @ %d MHz)", SPI_CLOCK_SPEED_HZ / 1000000);
    return ESP_OK;
}

esp_err_t gc9a01_init_display(uint8_t display_id) {
    if (display_id >= NUM_DISPLAYS) {
        return ESP_ERR_INVALID_ARG;
    }
    
    xSemaphoreTake(spi_mutex, portMAX_DELAY);
    
    // Select display via multiplexer
    display_mux_select(display_id);
    
    // NOTE: Hardware reset is NOT done here because RST is shared across
    // all displays. Resetting here would undo initialization of previously
    // initialized displays. The single shared reset is done in gc9a01_init().
    
    // Send GC9D01 initialization sequence
    gc9d01_send_init_sequence();
    
    xSemaphoreGive(spi_mutex);
    
    ESP_LOGI(TAG, "Display %d initialized (GC9D01 160x160)", display_id);
    return ESP_OK;
}

esp_err_t gc9a01_set_window(uint16_t x0, uint16_t y0, uint16_t x1, uint16_t y1) {
    uint8_t data[4];
    
    // Column address set (0x2A)
    gc9a01_send_command(GC9A01_CASET);
    data[0] = x0 >> 8;
    data[1] = x0 & 0xFF;
    data[2] = x1 >> 8;
    data[3] = x1 & 0xFF;
    gc9a01_write_data(data, 4);
    
    // Row address set (0x2B)
    gc9a01_send_command(GC9A01_RASET);
    data[0] = y0 >> 8;
    data[1] = y0 & 0xFF;
    data[2] = y1 >> 8;
    data[3] = y1 & 0xFF;
    gc9a01_write_data(data, 4);
    
    // Memory Write (0x2C)
    gc9a01_send_command(GC9A01_RAMWR);
    
    return ESP_OK;
}

esp_err_t gc9a01_clear(uint8_t display_id, uint16_t color) {
    if (display_id >= NUM_DISPLAYS) {
        return ESP_ERR_INVALID_ARG;
    }
    
    ESP_LOGW(TAG, "DEBUG clear: display_id=%d, color=0x%04X", display_id, color);
    
    xSemaphoreTake(spi_mutex, portMAX_DELAY);
    
    display_mux_select(display_id);
    gc9a01_set_window(0, 0, DISPLAY_WIDTH - 1, DISPLAY_HEIGHT - 1);
    
    // Prepare color buffer (one row at a time for DMA)
    uint16_t* buffer = heap_caps_malloc(DISPLAY_WIDTH * 2, MALLOC_CAP_DMA);
    if (buffer == NULL) {
        xSemaphoreGive(spi_mutex);
        return ESP_ERR_NO_MEM;
    }
    
    // Fill buffer with color (byte-swapped for big-endian SPI transfer)
    uint16_t swapped = __builtin_bswap16(color);
    for (int i = 0; i < DISPLAY_WIDTH; i++) {
        buffer[i] = swapped;
    }
    
    // Write all rows
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
    
    ESP_LOGW(TAG, "DEBUG draw_image: display_id=%d, %dx%d, first_pixel=0x%02X%02X",
             display_id, width, height, image_data[0], image_data[1]);
    
    xSemaphoreTake(spi_mutex, portMAX_DELAY);
    
    display_mux_select(display_id);
    
    // Center the image on the display
    uint16_t x0 = (DISPLAY_WIDTH - width) / 2;
    uint16_t y0 = (DISPLAY_HEIGHT - height) / 2;
    
    gc9a01_set_window(x0, y0, x0 + width - 1, y0 + height - 1);
    
    // Write image data row by row to avoid exceeding SPI DMA max transfer size.
    // Full image (160x160x2 = 51200 bytes) exceeds the hardware limit.
    size_t row_bytes = width * 2;  // 2 bytes per pixel (RGB565)
    for (uint16_t row = 0; row < height; row++) {
        gc9a01_write_data(image_data + (row * row_bytes), row_bytes);
    }
    
    xSemaphoreGive(spi_mutex);
    
    return ESP_OK;
}
