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
 * @brief Get the single profile data
 * @return Pointer to the profile
 */
profile_t* profile_get(void);

/**
 * @brief Get button configuration
 * @param button_id Button ID
 * @return Pointer to button config or NULL
 */
button_config_t* profile_get_button_config(uint8_t button_id);

esp_err_t profile_set_button_action(uint8_t button_id,
                                     uint8_t action_type, const uint8_t* action_data,
                                     uint16_t action_len);

/**
 * @brief Set long-press action for a button. folder_id=0xFF writes to root buttons.
 */
esp_err_t profile_set_button_long_press_action(uint8_t button_id, uint8_t folder_id,
                                                uint8_t action_type, const uint8_t* action_data,
                                                uint16_t action_len);

/**
 * @brief Set the label shown in the long-press section of the split display.
 *        folder_id=0xFF writes to root buttons. Empty string → firmware auto-generates.
 */
esp_err_t profile_set_button_long_press_name(uint8_t button_id, uint8_t folder_id, const char* name);

esp_err_t profile_set_button_name(uint8_t button_id, const char* name);

esp_err_t profile_set_folder_button_action(uint8_t folder_id,
                                            uint8_t button_id, uint8_t action_type,
                                            const uint8_t* action_data, uint16_t action_len);

esp_err_t profile_set_folder_button_name(uint8_t folder_id,
                                          uint8_t button_id, const char* name);

esp_err_t profile_set_folder_button_led(uint8_t folder_id,
                                         uint8_t button_id,
                                         uint8_t r, uint8_t g, uint8_t b,
                                         uint8_t brightness, uint8_t effect);


esp_err_t profile_set_led_color(uint8_t button_id,
                                 uint8_t r, uint8_t g, uint8_t b,
                                 uint8_t brightness, uint8_t effect);

/**
 * @brief Set encoder action for a specific slot
 * @param slot 0=CW, 1=CCW, 2=press short, 3=press long
 * @param action_type Action type
 * @param action_data Action data
 * @param action_len Action data length
 * @return ESP_OK on success
 */
esp_err_t profile_set_encoder_action(uint8_t slot, uint8_t action_type,
                                      const uint8_t* action_data, uint16_t action_len);

esp_err_t profile_save_to_storage(void);

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

/**
 * @brief Invalidate the PSRAM display cache for one button.
 *        Call after a new image is uploaded for that button so the next
 *        display refresh loads fresh data from storage.
 * @param button_id Button slot index (0..NUM_BUTTONS-1)
 * @param folder    true to invalidate the folder cache, false for the root cache
 */
void profile_image_cache_invalidate(uint8_t button_id, bool folder);

/**
 * @brief Store a pre-decoded RGB565 buffer directly into the image cache.
 *        Takes ownership of buf (freed on the next invalidation or update).
 *        Call immediately after a successful image transfer so the next display
 *        refresh uses the new image without a SPIFFS round-trip — prevents the
 *        "image disappears" race where profile_update_button_display loads the
 *        old SPIFFS image before save_task finishes writing the new one.
 * @param button_id  Button slot (0..NUM_BUTTONS-1)
 * @param folder     true = folder cache, false = root cache
 * @param buf        heap_caps_malloc'd RGB565 buffer (ownership transferred)
 */
void profile_image_cache_update(uint8_t button_id, bool folder, uint8_t* buf);

/**
 * @brief Refresh the display for one root button from current profile state.
 *        If image_size == 0, renders text; otherwise loads image from SPIFFS.
 */
void profile_refresh_button_display(uint8_t button_id);

#endif // PROFILE_MANAGER_H
