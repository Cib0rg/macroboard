/**
 * @file protocol_types.h
 * @brief Protocol data types and command definitions
 */

#ifndef PROTOCOL_TYPES_H
#define PROTOCOL_TYPES_H

#include <stdint.h>

// Protocol constants
#define PROTOCOL_MAGIC_BYTE     0xA5
#define PROTOCOL_END_BYTE       0x5A
#define PROTOCOL_PAYLOAD_SIZE   56
#define PROTOCOL_PACKET_SIZE    64

// Command IDs from PC to device
#define CMD_PING                    0x01
#define CMD_GET_DEVICE_INFO         0x02
#define CMD_SET_PROFILE             0x10
#define CMD_GET_PROFILE_INFO        0x11
#define CMD_START_IMAGE_TRANSFER    0x20
#define CMD_IMAGE_DATA_CHUNK        0x21
#define CMD_END_IMAGE_TRANSFER      0x22
#define CMD_GET_BUTTON_IMAGE        0x23
#define CMD_SET_BUTTON_ACTION       0x30
#define CMD_GET_BUTTON_ACTION       0x31
#define CMD_SET_BUTTON_NAME         0x32
#define CMD_SET_LED_COLOR           0x40
#define CMD_GET_LED_COLOR           0x42
#define CMD_SET_BACKLIGHT           0x41
#define CMD_SAVE_PROFILE            0x50
#define CMD_LOAD_PROFILE            0x51
#define CMD_DELETE_PROFILE          0x52
#define CMD_START_OTA_UPDATE        0x60
#define CMD_GET_OTA_STATUS          0x61
#define CMD_SET_WIFI_CREDENTIALS    0x70
#define CMD_GET_WIFI_STATUS         0x71
#define CMD_ENABLE_DEBUG_LOG        0x80
#define CMD_FACTORY_RESET           0x81

// Event IDs from device to PC
#define EVENT_BUTTON_PRESSED        0xF0
#define EVENT_ENCODER_ROTATED       0xF1
#define EVENT_ENCODER_BUTTON        0xF2
#define EVENT_PROFILE_CHANGED       0xF3
#define EVENT_DEVICE_READY          0xF4
#define EVENT_FOLDER_ENTERED        0xF5
#define EVENT_FOLDER_EXITED         0xF6
#define EVENT_ERROR                 0xFF

// Status codes
#define STATUS_OK                   0x00
#define STATUS_ERROR                0xFF
#define STATUS_RETRY                0x01

// Packet structure
typedef struct __attribute__((packed)) {
    uint8_t magic;              // 0xA5
    uint8_t command_id;
    uint16_t payload_length;
    uint16_t sequence_number;
    uint8_t payload[PROTOCOL_PAYLOAD_SIZE];
    uint8_t checksum;
    uint8_t end_byte;           // 0x5A
} protocol_packet_t;

// Command handler function type
typedef esp_err_t (*command_handler_t)(const uint8_t* payload, uint16_t length, 
                                        uint8_t* response, uint16_t* response_len);

#endif // PROTOCOL_TYPES_H
