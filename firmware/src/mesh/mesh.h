#pragma once

#include <stdint.h>
#include "esp_err.h"

#define MESH_BROADCAST       0xFF
#define MESH_TTL_DEFAULT     3
#define MESH_TTL_MAX         7
#define MESH_PAYLOAD_MAX     220
#define MESH_HISTORY_SIZE    32

/* ACK / retransmission */
#define MESH_PENDING_MAX     8      /* max in-flight unicast messages */
#define MESH_RETRANSMIT_MS   2000   /* ms between retransmit attempts  */
#define MESH_RETRANSMIT_MAX  3      /* drop after this many retries     */

typedef enum {
    MESH_TYPE_MSG       = 0,
    MESH_TYPE_ACK       = 1,
    MESH_TYPE_PING      = 2,
    MESH_TYPE_NEIGHBORS = 4,
} mesh_type_t;

/*
 * PING payload (10 bytes, little-endian):
 *   [uptime_s:4B][vbat_mV:2B][tx_pkts:2B][rx_pkts:2B]
 */
#define MESH_PING_PAYLOAD_SIZE  10

#define MESH_CHANNEL_DEFAULT  0
#define MESH_CHANNEL_MAX      3   /* 2 bits → channels 0–3 */

/*
 * Wire format (packed):
 *   flags   [7:6] version=0  [5:2] type  [1:0] channel (0–3)
 *   src     source node ID (1 byte, 0x00-0xFE; 0xFF = broadcast)
 *   dst     destination node ID (0xFF = broadcast)
 *   ttl     decremented at each hop, dropped when reaches 0
 *   seq     16-bit sequence number (little-endian) for deduplication
 *   len     payload length in bytes
 *   payload[len]
 *
 * ACK payload: 2 bytes little-endian sequence number being acknowledged.
 * Channel 0 is the default public channel (backward-compatible with old firmware).
 */
typedef struct __attribute__((packed)) {
    uint8_t  flags;
    uint8_t  src;
    uint8_t  dst;
    uint8_t  ttl;
    uint16_t seq;
    uint8_t  len;
    uint8_t  payload[MESH_PAYLOAD_MAX];
} mesh_packet_t;

#define MESH_HEADER_SIZE  (sizeof(mesh_packet_t) - MESH_PAYLOAD_MAX)

typedef void (*mesh_rx_cb_t)(uint8_t src, uint8_t dst, mesh_type_t type, uint8_t channel,
                             const uint8_t *payload, uint8_t len);

/* node_id = 0 → auto-assigned from the last byte of the ESP32 base MAC */
esp_err_t mesh_init(uint8_t node_id);
uint8_t   mesh_node_id(void);
void      mesh_set_rx_cb(mesh_rx_cb_t cb);
esp_err_t mesh_send(uint8_t dst, mesh_type_t type, uint8_t channel,
                    const uint8_t *payload, uint8_t len);
void      mesh_process(void);

/* Number of unicast messages currently waiting for ACK */
uint8_t   mesh_pending_count(void);

/* ---- Telemetry counters (updated by mesh layer) ---- */
uint16_t  mesh_tx_count(void);
uint16_t  mesh_rx_count(void);

/* ---- Neighbor table ---- */
#define MESH_NEIGHBOR_MAX  16

typedef struct {
    uint8_t  node_id;
    int8_t   rssi;     /* dBm, signed */
    int8_t   snr;      /* dB, signed  */
    uint32_t last_seen_s;  /* seconds since ESP32 boot */
} mesh_neighbor_t;

uint8_t mesh_neighbor_count(void);
/* Copies up to MESH_NEIGHBOR_MAX entries into out[]; returns actual count */
uint8_t mesh_get_neighbors(mesh_neighbor_t *out, uint8_t max);
