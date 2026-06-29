/**
 * @file plugin_display.h
 * @brief CMD_PLUGIN_DISPLAY (0x44): on-device ring + text rendering for plugin buttons.
 *
 * Payload: [button_id(1)][folder_id(1)][flags(1)][text_len(1)][text(0..52)]
 *   folder_id: 0xFF = root button; 0x00-0x0F = button inside that folder
 *   flags bit 0: isOn — 1 = green ring (#22C55E), 0 = purple ring (#8B5CF6)
 *
 * Firmware renders a solid ring and centered text into a PSRAM buffer, updates
 * the correct display cache layer (root or folder), and posts the buffer to the
 * display task only when the button is currently visible. Hidden folder buttons
 * are cached but not drawn — the backend re-sends on willAppear (folder open). No SPIFFS write.
 */

#pragma once

#include <stdint.h>
#include "esp_err.h"

esp_err_t handle_plugin_display(const uint8_t* payload, uint16_t length,
                                  uint8_t* response, uint16_t* response_len);
