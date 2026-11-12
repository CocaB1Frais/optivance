# OPTIVANCE — Historique des errata de la documentation Phase 0

**Date :** 11 novembre 2026
**Auteur :** Claude Code (revue indépendante, lecture seule)
**Statut :** les corrections E-01 à E-07 ont été appliquées le 11 novembre 2026 aux documents de référence (voir `CHANGELOG_PHASE0_REVIEW.md` §14). Ce document conserve les constats, options et textes proposés à l'origine; ses formulations « correction proposée » décrivent l'état de la revue initiale et ne signalent pas un travail encore en attente. Les numéros de ligne cités sont ceux d'avant correction.

## État de résolution actuel

| Erratum | Décision ou règle retenue | Correction appliquée aux références | Ce qui reste à valider ou ouvert |
|---|---|---|---|
| E-01 | Option A acceptée par le propriétaire : avant Phase 1, valider le volet de contraintes MFT; le choix du backend reste en Phase 4. | `IMPLEMENTATION_ROADMAP.md` et `ARCHITECTURE_VALIDATION_PLAN.md` distinguent les deux volets; D-06 n'a pas été modifiée. | Démonstration du helper factice et des contraintes UAC non exécutée; backend MFT, parité et couverture restent à établir en Phase 4 sous autorisation distincte. |
| E-02 | Le verbe classique Windows 10 est limité à Enterprise LTSC 2019; aucun support de menu n'est promis pour LTSC 2021. | Périmètre Shell du plan de validation aligné sur D-02. | Inscription, désactivation, désinstallation et comportement Explorer restent à valider techniquement. |
| E-03 | Le choix du toolset d'installation est un préalable à V-02A; aucune option n'a été choisie par ce document. | Le plan de validation nomme explicitement ce préalable. | Toolset et licence de ses composants restent ouverts avant V-02A, en cohérence avec D-16. |
| E-04 | Windows 11 23H2 Enterprise/Education n'est pas une cible maintenue après la fin de servicing notée dans la revue. | La spécification et le plan de validation excluent 23H2 du périmètre maintenu. | Reconfirmer l'état de servicing des versions Windows 11 retenues à chaque release. |
| E-05 | La date de fin de support standard de LTSC 2021 doit être prise en compte. | Une note de calendrier a été ajoutée au plan/à la roadmap; LTSC 2021 reste conditionnel au support de sécurité applicable effectivement reçu. | Vérifier ce support pour chaque environnement et chaque release; aucun support n'est présumé. |
| E-06 | La signature de test relève de V-02A/V-12; le certificat réel et sa chaîne relèvent de V-02B. | Les prérequis et dossiers de validation distinguent signature de test et certificat réel. | Le certificat réel, son titulaire et sa chaîne restent à décider avant distribution. |
| E-07 | Les défauts éditoriaux identifiés ont été corrigés; les anciennes formulations du changelog restent une trace datée. | Numérotation de la roadmap, statut de D-03 et clarification de l'historique ont été alignés. | Aucun point d'arbitrage produit nouveau; les validations techniques citées restent dues selon leur dossier. |

**Règle de statut :** aucun erratum documentaire ne vaut preuve d'exécution. Les validations techniques ci-dessus restent distinctes de la Phase 1 et ne sont pas autorisées par ce relevé. **D-16 demeure ouvert**; aucune décision de licence/distribution ni dépendance tierce n'est créée ici.

## E-01 — V-06 (MFT) avant Phase 1 vs prototype MFT en Phase 4 — **arbitré : option A (propriétaire, 11 nov. 2026)**

- **Constat.** `IMPLEMENTATION_ROADMAP.md` l.33 demande, avant Phase 1, de « choisir/documenter le backend MFT ou limiter explicitement le support avancé », et l.52 exige des preuves V-01 à V-06 avant autorisation de Phase 1. Or `ARCHITECTURE_VALIDATION_PLAN.md` l.170 fixe la porte de V-06 à « choix du backend avant promesse MFT; prototype MFT soumis à autorisation séparée », et la roadmap place le prototype MFT en Phase 4 (l.80).
- **Effet.** La porte Phase 1 est soit irréalisable sans avancer la Phase 4, soit ambiguë.
- **Options.**
  - A (recommandée) : avant Phase 1, ne valider de V-06 que la *contrainte* (UI jamais élevée, refus UAC → fallback walk, aucun service) au moyen d'un helper factice allowlisté; choix du backend MFT en Phase 4.
  - B : exécuter V-06 complet avant Phase 1 et retirer « Prototype MFT » de la Phase 4.
- **Décision.** Option A retenue par le propriétaire le 11 nov. 2026 (en chat).
- **Remplacements proposés lors de la revue initiale** (texte conservé à titre d'historique; leur application ultérieure est consignée dans le tableau de résolution ci-dessus) :

  1. `IMPLEMENTATION_ROADMAP.md` l.33 — remplacer :
     > 3. **V-05, V-06 :** confirmer le parcours classique non élevé et choisir/documenter le backend MFT ou limiter explicitement le support avancé; UAC ponctuelle uniquement si nécessaire, refus avec fallback.

     par :
     > 3. **V-05, V-06 (volet contraintes) :** confirmer le parcours classique non élevé; démontrer avec un helper factice allowlisté que l'UI reste non élevée, que l'UAC n'apparaît qu'après action explicite, qu'un refus revient au parcours classique avec méthode annoncée, que le helper quitte après l'opération et qu'aucun service n'est installé. Le choix du backend MFT et ses preuves de parité relèvent de la Phase 4.

  2. `IMPLEMENTATION_ROADMAP.md` l.52 — remplacer « les gates préalables V-01 à V-06, V-11, V-12 et V-13 » par « les gates préalables V-01 à V-05, le volet contraintes de V-06, V-11, V-12 et V-13 ».

  3. `IMPLEMENTATION_ROADMAP.md`, Phase 4, après la puce « N'ajouter le helper élevé ponctuel… » — ajouter :
     > - Exécuter le volet backend de V-06 : comparer les routes MFT au parcours V-05, choisir le backend ou limiter explicitement le support avancé, sous autorisation distincte.

  4. `ARCHITECTURE_VALIDATION_PLAN.md` l.170 — remplacer :
     > - **Porte :** choix du backend avant promesse MFT; prototype MFT soumis à autorisation séparée.

     par :
     > - **Porte :** volet contraintes (UI non élevée, UAC après action explicite, refus → fallback, helper allowlisté à durée limitée, aucun service), démontré avec un helper factice, avant Phase 1; volet backend (parité, couverture, absence d'écriture) en Phase 4, avant toute promesse MFT et sous autorisation séparée.

  5. `ARCHITECTURE_DECISIONS.md` — aucune modification : D-06 reste inchangée (la décision porte sur l'ordre des validations, pas sur la politique de privilèges).
  6. `CHANGELOG_PHASE0_REVIEW.md` — action historique : l'entrée datée sur l'arbitrage E-01 et les fichiers concernés figure dans la section 14.

## E-02 — Menu contextuel sur LTSC 2021 contraire à D-02

- **Constat.** D-02 (`ARCHITECTURE_DECISIONS.md` l.53) et V-12 (plan l.220) limitent le verbe classique à Windows 10 Enterprise LTSC 2019; LTSC 2021 n'a pas de garantie menu. Mais dans `ARCHITECTURE_VALIDATION_PLAN.md` :
  - l.58 : « un fallback facultatif sur les cibles W10 v1 » (englobe LTSC 2021 selon la convention de la §4);
  - l.67 : « fallback W10 après consentement »; l.68 : « W10 fallback choisi »;
  - l.70 : la ligne « Explorer doit parfois recharger » liste LTSC 2021 parmi les plateformes Shell.
- **Correction proposée.** Remplacer « W10 » par « W10 Enterprise LTSC 2019 » aux l.58, 67, 68; retirer LTSC 2021 des plateformes de la l.70. Aucun arbitrage nécessaire (alignement sur D-02).

## E-03 — « Installateur de base avant Phase 1 » sans toolset choisi — *arbitrage*

- **Constat.** `ARCHITECTURE_VALIDATION_PLAN.md` l.134 (V-02) exige un installateur de base avant Phase 1; D-04 (l.85) laisse le toolset WiX/Inno/NSIS/MSI ouvert. Le choix du toolset est donc un prérequis bloquant non déclaré comme tel.
- **Correction proposée.** Ajouter le choix du toolset (et du certificat de signature, cf. E-06) à la liste « Points techniques à valider avant les prototypes » (plan §5) comme arbitrage préalable à V-02.

## E-04 — Windows 11 23H2 Enterprise/Education n'est plus maintenu

- **Fait vérifié le 11 nov. 2026** ([Windows 11 release information](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information), table des versions) : la ligne 23H2 affiche « End of updates » pour Home/Pro **et** Enterprise/Education; dernière mise à jour 2026-11 B du 2026-11-10.
- **Lignes concernées.** `ARCHITECTURE_VALIDATION_PLAN.md` l.37; `UPDATED_PRODUCT_SPECIFICATION.md` l.21; mention historique `CHANGELOG_PHASE0_REVIEW.md` l.123 (à laisser comme trace datée).
- **Correction proposée.** Marquer 23H2 « hors périmètre — fin de servicing novembre 2026 ». Versions Windows 11 listées par .NET 10 x64 : 26H1, 25H2, 24H2 (23H2 Ent/Edu seulement, désormais sans servicing).

## E-05 — Échéance LTSC 2021 proche

- **Constat.** LTSC 2021 sort du support standard le 12 janv. 2027 (documents existants). Pour tester V-01 sur LTSC 2021 sans dépendre d'un ESU effectif, la fenêtre restante est d'environ deux mois.
- **Correction proposée.** Aucune modification obligatoire; ajouter au plan V-01 une note de calendrier : « tester LTSC 2021 avant le 12 janv. 2027, ou consigner l'absence de support actif ».

## E-06 — Certificat de signature non identifié comme prérequis

- **Constat.** IExplorerCommand exige un sparse package signé dont le Publisher concorde (D-02, V-12 avant Phase 1). Aucun document ne désigne le certificat (type, coût, titulaire) comme décision du propriétaire.
- **Correction proposée.** Ajouter « certificat de signature (titulaire, type, certificat de test pour les VM) » aux arbitrages préalables à V-02 et V-12.

## E-07 — Défauts mineurs

| Fichier | Ligne | Constat | Proposition |
|---|---|---|---|
| `IMPLEMENTATION_ROADMAP.md` | 6 → 147 | Numérotation `## 1` puis `## 3` (pas de section 2) | Renuméroter ou intituler « 2. Phases » |
| `ARCHITECTURE_DECISIONS.md` | 77 | D-03 utilise l'ancien libellé « à prototyper » | « à valider (technique) » selon la légende l.6 |
| `CHANGELOG_PHASE0_REVIEW.md` | §3 | « rétrograde le stack … recommandation provisoire » peut induire en erreur | Laisser (trace historique, §9 prévaut) ou ajouter « (remplacé par §9) » |

## Hors errata : écart entre la passation et les documents

La passation cite le slogan « Clean. Optimize. Perform. ». La spécification (l.12) indique que le nom est un nom de travail et que slogan/identité sont hors revue technique. Aucune correction proposée; à confirmer par le propriétaire si le slogan est désormais décidé.
