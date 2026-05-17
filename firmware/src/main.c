#include <stdio.h>
#include <string.h>
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "nvs_flash.h"

#include "ble/ble.h"
#include "hal/lora.h"
#include "hal/oled.h"
#include "mesh/mesh.h"
#include "power/power.h"

#ifdef MESH_WEB_ENABLED
#include "wifi/wifi.h"
#include "ws/ws_client.h"
#endif

static const char *TAG = "main";

#define PING_INTERVAL_MS 10000
#define LOOP_TICK_MS     10

/* ---- BLE / WebSocket ↔ mesh bridge ---- */

/* mesh → BLE + WebSocket : forward received packet to connected clients */
/* Wire format ESP→mobile/web: [src 1B] [channel 1B] [type 1B] [payload] */
static void on_mesh_rx(uint8_t src, uint8_t dst, mesh_type_t type, uint8_t channel,
                       const uint8_t *payload, uint8_t len)
{
    (void)dst;
    power_activity();

    uint8_t buf[3 + MESH_PAYLOAD_MAX];
    buf[0] = src;
    buf[1] = channel;
    buf[2] = (uint8_t)type;
    if (len > 0) {
        memcpy(&buf[3], payload, len);
    }

    ble_notify(buf, (uint16_t)(3 + len));

#ifdef MESH_WEB_ENABLED
    ws_client_send(buf, 3 + len);
#endif
}

/* BLE → mesh */
/* Wire format mobile→ESP: [dst 1B] [channel 1B] [payload] */
static void on_ble_write(const uint8_t *data, uint16_t len)
{
    if (len < 3) return;
    power_activity();
    mesh_send(data[0], MESH_TYPE_MSG, data[1], &data[2], (uint8_t)(len - 2));
}

#ifdef MESH_WEB_ENABLED
/* WebSocket → mesh */
/* Wire format web→ESP: [dst 1B] [channel 1B] [payload] */
static void on_ws_rx(const uint8_t *data, int len)
{
    if (len < 3) return;
    mesh_send(data[0], MESH_TYPE_MSG, data[1], &data[2], (uint8_t)(len - 2));
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

    /* Power management (light sleep on idle) */
    if (power_init() != ESP_OK) {
        ESP_LOGW(TAG, "power_init failed — sleep disabled");
    }

    ESP_LOGI(TAG, "ready  node=0x%02X", mesh_node_id());

    uint32_t ticks     = 0;
    uint32_t ping_tick = PING_INTERVAL_MS / LOOP_TICK_MS;

    for (;;) {
        mesh_process();

        /* Periodic broadcast ping with telemetry payload */
        if (++ticks % ping_tick == 0) {
            uint8_t ping[MESH_PING_PAYLOAD_SIZE];
            uint32_t uptime_s = (uint32_t)(esp_timer_get_time() / 1000000ULL);
            /* vbat not available without ADC driver; use 0 as placeholder */
            uint16_t vbat_mv  = 0;
            uint16_t tx_pkts  = mesh_tx_count();
            uint16_t rx_pkts  = mesh_rx_count();
            ping[0] = (uint8_t)(uptime_s);
            ping[1] = (uint8_t)(uptime_s >> 8);
            ping[2] = (uint8_t)(uptime_s >> 16);
            ping[3] = (uint8_t)(uptime_s >> 24);
            ping[4] = (uint8_t)(vbat_mv);
            ping[5] = (uint8_t)(vbat_mv >> 8);
            ping[6] = (uint8_t)(tx_pkts);
            ping[7] = (uint8_t)(tx_pkts >> 8);
            ping[8] = (uint8_t)(rx_pkts);
            ping[9] = (uint8_t)(rx_pkts >> 8);
            mesh_send(MESH_BROADCAST, MESH_TYPE_PING, MESH_CHANNEL_DEFAULT,
                      ping, MESH_PING_PAYLOAD_SIZE);

            /* Push neighbor table to connected BLE/WS clients */
            mesh_neighbor_t nbrs[MESH_NEIGHBOR_MAX];
            uint8_t cnt = mesh_get_neighbors(nbrs, MESH_NEIGHBOR_MAX);
            if (cnt > 0) {
                /* Frame: [src=node_id][channel=0][type=NEIGHBORS][count][{id,rssi,snr}...] */
                uint8_t nbuf[3 + 1 + MESH_NEIGHBOR_MAX * 3];
                nbuf[0] = mesh_node_id();
                nbuf[1] = MESH_CHANNEL_DEFAULT;
                nbuf[2] = (uint8_t)MESH_TYPE_NEIGHBORS;
                nbuf[3] = cnt;
                for (uint8_t i = 0; i < cnt; i++) {
                    nbuf[4 + i * 3 + 0] = nbrs[i].node_id;
                    nbuf[4 + i * 3 + 1] = (uint8_t)nbrs[i].rssi;
                    nbuf[4 + i * 3 + 2] = (uint8_t)nbrs[i].snr;
                }
                ble_notify(nbuf, (uint16_t)(4 + cnt * 3));
#ifdef MESH_WEB_ENABLED
                ws_client_send(nbuf, 4 + cnt * 3);
#endif
            }
        }

        power_maybe_sleep();
        vTaskDelay(pdMS_TO_TICKS(LOOP_TICK_MS));
    }
}
