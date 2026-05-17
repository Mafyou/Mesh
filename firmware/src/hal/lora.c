#include "hal/lora.h"

#include <string.h>
#include "driver/gpio.h"
#include "driver/spi_master.h"
#include "esp_attr.h"
#include "esp_check.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char *TAG = "lora";

/* SX1262 opcodes */
#define OP_SET_SLEEP           0x84
#define OP_SET_STANDBY         0x80
#define OP_SET_TX              0x83
#define OP_SET_RX              0x82
#define OP_SET_PACKET_TYPE     0x8A
#define OP_SET_RF_FREQ         0x86
#define OP_SET_PA_CONFIG       0x95
#define OP_SET_TX_PARAMS       0x8E
#define OP_SET_MOD_PARAMS      0x8B
#define OP_SET_PKT_PARAMS      0x8C
#define OP_SET_BUF_BASE        0x8F
#define OP_SET_DIO_IRQ         0x08
#define OP_SET_DIO3_TCXO       0x97
#define OP_SET_REGULATOR       0x96
#define OP_CALIBRATE           0x89
#define OP_GET_IRQ_STATUS      0x12
#define OP_CLR_IRQ_STATUS      0x02
#define OP_GET_RX_BUF_STATUS   0x13
#define OP_GET_PKT_STATUS      0x14
#define OP_WRITE_BUFFER        0x0E
#define OP_READ_BUFFER         0x1E

/* BW register values */
#define SX_BW_125              0x04
#define SX_BW_250              0x05
#define SX_BW_500              0x06

/* IRQ bit masks */
#define IRQ_TX_DONE            (1u << 0)
#define IRQ_RX_DONE            (1u << 1)
#define IRQ_TIMEOUT            (1u << 9)
#define IRQ_CRC_ERR            (1u << 6)
#define IRQ_ALL                0x03FFu

#define SPI_HOST               SPI2_HOST
#define SPI_FREQ_HZ            (8 * 1000 * 1000)
#define BUSY_TIMEOUT_MS        500

static spi_device_handle_t s_spi;
static DMA_ATTR uint8_t    s_tx[260];
static DMA_ATTR uint8_t    s_rx[260];

/* ---- Low-level SPI helpers ---- */

static esp_err_t wait_busy(uint32_t ms)
{
    TickType_t deadline = xTaskGetTickCount() + pdMS_TO_TICKS(ms);
    while (gpio_get_level(LORA_BUSY_GPIO)) {
        if (xTaskGetTickCount() > deadline) {
            return ESP_ERR_TIMEOUT;
        }
        vTaskDelay(1);
    }
    return ESP_OK;
}

static esp_err_t sx_write(uint8_t opcode, const uint8_t *data, size_t len)
{
    ESP_RETURN_ON_ERROR(wait_busy(BUSY_TIMEOUT_MS), TAG, "busy");

    s_tx[0] = opcode;
    if (data && len) {
        memcpy(&s_tx[1], data, len);
    }

    spi_transaction_t t = {
        .length    = (1 + len) * 8,
        .tx_buffer = s_tx,
        .rx_buffer = NULL,
    };
    return spi_device_transmit(s_spi, &t);
}

static esp_err_t sx_read(uint8_t opcode, uint8_t *out, size_t len)
{
    ESP_RETURN_ON_ERROR(wait_busy(BUSY_TIMEOUT_MS), TAG, "busy");

    memset(s_tx, 0, 2 + len);
    s_tx[0] = opcode;

    spi_transaction_t t = {
        .length    = (2 + len) * 8,
        .tx_buffer = s_tx,
        .rx_buffer = s_rx,
    };
    ESP_RETURN_ON_ERROR(spi_device_transmit(s_spi, &t), TAG, "spi");
    if (out) {
        memcpy(out, &s_rx[2], len);
    }
    return ESP_OK;
}

static esp_err_t sx_write_buffer(uint8_t offset, const uint8_t *data, uint8_t len)
{
    ESP_RETURN_ON_ERROR(wait_busy(BUSY_TIMEOUT_MS), TAG, "busy");

    s_tx[0] = OP_WRITE_BUFFER;
    s_tx[1] = offset;
    memcpy(&s_tx[2], data, len);

    spi_transaction_t t = {
        .length    = (2 + len) * 8,
        .tx_buffer = s_tx,
        .rx_buffer = NULL,
    };
    return spi_device_transmit(s_spi, &t);
}

static esp_err_t sx_read_buffer(uint8_t offset, uint8_t *data, uint8_t len)
{
    ESP_RETURN_ON_ERROR(wait_busy(BUSY_TIMEOUT_MS), TAG, "busy");

    memset(s_tx, 0, 3 + len);
    s_tx[0] = OP_READ_BUFFER;
    s_tx[1] = offset;
    /* byte 2 = NOP, data starts at s_rx[3] */

    spi_transaction_t t = {
        .length    = (3 + len) * 8,
        .tx_buffer = s_tx,
        .rx_buffer = s_rx,
    };
    ESP_RETURN_ON_ERROR(spi_device_transmit(s_spi, &t), TAG, "spi");
    memcpy(data, &s_rx[3], len);
    return ESP_OK;
}

static uint16_t sx_get_irq(void)
{
    uint8_t buf[2] = {0};
    sx_read(OP_GET_IRQ_STATUS, buf, 2);
    return (uint16_t)((buf[0] << 8) | buf[1]);
}

static void sx_clear_irq(uint16_t mask)
{
    uint8_t p[2] = {(uint8_t)(mask >> 8), (uint8_t)(mask & 0xFF)};
    sx_write(OP_CLR_IRQ_STATUS, p, 2);
}

static esp_err_t sx_standby(void)
{
    uint8_t p = 0x00;   /* STDBY_RC */
    return sx_write(OP_SET_STANDBY, &p, 1);
}

static esp_err_t sx_enter_rx(void)
{
    /* Continuous RX: timeout = 0xFFFFFF */
    uint8_t p[3] = {0xFF, 0xFF, 0xFF};
    return sx_write(OP_SET_RX, p, 3);
}

/* ---- Public API ---- */

esp_err_t lora_init(void)
{
    /* GPIO: RST output, BUSY + DIO1 inputs */
    gpio_config_t out = {
        .pin_bit_mask = (1ULL << LORA_RST_GPIO),
        .mode         = GPIO_MODE_OUTPUT,
        .pull_up_en   = GPIO_PULLUP_DISABLE,
        .pull_down_en = GPIO_PULLDOWN_DISABLE,
        .intr_type    = GPIO_INTR_DISABLE,
    };
    ESP_ERROR_CHECK(gpio_config(&out));

    gpio_config_t in = {
        .pin_bit_mask = (1ULL << LORA_BUSY_GPIO) | (1ULL << LORA_DIO1_GPIO),
        .mode         = GPIO_MODE_INPUT,
        .pull_up_en   = GPIO_PULLUP_DISABLE,
        .pull_down_en = GPIO_PULLDOWN_DISABLE,
        .intr_type    = GPIO_INTR_DISABLE,
    };
    ESP_ERROR_CHECK(gpio_config(&in));

    /* SPI bus */
    spi_bus_config_t bus = {
        .mosi_io_num     = LORA_SPI_MOSI,
        .miso_io_num     = LORA_SPI_MISO,
        .sclk_io_num     = LORA_SPI_SCK,
        .quadwp_io_num   = -1,
        .quadhd_io_num   = -1,
        .max_transfer_sz = sizeof(s_tx),
    };
    ESP_RETURN_ON_ERROR(spi_bus_initialize(SPI_HOST, &bus, SPI_DMA_CH_AUTO), TAG, "spi bus");

    spi_device_interface_config_t dev = {
        .clock_speed_hz = SPI_FREQ_HZ,
        .mode           = 0,
        .spics_io_num   = LORA_NSS_GPIO,
        .queue_size     = 1,
    };
    ESP_RETURN_ON_ERROR(spi_bus_add_device(SPI_HOST, &dev, &s_spi), TAG, "spi dev");

    /* Hardware reset */
    gpio_set_level(LORA_RST_GPIO, 0);
    vTaskDelay(pdMS_TO_TICKS(2));
    gpio_set_level(LORA_RST_GPIO, 1);
    vTaskDelay(pdMS_TO_TICKS(10));
    ESP_RETURN_ON_ERROR(wait_busy(BUSY_TIMEOUT_MS), TAG, "post-reset busy");

    /* Standby RC */
    ESP_RETURN_ON_ERROR(sx_standby(), TAG, "standby");

    /* DIO3 → TCXO 1.8 V, 5 ms startup (320 × 15.625 µs = 0x000140) */
    uint8_t tcxo[4] = {0x02, 0x00, 0x01, 0x40};
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_DIO3_TCXO, tcxo, 4), TAG, "tcxo");

    /* Calibrate all blocks */
    uint8_t cal = 0x7F;
    ESP_RETURN_ON_ERROR(sx_write(OP_CALIBRATE, &cal, 1), TAG, "calibrate");
    vTaskDelay(pdMS_TO_TICKS(10));
    ESP_RETURN_ON_ERROR(wait_busy(BUSY_TIMEOUT_MS), TAG, "cal busy");

    /* Standby RC, DC-DC regulator */
    ESP_RETURN_ON_ERROR(sx_standby(), TAG, "standby2");
    uint8_t reg = 0x01;
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_REGULATOR, &reg, 1), TAG, "regulator");

    /* LoRa packet type */
    uint8_t pkt_type = 0x01;
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_PACKET_TYPE, &pkt_type, 1), TAG, "pkt type");

    /* RF frequency */
    uint32_t fq = (uint32_t)((uint64_t)LORA_FREQ_HZ * (1u << 25) / 32000000UL);
    uint8_t freq[4] = {
        (uint8_t)(fq >> 24),
        (uint8_t)(fq >> 16),
        (uint8_t)(fq >> 8),
        (uint8_t)(fq),
    };
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_RF_FREQ, freq, 4), TAG, "freq");

    /* PA config: SX1262 high-power PA, 22 dBm capable */
    uint8_t pa[4] = {0x04, 0x07, 0x00, 0x01};
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_PA_CONFIG, pa, 4), TAG, "pa cfg");

    /* TX params: configured power, 200 µs ramp */
    uint8_t txp[2] = {(uint8_t)LORA_TX_DBM, 0x04};
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_TX_PARAMS, txp, 2), TAG, "tx params");

    /* Modulation: SF, BW 125 kHz, CR 4/5, LDRO off (SF10 symbol < 16 ms) */
    uint8_t mod[4] = {(uint8_t)LORA_SF, SX_BW_125, 0x01, 0x00};
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_MOD_PARAMS, mod, 4), TAG, "mod params");

    /* Packet params: preamble 8, explicit header, placeholder len, CRC on, IQ normal */
    uint8_t pkt[6] = {
        (uint8_t)(LORA_PREAMBLE >> 8),
        (uint8_t)(LORA_PREAMBLE),
        0x00,   /* explicit header */
        0xFF,   /* payload len placeholder, updated at each TX */
        0x01,   /* CRC on */
        0x00,   /* standard IQ */
    };
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_PKT_PARAMS, pkt, 6), TAG, "pkt params");

    /* TX buffer at 0x00, RX buffer at 0x80 */
    uint8_t base[2] = {0x00, 0x80};
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_BUF_BASE, base, 2), TAG, "buf base");

    /* Map all IRQs to DIO1 */
    uint8_t irq[8] = {
        (uint8_t)(IRQ_ALL >> 8), (uint8_t)(IRQ_ALL),   /* global mask */
        (uint8_t)(IRQ_ALL >> 8), (uint8_t)(IRQ_ALL),   /* DIO1 mask */
        0x00, 0x00,                                      /* DIO2 mask: none */
        0x00, 0x00,                                      /* DIO3 mask: none */
    };
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_DIO_IRQ, irq, 8), TAG, "irq cfg");

    /* Enter continuous RX */
    ESP_RETURN_ON_ERROR(sx_enter_rx(), TAG, "enter rx");

    ESP_LOGI(TAG, "SX1262 ready — %lu Hz  SF%d  BW%d kHz  +%d dBm",
             LORA_FREQ_HZ, LORA_SF, LORA_BW_KHZ, LORA_TX_DBM);
    return ESP_OK;
}

esp_err_t lora_send(const uint8_t *data, uint8_t len)
{
    if (!data || len == 0) {
        return ESP_ERR_INVALID_ARG;
    }

    /* Exit RX → standby */
    ESP_RETURN_ON_ERROR(sx_standby(), TAG, "standby");

    /* Update payload length in packet params */
    uint8_t pkt[6] = {
        (uint8_t)(LORA_PREAMBLE >> 8),
        (uint8_t)(LORA_PREAMBLE),
        0x00, len, 0x01, 0x00,
    };
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_PKT_PARAMS, pkt, 6), TAG, "pkt len");

    /* Write payload to TX buffer (base address 0x00) */
    ESP_RETURN_ON_ERROR(sx_write_buffer(0x00, data, len), TAG, "write buf");

    /* SetTx: no timeout */
    uint8_t tx_timeout[3] = {0x00, 0x00, 0x00};
    ESP_RETURN_ON_ERROR(sx_write(OP_SET_TX, tx_timeout, 3), TAG, "set tx");

    /* Poll for TxDone */
    TickType_t deadline = xTaskGetTickCount() + pdMS_TO_TICKS(5000);
    for (;;) {
        uint16_t irq = sx_get_irq();
        if (irq & IRQ_TX_DONE) {
            sx_clear_irq(IRQ_ALL);
            break;
        }
        if (xTaskGetTickCount() > deadline) {
            sx_clear_irq(IRQ_ALL);
            sx_enter_rx();
            return ESP_ERR_TIMEOUT;
        }
        vTaskDelay(1);
    }

    /* Back to continuous RX */
    return sx_enter_rx();
}

esp_err_t lora_recv(uint8_t *data, uint8_t *len_out, int16_t *rssi_out, int8_t *snr_out)
{
    if (!data || !len_out) {
        return ESP_ERR_INVALID_ARG;
    }

    uint16_t irq = sx_get_irq();

    if (!(irq & (IRQ_RX_DONE | IRQ_CRC_ERR | IRQ_TIMEOUT))) {
        return ESP_ERR_NOT_FOUND;   /* nothing received yet */
    }

    sx_clear_irq(IRQ_ALL);

    if (irq & IRQ_TIMEOUT) {
        sx_enter_rx();
        return ESP_ERR_TIMEOUT;
    }
    if (irq & IRQ_CRC_ERR) {
        return ESP_ERR_INVALID_CRC;
    }

    /* RxBufferStatus: [payload_len, rx_start_ptr] */
    uint8_t status[2] = {0};
    ESP_RETURN_ON_ERROR(sx_read(OP_GET_RX_BUF_STATUS, status, 2), TAG, "rx status");

    uint8_t payload_len = status[0];
    uint8_t rx_ptr      = status[1];

    if (payload_len == 0) {
        return ESP_ERR_INVALID_SIZE;
    }

    ESP_RETURN_ON_ERROR(sx_read_buffer(rx_ptr, data, payload_len), TAG, "read buf");
    *len_out = payload_len;

    if (rssi_out || snr_out) {
        uint8_t pkt_status[3] = {0};
        sx_read(OP_GET_PKT_STATUS, pkt_status, 3);
        /* LoRa: rssiPkt = -pkt_status[0] / 2 ; snrPkt = pkt_status[1] / 4 (signed) */
        if (rssi_out) *rssi_out = -(int16_t)pkt_status[0] / 2;
        if (snr_out)  *snr_out  = (int8_t)pkt_status[1] / 4;
    }

    return ESP_OK;
}

bool lora_busy(void)
{
    return gpio_get_level(LORA_BUSY_GPIO) != 0;
}
