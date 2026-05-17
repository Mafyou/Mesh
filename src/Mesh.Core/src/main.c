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

static const char *TAG = "main";

#define PING_INTERVAL_MS 10000
#define LOOP_TICK_MS     10

/* ---- BLE ↔ mesh bridge ---- */

/* mesh → BLE : forward received message to connected mobile */
static void on_mesh_rx(uint8_t src, uint8_t dst, mesh_type_t type,
                       const uint8_t *payload, uint8_t len)
{
    (void)dst;
    (void)type;

    /* Wire format ESP→mobile: [src 1B] [text] */
    uint8_t buf[1 + MESH_PAYLOAD_MAX];
    buf[0] = src;
    if (len > 0) {
        memcpy(&buf[1], payload, len);
    }
    ble_notify(buf, (uint16_t)(1 + len));
}

/* BLE → mesh : inject message from mobile into the mesh */
static void on_ble_write(const uint8_t *data, uint16_t len)
{
    /* Wire format mobile→ESP: [dst 1B] [text] */
    if (len < 2) {
        return;
    }
    uint8_t dst = data[0];
    mesh_send(dst, MESH_TYPE_MSG, &data[1], (uint8_t)(len - 1));
}

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
        oled_draw_text(2, 0, "BLE FAIL    ");
    }

    /* Static OLED display */
    snprintf(line, sizeof(line), "Node  0x%02X", mesh_node_id());
    oled_draw_text(1, 0, line);
    oled_draw_text(2, 0, "868MHz SF10 ");
    oled_draw_text(3, 0, "BLE ready   ");

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
