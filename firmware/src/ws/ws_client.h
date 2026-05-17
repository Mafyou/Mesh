#pragma once

#include <stdbool.h>
#include <stdint.h>
#include "esp_err.h"

/*
 * WebSocket client — bridges the mesh to the Blazor web server.
 *
 * Connection sequence:
 *   1. ws_client_start() connects to <uri>/ws/node
 *   2. Sends identification frame: [nodeId 1B][name UTF-8]
 *   3. Subsequent binary frames carry: [src/dst 1B][text UTF-8]
 *      (same wire format as the BLE NUS channel)
 *
 * Reconnects automatically. The identification frame is re-sent on
 * every (re)connection.
 */

typedef void (*ws_rx_cb_t)(const uint8_t *data, int len);

esp_err_t ws_client_start(const char *uri, uint8_t node_id,
                           const char *node_name, ws_rx_cb_t on_rx);
esp_err_t ws_client_send(const uint8_t *data, size_t len);
bool      ws_client_connected(void);
void      ws_client_stop(void);
