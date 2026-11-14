<p align="center"><img src="source/Optivance.Desktop/Assets/logo.png" alt="OPTIVANCE" height="64"></p>

<p align="center">
  <a href="https://github.com/CocaB1Frais/optivance/releases/latest/download/OPTIVANCE-win-x64.zip"><img src="https://img.shields.io/badge/T%C3%A9l%C3%A9charger-OPTIVANCE%20pour%20Windows-2E765C?style=for-the-badge&logo=windows&logoColor=white" alt="Télécharger OPTIVANCE"></a>
</p>

<p align="center">Utilitaire Windows tout-en-un, <b>100 % local</b> : aucune connexion, aucun compte, aucune télémétrie.</p>

| Module | Ce qu'il fait |
|---|---|
| **Stockage** | Analyse d'un disque entier en quelques secondes (lecture directe de la MFT NTFS), arborescence, treemap interactive, types de fichiers, **gros fichiers et doublons**, suppression vers la Corbeille. |
| **Monitoring** | Processeur, carte graphique (NVIDIA), mémoire, disques, réseau, températures disponibles sans pilote ; **widget** transparent personnalisable avec alertes. |
| **Nettoyage** | Fichiers temporaires, caches, rapports d'erreurs ; nettoyage approfondi des emplacements Windows. Aperçu et confirmation avant toute suppression. |
| **Optimisation** | Démarrage, mode d'alimentation, réglages Windows, services et tâches planifiées, avec avertissement des risques et restauration. |
| **Historique** | Toutes les actions réalisées, conservées sur le PC. |

## Installation

1. Cliquer sur **Télécharger** ci-dessus (ou page **Releases**) : `OPTIVANCE-win-x64.zip`.
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
| `source/Optivance.Desktop` | Interface WPF (.NET 10) |
| `source/Optivance.Core` | Logique pure (tests automatiques) |
| `source/Optivance.Infrastructure` | Accès Windows, client du moteur, composant administrateur |
| `source/engine` | Moteur Rust : analyse classique, lecture MFT, composant administrateur de session |
| `source/tests` | Tests automatiques |
| `source/tools/ScanProbe` | Outil de mesure et de diagnostic |
| `docs/` | Architecture, décisions, gouvernance, rapports de validation |

Prérequis : SDK .NET 10, Rust (rustup, cible `x86_64-pc-windows-msvc`).

```powershell
.\build.ps1          # tests puis OPTIVANCE.exe à la racine + zip dans dist\
```

### Publier une version

1. Modifier `<Version>` dans `Directory.Build.props` et ajouter la section correspondante dans `CHANGELOG.md`.
2. `git commit`, puis `git tag v1.2.3` et `git push --follow-tags`.
3. GitHub construit et publie automatiquement le zip dans **Releases**.

## Licence

© 2026 OPTIVANCE. Tous droits réservés — voir [LICENSE](LICENSE). Composants tiers : [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).
