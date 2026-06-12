/**
 * @file night_mode.h
 * @brief Night mode toggle — disables LEDs and display brightness without
 *        modifying the stored profile settings, and restores them on the next press.
 */

#ifndef NIGHT_MODE_H
#define NIGHT_MODE_H

#include <stdbool.h>
#include <stdint.h>

/**
 * @brief Toggle night mode.
 *        First call: saves current display brightness, turns off all LEDs except the trigger
 *        button (lit at 10% as indicator), sets display brightness to 0.
 *        Second call: restores saved brightness and re-applies LED colors from the current profile.
 * @param button_id  Button that triggered the toggle (kept lit as indicator during night mode).
 */
void night_mode_toggle(uint8_t button_id);

/**
 * @brief Return whether night mode is currently active.
 */
bool night_mode_is_active(void);

#endif // NIGHT_MODE_H
