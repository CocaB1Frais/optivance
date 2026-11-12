# OPTIVANCE — Checklist consolidée V-01 / V-02A (et V-02B)

**Date :** 11 novembre 2026
**Statut :** checklist documentaire. Rien n'est installé, créé ni exécuté. Chaque ligne demande une action ou une décision du propriétaire avant que Codex lance la mission M-C1 (`CODEX_CLAUDE_HANDOFF.md`).

Rappel ([ARCHITECTURE_VALIDATION_PLAN.md](../ARCHITECTURE_VALIDATION_PLAN.md) §4A) :
- **V-01** : démarrage WPF + Rust self-contained sur les cibles, sans runtime préinstallé.
- **V-02A** (avant Phase 1) : installation per-user, réparation, upgrade N→N+1 hors réseau, désinstallations standard et complète, **signature de test**.
- **V-02B** (avant distribution) : certificat réel, chaîne de signature, Publisher, horodatage.

## 1. Décisions du propriétaire

| # | Décision | Bloque | État |
|---|---|---|---|
| D1 | Autoriser l'étape de validation (au moins V-01/V-02A) | M-C1 partie B | **Ouvert** |
| D2 | Toolset d'installateur (WiX / Inno Setup / NSIS / MSI) | V-02A | **Ouvert** (recommandation ci-dessous) |
| D3 | Certificat réel | V-02B seulement | **Reporté à V-02B** (arbitrage E-06) |
| D4 | Licence du toolset et de ses composants redistribués | V-02A | Ouvert, dépend de D2 et D-16 |
| D5 | Exécution : build sur poste de dev, installation/tests en VM uniquement | M-C1 partie B | Règle du plan; autorisation d'activer Hyper-V requise |
| D6 | Périmètre V-06 avant Phase 1 | — | **Tranché : option A** |
| D7 | Autoriser les installations du poste de dev (§2) | M-C1 partie B | **Ouvert** |

**Recommandation D2 (non décidée).** Retenir un toolset produisant un installateur per-user sans élévation, scriptable en CI, capable d'enregistrer un sparse package et de signer : WiX et Inno Setup sont les candidats naturels; trancher après revue de licence (D4) et d'un critère simple : installation per-user documentée sans élévation. Aucun outil n'est installé avant D2/D7.

## 2. Poste de développement (build uniquement)

Constaté le 11 nov. 2026 : **Windows 11 Pro 25H2 (build 26200)**, listé par .NET 10 x64, mises à jour jusqu'au 2027-10-12 ([source](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information)).

| Élément | État constaté | Action après autorisation D7 |
|---|---|---|
| .NET 10 SDK x64 | absent | Installer; consigner la version exacte |
| Rust stable `x86_64-pc-windows-msvc` | absent | Installer rustup; épingler via `rust-toolchain.toml` |
| Visual Studio Build Tools (MSVC + Windows SDK, `signtool`) | non vérifié | Installer si absent |
| Toolset installateur | non choisi | Après D2 |
| Certificat de test (VM uniquement) | absent | Générer pour V-02A/V-12; jamais approuvé sur le poste personnel |
| `DOTNET_CLI_TELEMETRY_OPTOUT=1` | défini pour Claude Code dans `.claude/settings.json` | Définir aussi pour Codex/CI |
| Git | dépôt sans commit | Commit de référence sur `main` après accord |

## 3. VM de test (snapshot avant chaque run, compte standard, réseau coupé pour les essais hors réseau)

| Cible | Statut | Image | Échéance / remarque |
|---|---|---|---|
| Windows 11 x64 maintenu (25H2 ou 24H2) | Principale | ISO officielle, sans .NET préinstallé | — |
| Windows 10 Enterprise LTSC 2019 x64 (17763) | Secondaire | Évaluation ou licence (disponibilité à vérifier) | Consigner build et patch |
| Windows 10 Enterprise LTSC 2021 x64 (19044) | Conditionnelle | Idem | **Avant le 12 janv. 2027**, ou consigner l'absence de support actif |

## 4. Pré-enregistrement (M-C1 partie A, à approuver avant tout run)

- Prototype minimal : fenêtre WPF + exécutable Rust démarré/arrêté + aller-retour Named Pipe trivial (le protocole complet relève de V-03); jetable, hors Phase 1.
- Critères observables, par exemple : aucun runtime .NET listé sur la VM avant installation; démarrage sans erreur; inventaire du runtime embarqué; aucune élévation pendant l'installation; après désinstallation standard, préférences/historique présents; après complète (confirmée), absents; aucune clé HKCU/package hors liste documentée.
- Capture avant/après : HKCU, packages enregistrés, dossiers Startup, profil.
- Compte rendu : gabarit du skill `optivance-evidence-report`.

## 5. Hors périmètre

Menu contextuel et sparse package (V-12), Named Pipes complets (V-03), MFT (V-06), SQLite (V-13), certificat réel (V-02B).
