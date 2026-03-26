/**
 * @file action_executor.h
 * @brief Button action executor
 */

#ifndef ACTION_EXECUTOR_H
#define ACTION_EXECUTOR_H

#include <stdint.h>
#include "esp_err.h"

/**
 * @brief Execute button action
 * @param button_id Button ID
 * @return ESP_OK on success
 */
esp_err_t action_execute(uint8_t button_id);

#endif // ACTION_EXECUTOR_H
