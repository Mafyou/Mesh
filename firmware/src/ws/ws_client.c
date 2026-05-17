#ifdef MESH_WEB_ENABLED

#include "ws/ws_client.h"

#include <string.h>
#include "esp_log.h"
#include "esp_websocket_client.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char *TAG = "ws";

static esp_websocket_client_handle_t s_client;
static ws_rx_cb_t  s_rx_cb;
static volatile bool s_connected;
static uint8_t     s_node_id;
static char        s_node_name[32];

/* Re-sent on every (re)connection so the server always knows the node ID. */
static void send_ident(void)
{
    size_t  name_len = strlen(s_node_name);
    uint8_t buf[1 + 32];
    buf[0] = s_node_id;
    memcpy(&buf[1], s_node_name, name_len);
    esp_websocket_client_send_bin(s_client, (char *)buf,
                                  (int)(1 + name_len), portMAX_DELAY);
}

static void ws_event_handler(void *arg, esp_event_base_t base,
                              int32_t id, void *data)
{
    esp_websocket_event_data_t *evt = (esp_websocket_event_data_t *)data;

    switch (id) {
    case WEBSOCKET_EVENT_CONNECTED:
        s_connected = true;
        send_ident();
        ESP_LOGI(TAG, "connected  node=%s (0x%02X)", s_node_name, s_node_id);
        break;

    case WEBSOCKET_EVENT_DISCONNECTED:
        s_connected = false;
        ESP_LOGW(TAG, "disconnected — reconnecting …");
        break;

    case WEBSOCKET_EVENT_DATA:
        /* Only forward binary frames with actual payload. */
        if (s_rx_cb && evt->op_code == 0x02 && evt->data_len > 0) {
            s_rx_cb((const uint8_t *)evt->data_ptr, evt->data_len);
        }
        break;

    case WEBSOCKET_EVENT_ERROR:
        ESP_LOGE(TAG, "error");
        break;

    default:
        break;
    }
}

esp_err_t ws_client_start(const char *uri, uint8_t node_id,
                           const char *node_name, ws_rx_cb_t on_rx)
{
    s_rx_cb    = on_rx;
    s_node_id  = node_id;
    strncpy(s_node_name, node_name, sizeof(s_node_name) - 1);

    esp_websocket_client_config_t cfg = {
        .uri                  = uri,
        .reconnect_timeout_ms = 5000,
        .network_timeout_ms   = 10000,
    };

    s_client = esp_websocket_client_init(&cfg);
    if (!s_client) {
        ESP_LOGE(TAG, "client init failed");
        return ESP_FAIL;
    }

    ESP_ERROR_CHECK(esp_websocket_register_events(
        s_client, WEBSOCKET_EVENT_ANY, ws_event_handler, NULL));
    ESP_ERROR_CHECK(esp_websocket_client_start(s_client));

    /* Block until connected or timeout (15 s). */
    for (int i = 0; i < 150 && !s_connected; i++) {
        vTaskDelay(pdMS_TO_TICKS(100));
    }

    return s_connected ? ESP_OK : ESP_ERR_TIMEOUT;
}

esp_err_t ws_client_send(const uint8_t *data, size_t len)
{
    if (!s_connected || !s_client) {
        return ESP_ERR_INVALID_STATE;
    }
    int sent = esp_websocket_client_send_bin(
        s_client, (const char *)data, (int)len, pdMS_TO_TICKS(1000));
    return sent >= 0 ? ESP_OK : ESP_FAIL;
}

bool ws_client_connected(void)
{
    return s_connected;
}

void ws_client_stop(void)
{
    if (s_client) {
        esp_websocket_client_stop(s_client);
        esp_websocket_client_destroy(s_client);
        s_client = NULL;
    }
    s_connected = false;
}

#endif /* MESH_WEB_ENABLED */
