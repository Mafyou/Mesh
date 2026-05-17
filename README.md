# Mesh United

Système de messagerie mesh sur ESP32 Heltec WiFi LoRa 32 V3, avec une application mobile .NET MAUI et une interface web Blazor.

## Architecture

```
┌─────────────────────────┐        BLE (NUS)        ┌──────────────────────┐
│  Firmware  Mesh.Core    │ ◄──────────────────────► │  App mobile MAUI     │
│  ESP-IDF / PlatformIO   │                          │  (iOS, Android, Win) │
│  C  –  LoRa SX1262      │  WebSocket /ws/node      └──────────────────────┘
│                         │ ◄──────────────────────► ┌──────────────────────┐
└─────────────────────────┘                          │  App web Blazor      │
          LoRa 868 MHz                               │  mesh-united.azurewebsites.net │
         ┌──────┴──────┐                             └──────────────────────┘
    Nœud 2          Nœud 3 …
```

## Projets

| Répertoire | Type | Description |
|---|---|---|
| `firmware/` | Firmware C (actif) | Firmware ESP32 en production : LoRa, BLE, OLED, WiFi, WebSocket |
| `src/Mesh.Core/` | Firmware C (référence) | Première itération : LoRa + mesh uniquement, sans BLE ni WiFi — conservé comme référence d'implémentation |
| `src/Mesh.Mobile` | .NET MAUI | App mobile multiplateforme |
| `src/Mesh.Mobile.Core` | Bibliothèque MAUI | Protocole partagé (encode/decode, BLE, paramètres) |
| `src/Mesh.Web` | Blazor Server+WASM | Interface web — <https://mesh-united.azurewebsites.net> |
| `tests/Mesh.Tests` | xUnit | Tests unitaires du protocole |
| `tests/Mesh.Mobile.Core.UnitTests` | xUnit | Tests unitaires de `MeshProtocol`, `SettingsService`, `NodeContact`, `MessageItem` |

## Firmware (ESP-IDF / PlatformIO)

Cible : **Heltec WiFi LoRa 32 V3**

### Couches

- **`hal/lora`** — pilote SX1262 (868 MHz, SF10)
- **`hal/oled`** — affichage SSD1306 I²C
- **`ble/ble`** — Nordic UART Service (NUS) via ESP-IDF BLE
- **`mesh/mesh`** — routage mesh léger avec ACK et retransmission
- **`wifi/wifi`** — station WiFi (bloquant jusqu'à l'obtention d'une IP)
- **`ws/ws_client`** — client WebSocket vers le serveur Blazor, avec reconnexion automatique

### Protocole mesh (wire format)

```
flags(1B) | src(1B) | dst(1B) | ttl(1B) | seq(2B LE) | len(1B) | payload[len]
```

- TTL par défaut : 3 (max 7), payload max : 220 octets
- Déduplication par historique circulaire (32 entrées)
- Types : `MSG(0)`, `ACK(1)`, `PING(2)` — Broadcast : `0xFF`
- ID de nœud : dernier octet de l'adresse MAC

### ACK et retransmission

Pour les messages unicast (`MSG`), le firmware :

1. Stocke le paquet dans une file d'attente (max 8 slots)
2. Le destinataire envoie un ACK (payload = seq original en LE 2B)
3. Si aucun ACK dans 2 s : retransmission automatique (max 3 tentatives)
4. Si un doublon arrive pour un unicast déjà vu : le destinataire renvoie l'ACK (idempotent)

Le broadcast n'est pas ACKé.

### Pont BLE ↔ mesh et WebSocket ↔ mesh

```
Mobile/Web → ESP  :  [dst 1B] [texte UTF-8]
ESP → Mobile/Web  :  [src 1B] [texte UTF-8]
```

- **BLE** : nœud annoncé `Mesh-XX`. Ping broadcast toutes les 10 s.
- **WebSocket** : premier frame d'identification `[nodeId 1B][nom UTF-8]`. Reconnexion auto.

### Activer la passerelle web (WiFi + WebSocket)

Créer un fichier `platformio_secrets.ini` (gitignore) :

```ini
[env:heltec_wifi_lora_32_V3_web]
platform = espressif32
board = heltec_wifi_lora_32_V3
framework = espidf
build_flags =
    -fno-ira-loop-pressure
    -DMESH_WEB_ENABLED
    -DWIFI_SSID=\"MonReseau\"
    -DWIFI_PASS=\"MonMotDePasse\"
    -DWS_URI=\"wss://mesh-united.azurewebsites.net/ws/node\"
```

Puis : `pio run -e heltec_wifi_lora_32_V3_web`

Le `sdkconfig.defaults` active `esp_websocket_client` et le bundle TLS (nécessaire pour `wss://`).

## Application mobile (Mesh.Mobile)

Framework : **.NET 10 MAUI** — iOS, Android, Windows.

### Pages

| Route Shell | Description |
|---|---|
| `NetworkPage` | Vue d'ensemble de la connectivité mesh et état BLE |
| `MessagesPage` | Feed en temps réel + envoi broadcast |
| `NodesPage` | Liste des nœuds découverts via BLE |
| `SettingsPage` | Alias utilisateur + activation des notifications |

### Fonctionnalités

- Scan BLE avec liste de nœuds découverts — l'utilisateur choisit le nœud à rejoindre
- Envoi de messages broadcast avec alias préfixé automatiquement (`alias: texte`)
- Réception et affichage des messages avec horodatage
- Page Paramètres (alias utilisateur et notifications, persistés dans `Preferences`)
- Indicateur d'état de connexion BLE
- Notifications push pour les messages entrants (`IMeshNotificationService`)
- Gestion des nœuds préférés (liste ordonnée, normalisée, persistée)

### Services (`Mesh.Mobile.Core`)

- `BleService` — scan, liste `DiscoveredDevices`, connexion explicite, NUS RX/TX
- `SettingsService` — alias, dernier nœud, nœuds préférés, formatage des messages sortants
- `IMeshNotificationService` / `ShinyNotificationService` — notifications push locales
- `MeshProtocol` — encode/decode les paquets BLE/mesh (`EncodeWrite`, `DecodeNotify`)

### Play Store

**Description courte (80 car. max)**
```
Messagerie radio mesh sans internet — LoRa + BLE sur ESP32
```

**Description longue**
```
Mesh United vous permet d'envoyer et recevoir des messages texte courts
sur un réseau radio maillé (mesh) basé sur la technologie LoRa, sans avoir
besoin d'internet ni d'infrastructure.

Chaque nœud du réseau est un ESP32 Heltec WiFi LoRa 32 V3 que vous pouvez
construire et flasher vous-même. L'application se connecte au nœud le plus
proche via Bluetooth Low Energy (BLE) et peut ainsi envoyer ou recevoir des
messages relayés de nœud en nœud, jusqu'à plusieurs kilomètres.

Fonctionnalités :
• Connexion BLE automatique aux nœuds Mesh détectés à proximité
• Envoi de messages en broadcast (vers tous les nœuds du réseau)
• Réception des messages avec horodatage et identifiant de source
• Alias personnalisable affiché dans les messages envoyés
• Interface simple et rapide, sans options techniques superflues
• Compatible Android et iOS

Mesh United est un projet open source. Le firmware et le code source de
l'application sont disponibles sur GitHub.

Cas d'usage : randonnée, événements outdoor, communication hors réseau,
expérimentation radio amateur, urgences.
```

## Application web (Mesh.Web)

URL : **<https://mesh-united.azurewebsites.net>**  
Framework : .NET 10 Blazor Web App (Server + WASM) — Azure Web App.

### Pages

| Route | Description |
|---|---|
| `/login` | Authentification par mot de passe (cookie 30 j) |
| `/messages` | Feed en temps réel + envoi broadcast (protégé) |
| `/nodes` | Liste des nœuds ESP32 connectés avec topologie (protégé) |
| `/flasher` | Flash firmware via Web Serial API (Chrome/Edge) |

### Architecture

- `IMeshService` — interface définie côté client (DTOs : `NodeInfo`, `MeshMessage`, `TopologyEdge`)
- `MeshService` — singleton serveur : WebSocket ESP32, 500 derniers messages, persistance JSON automatique (`mesh_history.json`), graphe de topologie calculé à la volée
- `/ws/node` — endpoint WebSocket pour les nœuds ESP32
- Auth : cookie ASP.NET Core — mot de passe configuré via app setting `MeshAdminPassword`

### Configuration Azure

| App Setting | Valeur |
|---|---|
| `MeshAdminPassword` | mot de passe de l'interface web |

## Tests

- `tests/Mesh.Tests` — xUnit + Shouldly + Moq : 46 tests couvrant `MeshProtocol`, `SettingsService`, `NodeContact`, `MessageItem`
- `tests/Mesh.Mobile.Core.UnitTests` — xUnit + Shouldly : 36 tests couvrant les mêmes modules (sans Moq)

## CI/CD (GitHub Actions)

1. **Tests** — workload Android, `Mesh.Tests` + `Mesh.Mobile.Core.UnitTests` (82 tests au total)
2. **Build firmware** — compile `firmware/` via PlatformIO, publie `firmware.bin` en artifact
3. **Publish Web** — publie Blazor en Release, intègre le firmware dans `wwwroot/firmware/`, déploie sur Azure Web App

Secrets GitHub requis : `AZURE_WEBAPP_NAME`, `AZURE_WEBAPP_PUBLISH_PROFILE`.

## Stack

| Domaine | Technologie |
|---|---|
| Firmware | C · ESP-IDF · PlatformIO |
| Matériel | Heltec WiFi LoRa 32 V3 · SX1262 · SSD1306 |
| Radio | LoRa 868 MHz SF10 |
| BLE | Nordic UART Service (NUS) |
| App mobile | .NET 10 MAUI |
| Interface web | Blazor Server + WebAssembly |
| Hébergement | Azure Web App |
| Tests | xUnit · Shouldly |
| CI/CD | GitHub Actions |
| IDE | Visual Studio Code |

## Ce qui reste à faire

- App mobile : notifications push avancées (multi-nœuds, routage par nœud)
- App web : historique par nœud, visualisation interactive du graphe de topologie
