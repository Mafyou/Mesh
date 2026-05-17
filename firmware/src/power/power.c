#include "power/power.h"
#include "ble/ble.h"
#include "hal/lora.h"

#include "driver/gpio.h"
#include "esp_check.h"
#include "esp_log.h"
#include "esp_sleep.h"
#include "esp_timer.h"

static const char *TAG = "power";

static int64_t s_last_activity_us = 0;

esp_err_t power_init(void)
{
    /* Configure DIO1 as GPIO wakeup source (HIGH level = LoRa IRQ pending) */
    ESP_RETURN_ON_ERROR(
        gpio_wakeup_enable(LORA_DIO1_GPIO, GPIO_INTR_HIGH_LEVEL),
        TAG, "gpio_wakeup_enable");

    ESP_RETURN_ON_ERROR(
        esp_sleep_enable_gpio_wakeup(),
        TAG, "enable_gpio_wakeup");

    s_last_activity_us = esp_timer_get_time();
    ESP_LOGI(TAG, "light-sleep enabled  idle=%u s  wake=DIO1",
             POWER_IDLE_THRESHOLD_MS / 1000U);
    return ESP_OK;
}

void power_activity(void)
{
    s_last_activity_us = esp_timer_get_time();
}

void power_maybe_sleep(void)
{
    /* Stay awake while BLE is active (connected or advertising) so the device
     * remains discoverable. esp_light_sleep_start() suspends the BLE controller,
     * which would stop advertisements and hide the node from scans. */
    if (ble_connected() || ble_advertising()) {
        s_last_activity_us = esp_timer_get_time();
        return;
    }

    int64_t idle_us = esp_timer_get_time() - s_last_activity_us;
    if (idle_us < (int64_t)POWER_IDLE_THRESHOLD_MS * 1000LL) {
        return;
    }

    /* Don't sleep if a LoRa IRQ is already pending (DIO1 HIGH) */
    if (gpio_get_level(LORA_DIO1_GPIO)) {
        power_activity();
        return;
    }

    ESP_LOGD(TAG, "entering light sleep  idle=%.1f s", (double)idle_us / 1e6);
    esp_light_sleep_start();

    /* Woke up — reset idle timer so we don't re-enter immediately */
    s_last_activity_us = esp_timer_get_time();
    ESP_LOGD(TAG, "woke up");
}
