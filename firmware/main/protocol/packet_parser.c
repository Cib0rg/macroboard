/**
 * @file packet_parser.c
 * @brief Protocol packet parser implementation
 */

#include "packet_parser.h"
#include "utils/crc.h"
#include "esp_log.h"
#include <string.h>

static const char* TAG = "PACKET";

uint8_t packet_calculate_checksum(const protocol_packet_t* packet) {
    return xor_checksum((const uint8_t*)packet, PROTOCOL_PACKET_SIZE - 2);
}

esp_err_t packet_parse(const uint8_t* data, size_t length, protocol_packet_t* packet) {
    if (data == NULL || packet == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (length != PROTOCOL_PACKET_SIZE) {
        ESP_LOGW(TAG, "Invalid packet length: %d (expected %d)", length, PROTOCOL_PACKET_SIZE);
        return ESP_ERR_INVALID_SIZE;
    }
    
    // Copy data to packet structure
    memcpy(packet, data, PROTOCOL_PACKET_SIZE);
    
    // Validate magic byte
    if (packet->magic != PROTOCOL_MAGIC_BYTE) {
        ESP_LOGW(TAG, "Invalid magic byte: 0x%02X", packet->magic);
        return ESP_ERR_INVALID_ARG;
    }
    
    // Validate end byte
    if (packet->end_byte != PROTOCOL_END_BYTE) {
        ESP_LOGW(TAG, "Invalid end byte: 0x%02X", packet->end_byte);
        return ESP_ERR_INVALID_ARG;
    }
    
    // Validate checksum
    uint8_t calculated_checksum = packet_calculate_checksum(packet);
    if (packet->checksum != calculated_checksum) {
        ESP_LOGW(TAG, "Checksum mismatch: got 0x%02X, expected 0x%02X", 
                 packet->checksum, calculated_checksum);
        return ESP_ERR_INVALID_CRC;
    }
    
    // Validate payload length
    if (packet->payload_length > PROTOCOL_PAYLOAD_SIZE) {
        ESP_LOGW(TAG, "Invalid payload length: %d", packet->payload_length);
        return ESP_ERR_INVALID_SIZE;
    }
    
    ESP_LOGD(TAG, "Packet parsed: cmd=0x%02X, seq=%d, len=%d", 
             packet->command_id, packet->sequence_number, packet->payload_length);
    
    return ESP_OK;
}

esp_err_t packet_build(uint8_t command_id, const uint8_t* payload, uint16_t payload_len,
                        uint16_t sequence, protocol_packet_t* packet) {
    if (packet == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    
    if (payload_len > PROTOCOL_PAYLOAD_SIZE) {
        return ESP_ERR_INVALID_SIZE;
    }
    
    // Clear packet
    memset(packet, 0, sizeof(protocol_packet_t));
    
    // Fill packet fields
    packet->magic = PROTOCOL_MAGIC_BYTE;
    packet->command_id = command_id;
    packet->payload_length = payload_len;
    packet->sequence_number = sequence;
    
    // Copy payload
    if (payload != NULL && payload_len > 0) {
        memcpy(packet->payload, payload, payload_len);
    }
    
    // Calculate checksum
    packet->checksum = packet_calculate_checksum(packet);
    
    // Set end byte
    packet->end_byte = PROTOCOL_END_BYTE;
    
    ESP_LOGD(TAG, "Packet built: cmd=0x%02X, seq=%d, len=%d", 
             command_id, sequence, payload_len);
    
    return ESP_OK;
}
