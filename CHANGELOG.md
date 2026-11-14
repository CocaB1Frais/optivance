# Journal des versions

## 1.0.1 — 2026-11-14

- Dépôt réorganisé : tout le code dans `source/`, racine réduite à l'essentiel.
- Bouton « Télécharger » en tête de la page GitHub ; archive au nom fixe `OPTIVANCE-win-x64.zip`.
- `build.ps1` place `OPTIVANCE.exe` directement à la racine du dossier.

## 1.0.0 — 2026-11-14

Première version distribuée.

- **Stockage** : analyse MFT directe (une autorisation par session), repli automatique en analyse standard, arborescence, treemap avec cadres de dossiers, types de fichiers, suppression (Corbeille ou définitive), onglet **Gros fichiers et doublons** (filtres taille / date / extension, doublons vérifiés par contenu).
- **Monitoring** : capteurs sans pilote, widget transparent (indicateurs au choix, mode réduit, déplaçable), alertes température et espace disque, raccourcis globaux.
- **Nettoyage** : standard (Corbeille) et approfondi (emplacements Windows), aperçu et confirmation.
- **Optimisation** : alimentation, réglages Windows, démarrage, services et tâches planifiées ; niveau de risque, sauvegarde et restauration.
- **Historique** et **Paramètres** locaux ; zone de notification ; barre de titre intégrée.
- Version portable : un seul `OPTIVANCE.exe` (.NET inclus) et son dossier `moteur`.
