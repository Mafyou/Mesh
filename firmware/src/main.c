#include <stdio.h>
#include <string.h>
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "nvs_flash.h"

#include "ble/ble.h"
#include "hal/lora.h"
#include "hal/oled.h"
#include "mesh/mesh.h"

#ifdef MESH_WEB_ENABLED
#include "wifi/wifi.h"
#include "ws/ws_client.h"
#endif

static const char *TAG = "main";

#define PING_INTERVAL_MS 10000
#define LOOP_TICK_MS     10

/* ---- BLE / WebSocket ↔ mesh bridge ---- */

/* mesh → BLE + WebSocket : forward received packet to connected clients */
static void on_mesh_rx(uint8_t src, uint8_t dst, mesh_type_t type,
                       const uint8_t *payload, uint8_t len)
{
    (void)dst;
    (void)type;

    /* Wire format ESP→mobile/web: [src 1B] [text] */
    uint8_t buf[1 + MESH_PAYLOAD_MAX];
    buf[0] = src;
    if (len > 0) {
        memcpy(&buf[1], payload, len);
    }

    ble_notify(buf, (uint16_t)(1 + len));

#ifdef MESH_WEB_ENABLED
    ws_client_send(buf, 1 + len);
#endif
}

/* BLE → mesh */
static void on_ble_write(const uint8_t *data, uint16_t len)
{
    /* Wire format mobile→ESP: [dst 1B] [text] */
    if (len < 2) return;
    mesh_send(data[0], MESH_TYPE_MSG, &data[1], (uint8_t)(len - 1));
}

#ifdef MESH_WEB_ENABLED
/* WebSocket → mesh */
static void on_ws_rx(const uint8_t *data, int len)
{
    /* Wire format web→ESP: [dst 1B] [text] */
    if (len < 2) return;
    mesh_send(data[0], MESH_TYPE_MSG, &data[1], (uint8_t)(len - 1));
}
#endif

/* ---- Entry point ---- */

void app_main(void)
{
    char line[22];

    /* NVS — required for BLE RF calibration persistence */
    esp_err_t nvs_err = nvs_flash_init();
    if (nvs_err == ESP_ERR_NVS_NO_FREE_PAGES || nvs_err == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_ERROR_CHECK(nvs_flash_erase());
        nvs_err = nvs_flash_init();
    }
    ESP_ERROR_CHECK(nvs_err);

    /* OLED */
    if (oled_init() != ESP_OK) {
        ESP_LOGE(TAG, "OLED failed");
        return;
    }
    oled_draw_text(0, 0, "Mesh.Core");
    oled_draw_text(1, 0, "Init LoRa...");

    /* LoRa SX1262 */
    esp_err_t err = lora_init();
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "LoRa: %s", esp_err_to_name(err));
        oled_draw_text(1, 0, "LoRa FAIL   ");
        return;
    }

    /* Mesh — auto node ID from MAC */
    err = mesh_init(0);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "Mesh: %s", esp_err_to_name(err));
        return;
    }
    mesh_set_rx_cb(on_mesh_rx);

    /* BLE */
    snprintf(line, sizeof(line), "Mesh-%02X", mesh_node_id());
    err = ble_init(line, on_ble_write);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "BLE: %s", esp_err_to_name(err));
    }

    /* Static OLED */
    snprintf(line, sizeof(line), "Node  0x%02X", mesh_node_id());
    oled_draw_text(1, 0, line);
    oled_draw_text(2, 0, "868MHz SF10 ");

#ifdef MESH_WEB_ENABLED
    /* WiFi */
    oled_draw_text(3, 0, "WiFi...     ");
    err = wifi_sta_init(WIFI_SSID, WIFI_PASS);
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "WiFi: %s", esp_err_to_name(err));
        oled_draw_text(3, 0, "WiFi FAIL   ");
    } else {
        /* WebSocket to Blazor server */
        oled_draw_text(3, 0, "WS...       ");
        char ws_name[16];
        snprintf(ws_name, sizeof(ws_name), "Mesh-%02X", mesh_node_id());
        err = ws_client_start(WS_URI, mesh_node_id(), ws_name, on_ws_rx);
        if (err != ESP_OK) {
            ESP_LOGW(TAG, "WS: %s", esp_err_to_name(err));
            oled_draw_text(3, 0, "BLE only    ");
        } else {
            oled_draw_text(3, 0, "BLE+WS      ");
        }
    }
#else
    oled_draw_text(3, 0, "BLE ready   ");
#endif

    ESP_LOGI(TAG, "ready  node=0x%02X", mesh_node_id());

    uint32_t ticks     = 0;
    uint32_t ping_tick = PING_INTERVAL_MS / LOOP_TICK_MS;

    for (;;) {
        mesh_process();

        /* Periodic broadcast ping */
        if (++ticks % ping_tick == 0) {
            const char *msg = "ping";
            mesh_send(MESH_BROADCAST, MESH_TYPE_PING,
                      (const uint8_t *)msg, (uint8_t)strlen(msg));
        }

        vTaskDelay(pdMS_TO_TICKS(LOOP_TICK_MS));
    }
}
