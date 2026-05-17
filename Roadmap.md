# Roadmap — Features à forte valeur utilisateur

---

## Priorité 1 — Impact direct et immédiat pour l'utilisateur

### ✅ Light Sleep ESP32
- **Pourquoi c'est important** : Un nœud qui tient une journée est inutilisable en randonnée ou en urgence. Passer de ~12h à ~100h d'autonomie change radicalement les cas d'usage.
- **Firmware** : Activer `esp_light_sleep_start()` après N secondes sans trafic — réveil sur interrupt GPIO DIO1 du SX1262
- **Impact app** : Aucun changement nécessaire, totalement transparent
- _Source : Meshtastic (Power Saving Mode ESP32)_

### ✅ QR Code partage de nœud
- **Pourquoi c'est important** : Aujourd'hui, partager un nœud demande de dicter un ID hexadécimal. Un QR code rend l'onboarding instantané sur le terrain ou lors d'événements — c'est le premier contact avec l'app qui doit être fluide.
- **App** : Générer un QR code `meshunited://node/add?id=XX&alias=NomNoeud` dans `NodesPage` — le scanner dans `SettingsPage` ajoute le nœud aux favoris automatiquement
- _Source : MeshCore (QR code deep linking)_

### ✅ Canaux (Channels)
- **Pourquoi c'est important** : Sans canaux, tout le monde voit tout. Un canal "équipe secours" séparé d'un canal public est le besoin n°1 dès qu'on dépasse 3 personnes.
- **Firmware** : Ajouter 1 octet `channel_id` dans le header — canal 0 = broadcast actuel (rétrocompatible)
- **App** : Sélecteur de canal + filtrage dans `MessagesPage`
- _Sources : Meshtastic (channels), MeshCore (group messages)_

### ✅ Chiffrement AES-256-CTR par canal
- **Pourquoi c'est important** : Sans chiffrement, n'importe quel nœud passif dans la zone peut lire tous les messages. Indispensable pour les cas d'usage "équipe" ou "urgence".
- **Firmware** : Clés pré-calculées `SHA256(nom_canal)` ; AES-256-CTR, nonce `[src:1B][seq:2B]` ; relay opaque (les relayeurs ne voient que des bytes chiffrés)
- **App** : Aucun changement — BLE chiffré par le système, firmware gère tout
- _Source : Meshtastic (PSK channels, SHA256 key derivation)_

### ✅ Horodatage d'émission dans les messages
- **Pourquoi c'est important** : Si un message arrive 5 minutes après avoir été envoyé (le réseau mesh est lent), l'utilisateur doit savoir quand il a été écrit — pas quand son téléphone l'a reçu. C'est une question de confiance dans l'outil.
- **Firmware** : Ajouter 4 octets Unix epoch en début de payload : `[timestamp:4B LE][texte UTF-8]`
- **App** : Remplacer l'horodatage de réception par celui embarqué dans le paquet
- _Source : MeshCore (plain text messages with timestamp)_

---

## Priorité 2 — Valeur réelle mais moins visible

### ✅ Table de voisinage (Neighbor Table)
- **Pourquoi c'est utile** : Savoir si son nœud "voit" directement d'autres nœuds avant d'envoyer — évite la frustration de ne pas savoir si le réseau fonctionne.
- **Firmware** : Ring buffer 16 entrées `{node_id, rssi, snr, last_seen_ts}` — mis à jour à chaque paquet reçu — exposé via un type `NEIGHBORS(4)`
- **App** : Colonne "Lien direct ✓" avec barres de signal dans `NodesPage`
- _Sources : MeshCore (neighbor tables), Meshtastic (NeighborInfo module)_

### ✅ Télémétrie dans le PING (batterie + uptime)
- **Pourquoi c'est utile** : En déploiement de terrain, savoir qu'un nœud relayeur est à 10% de batterie permet d'anticiper une coupure réseau.
- **Firmware** : Étendre le payload `PING(2)` → `[uptime:4B][vbat_mV:2B][tx_pkts:2B][rx_pkts:2B]`
- **App** : Badge batterie + uptime dans `NodesPage`
- _Sources : MeshCore (CMD_GET_STATS), Meshtastic (Telemetry module)_

### ✅ Routage direct après apprentissage
- **Pourquoi c'est utile** : Invisible pour l'utilisateur, mais réduit les collisions et améliore la fiabilité sur les réseaux avec 5+ nœuds — il sent la différence sans savoir pourquoi.
- **Firmware** : Mémoriser la route retournée par l'ACK, l'utiliser pour les messages suivants au même destinataire
- _Source : MeshCore (learned path routing)_

---

## Compatibilité MeshCore

### ✅ Support nœuds MeshCore dans l'app (BLE)
- **Pourquoi** : MeshCore est le protocol le plus proche du nôtre — permettre à des utilisateurs MeshCore de rejoindre l'app sans reflasher leur nœud
- **App** : Détection automatique au scan, handshake `CMD_APP_START`, encode/decode packets MeshCore (`0x03` send, `0x08` recv)
- **Firmware** : Aucun changement — les nœuds Mesh United continuent de fonctionner normalement
- **Clé** : Même UUIDs NUS (`6E400001/02/03`) pour les deux protocols
- _Source : MeshCore BLE Companion Protocol_

---

## Priorité 3 — Outils techniques / power users

### Traceroute
- Visualiser le chemin exact d'un message dans le mesh
- Nouveau type `TRACE(5)` — chaque relayeur append son ID — le destinataire renvoie le chemin
- Utile pour déboguer la topologie, peu pertinent pour l'utilisateur lambda
- _Sources : Meshtastic (Traceroute), MeshCore (returned path packets)_

### RSSI + SNR par message
- Indicateur de qualité signal dans `MessagesPage` — utile pour les radio-amateurs, bruit pour les autres
- **Firmware** : Frame BLE/WS étendu : `[src:1B][rssi:1B][snr:1B][texte UTF-8]`
- _Sources : MeshCore (radio stats), Meshtastic (SNR v3)_

### RSSI/SNR sur l'OLED
- Afficher le signal du dernier paquet reçu sur l'écran du boîtier
- Utile uniquement pour les intégrateurs/installateurs

### Détection de boucle avancée
- Modes `off / minimal / strict` sur le ring buffer de dédup (actuellement figé à 32 entrées)
- Invisible pour l'utilisateur, utile uniquement sur grands réseaux
- _Source : MeshCore (loop detection modes)_

### Passerelle MQTT
- Sur `MESH_WEB_ENABLED`, publier les messages sur `mesh/messages/{node_id}` en MQTT
- Intégration Node-RED, dashboards IoT — public très restreint
- _Source : Meshtastic (MQTT gateway)_
