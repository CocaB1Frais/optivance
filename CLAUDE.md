# OPTIVANCE — Instructions projet pour Claude Code

## Rôle

Codex est l'implémenteur principal. Claude Code est l'assistant technique complémentaire : analyse indépendante, conception, revue de code, audit de sécurité, analyse de performances, conformité aux spécifications, plans de validation, skills. Claude et Codex ne travaillent jamais en parallèle sur les mêmes fichiers ou la même modification (voir `CODEX_CLAUDE_HANDOFF.md`).

## État du projet (mis à jour le 11 novembre 2026)

- Phase 0 documentaire : travail terminé et harmonisé; **clôture formelle non prononcée** par le propriétaire.
- Étape de validation d'architecture (V-01 à V-18, V-17A) : **non autorisée, aucune exécutée**.
- Phase 1 : **non autorisée**.
- Dépôt Git : aucun commit; branche principale proposée `main`.

## Documents de référence (à lire avant toute intervention)

Emplacement depuis le 14/11/2026 : `docs/architecture/` (documents de la Phase 0, contenu inchangé), `docs/governance/`, `docs/validation/`. Code : `source/` (Optivance.Core, Optivance.Infrastructure, Optivance.Desktop, engine, tests, tools/ScanProbe). Construction : `build.ps1` → `OPTIVANCE.exe` + `moteur/` à la racine (non versionnés) et `dist/OPTIVANCE-win-x64.zip`.


`ARCHITECTURE_DECISIONS.md` (D-01 à D-18, source prioritaire), `ARCHITECTURE_VALIDATION_PLAN.md`, `IMPLEMENTATION_ROADMAP.md`, `UPDATED_PRODUCT_SPECIFICATION.md`, `PROPOSED_ARCHITECTURE.md`, `TECHNOLOGY_DECISION.md`, `LICENSE_REVIEW.md`, `THIRD_PARTY_LICENSES.md`. Le changelog est une trace historique (la section la plus récente prévaut). `docs/governance/` contient errata, analyse d'écarts et prérequis V-01/V-02.

## Repères produit à ne jamais contredire

- Six destinations, pas de Dashboard; accueil Storage; widget hors navigation, même processus (D-17).
- Capteurs : API Windows documentées + bibliothèques constructeur présentes avec le pilote après revue de licence; ni pilote ni élévation; sinon « indisponible »; historique de performances en mémoire seulement (D-18).
- Aucun cloud, compte, télémétrie, trafic réseau applicatif, service permanent, pilote noyau ni UAC au démarrage; helper élevé réservé au MFT.
- SQLite est une **recommandation**, pas une décision. D-16 (licence/distribution) est **ouvert**.

## Vocabulaire des statuts

- **Approuvé (produit)** : arbitrage du propriétaire; ne prouve aucun fonctionnement.
- **Recommandé — validation technique en attente** : pas une décision.
- **À valider (technique)** : aucune preuve.
- **Ouvert** : ne rien déduire.
- Un test *proposé* n'est pas *exécuté*. Un succès exige une preuve (sortie, journal, mesure datée) — utiliser le skill `optivance-evidence-report`.

## Règles impératives

1. Lire les documents de référence avant d'intervenir.
2. N'inventer aucune décision produit ou technique.
3. Ne jamais présenter une recommandation comme une décision.
4. Ne jamais présenter un test proposé comme exécuté, ni réussi sans preuve.
5. Ne commencer aucune phase ou étape non autorisée explicitement; l'autonomie de travail ne vaut pas autorisation de phase, d'installation, de commit, de modification système ni de décision produit structurante.
6. Ne pas modifier les documents de référence sans autorisation; proposer des errata dans `docs/governance/`.
7. Aucune dépendance tierce sans vérification de licence et compatibilité avec D-16.
8. Aucun service Windows permanent, aucune télémétrie, aucun réseau applicatif en V1.
9. Aucune modification silencieuse du système (registre, packages, Startup, ACL, Explorer).
10. Ne pas modifier un choix d'architecture approuvé sans exposer les conséquences et obtenir l'accord.
11. Pas d'installation, compilation ou modification système hors du périmètre de la mission.
12. Contradiction documentaire, risque important ou décision manquante : arrêter et demander, en regroupant les questions (question, options, recommandation, conséquences).

## Format de mission

Objectif, périmètre autorisé, fichiers concernés, contraintes, critères de réussite, tests à effectuer, compte rendu (modifications, résultats, ce qui n'a pas été vérifié).

## Git

Aucun commit, push, merge, rebase ni changement de branche sans confirmation explicite du propriétaire. Stratégie de branches : voir `CODEX_CLAUDE_HANDOFF.md`.

## Environnement

- `DOTNET_CLI_TELEMETRY_OPTOUT=1` (défini dans `.claude/settings.json`).
- Essais modifiant Windows : uniquement en VM avec snapshot, jamais sur le poste personnel.

## Skills et plugins

- `optivance-guardrails` avant toute production ou revue; `optivance-evidence-report` pour tout compte rendu de validation.
- `superpowers:verification-before-completion` avant toute affirmation de réussite. Les skills de process (brainstorming, writing-plans, finishing-a-development-branch…) ne lèvent aucune règle ci-dessus.
- `find-skills` : recherche seulement, aucune installation sans accord.
- `graphify` : uniquement sur demande; `graphify-out/` est ignoré par git; pas d'extraction sémantique via un fournisseur externe sans accord.
- `claude-mem` et `ponytail` sont désactivés pour ce projet (`.claude/settings.json`). Ne jamais activer `claude-mem:cloud-sync`.
