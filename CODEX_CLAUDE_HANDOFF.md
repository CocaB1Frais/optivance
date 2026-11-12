# OPTIVANCE — Passation et coordination Codex / Claude Code

**Date :** 11 novembre 2026
**Statut :** document de coordination. Il n'autorise aucune phase, aucun test, aucune installation ni aucun commit.

## 1. Répartition des rôles

| Activité | Codex | Claude Code |
|---|---|---|
| Implémentation (code, scripts, manifestes, prototypes autorisés) | **Responsable** | Revue |
| Exécution des validations V-xx autorisées | **Responsable** | Revue des comptes rendus (`optivance-evidence-report`) |
| Documents de référence (décisions, plan, roadmap, spec, architecture) | Propose des errata | **Responsable** des mises à jour autorisées |
| Études documentaires (ex. V-17A sources capteurs) | — | **Responsable** |
| Revues de sécurité, de conformité et de performance | — | **Responsable** |
| Skills et configuration Claude Code | — | **Responsable** |

## 2. Règles anti-conflit

1. Un fichier n'a qu'un seul rédacteur à la fois. Avant de commencer, chaque agent annonce les fichiers qu'il va toucher dans la mission.
2. Codex ne modifie pas les documents de référence; il signale un écart dans son compte rendu. Claude ne modifie pas le code ni les artefacts de Codex; il publie ses constats de revue.
3. Les comptes rendus de validation sont rédigés par l'exécutant (Codex) et relus par Claude, jamais l'inverse sur le même dossier.
4. Toute modification d'une décision passe par le propriétaire.

## 3. Stratégie Git proposée (aucune action exécutée)

- Branche principale : **`main`** (le dépôt local est actuellement sur `master` sans aucun commit).
- Premier commit proposé : état documentaire harmonisé du 11 novembre 2026 (14 documents d'origine, `CLAUDE.md`, `CODEX_CLAUDE_HANDOFF.md`, `.gitignore`, `.claude/settings.json`, `.claude/skills/`, `governance/`).
- Branches de travail : `codex/<mission>` pour Codex, `claude/<mission>` pour Claude; une branche par mission, fusion dans `main` après revue et accord du propriétaire.
- Les comptes rendus de validation sont versionnés avec la mission qui les produit.
- Aucun commit, push, merge ou changement de branche sans confirmation explicite du propriétaire.

## 4. Prochaine mission Codex — prête à lancer après autorisation

### M-C1 — Pré-enregistrement et préparation de V-01 / V-02A

**Objectif.** Préparer l'exécution de V-01 (démarrage WPF + Rust self-contained sur les cibles) et V-02A (installation per-user, upgrade N→N+1 hors réseau, deux désinstallations, signature de test), puis, après autorisation distincte, les exécuter.

**Préalables (propriétaire).** Autorisation de l'étape de validation; choix du toolset d'installation; autorisation d'installer .NET 10 SDK, Rust stable MSVC, Visual Studio Build Tools/Windows SDK sur le poste de dev; autorisation et source des VM (Windows 11 x64 maintenu, Windows 10 Enterprise LTSC 2019, LTSC 2021 avant le 12 janv. 2027). Détail : `governance/V01_V02_PREREQUISITES.md`.

**Partie A — documentaire (aucune installation).**
- Fichier à créer : `validation/V-01_V-02A_PROTOCOL.md`.
- Contenu : définition du prototype minimal (fenêtre WPF + exécutable Rust lancé/arrêté + aller-retour Named Pipe trivial; le protocole complet relève de V-03); critères pass/fail observables et seuils; méthode de capture avant/après (HKCU, packages, Startup, profil); liste des commandes prévues; format du compte rendu selon `optivance-evidence-report`.
- Critère de réussite : protocole relu par Claude et approuvé par le propriétaire **avant** toute exécution.

**Partie B — exécution (après autorisation distincte).**
- Périmètre : prototype jetable dans `prototypes/v01-v02a/` (non destiné à la Phase 1), VM avec snapshot, compte standard.
- Interdits : dépendance tierce non revue, réseau applicatif, service, modification hors VM, signature autre que certificat de test.
- Livrable : `validation/V-01_REPORT.md` et `validation/V-02A_REPORT.md`, avec preuves.

**Fichiers concernés.** `validation/**`, `prototypes/v01-v02a/**` uniquement. Aucun document de référence.

### En parallèle côté Claude (après accord)
- **V-17A** : étude documentaire des sources de capteurs (`validation/V-17A_SENSOR_SOURCES.md`), fichiers disjoints de M-C1.
- Revue du protocole M-C1 partie A.
