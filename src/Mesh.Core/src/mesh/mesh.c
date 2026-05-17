#include "mesh/mesh.h"
#include "hal/lora.h"

#include <string.h>
#include "esp_efuse.h"
#include "esp_log.h"
#include "esp_mac.h"

static const char *TAG = "mesh";

typedef struct {
    uint8_t  src;
    uint16_t seq;
} hist_entry_t;

static uint8_t      s_node_id;
static uint16_t     s_seq;
static hist_entry_t s_history[MESH_HISTORY_SIZE];
static uint8_t      s_hist_head;
static mesh_rx_cb_t s_rx_cb;

static bool history_seen(uint8_t src, uint16_t seq)
{
    for (int i = 0; i < MESH_HISTORY_SIZE; i++) {
        if (s_history[i].src == src && s_history[i].seq == seq) {
            return true;
        }
    }
    return false;
}

static void history_add(uint8_t src, uint16_t seq)
{
    s_history[s_hist_head].src = src;
    s_history[s_hist_head].seq = seq;
    s_hist_head = (uint8_t)((s_hist_head + 1) % MESH_HISTORY_SIZE);
}

esp_err_t mesh_init(uint8_t node_id)
{
    memset(s_history, 0, sizeof(s_history));
    s_hist_head = 0;
    s_seq       = 0;

    if (node_id != 0) {
        s_node_id = node_id;
    } else {
        uint8_t mac[6] = {0};
        esp_efuse_mac_get_default(mac);
        s_node_id = mac[5];
        if (s_node_id == 0x00) s_node_id = 0x01;
        if (s_node_id == MESH_BROADCAST) s_node_id = 0xFE;
    }

    ESP_LOGI(TAG, "node 0x%02X", s_node_id);
    return ESP_OK;
}

uint8_t mesh_node_id(void)
{
    return s_node_id;
}

void mesh_set_rx_cb(mesh_rx_cb_t cb)
{
    s_rx_cb = cb;
}

esp_err_t mesh_send(uint8_t dst, mesh_type_t type, const uint8_t *payload, uint8_t len)
{
    if (len > MESH_PAYLOAD_MAX) {
        return ESP_ERR_INVALID_ARG;
    }

    mesh_packet_t pkt;
    memset(&pkt, 0, sizeof(pkt));
    pkt.flags = (uint8_t)((uint8_t)type << 2);
    pkt.src   = s_node_id;
    pkt.dst   = dst;
    pkt.ttl   = MESH_TTL_DEFAULT;
    pkt.seq   = s_seq++;
    pkt.len   = len;
    if (payload && len) {
        memcpy(pkt.payload, payload, len);
    }

    history_add(pkt.src, pkt.seq);

    esp_err_t err = lora_send((uint8_t *)&pkt, (uint8_t)(MESH_HEADER_SIZE + len));
    if (err == ESP_OK) {
        ESP_LOGD(TAG, "TX → 0x%02X  seq=%u  %u bytes", dst, pkt.seq, len);
    }
    return err;
}

void mesh_process(void)
{
    uint8_t  buf[LORA_MTU];
    uint8_t  wire_len = 0;
    int16_t  rssi     = 0;

    esp_err_t err = lora_recv(buf, &wire_len, &rssi);
    if (err == ESP_ERR_NOT_FOUND) {
        return;
    }
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "recv err: %s", esp_err_to_name(err));
        return;
    }
    if (wire_len < (uint8_t)MESH_HEADER_SIZE) {
        return;
    }

    mesh_packet_t *pkt = (mesh_packet_t *)buf;

    if (pkt->src == s_node_id) {
        return;   /* echo of our own rebroadcast */
    }
    if (pkt->len > wire_len - (uint8_t)MESH_HEADER_SIZE) {
        return;   /* malformed */
    }
    if (history_seen(pkt->src, pkt->seq)) {
        return;   /* already forwarded */
    }

    history_add(pkt->src, pkt->seq);
    ESP_LOGD(TAG, "RX from 0x%02X → 0x%02X  seq=%u  RSSI %d dBm",
             pkt->src, pkt->dst, pkt->seq, rssi);

    /* Deliver to local handler */
    if (pkt->dst == s_node_id || pkt->dst == MESH_BROADCAST) {
        mesh_type_t type = (mesh_type_t)((pkt->flags >> 2) & 0x0F);
        ESP_LOGI(TAG, "[0x%02X→0x%02X] %.*s  (RSSI %d dBm)",
                 pkt->src, pkt->dst, pkt->len, (char *)pkt->payload, rssi);
        if (s_rx_cb) {
            s_rx_cb(pkt->src, pkt->dst, type, pkt->payload, pkt->len);
        }
    }

    /* Flood-forward if TTL allows */
    if (pkt->ttl > 0) {
        pkt->ttl--;
        lora_send(buf, wire_len);
    }
}
