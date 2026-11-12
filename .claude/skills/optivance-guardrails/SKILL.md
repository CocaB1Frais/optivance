---
name: optivance-guardrails
description: Use before producing, reviewing or planning anything for OPTIVANCE (docs, code review, missions for Codex, validation plans). Checks phase authorization, decision statuses (approved vs recommended vs open), product invariants (no Dashboard, sensors rules, no telemetry/service/driver, MFT-only elevation) and license gates before any output.
---

# OPTIVANCE — garde-fous de conformité

Checklist à dérouler avant de rendre un travail OPTIVANCE. Chaque point bloquant non satisfait = arrêter et le signaler au propriétaire.

## 1. Autorisation
- [ ] La tâche est-elle dans une phase/étape **autorisée** ? (Phase 0 documentaire seulement tant que `CLAUDE.md` ne dit pas autre chose.)
- [ ] Elle n'implique ni installation, ni compilation, ni test technique, ni commit/push/merge/changement de branche, ni modification système — ou l'accord explicite est cité.

## 2. Statuts
- [ ] Chaque affirmation de décision renvoie à un D-xx **Approuvé** dans `ARCHITECTURE_DECISIONS.md`.
- [ ] SQLite, chemin représentatif, sparse package, raccourci Startup, BALANCED par défaut, cadence 2–3 s, valeurs de workers sont écrits comme **recommandations**.
- [ ] D-16, toolset, certificat (V-02B), backend MFT, budgets, seuils, catégories de nettoyage sont écrits comme **ouverts**.
- [ ] Aucun test, mesure ou validation n'est présenté comme exécuté sans preuve jointe.

## 3. Invariants produit
- [ ] Navigation : Storage, Cleaning, Monitoring, Optimization, History, Settings — **pas de Dashboard**; accueil Storage; processus live dans Monitoring seul; widget même processus (D-17).
- [ ] Capteurs : API Windows documentées + bibliothèques constructeur présentes avec le pilote après revue de licence; ni pilote ni élévation; sinon « indisponible »; historique de performances en mémoire seulement (D-18).
- [ ] Aucun cloud, compte, télémétrie, trafic réseau applicatif, updater, service permanent, pilote noyau, UAC au démarrage. Helper élevé : MFT uniquement, allowlisté, ponctuel.
- [ ] Nettoyage : aperçu, sélection par fichier, confirmation, Corbeille par défaut, suppression définitive distincte, aucune promesse d'effacement sécurisé.
- [ ] Optimisation : processus/services lecture seule; seules les entrées de démarrage prises en charge modifiables avec confirmation, sauvegarde, restauration.
- [ ] Taille logique par défaut; hardlinks visibles, représentant unique, ADS hors totaux; états Terminé/Interrompu/Échec, seul Terminé dans les statistiques.
- [ ] Compatibilité : Windows 11 x64 maintenu; W10 Enterprise LTSC 2019; LTSC 2021 conditionnel; menu contextuel LTSC 2021 non garanti.

## 4. Dépendances et licences
- [ ] Aucune dépendance tierce introduite sans version exacte, licence, obligations et compatibilité avec D-16.

## 5. Coordination
- [ ] Les fichiers touchés ne sont pas en cours de modification par Codex (`CODEX_CLAUDE_HANDOFF.md`).
- [ ] Les questions bloquantes sont regroupées : question, options, recommandation, conséquences.
