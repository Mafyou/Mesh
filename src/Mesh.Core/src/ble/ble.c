#include "ble/ble.h"

#include <string.h>
#include "esp_log.h"
#include "nimble/nimble_port.h"
#include "nimble/nimble_port_freertos.h"
#include "host/ble_hs.h"
#include "host/ble_uuid.h"
#include "services/gap/ble_svc_gap.h"
#include "services/gatt/ble_svc_gatt.h"

static const char *TAG = "ble";

/* Nordic UART Service UUIDs (128-bit, little-endian) */
#define NUS_SVC_UUID  BLE_UUID128_DECLARE( \
    0x9E,0xCA,0xDC,0x24,0x0E,0xE5,0xA9,0xE0, \
    0x93,0xF3,0xA3,0xB5,0x01,0x00,0x40,0x6E)

#define NUS_RX_UUID   BLE_UUID128_DECLARE( \
    0x9E,0xCA,0xDC,0x24,0x0E,0xE5,0xA9,0xE0, \
    0x93,0xF3,0xA3,0xB5,0x02,0x00,0x40,0x6E)

#define NUS_TX_UUID   BLE_UUID128_DECLARE( \
    0x9E,0xCA,0xDC,0x24,0x0E,0xE5,0xA9,0xE0, \
    0x93,0xF3,0xA3,0xB5,0x03,0x00,0x40,0x6E)

static volatile uint16_t s_conn_handle = BLE_HS_CONN_HANDLE_NONE;
static volatile bool     s_subscribed  = false;
static uint16_t          s_tx_handle;
static ble_write_cb_t    s_write_cb;
static char              s_device_name[32];

/* ---- GATT callbacks ---- */

static int gatt_rx_write_cb(uint16_t conn_handle, uint16_t attr_handle,
                             struct ble_gatt_access_ctxt *ctxt, void *arg)
{
    (void)conn_handle;
    (void)attr_handle;
    (void)arg;

    if (ctxt->op != BLE_GATT_ACCESS_OP_WRITE_CHR) {
        return 0;
    }

    uint16_t len = OS_MBUF_PKTLEN(ctxt->om);
    if (len == 0 || len > 244) {
        return BLE_ATT_ERR_INVALID_ATTR_VALUE_LEN;
    }

    uint8_t buf[244];
    uint16_t copied = 0;
    ble_hs_mbuf_to_flat(ctxt->om, buf, sizeof(buf), &copied);

    if (s_write_cb && copied > 0) {
        s_write_cb(buf, copied);
    }
    return 0;
}

static int gatt_tx_access_cb(uint16_t conn_handle, uint16_t attr_handle,
                              struct ble_gatt_access_ctxt *ctxt, void *arg)
{
    (void)conn_handle;
    (void)attr_handle;
    (void)ctxt;
    (void)arg;
    return 0;
}

/* ---- GATT service table ---- */

static const struct ble_gatt_svc_def s_gatt_svcs[] = {
    {
        .type            = BLE_GATT_SVC_TYPE_PRIMARY,
        .uuid            = NUS_SVC_UUID,
        .characteristics = (struct ble_gatt_chr_def[]) {
            {   /* RX char — mobile writes here */
                .uuid      = NUS_RX_UUID,
                .access_cb = gatt_rx_write_cb,
                .flags     = BLE_GATT_CHR_F_WRITE | BLE_GATT_CHR_F_WRITE_NO_RSP,
            },
            {   /* TX char — we notify here */
                .uuid       = NUS_TX_UUID,
                .access_cb  = gatt_tx_access_cb,
                .val_handle = &s_tx_handle,
                .flags      = BLE_GATT_CHR_F_NOTIFY,
            },
            { 0 },
        },
    },
    { 0 },
};

/* ---- Advertising ---- */

static void start_advertising(void)
{
    struct ble_hs_adv_fields fields = {
        .flags             = BLE_HS_ADV_F_DISC_GEN | BLE_HS_ADV_F_BREDR_UNSUP,
        .name              = (uint8_t *)s_device_name,
        .name_len          = (uint8_t)strlen(s_device_name),
        .name_is_complete  = 1,
    };
    int rc = ble_gap_adv_set_fields(&fields);
    if (rc != 0) {
        ESP_LOGE(TAG, "adv fields: %d", rc);
        return;
    }

    struct ble_gap_adv_params params = {
        .conn_mode = BLE_GAP_CONN_MODE_UND,
        .disc_mode = BLE_GAP_DISC_MODE_GEN,
        .itvl_min  = BLE_GAP_ADV_ITVL_MS(200),
        .itvl_max  = BLE_GAP_ADV_ITVL_MS(300),
    };
    rc = ble_gap_adv_start(BLE_OWN_ADDR_PUBLIC, NULL, BLE_HS_FOREVER,
                           &params, NULL, NULL);
    if (rc != 0 && rc != BLE_HS_EALREADY) {
        ESP_LOGE(TAG, "adv start: %d", rc);
    }
}

/* ---- GAP event handler ---- */

static int gap_event_cb(struct ble_gap_event *event, void *arg)
{
    (void)arg;

    switch (event->type) {
    case BLE_GAP_EVENT_CONNECT:
        if (event->connect.status == 0) {
            s_conn_handle = event->connect.conn_handle;
            s_subscribed  = false;
            ESP_LOGI(TAG, "connected  handle=%d", s_conn_handle);
        } else {
            ESP_LOGW(TAG, "connect failed  status=%d", event->connect.status);
            s_conn_handle = BLE_HS_CONN_HANDLE_NONE;
            start_advertising();
        }
        break;

    case BLE_GAP_EVENT_DISCONNECT:
        ESP_LOGI(TAG, "disconnected  reason=%d", event->disconnect.reason);
        s_conn_handle = BLE_HS_CONN_HANDLE_NONE;
        s_subscribed  = false;
        start_advertising();
        break;

    case BLE_GAP_EVENT_ADV_COMPLETE:
        start_advertising();
        break;

    case BLE_GAP_EVENT_SUBSCRIBE:
        if (event->subscribe.attr_handle == s_tx_handle) {
            s_subscribed = event->subscribe.cur_notify;
            ESP_LOGI(TAG, "notify %s", s_subscribed ? "enabled" : "disabled");
        }
        break;

    case BLE_GAP_EVENT_MTU:
        ESP_LOGI(TAG, "MTU updated: %d", event->mtu.value);
        break;

    default:
        break;
    }
    return 0;
}

/* ---- NimBLE host task ---- */

static void on_sync(void)
{
    ble_addr_t addr;
    ble_hs_id_infer_auto(0, &addr.type);
    start_advertising();
    ESP_LOGI(TAG, "BLE advertising as \"%s\"", s_device_name);
}

static void on_reset(int reason)
{
    ESP_LOGW(TAG, "NimBLE reset: %d", reason);
}

static void ble_host_task(void *arg)
{
    nimble_port_run();
    nimble_port_freertos_deinit();
}

/* ---- Public API ---- */

esp_err_t ble_init(const char *device_name, ble_write_cb_t on_write)
{
    strncpy(s_device_name, device_name, sizeof(s_device_name) - 1);
    s_write_cb = on_write;

    esp_err_t err = nimble_port_init();
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "nimble_port_init: %s", esp_err_to_name(err));
        return err;
    }

    ble_svc_gap_init();
    ble_svc_gatt_init();

    int rc = ble_gatts_count_cfg(s_gatt_svcs);
    if (rc != 0) {
        ESP_LOGE(TAG, "gatts_count_cfg: %d", rc);
        return ESP_FAIL;
    }
    rc = ble_gatts_add_svcs(s_gatt_svcs);
    if (rc != 0) {
        ESP_LOGE(TAG, "gatts_add_svcs: %d", rc);
        return ESP_FAIL;
    }

    ble_svc_gap_device_name_set(s_device_name);

    ble_hs_cfg.sync_cb  = on_sync;
    ble_hs_cfg.reset_cb = on_reset;

    nimble_port_freertos_init(ble_host_task);
    return ESP_OK;
}

esp_err_t ble_notify(const uint8_t *data, uint16_t len)
{
    if (!s_subscribed || s_conn_handle == BLE_HS_CONN_HANDLE_NONE) {
        return ESP_ERR_INVALID_STATE;
    }

    struct os_mbuf *om = ble_hs_mbuf_from_flat(data, len);
    if (!om) {
        return ESP_ERR_NO_MEM;
    }

    int rc = ble_gattc_notify_custom(s_conn_handle, s_tx_handle, om);
    return rc == 0 ? ESP_OK : ESP_FAIL;
}

bool ble_connected(void)
{
    return s_conn_handle != BLE_HS_CONN_HANDLE_NONE;
}
