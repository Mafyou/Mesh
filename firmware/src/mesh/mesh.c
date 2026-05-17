#include "mesh/mesh.h"
#include "hal/lora.h"

#include <string.h>
#include "esp_efuse.h"
#include "esp_log.h"
#include "esp_mac.h"
#include "esp_timer.h"

static const char *TAG = "mesh";

/* ---- history (deduplication) ---- */

typedef struct {
    uint8_t  src;
    uint16_t seq;
} hist_entry_t;

static hist_entry_t s_history[MESH_HISTORY_SIZE];
static uint8_t      s_hist_head;

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

/* ---- pending queue (ACK / retransmission) ---- */

typedef struct {
    bool        used;
    uint16_t    seq;
    uint8_t     dst;
    mesh_type_t type;
    uint8_t     payload[MESH_PAYLOAD_MAX];
    uint8_t     len;
    int64_t     sent_at_ms;
    uint8_t     retries;
} pending_t;

static pending_t s_pending[MESH_PENDING_MAX];

static void pending_add(const mesh_packet_t *pkt)
{
    for (int i = 0; i < MESH_PENDING_MAX; i++) {
        if (!s_pending[i].used) {
            s_pending[i].used       = true;
            s_pending[i].seq        = pkt->seq;
            s_pending[i].dst        = pkt->dst;
            s_pending[i].type       = (mesh_type_t)((pkt->flags >> 2) & 0x0F);
            s_pending[i].len        = pkt->len;
            s_pending[i].sent_at_ms = esp_timer_get_time() / 1000;
            s_pending[i].retries    = 0;
            if (pkt->len) {
                memcpy(s_pending[i].payload, pkt->payload, pkt->len);
            }
            return;
        }
    }
    ESP_LOGW(TAG, "pending queue full — dropping");
}

static void pending_ack(uint16_t seq)
{
    for (int i = 0; i < MESH_PENDING_MAX; i++) {
        if (s_pending[i].used && s_pending[i].seq == seq) {
            s_pending[i].used = false;
            ESP_LOGD(TAG, "ACK  seq=%u", seq);
            return;
        }
    }
}

/* ---- node state ---- */

static uint8_t      s_node_id;
static uint16_t     s_seq;
static mesh_rx_cb_t s_rx_cb;

/* ---- internal ACK send ---- */

static void send_ack(uint8_t dst, uint16_t acked_seq)
{
    mesh_packet_t pkt;
    memset(&pkt, 0, sizeof(pkt));
    pkt.flags      = (uint8_t)(MESH_TYPE_ACK << 2);
    pkt.src        = s_node_id;
    pkt.dst        = dst;
    pkt.ttl        = MESH_TTL_DEFAULT;
    pkt.seq        = s_seq++;
    pkt.len        = 2;
    pkt.payload[0] = (uint8_t)(acked_seq & 0xFF);
    pkt.payload[1] = (uint8_t)(acked_seq >> 8);
    history_add(pkt.src, pkt.seq);
    lora_send((uint8_t *)&pkt, (uint8_t)(MESH_HEADER_SIZE + 2));
    ESP_LOGD(TAG, "ACK → 0x%02X  acked=%u", dst, acked_seq);
}

/* ---- Public API ---- */

esp_err_t mesh_init(uint8_t node_id)
{
    memset(s_history, 0, sizeof(s_history));
    memset(s_pending, 0, sizeof(s_pending));
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

uint8_t mesh_pending_count(void)
{
    uint8_t count = 0;
    for (int i = 0; i < MESH_PENDING_MAX; i++) {
        if (s_pending[i].used) count++;
    }
    return count;
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
    if (err != ESP_OK) {
        return err;
    }

    ESP_LOGD(TAG, "TX → 0x%02X  seq=%u  %u bytes", dst, pkt.seq, len);

    /* Track unicast MSG for retransmission */
    if (dst != MESH_BROADCAST && type == MESH_TYPE_MSG) {
        pending_add(&pkt);
    }

    return ESP_OK;
}

void mesh_process(void)
{
    /* ---- 1. Receive incoming LoRa packet ---- */

    uint8_t  buf[LORA_MTU];
    uint8_t  wire_len = 0;
    int16_t  rssi     = 0;

    esp_err_t err = lora_recv(buf, &wire_len, &rssi);

    if (err != ESP_ERR_NOT_FOUND && err == ESP_OK
        && wire_len >= (uint8_t)MESH_HEADER_SIZE) {

        mesh_packet_t *pkt  = (mesh_packet_t *)buf;
        mesh_type_t    type = (mesh_type_t)((pkt->flags >> 2) & 0x0F);

        if (pkt->src == s_node_id) {
            goto retransmit_check; /* echo of our own rebroadcast */
        }
        if (pkt->len > wire_len - (uint8_t)MESH_HEADER_SIZE) {
            goto retransmit_check; /* malformed */
        }

        if (history_seen(pkt->src, pkt->seq)) {
            /*
             * Duplicate — if it's a unicast MSG to us the remote is
             * probably waiting for an ACK that got lost; re-send it.
             */
            if (pkt->dst == s_node_id && type == MESH_TYPE_MSG) {
                send_ack(pkt->src, pkt->seq);
            }
            goto retransmit_check;
        }

        history_add(pkt->src, pkt->seq);

        ESP_LOGD(TAG, "RX from 0x%02X → 0x%02X  seq=%u  RSSI %d dBm",
                 pkt->src, pkt->dst, pkt->seq, rssi);

        /* ---- Deliver to local handler ---- */
        if (pkt->dst == s_node_id || pkt->dst == MESH_BROADCAST) {

            if (type == MESH_TYPE_ACK) {
                /* ACK: decode acked seq and clear from pending */
                if (pkt->len >= 2) {
                    uint16_t acked = (uint16_t)(pkt->payload[0]
                                    | ((uint16_t)pkt->payload[1] << 8));
                    pending_ack(acked);
                }

            } else {
                /* MSG or PING: deliver to application */
                ESP_LOGI(TAG, "[0x%02X→0x%02X] %.*s  (RSSI %d dBm)",
                         pkt->src, pkt->dst, pkt->len, (char *)pkt->payload, rssi);

                if (s_rx_cb) {
                    s_rx_cb(pkt->src, pkt->dst, type, pkt->payload, pkt->len);
                }

                /* ACK unicast MSG so the sender stops retrying */
                if (pkt->dst == s_node_id && type == MESH_TYPE_MSG) {
                    send_ack(pkt->src, pkt->seq);
                }
            }
        }

        /* ---- Flood-forward ---- */
        if (pkt->ttl > 0) {
            pkt->ttl--;
            lora_send(buf, wire_len);
        }
    }

retransmit_check:
    /* ---- 2. Retransmit timed-out pending messages ---- */

    int64_t now_ms = esp_timer_get_time() / 1000;

    for (int i = 0; i < MESH_PENDING_MAX; i++) {
        if (!s_pending[i].used) continue;
        if (now_ms - s_pending[i].sent_at_ms < MESH_RETRANSMIT_MS) continue;

        if (s_pending[i].retries >= MESH_RETRANSMIT_MAX) {
            ESP_LOGW(TAG, "drop  seq=%u  dst=0x%02X  (no ACK after %d retries)",
                     s_pending[i].seq, s_pending[i].dst, MESH_RETRANSMIT_MAX);
            s_pending[i].used = false;
            continue;
        }

        s_pending[i].retries++;
        s_pending[i].sent_at_ms = now_ms;

        mesh_packet_t pkt;
        memset(&pkt, 0, sizeof(pkt));
        pkt.flags = (uint8_t)((uint8_t)s_pending[i].type << 2);
        pkt.src   = s_node_id;
        pkt.dst   = s_pending[i].dst;
        pkt.ttl   = MESH_TTL_DEFAULT;
        pkt.seq   = s_pending[i].seq;
        pkt.len   = s_pending[i].len;
        if (s_pending[i].len) {
            memcpy(pkt.payload, s_pending[i].payload, s_pending[i].len);
        }

        ESP_LOGD(TAG, "retx  seq=%u  dst=0x%02X  attempt=%d",
                 pkt.seq, pkt.dst, s_pending[i].retries);
        lora_send((uint8_t *)&pkt, (uint8_t)(MESH_HEADER_SIZE + pkt.len));
    }
}
