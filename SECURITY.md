# Politique de sécurité

## Versions supportées

Seule la dernière version du firmware et des applications est activement maintenue et corrigée.

| Composant | Supporté |
|---|---|
| `firmware/` (branche `main`) | ✅ |
| `src/Mesh.Web` (branche `main`) | ✅ |
| `src/Mesh.Mobile` (branche `main`) | ✅ |
| Versions antérieures | ❌ |

## Signaler une vulnérabilité

**Ne pas ouvrir d'issue publique pour signaler une faille de sécurité.**

Si vous découvrez une vulnérabilité, merci de la signaler de manière responsable en envoyant un e-mail à l'adresse indiquée dans le profil GitHub du mainteneur. Incluez dans votre message :

- une description détaillée de la vulnérabilité ;
- les étapes pour la reproduire ;
- l'impact potentiel (confidentialité des messages, accès non autorisé au réseau mesh, etc.) ;
- si possible, une suggestion de correctif.

Vous recevrez un accusé de réception sous 72 heures. Une mise à jour sur l'avancement du traitement sera fournie dans les 7 jours.

## Périmètre

Ce projet est un système de messagerie radio sur réseau mesh local. Les points d'attention particuliers sont :

- **Chiffrement des messages** : les trames LoRa et BLE ne sont pas chiffrées dans la version actuelle — c'est une limitation documentée, pas une vulnérabilité à exploiter.
- **Authentification BLE** : tout appareil à portée peut se connecter au nœud ESP32 via BLE NUS — comportement intentionnel pour un réseau mesh ouvert.
- **Interface web** : l'interface Blazor ne gère pas de données utilisateur sensibles ; les firmwares exposés en téléchargement sont signés par le pipeline CI.

## Divulgation responsable

Une fois la vulnérabilité corrigée et une version publiée, vous serez mentionné dans les notes de version si vous le souhaitez.
