# Contribuer à Mesh United

Merci de l'intérêt que vous portez au projet. Ce guide explique comment contribuer efficacement.

## Table des matières

- [Signaler un bug](#signaler-un-bug)
- [Proposer une fonctionnalité](#proposer-une-fonctionnalité)
- [Mettre en place l'environnement de développement](#mettre-en-place-lenvironnement-de-développement)
- [Soumettre une pull request](#soumettre-une-pull-request)
- [Conventions de code](#conventions-de-code)

---

## Signaler un bug

Avant d'ouvrir une issue, vérifiez qu'elle n'existe pas déjà. Si elle n'existe pas, ouvrez-en une nouvelle en précisant :

- la version du firmware ou du SDK concerné ;
- le matériel utilisé (modèle d'ESP32, smartphone, OS) ;
- les étapes exactes pour reproduire le problème ;
- le comportement observé et le comportement attendu ;
- les logs pertinents (port série, Azure Log Stream, logcat Android, etc.).

---

## Proposer une fonctionnalité

Ouvrez une issue avec le label `enhancement` en décrivant :

- le problème que la fonctionnalité résoudrait ;
- l'approche envisagée ;
- les éventuelles contraintes matérielles ou protocole.

---

## Mettre en place l'environnement de développement

### Prérequis

| Outil | Version minimale |
|---|---|
| .NET SDK | 10.0 |
| PlatformIO CLI | dernière stable |
| Visual Studio / Rider | avec workload MAUI |
| Heltec WiFi LoRa 32 V3 | — |

### Cloner et compiler

```bash
git clone https://github.com/Mafyou/mesh-united.git
cd mesh-united
```

**Firmware**

```bash
cd firmware
pio run -e heltec_wifi_lora_32_V3
pio run -e heltec_wifi_lora_32_V3 -t upload
```

**Web**

```bash
dotnet run --project src/Mesh.Web/Mesh.Web/Mesh.Web.csproj
```

**Tests**

```bash
dotnet test
```

---

## Soumettre une pull request

1. Forkez le dépôt et créez une branche depuis `main` :
   ```bash
   git checkout -b fix/nom-du-correctif
   ```
2. Faites vos modifications en respectant les conventions ci-dessous.
3. Vérifiez que tous les tests passent (`dotnet test`).
4. Ouvrez une pull request vers `main` avec une description claire de ce qui change et pourquoi.

Les PR sont relues dans les meilleurs délais. Des retours peuvent être demandés avant la fusion.

---

## Conventions de code

### C (firmware)

- Nommage snake_case pour les fonctions et variables.
- Une fonction = une responsabilité. Pas de logique métier dans les ISR.
- Documenter les paramètres non évidents avec un commentaire court sur le pourquoi, pas le quoi.

### C# (.NET MAUI / Blazor)

- Suivre les conventions de nommage Microsoft (PascalCase pour les types, camelCase pour les variables locales).
- Utiliser les analyseurs de build (`<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`).
- Pas de commentaires redondants avec le nom de la méthode. Documenter uniquement les invariants non évidents.
- Chaque classe publique ajoutée doit avoir au moins un test unitaire.

### Commits

Format : `type: description courte en français`

Types acceptés : `fix`, `feat`, `refactor`, `test`, `docs`, `ci`, `chore`.

Exemple : `fix: correction du décodage du paquet broadcast vide`
