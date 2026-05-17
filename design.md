# Charte graphique Mesh United – Design System

Inspirée du fonctionnement terrain de **Mesh United** : communication offline, réseau maillé LoRa, simplicité d’usage, robustesse outdoor et esprit open source.

---

# Style directeur

- **Flat design moderne orienté terrain** : interface ultra lisible même en extérieur.
- Ambiance : technique mais accessible, robuste, minimaliste, rapide.
- Inspiration : radio terrain, applications d’urgence modernes, outils outdoor, interfaces réseau simplifiées.
- Objectif UX : communication instantanée sans surcharge visuelle.
- Priorité : lisibilité, contraste, autonomie batterie, fluidité.
- À éviter :
  - glassmorphisme lourd
  - animations excessives
  - interfaces “gaming”
  - surcharge d’options radio techniques

---

# Palette – Mode clair

- Fond principal        : #F5F7FA
- Fond secondaire       : #E9EEF4
- Carte / panneau       : #FFFFFF
- Texte principal       : #0F172A
- Texte secondaire      : #64748B
- Primaire réseau       : #2563EB
- Accent LoRa           : #16A34A
- Accent signal         : #0EA5E9
- Attention             : #D97706
- Succès                : #22C55E
- Erreur                : #DC2626
- Séparateur            : #DCE3EC

---

# Palette – Mode sombre

- Fond principal        : #0B1220
- Fond secondaire       : #121A2B
- Carte / panneau       : #182235
- Texte principal       : #F8FAFC
- Texte secondaire      : #94A3B8
- Primaire réseau       : #60A5FA
- Accent LoRa           : #4ADE80
- Accent signal         : #38BDF8
- Attention             : #F59E0B
- Succès                : #4ADE80
- Erreur                : #F87171
- Séparateur            : #243146

---

# Typographie

- Police principale : Inter, system-ui, sans-serif

- Titre principal   : 26sp / Bold
- Titre section     : 20sp / SemiBold
- Titre carte       : 18sp / SemiBold
- Texte standard    : 16sp / Regular
- Petit texte       : 14sp / Regular
- Micro label       : 12sp / Medium

Style :
- Texte toujours très lisible
- Contraste élevé
- Éviter les textes gris trop clairs en extérieur

---

# Espacements

- Espacement compact : 8
- Espacement normal  : 16
- Espacement large   : 24
- Padding carte      : 16
- Gap composants     : 12

---

# Formes et ombres

- Radius bouton/input : 12
- Radius carte        : 14
- Radius badge        : 999 (pill badges)
- Ombres              : très légères
- Ton visuel          : robuste, moderne, terrain, fiable

### Ombre light
- Shadow=True
- ShadowBrush=#18000000
- ShadowOffset=0,4
- ShadowRadius=10

### Ombre dark
- Shadow=True
- ShadowBrush=#30000000
- ShadowOffset=0,4
- ShadowRadius=10

---

# Composants MAUI — conventions

## Boutons

### Primaire
- BackgroundColor=Primaire réseau
- TextColor=White
- CornerRadius=12
- HeightRequest=50
- FontSize=16
- FontAttributes=Bold

### Secondaire
- BackgroundColor=Transparent
- BorderColor=Primaire réseau
- TextColor=Primaire réseau
- BorderWidth=1.5
- CornerRadius=12
- HeightRequest=50

### Danger
- BackgroundColor=Erreur
- TextColor=White
- CornerRadius=12

---

# Cartes

Utiliser `Border` au lieu de `Frame`.

- BackgroundColor=Carte
- Stroke=Séparateur
- StrokeThickness=1
- StrokeShape=RoundRectangle 14
- Padding=16

Types de cartes :
- Message reçu
- Nœud Mesh détecté
- Historique réseau
- État BLE
- État signal LoRa

---

# Inputs / formulaires

## Entry / Editor
- BackgroundColor=Fond secondaire
- CornerRadius=12
- Padding=12
- HeightRequest=50

## Labels
- Toujours au-dessus
- FontSize=14
- TextColor=Texte secondaire

---

# Badges / statuts

## Nœud connecté
- Fond : vert 20%
- Texte : Accent LoRa

## Hors ligne
- Fond : orange 20%
- Texte : Attention

## Déconnecté
- Fond : rouge 20%
- Texte : Erreur

## Relay actif
- Fond : bleu 20%
- Texte : Primaire réseau

---

# Séparateurs

- `BoxView`
- HeightRequest=1
- Color=Séparateur
- MarginTop=8
- MarginBottom=8

---

# Icônes

Style :
- minimalistes
- outline ou semi-filled
- Material Symbols recommandé

Icônes clés :
- signal
- bluetooth
- message
- route
- node
- antenna
- offline
- emergency

---

# Navigation MAUI — conventions

## TabBar bas

Maximum 4 onglets :

1. Réseau
2. Messages
3. Nœuds
4. Paramètres

### Couleurs
- Actif : Primaire réseau
- Inactif : Texte secondaire

### Icônes
- Taille : 24
- Style : Material Symbols Rounded

Pas de hamburger menu principal.

---

# Structure des écrans

## Réseau (`NetworkPage`)

Affiche :
- état BLE
- état LoRa
- nombre de nœuds
- portée estimée
- activité réseau

En haut :
- statut réseau live
- indicateur animé discret

---

## Messages (`MessagesPage`)

Structure type messagerie minimaliste.

### Message entrant
- aligné gauche
- fond secondaire

### Message utilisateur
- aligné droite
- fond primaire réseau
- texte blanc

Afficher :
- alias
- heure
- hop count éventuel

Input toujours fixé en bas.

---

## Nœuds (`NodesPage`)

Liste simple des nœuds détectés.

Chaque carte :
- nom du nœud
- ID
- RSSI
- dernière activité
- distance estimée

Quick actions :
- ping
- détails
- favoris

---

## Paramètres (`SettingsPage`)

Minimaliste.

Contient :
- alias utilisateur
- mode sombre
- informations firmware
- version app
- GitHub
- diagnostics BLE

Éviter les paramètres radio complexes.

---

# Animations MAUI — conventions

Utiliser uniquement :
- FadeToAsync
- ScaleToAsync
- TranslateToAsync

### Durées
- Feedback tap : 80ms
- Transition écran : 250ms
- Apparition carte : 180ms

Animations discrètes uniquement.

---

# Web (MeshUnited.Web) — conventions CSS

- CSS séparé uniquement
- Variables CSS globales
- Même palette que mobile
- Responsive mobile-first

---

# Variables CSS

```css
:root {
  --color-bg: #F5F7FA;
  --color-card: #FFFFFF;
  --color-primary: #2563EB;
  --color-lora: #16A34A;
  --color-text: #0F172A;
  --color-text-secondary: #64748B;
  --color-border: #DCE3EC;
}
```

---

# Gradients

## Gradient principal

```css
linear-gradient(135deg, #2563EB 0%, #16A34A 100%)
```

## Gradient sombre

```css
linear-gradient(135deg, #1E3A5F 0%, #14532D 100%)
```

## Gradient signal

```css
linear-gradient(135deg, #0EA5E9 0%, #2563EB 100%)
```

---

# États vides (Empty States)

Style :
- simple
- rassurant
- orienté action

### Exemple
- Icône radio : 56px
- Texte :
  “Aucun nœud détecté”
- CTA :
  “Relancer le scan”

---

# FAB (Floating Action Button)

Utilisé uniquement pour :
- nouveau message
- scan réseau

### Style
- Taille : 56×56
- Shape : cercle
- Fond : gradient principal
- Icône blanche 24px

---

# UX & philosophie produit

Mesh United doit donner l’impression :
- d’un outil fiable
- utilisable en urgence
- rapide