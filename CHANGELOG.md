# Journal des versions

## 1.0.3 — 2026-11-14

- **Désinstallation** en un clic : `Desinstaller OPTIVANCE.cmd` dans le dossier, ou Paramètres › Désinstallation (réglages restaurés, application et données supprimées, aucun autre fichier touché).
- **Optimisation** et **Nettoyage** redessinés en cartes : onglets, recherche, filtre de risque, masquage des réglages appliqués, date d'application.
- Barre de titre intégrée : plus de bande ni de trait, icônes réduire / agrandir / fermer dessinées.

## 1.0.2 — 2026-11-14

- **Cartes AMD** : température, consommation, fréquence et activité via la bibliothèque ADL du pilote AMD (cartes dédiées et puces graphiques intégrées ; vérifié sur la puce intégrée d'un Ryzen 9600X).
- Application plus légère (67 Mo au lieu de 73 Mo) : traductions inutilisées de .NET retirées.
- `build.ps1` ne garde plus de copie en double ; option `-Clean` pour un dossier minimal.
- Numéro de version affiché dans la barre latérale.

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
