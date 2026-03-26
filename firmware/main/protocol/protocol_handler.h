/**
 * @file protocol_handler.h
 * @brief Protocol command handler
 */

#ifndef PROTOCOL_HANDLER_H
#define PROTOCOL_HANDLER_H

#include <stdint.h>
#include "esp_err.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"

// Global protocol command queue
extern QueueHandle_t protocol_cmd_queue;

/**
 * @brief Initialize protocol handler
 * @return ESP_OK on success
 */
esp_err_t protocol_handler_init(void);

/**
 * @brief Protocol processing task
 * @param arg Task argument (unused)
 */
void protocol_task(void* arg);

/**
 * @brief Handle incoming packet
 * @param data Packet data
 * @param length Packet length
 * @return ESP_OK on success
 */
esp_err_t protocol_handle_packet(const uint8_t* data, size_t length);

/**
 * @brief Send response packet
 * @param command_id Command ID
 * @param payload Response payload
 * @param payload_len Payload length
 * @return ESP_OK on success
 */
esp_err_t protocol_send_response(uint8_t command_id, const uint8_t* payload, uint16_t payload_len);

/**
 * @brief Send event to PC
 * @param event_id Event ID
 * @param payload Event payload
 * @param payload_len Payload length
 * @return ESP_OK on success
 */
esp_err_t protocol_send_event(uint8_t event_id, const uint8_t* payload, uint16_t payload_len);

#endif // PROTOCOL_HANDLER_H
