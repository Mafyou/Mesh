#include "mesh/mesh.h"
#include "hal/lora.h"

#include <string.h>
#include "esp_efuse.h"
#include "esp_log.h"
#include "esp_mac.h"
#include "esp_timer.h"
#include "aes/esp_aes.h"

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

/* ---- telemetry counters ---- */

static uint16_t s_tx_count;
static uint16_t s_rx_count;

uint16_t mesh_tx_count(void) { return s_tx_count; }
uint16_t mesh_rx_count(void) { return s_rx_count; }

/* ---- neighbor table ---- */

static mesh_neighbor_t s_neighbors[MESH_NEIGHBOR_MAX];
static uint8_t         s_neighbor_count;

static void neighbor_update(uint8_t node_id, int8_t rssi, int8_t snr)
{
    uint32_t now_s = (uint32_t)(esp_timer_get_time() / 1000000ULL);

    for (int i = 0; i < s_neighbor_count; i++) {
        if (s_neighbors[i].node_id == node_id) {
            s_neighbors[i].rssi        = rssi;
            s_neighbors[i].snr         = snr;
            s_neighbors[i].last_seen_s = now_s;
            return;
        }
    }
    if (s_neighbor_count < MESH_NEIGHBOR_MAX) {
        s_neighbors[s_neighbor_count++] = (mesh_neighbor_t){
            .node_id      = node_id,
            .rssi         = rssi,
            .snr          = snr,
            .last_seen_s  = now_s,
        };
    }
}

uint8_t mesh_neighbor_count(void) { return s_neighbor_count; }

uint8_t mesh_get_neighbors(mesh_neighbor_t *out, uint8_t max)
{
    uint8_t n = s_neighbor_count < max ? s_neighbor_count : max;
    for (uint8_t i = 0; i < n; i++) out[i] = s_neighbors[i];
    return n;
}

static bool is_direct_neighbor(uint8_t node_id)
{
    for (int i = 0; i < s_neighbor_count; i++) {
        if (s_neighbors[i].node_id == node_id) return true;
    }
    return false;
}

/* ---- crypto (AES-256-CTR per channel) ---- */

/*
 * Pre-computed SHA256 keys — one per channel.
 * Key[n] = SHA256(channel_name[n]), computed at build time.
 * Channel names (canonical, ASCII): "Public", "Equipe", "Urgence", "Prive"
 * The mobile app must derive keys the same way.
 */
static const uint8_t CHANNEL_KEYS[4][32] = {
    /* SHA256("Public")  */ {0x59,0x19,0x35,0xb1,0x5b,0x1c,0x88,0xe2,0xd5,0xf6,0xbe,0x0a,0x05,0x46,0x04,0xfc,0xf3,0x6f,0x05,0x85,0xa6,0xf5,0x10,0x98,0xfa,0x38,0x03,0x82,0x6f,0xff,0x27,0x8c},
    /* SHA256("Equipe")  */ {0xde,0x0f,0x23,0x9e,0xc4,0x27,0x44,0xd3,0x60,0x4a,0x16,0xb9,0x00,0x21,0x2b,0x0a,0x30,0x28,0x13,0xee,0x03,0x1c,0xbc,0xad,0xb6,0xbd,0x05,0x65,0x82,0x78,0x52,0xc3},
    /* SHA256("Urgence") */ {0xed,0xd8,0x9b,0x65,0x07,0xdd,0x1e,0x74,0x98,0xca,0x07,0xcf,0x02,0x1f,0x2d,0x86,0xb0,0x42,0xba,0xa0,0x59,0xeb,0xcb,0x44,0xd6,0x2c,0x5d,0x0c,0x20,0x79,0x34,0x9e},
    /* SHA256("Prive")   */ {0x72,0x5f,0x71,0xe1,0x7f,0xe9,0xe5,0x77,0xbc,0xa5,0x4f,0x25,0x57,0xce,0xde,0x1b,0x04,0xce,0x44,0xbe,0x70,0xe0,0xb3,0xdf,0x48,0xf7,0xfd,0x98,0x90,0x70,0x12,0x30},
};

/*
 * AES-256-CTR in-place encrypt/decrypt.
 * Nonce: [src:1B][seq_lo:1B][seq_hi:1B][0×13]
 * Encrypt == decrypt for CTR mode.
 */
static void mesh_crypt_payload(uint8_t channel, uint8_t src, uint16_t seq,
                               uint8_t *payload, uint8_t len)
{
    if (!len || channel > 3) return;

    uint8_t nonce[16] = {0};
    nonce[0] = src;
    nonce[1] = (uint8_t)(seq & 0xFF);
    nonce[2] = (uint8_t)(seq >> 8);

    uint8_t stream_block[16] = {0};
    size_t  nc_off = 0;

    esp_aes_context ctx;
    esp_aes_init(&ctx);
    esp_aes_setkey(&ctx, CHANNEL_KEYS[channel], 256);
    esp_aes_crypt_ctr(&ctx, len, &nc_off, nonce, stream_block, payload, payload);
    esp_aes_free(&ctx);
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

esp_err_t mesh_send(uint8_t dst, mesh_type_t type, uint8_t channel,
                    const uint8_t *payload, uint8_t len)
{
    if (len > MESH_PAYLOAD_MAX) {
        return ESP_ERR_INVALID_ARG;
    }

    mesh_packet_t pkt;
    memset(&pkt, 0, sizeof(pkt));
    pkt.flags = (uint8_t)(((uint8_t)type << 2) | (channel & MESH_CHANNEL_MAX));
    pkt.src   = s_node_id;
    pkt.dst   = dst;
    /* Use TTL=1 for known direct neighbors to avoid unnecessary flooding */
    pkt.ttl   = (dst != MESH_BROADCAST && is_direct_neighbor(dst)) ? 1 : MESH_TTL_DEFAULT;
    pkt.seq   = s_seq++;
    pkt.len   = len;
    if (payload && len) {
        memcpy(pkt.payload, payload, len);
    }

    history_add(pkt.src, pkt.seq);

    if ((type == MESH_TYPE_MSG || type == MESH_TYPE_PING) && len > 0) {
        mesh_crypt_payload(channel, pkt.src, pkt.seq, pkt.payload, pkt.len);
    }

    esp_err_t err = lora_send((uint8_t *)&pkt, (uint8_t)(MESH_HEADER_SIZE + len));
    if (err != ESP_OK) {
        return err;
    }

    s_tx_count++;
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
    int8_t   snr      = 0;

    esp_err_t err = lora_recv(buf, &wire_len, &rssi, &snr);

    if (err != ESP_ERR_NOT_FOUND && err == ESP_OK
        && wire_len >= (uint8_t)MESH_HEADER_SIZE) {

        mesh_packet_t *pkt    = (mesh_packet_t *)buf;
        mesh_type_t    type   = (mesh_type_t)((pkt->flags >> 2) & 0x0F);
        uint8_t        ch     = pkt->flags & MESH_CHANNEL_MAX;

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
        s_rx_count++;

        /* Track direct neighbors — only packets that haven't been relayed yet */
        if (pkt->ttl == MESH_TTL_DEFAULT) {
            neighbor_update(pkt->src, (int8_t)(rssi < -128 ? -128 : rssi), snr);
        }

        ESP_LOGD(TAG, "RX from 0x%02X → 0x%02X  seq=%u  RSSI %d dBm  SNR %d dB",
                 pkt->src, pkt->dst, pkt->seq, rssi, snr);

        /* ---- Flood-forward (payload still encrypted) ---- */
        if (pkt->ttl > 0) {
            pkt->ttl--;
            lora_send(buf, wire_len);
        }

        /* ---- Deliver to local handler ---- */
        if (pkt->dst == s_node_id || pkt->dst == MESH_BROADCAST) {

            if (type == MESH_TYPE_ACK) {
                /* ACK payload is not encrypted */
                if (pkt->len >= 2) {
                    uint16_t acked = (uint16_t)(pkt->payload[0]
                                    | ((uint16_t)pkt->payload[1] << 8));
                    pending_ack(acked);
                }

            } else {
                /* Decrypt MSG and PING payloads before delivery */
                if ((type == MESH_TYPE_MSG || type == MESH_TYPE_PING) && pkt->len > 0) {
                    mesh_crypt_payload(ch, pkt->src, pkt->seq, pkt->payload, pkt->len);
                }

                ESP_LOGI(TAG, "[0x%02X→0x%02X] %.*s  (RSSI %d dBm)",
                         pkt->src, pkt->dst, pkt->len, (char *)pkt->payload, rssi);

                if (s_rx_cb) {
                    s_rx_cb(pkt->src, pkt->dst, type, ch, pkt->payload, pkt->len);
                }

                /* ACK unicast MSG so the sender stops retrying */
                if (pkt->dst == s_node_id && type == MESH_TYPE_MSG) {
                    send_ack(pkt->src, pkt->seq);
                }
            }
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
        pkt.ttl   = is_direct_neighbor(s_pending[i].dst) ? 1 : MESH_TTL_DEFAULT;
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
