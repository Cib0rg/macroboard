/**
 * @file crc.h
 * @brief CRC calculation utilities
 */

#ifndef CRC_H
#define CRC_H

#include <stdint.h>
#include <stddef.h>

/**
 * @brief Calculate CRC32
 * @param data Data buffer
 * @param length Data length
 * @return CRC32 value
 */
uint32_t crc32_calculate(const uint8_t* data, size_t length);

/**
 * @brief Calculate XOR checksum
 * @param data Data buffer
 * @param length Data length
 * @return XOR checksum
 */
uint8_t xor_checksum(const uint8_t* data, size_t length);

#endif // CRC_H
