/**
 * @file common.h
 * @brief Common includes for all source files
 */

#ifndef COMMON_H
#define COMMON_H

// Standard C libraries
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

// ESP-IDF core
#include "esp_err.h"
#include "esp_log.h"
#include "esp_system.h"
#include "esp_timer.h"
#include "esp_heap_caps.h"
#include "esp_mac.h"
#include "esp_rom_sys.h"

// FreeRTOS
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "freertos/semphr.h"
#include "freertos/event_groups.h"

#endif // COMMON_H
