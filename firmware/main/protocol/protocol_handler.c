/**
 * @file protocol_handler.c
 * @brief Protocol command handler implementation
 */

#include "common.h"
#include "protocol_handler.h"
#include "packet_parser.h"
#include "protocol_types.h"
#include "image_transfer.h"
#include "config.h"
#include "profile/profile_manager.h"
#include "storage/profile_storage.h"
#include "usb/usb_hid_raw.h"

static const char* TAG = "PROTOCOL";

QueueHandle_t protocol_cmd_queue = NULL;
static uint16_t response_sequence = 0;

// Forward declarations of command handlers
static esp_err_t handle_ping(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);
static esp_err_t handle_get_device_info(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);
static esp_err_t handle_set_profile(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);
static esp_err_t handle_get_profile_info(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);
static esp_err_t handle_start_image_transfer(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);
static esp_err_t handle_image_data_chunk(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);
static esp_err_t handle_end_image_transfer(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);
static esp_err_t handle_set_button_action(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);
static esp_err_t handle_set_led_color(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);
static esp_err_t handle_set_backlight(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);
static esp_err_t handle_save_profile(const uint8_t* payload, uint16_t length, uint8_t* response, uint16_t* response_len);

// Command handler table
typedef struct {
    uint8_t command_id;
    command_handler_t handler;
} command_entry_t;

static const command_entry_t command_table[] = {
    {CMD_PING, handle_ping},
    {CMD_GET_DEVICE_INFO, handle_get_device_info},
    {CMD_SET_PROFILE, handle_set_profile},
    {CMD_GET_PROFILE_INFO, handle_get_profile_info},
    {CMD_START_IMAGE_TRANSFER, handle_start_image_transfer},
    {CMD_IMAGE_DATA_CHUNK, handle_image_data_chunk},
    {CMD_END_IMAGE_TRANSFER, handle_end_image_transfer},
    {CMD_SET_BUTTON_ACTION, handle_set_button_action},
    {CMD_SET_LED_COLOR, handle_set_led_color},
    {CMD_SET_BACKLIGHT, handle_set_backlight},
    {CMD_SAVE_PROFILE, handle_save_profile},
};

esp_err_t protocol_handler_init(void) {
    ESP_LOGI(TAG, "Initializing protocol handler");
    
    protocol_cmd_queue = xQueueCreate(5, sizeof(protocol_packet_t));
    if (protocol_cmd_queue == NULL) {
        ESP_LOGE(TAG, "Failed to create protocol command queue");
        return ESP_FAIL;
    }
    
    ESP_LOGI(TAG, "Protocol handler initialized");
    return ESP_OK;
}

esp_err_t protocol_handle_packet(const uint8_t* data, size_t length) {
    protocol_packet_t packet;
    
    esp_err_t ret = packet_parse(data, length, &packet);
    if (ret != ESP_OK) {
        ESP_LOGW(TAG, "Failed to parse packet: %s", esp_err_to_name(ret));
        return ret;
    }
    
    // Send to protocol task queue
    if (xQueueSend(protocol_cmd_queue, &packet, pdMS_TO_TICKS(100)) != pdTRUE) {
        ESP_LOGW(TAG, "Protocol queue full, dropping packet");
        return ESP_ERR_TIMEOUT;
    }
    
    return ESP_OK;
}

esp_err_t protocol_send_response(uint8_t command_id, const uint8_t* payload, uint16_t payload_len) {
    protocol_packet_t packet;
    
    esp_err_t ret = packet_build(command_id, payload, payload_len, response_sequence++, &packet);
    if (ret != ESP_OK) {
        return ret;
    }
    
    return usb_hid_raw_send((uint8_t*)&packet, sizeof(packet));
}

esp_err_t protocol_send_event(uint8_t event_id, const uint8_t* payload, uint16_t payload_len) {
    return protocol_send_response(event_id, payload, payload_len);
}

void protocol_task(void* arg) {
    protocol_packet_t packet;
    uint8_t response_payload[PROTOCOL_PAYLOAD_SIZE];
    uint16_t response_len;
    
    ESP_LOGI(TAG, "Protocol task started");
    
    while (1) {
        if (xQueueReceive(protocol_cmd_queue, &packet, portMAX_DELAY)) {
            ESP_LOGI(TAG, "Processing command 0x%02X", packet.command_id);
            
            // Find command handler
            command_handler_t handler = NULL;
            for (int i = 0; i < sizeof(command_table) / sizeof(command_entry_t); i++) {
                if (command_table[i].command_id == packet.command_id) {
                    handler = command_table[i].handler;
                    break;
                }
            }
            
            if (handler != NULL) {
                // Execute handler
                response_len = 0;
                esp_err_t ret = handler(packet.payload, packet.payload_length, 
                                        response_payload, &response_len);
                
                if (ret == ESP_OK) {
                    // Send response
                    protocol_send_response(packet.command_id, response_payload, response_len);
                } else {
                    // Send error response
                    response_payload[0] = STATUS_ERROR;
                    protocol_send_response(packet.command_id, response_payload, 1);
                }
            } else {
                ESP_LOGW(TAG, "Unknown command: 0x%02X", packet.command_id);
                response_payload[0] = STATUS_ERROR;
                protocol_send_response(packet.command_id, response_payload, 1);
            }
        }
    }
}

// Command handler implementations

static esp_err_t handle_ping(const uint8_t* payload, uint16_t length, 
                              uint8_t* response, uint16_t* response_len) {
    // Response: firmware version + uptime + current profile
    response[0] = FIRMWARE_VERSION_MAJOR;
    response[1] = FIRMWARE_VERSION_MINOR;
    response[2] = FIRMWARE_VERSION_PATCH;
    response[3] = 0; // Build number
    
    uint32_t uptime = esp_timer_get_time() / 1000000; // seconds
    memcpy(&response[4], &uptime, 4);
    
    response[8] = profile_get_current_id();
    
    *response_len = 9;
    return ESP_OK;
}

static esp_err_t handle_get_device_info(const uint8_t* payload, uint16_t length,
                                         uint8_t* response, uint16_t* response_len) {
    // Device ID (UUID) - for now use MAC address
    uint8_t mac[6];
    esp_efuse_mac_get_default(mac);
    memcpy(&response[0], mac, 6);
    memset(&response[6], 0, 10); // Pad to 16 bytes
    
    // Firmware version
    response[16] = FIRMWARE_VERSION_MAJOR;
    response[17] = FIRMWARE_VERSION_MINOR;
    response[18] = FIRMWARE_VERSION_PATCH;
    response[19] = 0;
    
    // Device capabilities
    response[20] = NUM_BUTTONS;
    response[21] = NUM_PROFILES;
    response[22] = profile_get_current_id();
    
    // Free flash space
    uint32_t free_space = 0; // TODO: implement
    memcpy(&response[23], &free_space, 4);
    
    *response_len = 27;
    return ESP_OK;
}

static esp_err_t handle_set_profile(const uint8_t* payload, uint16_t length,
                                     uint8_t* response, uint16_t* response_len) {
    if (length < 1) {
        return ESP_ERR_INVALID_ARG;
    }
    
    uint8_t profile_id = payload[0];
    
    esp_err_t ret = profile_switch(profile_id);
    
    response[0] = (ret == ESP_OK) ? STATUS_OK : STATUS_ERROR;
    response[1] = profile_get_current_id();
    *response_len = 2;
    
    return ESP_OK;
}

static esp_err_t handle_get_profile_info(const uint8_t* payload, uint16_t length,
                                          uint8_t* response, uint16_t* response_len) {
    if (length < 1) {
        return ESP_ERR_INVALID_ARG;
    }
    
    uint8_t profile_id = payload[0];
    profile_t* profile = profile_get(profile_id);
    
    if (profile == NULL) {
        return ESP_ERR_NOT_FOUND;
    }
    
    response[0] = profile_id;
    strncpy((char*)&response[1], profile->name, 32);
    response[33] = 1; // Is configured
    
    *response_len = 34;
    return ESP_OK;
}

static esp_err_t handle_start_image_transfer(const uint8_t* payload, uint16_t length,
                                               uint8_t* response, uint16_t* response_len) {
    if (length < 11) {
        return ESP_ERR_INVALID_ARG;
    }
    
    uint8_t profile_id = payload[0];
    uint8_t button_id = payload[1];
    uint32_t image_size;
    memcpy(&image_size, &payload[2], 4);
    uint8_t format = payload[6];
    
    esp_err_t ret = image_transfer_start(profile_id, button_id, image_size, format);
    
    response[0] = (ret == ESP_OK) ? STATUS_OK : STATUS_ERROR;
    uint16_t transfer_id = 0; // TODO: implement transfer ID
    memcpy(&response[1], &transfer_id, 2);
    uint16_t max_chunk = 50;
    memcpy(&response[3], &max_chunk, 2);
    
    *response_len = 5;
    return ESP_OK;
}

static esp_err_t handle_image_data_chunk(const uint8_t* payload, uint16_t length,
                                          uint8_t* response, uint16_t* response_len) {
    if (length < 6) {
        return ESP_ERR_INVALID_ARG;
    }
    
    uint16_t chunk_num;
    uint16_t chunk_size;
    memcpy(&chunk_num, &payload[2], 2);
    memcpy(&chunk_size, &payload[4], 2);
    
    esp_err_t ret = image_transfer_chunk(&payload[6], chunk_size, chunk_num);
    
    response[0] = (ret == ESP_OK) ? STATUS_OK : STATUS_ERROR;
    uint16_t next_chunk = chunk_num + 1;
    memcpy(&response[1], &next_chunk, 2);
    
    *response_len = 3;
    return ESP_OK;
}

static esp_err_t handle_end_image_transfer(const uint8_t* payload, uint16_t length,
                                            uint8_t* response, uint16_t* response_len) {
    if (length < 10) {
        return ESP_ERR_INVALID_ARG;
    }
    
    uint32_t expected_crc;
    memcpy(&expected_crc, &payload[6], 4);
    
    uint32_t calculated_crc;
    esp_err_t ret = image_transfer_end(&calculated_crc);
    
    response[0] = (ret == ESP_OK && calculated_crc == expected_crc) ? STATUS_OK : STATUS_ERROR;
    memcpy(&response[1], &calculated_crc, 4);
    
    *response_len = 5;
    return ESP_OK;
}

static esp_err_t handle_set_button_action(const uint8_t* payload, uint16_t length,
                                           uint8_t* response, uint16_t* response_len) {
    if (length < 5) {
        return ESP_ERR_INVALID_ARG;
    }
    
    uint8_t profile_id = payload[0];
    uint8_t button_id = payload[1];
    uint8_t action_type = payload[2];
    uint16_t action_len;
    memcpy(&action_len, &payload[3], 2);
    
    esp_err_t ret = profile_set_button_action(profile_id, button_id, action_type, 
                                                &payload[5], action_len);
    
    response[0] = (ret == ESP_OK) ? STATUS_OK : STATUS_ERROR;
    *response_len = 1;
    
    return ESP_OK;
}

static esp_err_t handle_set_led_color(const uint8_t* payload, uint16_t length,
                                       uint8_t* response, uint16_t* response_len) {
    if (length < 7) {
        return ESP_ERR_INVALID_ARG;
    }
    
    uint8_t profile_id = payload[0];
    uint8_t button_id = payload[1];
    uint8_t r = payload[2];
    uint8_t g = payload[3];
    uint8_t b = payload[4];
    uint8_t brightness = payload[5];
    uint8_t effect = payload[6];
    
    esp_err_t ret = profile_set_led_color(profile_id, button_id, r, g, b, brightness, effect);
    
    response[0] = (ret == ESP_OK) ? STATUS_OK : STATUS_ERROR;
    *response_len = 1;
    
    return ESP_OK;
}

static esp_err_t handle_set_backlight(const uint8_t* payload, uint16_t length,
                                       uint8_t* response, uint16_t* response_len) {
    if (length < 1) {
        return ESP_ERR_INVALID_ARG;
    }
    
    uint8_t enabled = payload[0];
    
    extern esp_err_t gc9a01_set_backlight(bool enabled);
    esp_err_t ret = gc9a01_set_backlight(enabled != 0);
    
    response[0] = (ret == ESP_OK) ? STATUS_OK : STATUS_ERROR;
    *response_len = 1;
    
    return ESP_OK;
}

static esp_err_t handle_save_profile(const uint8_t* payload, uint16_t length,
                                      uint8_t* response, uint16_t* response_len) {
    if (length < 1) {
        return ESP_ERR_INVALID_ARG;
    }
    
    uint8_t profile_id = payload[0];
    
    esp_err_t ret = profile_save_to_storage(profile_id);
    
    response[0] = (ret == ESP_OK) ? STATUS_OK : STATUS_ERROR;
    uint32_t bytes_written = 0; // TODO: implement
    memcpy(&response[1], &bytes_written, 4);
    
    *response_len = 5;
    return ESP_OK;
}
