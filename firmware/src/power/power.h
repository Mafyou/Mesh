#pragma once

#include <stdbool.h>
#include "esp_err.h"

/*
 * Light-sleep power management.
 *
 * After POWER_IDLE_THRESHOLD_MS of no LoRa activity and no BLE client,
 * the node enters ESP32 light sleep. It wakes when DIO1 goes HIGH
 * (SX1262 RxDone IRQ) or after a periodic timer.
 *
 * Call power_activity() on every LoRa RX or BLE write to reset the timer.
 * Call power_maybe_sleep() once per main loop tick.
 */

#define POWER_IDLE_THRESHOLD_MS  30000U   /* idle before sleeping (30 s) */

esp_err_t power_init(void);
void      power_activity(void);
void      power_maybe_sleep(void);
