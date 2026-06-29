/**
 * @file plugin_display.c
 * @brief CMD_PLUGIN_DISPLAY (0x44): on-device ring + text rendering for plugin buttons.
 *
 * Replaces JPEG image transfer for plugin-driven display updates (e.g. Home Assistant).
 * A single 56-byte packet replaces ~6500 bytes of JPEG chunks; no SPIFFS write needed.
 *
 * Ring geometry matches the C# DrawRing helper (EllipsePolygon center_r=76, stroke=4):
 *   outer edge at radius 78 px, inner edge at radius 74 px from the display centre.
 */

#include "common.h"
#include "plugin_display.h"
#include "protocol_types.h"
#include "hardware/display_task.h"
#include "profile/profile_manager.h"
#include "utils/text_render.h"
#include "config.h"
#include <string.h>

static const char* TAG = "PLUGIN_DISP";

#define DISPLAY_W  160
#define DISPLAY_H  160

// Ring radii² — avoids sqrt in the inner loop.
// Matches C# DrawRing: EllipsePolygon(r=76, stroke=4) → pixels at 74..78 from centre.
#define RING_OUTER_R2  (78 * 78)  // 6084
#define RING_INNER_R2  (74 * 74)  // 5476

// Green  #22C55E → RGB565 LE = 0x262B  → big-endian bytes [0x26][0x2B]
// Purple #8B5CF6 → RGB565 LE = 0x8AFE  → big-endian bytes [0x8A][0xFE]
#define RING_COLOR_GREEN   0x262Bu
#define RING_COLOR_PURPLE  0x8AFEu

static void draw_ring_to_buf(uint8_t* buf, uint16_t ring_color) {
    const int cx = DISPLAY_W / 2;
    const int cy = DISPLAY_H / 2;
    const uint8_t hi = (uint8_t)((ring_color >> 8) & 0xFF);
    const uint8_t lo = (uint8_t)(ring_color & 0xFF);

    for (int y = 0; y < DISPLAY_H; y++) {
        int dy  = y - cy;
        int dy2 = dy * dy;
        for (int x = 0; x < DISPLAY_W; x++) {
            int dx = x - cx;
            int d2 = dx * dx + dy2;
            if (d2 >= RING_INNER_R2 && d2 <= RING_OUTER_R2) {
                int idx  = (y * DISPLAY_W + x) * 2;
                buf[idx]     = hi;
                buf[idx + 1] = lo;
            }
        }
    }
}

esp_err_t handle_plugin_display(const uint8_t* payload, uint16_t length,
                                  uint8_t* response, uint16_t* response_len) {
    *response_len = 1;

    // Payload: [button_id(1)][folder_id(1)][flags(1)][text_len(1)][text(0..52)]
    // folder_id 0xFF = root button; 0x00-0x0F = button inside that folder.
    if (length < 4) {
        response[0] = STATUS_ERROR;
        return ESP_ERR_INVALID_ARG;
    }

    uint8_t button_id = payload[0];
    uint8_t folder_id = payload[1];   // 0xFF = root
    uint8_t flags     = payload[2];
    uint8_t text_len  = payload[3];
    bool    is_on     = (flags & 0x01) != 0;

    if (button_id >= NUM_BUTTONS) {
        ESP_LOGW(TAG, "Invalid button_id %u", button_id);
        response[0] = STATUS_ERROR;
        return ESP_ERR_INVALID_ARG;
    }
    if (folder_id != 0xFF && folder_id >= NUM_FOLDERS) {
        ESP_LOGW(TAG, "Invalid folder_id %u", folder_id);
        response[0] = STATUS_ERROR;
        return ESP_ERR_INVALID_ARG;
    }

    char text[64] = {0};
    uint8_t copy_len = (text_len < 63) ? text_len : 63;
    if (copy_len > 0 && length >= (uint16_t)(4u + copy_len)) {
        memcpy(text, &payload[4], copy_len);
    }

    // Allocate RGB565 big-endian buffer in PSRAM (160 × 160 × 2 = 51200 bytes)
    uint8_t* buf = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_SPIRAM);
    if (buf == NULL) {
        ESP_LOGE(TAG, "OOM allocating display buffer for button %u", button_id);
        response[0] = STATUS_ERROR;
        return ESP_ERR_NO_MEM;
    }

    uint16_t ring_color = is_on ? RING_COLOR_GREEN : RING_COLOR_PURPLE;

    // 1. Fill entire frame: black background + centred white text
    text_render_fill_region(buf, DISPLAY_W, 0, DISPLAY_H, text, 0xFFFF, 0x0000);

    // 2. Overlay ring at display edges (minimal overlap with centred text)
    draw_ring_to_buf(buf, ring_color);

    // 3. Determine whether this button is currently visible on screen.
    //    A root button (folder_id=0xFF) is visible when the user is at root level.
    //    A folder button is visible only while that specific folder is open.
    bool is_folder_btn = (folder_id != 0xFF);
    uint8_t cur_folder = profile_get_current_folder();
    bool is_visible    = is_folder_btn
        ? (cur_folder == folder_id)
        : (cur_folder == 0xFF);

    // 4. Update the display cache for the correct navigation layer.
    //    Only write the folder cache when we are actually in that folder — the cache
    //    is shared across all folder buttons (one folder at a time), so writing it
    //    for a different folder than the one currently open would corrupt the cache.
    bool update_cache = !is_folder_btn || is_visible;
    if (update_cache) {
        uint8_t* cache_buf = heap_caps_malloc(DISPLAY_BUFFER_SIZE, MALLOC_CAP_SPIRAM);
        if (cache_buf) {
            memcpy(cache_buf, buf, DISPLAY_BUFFER_SIZE);
            profile_image_cache_update(button_id, is_folder_btn, cache_buf);
        }
    }

    // 5. Push to display task only when the button is currently visible.
    //    Hidden buttons (e.g. folder closed) should not overwrite whatever is on screen.
    //    The backend re-sends CMD_PLUGIN_DISPLAY on willAppear when the folder opens.
    if (is_visible) {
        esp_err_t ret = display_post_draw(button_id, buf);
        if (ret != ESP_OK) {
            ESP_LOGE(TAG, "display_post_draw failed for button %u", button_id);
            response[0] = STATUS_ERROR;
            return ret;
        }
    } else {
        free(buf);
    }

    ESP_LOGI(TAG, "button=%u folder=%s%u isOn=%d visible=%d text='%.32s'",
             button_id,
             is_folder_btn ? "F" : "root/", is_folder_btn ? folder_id : 0,
             (int)is_on, (int)is_visible, text);
    response[0] = STATUS_OK;
    return ESP_OK;
}
