---
name: optivance-evidence-report
description: Use when writing, reviewing or accepting the report of any OPTIVANCE validation dossier (V-01 to V-18, V-17A, V-02A/B) or any claim that something works, was measured or passed. Enforces pre-registered criteria, environment details and attached evidence; a claim without evidence must be reported as not verified.
---

# OPTIVANCE — compte rendu de validation fondé sur la preuve

## Règle
Un dossier V-xx n'est **réussi** que si chaque critère préenregistré est satisfait et prouvé par une pièce jointe ou une sortie citée. Sinon : « échec », « non concluant » ou « non exécuté ». Jamais « réussi » par défaut.

## Avant exécution (pré-enregistrement)
1. Dossier visé et version du plan (`ARCHITECTURE_VALIDATION_PLAN.md`, date de révision).
2. Critères pass/fail observables et seuils numériques fixés **avant** la mesure.
3. Environnement prévu (OS/édition/build, VM/snapshot, compte standard ou non).
4. Autorisation du propriétaire citée (date, périmètre).

## Gabarit du compte rendu

```
# V-xx — <titre> — compte rendu
Date : AAAA-MM-JJ   Auteur : <Codex|Claude>   Autorisation : <référence>
Verdict : RÉUSSI | ÉCHEC | NON CONCLUANT | NON EXÉCUTÉ

## Environnement
OS / édition / build / niveau de patch : …
Support de sécurité actif (LTSC 2021) : oui/non + preuve
Machine / VM / snapshot : …   Compte : standard/admin
Versions : .NET SDK/runtime, Rust, toolset, paquet (hash) : …

## Critères préenregistrés
| Critère | Seuil | Résultat mesuré | Preuve (fichier/sortie) | Statut |

## Déroulé
Étapes exécutées, commandes, durées.

## Anomalies et écarts
…

## Non vérifié / limites
…

## Conséquence architecturale proposée
(ne modifie aucune décision sans arbitrage du propriétaire)
```

## Contrôles de relecture
- [ ] Chaque ligne « réussi » a une preuve vérifiable.
- [ ] Les seuils n'ont pas été modifiés après la mesure.
- [ ] Les essais modifiant Windows ont eu lieu en VM avec snapshot.
- [ ] Aucune recommandation n'est requalifiée en décision par le rapport.
