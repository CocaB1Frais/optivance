# Storage V1 — décisions du propriétaire et état vérifié

**Date :** 13 novembre 2026 · **Rédaction :** Claude (développeur principal depuis le 12/11/2026).
Ce document consigne des arbitrages du propriétaire ; il ne modifie pas les documents de référence (règle 6). Les écarts à reporter dans `ARCHITECTURE_DECISIONS.md` sont listés en fin de document.

## Arbitrages du propriétaire (13/11/2026)

| Sujet | Décision | Effet sur les références |
|---|---|---|
| Backend MFT (D-06 laissait le choix ouvert) | **Lecture directe de la `$MFT`** ; l'énumération `FSCTL_ENUM_USN_DATA` reste en secours automatique. | D-06 : politique de privilèges inchangée ; choix du backend tranché pour Storage. |
| Analyse d'un disque NTFS | Bouton principal = analyse MFT avec **UAC à chaque analyse** ; refus ou échec → **repli automatique** sur l'analyse standard, cause affichée. Dossiers et volumes non NTFS : analyse standard. | Conforme à D-06 (aucune élévation au démarrage, aucune mémorisation). |
| Suppression | **Depuis Storage, comme WizTree** (arborescence, liste des fichiers, treemap) : Suppr → Corbeille avec confirmation ; Maj+Suppr → suppression définitive avec confirmation renforcée ; index mis à jour sans nouvelle analyse. | Les règles de nettoyage (confirmation, Corbeille par défaut, définitif distinct) sont respectées ; l'emplacement (Storage et non Cleaning) est un choix du propriétaire à reporter. |
| Git | Commit initial sur `main`, travail sur `claude/storage-v1`, un commit par étape validée ; aucun push. | — |

## Mesures réelles (Windows 11 25H2, volume C: NTFS, poste de développement)

| Mesure | Résultat | Source |
|---|---|---|
| Analyse MFT de bout en bout (client C#, UAC comprise) | 6,9 s ; moteur 3,8 s dont lecture MFT 2,3 s ; 826 027 entrées ; état `completed` | `ScanProbe --mft C` |
| Analyse depuis l'interface | 6,8 s affichées, « MFT directe · terminée », 695 013 fichiers, 131 274 dossiers | capture de l'application |
| Précision de la taille allouée | 173,15 Go indexés pour 174,48 Go occupés (écart ≈ métafichiers NTFS et index de dossiers) | `ScanProbe --mft C` + `Win32_LogicalDisk` |
| Ancienne méthode (énumération + ouvertures) | ~35-48 s, résultat partiel (hardlinks incomplets) | essai du 13/11 |

## Comparaison avec WizTree 4.31 (13/11/2026 ~01:02, C:, l'un après l'autre, tous deux élevés, UAC comprise)

| | WizTree 4.31 | OPTIVANCE (mft-raw) | Écart |
|---|---|---|---|
| Durée (lancement → résultat, clic UAC compris) | 4,9 s (export CSV 1 niveau) | 6,1 s (moteur seul : 3,7 s) | +1,2 s |
| Fichiers | 695 176 | 695 128 | −48 (0,007 %) |
| Dossiers | 131 385 | 131 304 | −81 (0,06 %) |
| Taille allouée | 174 123 040 768 | 173 148 868 608 | −0,56 % (métafichiers NTFS exclus par OPTIVANCE) |
| Taille logique | 205 010 393 772 | 187 850 859 278 | WizTree additionne chaque lien physique ; OPTIVANCE compte un fichier hardlinké une seule fois (D-07). Somme de tous les liens côté OPTIVANCE : ≈ 204 Go. |
| Espace occupé (volume) | 174 491 447 296 | | référence Windows |

Le CSV de WizTree (chemins personnels) a été supprimé après lecture de la ligne racine. Le temps du clic UAC n'est pas isolé : l'écart de durée est indicatif.

## Non vérifié à ce jour

- Windows 10 Enterprise LTSC 2019/2021 (aucun environnement disponible).
- Volumes très fragmentés dont la `$MFT` est décrite par une liste d'attributs (repli prévu, non rencontré).
- Suppression déclenchée depuis l'interface (testée au niveau des fonctions, pas par un clic réel automatisé).
- Paquet self-contained / installateur (non concerné par cette étape).

## À reporter dans les références (après validation du propriétaire)

1. D-06 : backend MFT retenu = lecture directe `$MFT`, secours FSCTL.
2. D-11 / spécification Storage : suppression autorisée depuis Storage avec les garde-fous ci-dessus.
3. D-10 : protocole local passé en v3 (lignes compactes, date de modification) ; helper MFT : arguments stricts, `SecurityIdentification`, PID + image vérifiés, `FILE_FLAG_FIRST_PIPE_INSTANCE`, DLL limitées à System32.
