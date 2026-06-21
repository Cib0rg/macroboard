#pragma once

#include "esp_err.h"
#include <stdint.h>

/**
 * Initialise the display command queue.
 * Must be called before display_task is created.
 */
esp_err_t display_task_init(void);

/**
 * Post a draw command to the display task (Core 1).
 *
 * The display_task takes ownership of rgb565_buf and frees it after the draw
 * (or immediately if the queue is full).  The caller must NOT free the buffer
 * after a successful or failed post.
 *
 * @param button_id   Physical display index (0..NUM_BUTTONS-1)
 * @param rgb565_buf  PSRAM-allocated RGB565 big-endian buffer (DISPLAY_BUFFER_SIZE bytes)
 * @return ESP_OK if queued, ESP_FAIL if queue full (buffer freed by callee)
 */
esp_err_t display_post_draw(uint8_t button_id, uint8_t* rgb565_buf);

/**
 * FreeRTOS task function.  Pin to Core 1 with TASK_PRIORITY_DISPLAY.
 */
void display_task(void* arg);
