# Intent du projet — Mesh.Core

## Rôle dans l'architecture

Mesh.Core est la couche firmware du système Mesh.
Il tourne sur le Heltec WiFi LoRa 32 V3 et constitue le cœur réseau et matériel du projet.
Il ne gère pas l'UX finale : il expose des primitives fiables que l'application mobile consomme.

## Ce que ce composant doit faire

- Piloter le matériel du Heltec v3 : OLED, LoRa SX1262, GPIO, LED.
- Implémenter la couche transport LoRa : envoi, réception, acquittement.
- Gérer le routage mesh : propagation des messages, déduplication, TTL.
- Exposer une interface BLE pour la communication avec l'application mobile.
- Maintenir un état minimal du nœud : identité, voisins, file d'envoi.

## Ce que ce composant ne fait pas

- Il ne prend pas de décisions d'UX.
- Il ne stocke pas d'historique de messages au-delà du nécessaire au routage.
- Il ne gère pas de logique applicative propre à un usage particulier.

## Stack technique

- Langage : C (ESP-IDF natif).
- Framework : ESP-IDF via PlatformIO.
- Cible matérielle : Heltec WiFi LoRa 32 V3.
- IDE : Visual Studio Code + PlatformIO.

## Protocole mesh

Inspiré de MeshCore — format binaire compact, sans protobuf.

| Champ   | Taille | Rôle |
|---------|--------|------|
| flags   | 1 B    | version (2b) + type (4b) + réservé (2b) |
| src     | 1 B    | ID du nœud source (0x00–0xFE) |
| dst     | 1 B    | ID de destination (0xFF = broadcast) |
| ttl     | 1 B    | décrémenté à chaque saut, supprimé à 0 |
| seq     | 2 B    | numéro de séquence pour la déduplication |
| len     | 1 B    | longueur du payload |
| payload | ≤ 220 B | contenu applicatif |

Total max : 227 B, bien dans le MTU LoRa de 255 B.

## Radio LoRa (EU868)

| Paramètre  | Valeur   |
|------------|----------|
| Fréquence  | 868 MHz  |
| SF         | 10       |
| BW         | 125 kHz  |
| CR         | 4/5      |
| Préambule  | 8 sym.   |
| Puissance  | +14 dBm  |

## Architecture du code

```
src/
├── main.c              — init OLED + LoRa + Mesh, boucle principale
├── hal/
│   ├── oled.h / .c     — SSD1306 I2C, police ASCII 5×7, 128×64 px
│   ├── lora.h / .c     — SX1262 SPI : init, send, recv (RX continu)
└── mesh/
    ├── mesh.h / .c     — flooding TTL, déduplication séquentielle
```

## État actuel

| Module       | État        | Détail |
|-------------|-------------|--------|
| OLED        | ✓ Opérationnel | SSD1306 I2C, police complète ASCII 0x20–0x7E |
| LoRa SX1262 | ✓ Implémenté   | SPI, init TCXO, calibration, send/recv polling |
| Routage mesh | ✓ Implémenté  | Flooding + dédup sur ring buffer 32 entrées |
| BLE          | ✗ Non démarré | Prévu avec NimBLE (ESP-IDF natif) |
| File TX      | ✗ Non démarré | Polling simple pour l'instant |

## Prochaines étapes

1. Flasher et tester le LoRa point-à-point entre deux nœuds.
2. Valider le flooding mesh à 3 nœuds.
3. Ajouter BLE NimBLE — canal vers l'app mobile.
4. Définir le protocole BLE (GATT service, caractéristiques TX/RX).
5. Ajouter ACK et retransmission au niveau mesh.
6. Gérer la table de voisinage (RSSI, dernier vu, next-hop).

## Principes de développement

- Écrire du C propre, sans dépendances Arduino.
- Chaque couche est isolée dans son propre module.
- Les erreurs remontent toujours via `esp_err_t`.
- Le code doit compiler sans warning avec les flags ESP-IDF par défaut.
- Tester chaque pilote matériel de façon autonome avant intégration.
