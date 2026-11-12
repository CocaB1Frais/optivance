# OPTIVANCE V0.2 — Protocole de mesure Storage

Date de préparation : 2026-11-11  
Autorisation : mission V0.2 fournie par le propriétaire. Ce protocole ne démarre pas la Phase 1.

## Charge de référence

- Cible : racine `C:\`, identique à celle du scan V0.1 décrit par le propriétaire.
- Méthode : parcours classique Rust par répertoires; aucun accès MFT, aucune élévation, aucune lecture du contenu des fichiers.
- Contrôles enregistrés : nombre de fichiers/dossiers, avertissements, durée totale, stades d’énumération/métadonnées/agrégation/IPC/sérialisation, volume transféré et mémoire échantillonnée.
- Pas de vidage du cache Windows, de modification du volume, de service, de changement ACL ou d’activité d’écriture par le scanner.
- L’application et les autres modules ne lancent pas de scan automatiquement.

## Comparaison

La référence V0.1 est mesurée avant les changements du moteur. Le même binaire de probe et le même volume sont ensuite utilisés pour V0.2. Les résultats sont comparés avec les mêmes conditions d’exécution et de privilège. Aucun cache n’est forcé froid ou vidé; la température du cache reste une limite documentée. Si deux passes sont possibles par version, les deux durées seront conservées et les passes répétées seront interprétées comme naturellement chaudes, non comme une mesure froide.

Le test vise à décomposer les coûts, pas à démontrer la parité avec WizTree. Les mesures ne sont pas un seuil d’acceptation produit et ne valident ni MFT ni la compatibilité des systèmes cibles.

## Sécurité des preuves

Les sorties temporaires brutes peuvent contenir des chemins locaux dans les avertissements. Elles seront conservées uniquement dans un fichier temporaire pendant l’exécution, puis supprimées après extraction d’un résumé qui ne contient pas de chemins personnels.
