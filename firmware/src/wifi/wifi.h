#pragma once

#include <stdbool.h>
#include "esp_err.h"

/*
 * WiFi station — connects to an AP and blocks until IP is obtained.
 * Call once from app_main after nvs_flash_init().
 */

esp_err_t wifi_sta_init(const char *ssid, const char *password);
bool      wifi_sta_connected(void);
