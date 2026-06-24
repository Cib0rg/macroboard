#pragma once

/**
 * @brief Periodic heartbeat task.
 *
 * Sends EVENT_HEARTBEAT (0xF8) every 2 seconds so the host can detect
 * that the device is alive and the TX path is working after a backend
 * restart (when EVENT_DEVICE_READY 0xF4 was already sent at boot and
 * is no longer in the TX FIFO).
 *
 * Runs directly via usb_vendor_send — NOT through protocol_cmd_queue —
 * so it works even when protocol_task is temporarily blocked on SPIFFS.
 */
void heartbeat_task(void* arg);
