/**
 * @file packet_parser.h
 * @brief Protocol packet parser
 */

#ifndef PACKET_PARSER_H
#define PACKET_PARSER_H

#include <stdint.h>
#include "esp_err.h"
#include "protocol_types.h"

/**
 * @brief Parse and validate protocol packet
 * @param data Raw packet data
 * @param length Data length
 * @param packet Output parsed packet
 * @return ESP_OK on success
 */
esp_err_t packet_parse(const uint8_t* data, size_t length, protocol_packet_t* packet);

/**
 * @brief Build protocol packet
 * @param command_id Command ID
 * @param payload Payload data
 * @param payload_len Payload length
 * @param sequence Sequence number
 * @param packet Output packet
 * @return ESP_OK on success
 */
esp_err_t packet_build(uint8_t command_id, const uint8_t* payload, uint16_t payload_len,
                        uint16_t sequence, protocol_packet_t* packet);

/**
 * @brief Calculate packet checksum
 * @param packet Packet to calculate checksum for
 * @return Checksum value
 */
uint8_t packet_calculate_checksum(const protocol_packet_t* packet);

#endif // PACKET_PARSER_H
