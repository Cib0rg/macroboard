/**
 * @file action_executor.h
 * @brief Button action executor
 */

#ifndef ACTION_EXECUTOR_H
#define ACTION_EXECUTOR_H

#include <stdint.h>
#include "esp_err.h"
#include "profile_types.h"

/**
 * @brief Execute button action (looks up action from current profile)
 * @param button_id Button ID
 * @return ESP_OK on success
 */
esp_err_t action_execute(uint8_t button_id);

/**
 * @brief Execute button long press action (looks up from current profile)
 * @param button_id Button ID
 * @return ESP_OK on success
 */
esp_err_t action_execute_long_press(uint8_t button_id);

/**
 * @brief Execute a raw action by type and data (used by encoder, sequences, etc.)
 * @param type Action type
 * @param data Action data
 * @param data_len Action data length
 * @return ESP_OK on success
 */
esp_err_t action_execute_raw(action_type_t type, const uint8_t* data, uint16_t data_len);

#endif // ACTION_EXECUTOR_H
