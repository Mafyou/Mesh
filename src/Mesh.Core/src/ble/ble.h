#pragma once

#include <stdbool.h>
#include <stdint.h>
#include "esp_err.h"

/*
 * Nordic UART Service (NUS) over BLE.
 *
 * Wire format shared with the MAUI app:
 *   ESP → mobile (notify):  [src 1B] [UTF-8 text]
 *   mobile → ESP (write):   [dst 1B] [UTF-8 text]   (dst 0xFF = broadcast)
 */

typedef void (*ble_write_cb_t)(const uint8_t *data, uint16_t len);

/* device_name  : advertised name, e.g. "Mesh-3C"
   on_write     : called when the mobile sends data; runs in NimBLE task  */
esp_err_t ble_init(const char *device_name, ble_write_cb_t on_write);

/* Send a notification to the connected mobile client.
   Returns ESP_ERR_INVALID_STATE if nobody is connected or subscribed. */
esp_err_t ble_notify(const uint8_t *data, uint16_t len);

bool ble_connected(void);
