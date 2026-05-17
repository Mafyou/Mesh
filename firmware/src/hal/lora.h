#pragma once

#include <stdbool.h>
#include <stdint.h>
#include "esp_err.h"

/* SX1262 pins on Heltec WiFi LoRa 32 V3 */
#define LORA_SPI_SCK    9
#define LORA_SPI_MISO   11
#define LORA_SPI_MOSI   10
#define LORA_NSS_GPIO   8
#define LORA_RST_GPIO   12
#define LORA_BUSY_GPIO  13
#define LORA_DIO1_GPIO  14

/* EU868 defaults — BW 125 kHz, SF 10, CR 4/5 */
#define LORA_FREQ_HZ    868000000UL
#define LORA_BW_KHZ     125
#define LORA_SF         10
#define LORA_CR         5
#define LORA_PREAMBLE   8
#define LORA_TX_DBM     14

#define LORA_MTU        255

esp_err_t lora_init(void);
esp_err_t lora_send(const uint8_t *data, uint8_t len);
/* rssi_out and snr_out may be NULL if not needed */
esp_err_t lora_recv(uint8_t *data, uint8_t *len, int16_t *rssi_out, int8_t *snr_out);
bool      lora_busy(void);
