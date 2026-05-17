#pragma once

#include <stdint.h>
#include "esp_err.h"

#define OLED_WIDTH      128
#define OLED_PAGES      8
#define OLED_CHAR_WIDTH 6

esp_err_t oled_init(void);
esp_err_t oled_clear(void);
esp_err_t oled_draw_text(uint8_t page, uint8_t col, const char *text);
