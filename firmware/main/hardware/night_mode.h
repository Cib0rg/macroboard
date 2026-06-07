/**
 * @file night_mode.h
 * @brief Night mode toggle — disables LEDs and display brightness without
 *        modifying the stored profile settings, and restores them on the next press.
 */

#ifndef NIGHT_MODE_H
#define NIGHT_MODE_H

#include <stdbool.h>

/**
 * @brief Toggle night mode.
 *        First call: saves current display brightness, turns off all LEDs, sets brightness to 0.
 *        Second call: restores saved brightness and re-applies LED colors from the current profile.
 */
void night_mode_toggle(void);

/**
 * @brief Return whether night mode is currently active.
 */
bool night_mode_is_active(void);

#endif // NIGHT_MODE_H
