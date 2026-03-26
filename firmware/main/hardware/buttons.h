/**
 * @file buttons.h
 * @brief Button driver with interrupt-based debouncing
 */

#ifndef BUTTONS_H
#define BUTTONS_H

#include <stdint.h>
#include "esp_err.h"
#include "profile/profile_types.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"

// Global button event queue
extern QueueHandle_t button_event_queue;

/**
 * @brief Initialize button driver
 * @return ESP_OK on success
 */
esp_err_t buttons_init(void);

/**
 * @brief Button processing task
 * @param arg Task argument (unused)
 */
void button_task(void* arg);

#endif // BUTTONS_H
