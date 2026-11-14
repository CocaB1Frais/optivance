# OPTIVANCE

Utilitaire Windows tout-en-un, **100 % local** : aucune connexion, aucun compte, aucune télémétrie.

| Module | Ce qu'il fait |
|---|---|
| **Stockage** | Analyse d'un disque entier en quelques secondes (lecture directe de la MFT NTFS), arborescence, treemap interactive, types de fichiers, **gros fichiers et doublons**, suppression vers la Corbeille. |
| **Monitoring** | Processeur, carte graphique (NVIDIA), mémoire, disques, réseau, températures disponibles sans pilote ; **widget** transparent personnalisable avec alertes. |
| **Nettoyage** | Fichiers temporaires, caches, rapports d'erreurs ; nettoyage approfondi des emplacements Windows. Aperçu et confirmation avant toute suppression. |
| **Optimisation** | Démarrage, mode d'alimentation, réglages Windows, services et tâches planifiées, avec avertissement des risques et restauration. |
| **Historique** | Toutes les actions réalisées, conservées sur le PC. |

## Installation

1. Télécharger `OPTIVANCE-x.y.z-win-x64.zip` depuis la page **Releases**.
2. Extraire le dossier où vous voulez (Bureau, Documents, clé USB…).
3. Lancer **`OPTIVANCE.exe`**.

Rien d'autre à installer : .NET est inclus. Windows 10 (1809+) ou 11, 64 bits.

> Au premier lancement, Windows peut afficher « Windows a protégé votre ordinateur » (application non signée) :
> cliquer sur **Informations complémentaires** puis **Exécuter quand même**.

L'autorisation administrateur demandée au lancement (une fois par session) sert à l'analyse rapide des disques, au nettoyage approfondi et aux réglages système. Elle peut être refusée : OPTIVANCE fonctionne alors en mode standard.

## Mise à jour

Télécharger la nouvelle version et remplacer le dossier. Les paramètres et l'historique sont conservés dans `%LOCALAPPDATA%\OPTIVANCE`.
Paramètres › À propos ouvre la page des versions dans le navigateur (l'application elle-même ne se connecte à rien).

## Désinstallation

Supprimer le dossier d'OPTIVANCE, et `%LOCALAPPDATA%\OPTIVANCE` pour effacer paramètres et historique. Si elles ont été activées : décocher « Lancer avec Windows » et l'entrée du clic droit du Bureau avant de supprimer.

---

## Développement

| Dossier | Contenu |
|---|---|
| `src/Optivance.Core` | Logique pure (tests automatiques) |
| `src/Optivance.Infrastructure` | Accès Windows, client du moteur, composant administrateur |
| `src/Optivance.Desktop` | Interface WPF (.NET 10) |
| `engine/` | Moteur Rust : analyse classique, lecture MFT, composant administrateur de session |
| `tests/` | Tests automatiques |
| `tools/ScanProbe` | Outil de mesure et de diagnostic |
| `docs/` | Architecture, décisions, gouvernance, rapports de validation |

Prérequis : SDK .NET 10, Rust (rustup, cible `x86_64-pc-windows-msvc`).

```powershell
.\build.ps1          # tests puis version portable dans dist\OPTIVANCE\
```

### Publier une version

1. Modifier `<Version>` dans `Directory.Build.props` et ajouter la section correspondante dans `CHANGELOG.md`.
2. `git commit`, puis `git tag v1.2.3` et `git push --follow-tags`.
3. GitHub construit et publie automatiquement le zip dans **Releases**.

## Licence

© 2026 OPTIVANCE. Tous droits réservés — voir [LICENSE](LICENSE). Composants tiers : [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).
