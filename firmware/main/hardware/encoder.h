/**
 * @file encoder.h
 * @brief Rotary encoder driver
 */

#ifndef ENCODER_H
#define ENCODER_H

#include <stdint.h>
#include "esp_err.h"
#include "profile/profile_types.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"

// Global encoder event queue
extern QueueHandle_t encoder_event_queue;

/**
 * @brief Initialize encoder driver
 * @return ESP_OK on success
 */
esp_err_t encoder_init(void);

/**
 * @brief Encoder processing task
 * @param arg Task argument (unused)
 */
void encoder_task(void* arg);

#endif // ENCODER_H
