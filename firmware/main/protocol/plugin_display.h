/**
 * @file plugin_display.h
 * @brief CMD_PLUGIN_DISPLAY (0x44): on-device ring + text rendering for plugin buttons.
 *
 * Payload: [button_id(1)][flags(1)][text_len(1)][text(0..53)]
 *   flags bit 0: isOn — 1 = green ring (#22C55E), 0 = purple ring (#8B5CF6)
 *
 * The firmware renders a solid ring and centered text into a PSRAM buffer, updates
 * the root display cache (so profile_refresh_displays doesn't wipe the plugin state),
 * and posts the buffer to the display task. No SPIFFS write.
 */

#pragma once

#include <stdint.h>
#include "esp_err.h"

esp_err_t handle_plugin_display(const uint8_t* payload, uint16_t length,
                                  uint8_t* response, uint16_t* response_len);
