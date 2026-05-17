# Mesh.Core — Implémentation de référence (LoRa + mesh)

> **Ce répertoire n'est pas le firmware actif.**
> Le firmware en production est dans [`firmware/`](../../firmware/).

## Rôle

`src/Mesh.Core` est la première itération du firmware Mesh United, développée avant l'intégration BLE. Elle couvre uniquement les couches radio et routage :

- Pilote LoRa SX1262 (868 MHz, SF10)
- OLED SSD1306
- Routage mesh : flooding TTL, déduplication sur ring buffer

Elle est conservée comme référence d'implémentation des couches basses. Le document [`intent_projet.md`](intent_projet.md) retrace les choix d'architecture initiaux.

## Différences avec `firmware/`

| Fonctionnalité | `src/Mesh.Core` | `firmware/` |
|---|---|---|
| LoRa + mesh routing | ✅ | ✅ |
| OLED | ✅ | ✅ |
| BLE (Nordic UART Service) | ❌ | ✅ |
| WiFi + WebSocket | ❌ | ✅ |
| Utilisé par la CI/CD | ❌ | ✅ |
| Flashé sur les nœuds | ❌ | ✅ |

## Pour flasher un nœud

Utiliser le répertoire `firmware/` à la racine du dépôt :

```sh
cd firmware
pio run -e heltec_wifi_lora_32_V3 -t upload
```
