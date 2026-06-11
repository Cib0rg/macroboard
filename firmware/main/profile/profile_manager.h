/**
 * @file profile_manager.h
 * @brief Profile manager
 */

#ifndef PROFILE_MANAGER_H
#define PROFILE_MANAGER_H

#include <stdint.h>
#include "esp_err.h"
#include <stdbool.h>
#include "profile_types.h"

/**
 * @brief Initialize profile manager
 * @return ESP_OK on success
 */
esp_err_t profile_manager_init(void);

/**
 * @brief Switch to different profile
 * @param profile_id Profile ID to switch to
 * @return ESP_OK on success
 */
esp_err_t profile_switch(uint8_t profile_id);

/**
 * @brief Get current profile ID
 * @return Current profile ID
 */
uint8_t profile_get_current_id(void);

/**
 * @brief Get profile data
 * @param profile_id Profile ID
 * @return Pointer to profile or NULL
 */
profile_t* profile_get(uint8_t profile_id);

/**
 * @brief Get button configuration
 * @param button_id Button ID
 * @return Pointer to button config or NULL
 */
button_config_t* profile_get_button_config(uint8_t button_id);

/**
 * @brief Set button action
 * @param profile_id Profile ID
 * @param button_id Button ID
 * @param action_type Action type
 * @param action_data Action data
 * @param action_len Action data length
 * @return ESP_OK on success
 */
esp_err_t profile_set_button_action(uint8_t profile_id, uint8_t button_id,
                                     uint8_t action_type, const uint8_t* action_data,
                                     uint16_t action_len);

/**
 * @brief Set display name for a button (shown when no image is assigned)
 * @param profile_id Profile ID
 * @param button_id  Button ID
 * @param name       Null-terminated UTF-8 string (max BUTTON_NAME_MAX_LEN-1 chars)
 * @return ESP_OK on success
 */
esp_err_t profile_set_button_name(uint8_t profile_id, uint8_t button_id, const char* name);

esp_err_t profile_set_folder_button_action(uint8_t profile_id, uint8_t folder_id,
                                            uint8_t button_id, uint8_t action_type,
                                            const uint8_t* action_data, uint16_t action_len);

esp_err_t profile_set_folder_button_name(uint8_t profile_id, uint8_t folder_id,
                                          uint8_t button_id, const char* name);

esp_err_t profile_set_folder_button_led(uint8_t profile_id, uint8_t folder_id,
                                         uint8_t button_id,
                                         uint8_t r, uint8_t g, uint8_t b,
                                         uint8_t brightness, uint8_t effect);

/**
 * @brief Set LED color for button
 * @param profile_id Profile ID
 * @param button_id Button ID
 * @param r Red component
 * @param g Green component
 * @param b Blue component
 * @param brightness Brightness
 * @param effect LED effect
 * @return ESP_OK on success
 */
esp_err_t profile_set_led_color(uint8_t profile_id, uint8_t button_id,
                                 uint8_t r, uint8_t g, uint8_t b,
                                 uint8_t brightness, uint8_t effect);

/**
 * @brief Save profile to storage
 * @param profile_id Profile ID
 * @return ESP_OK on success
 */
esp_err_t profile_save_to_storage(uint8_t profile_id);

/**
 * @brief Create default profiles
 * @return ESP_OK on success
 */
esp_err_t profile_create_defaults(void);

/**
 * @brief Enter a folder (push to folder stack)
 * @param folder_id       Folder ID to enter
 * @param entry_button_id Button that was pressed to enter (will show the back icon)
 * @return ESP_OK on success
 */
esp_err_t profile_folder_enter(uint8_t folder_id, uint8_t entry_button_id);

/**
 * @brief Exit current folder (pop from folder stack)
 * @return ESP_OK on success
 */
esp_err_t profile_folder_exit(void);

/**
 * @brief Get current folder ID
 * @return Current folder ID (0xFF if at root level)
 */
uint8_t profile_get_current_folder(void);

/**
 * @brief Check if currently inside a folder
 * @return true if inside folder, false if at root
 */
bool profile_is_in_folder(void);

/**
 * @brief Get folder depth (0 = root, 1+ = inside folders)
 * @return Current folder depth
 */
uint8_t profile_get_folder_depth(void);

/**
 * @brief Re-apply LED colors from the current profile to hardware without
 *        modifying stored settings.  Used by night mode to restore LEDs.
 */
void profile_restore_leds(void);

/**
 * @brief Refresh all button displays from the current profile's image/text data.
 *        Call after any profile switch so stale images from the previous profile
 *        are not left on screen.
 */
void profile_refresh_displays(void);

#endif // PROFILE_MANAGER_H
