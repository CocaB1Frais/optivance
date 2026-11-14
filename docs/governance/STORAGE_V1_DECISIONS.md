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

### Après optimisation (13/11/2026, mêmes conditions, `ScanProbe --mft C`)

| Mesure | Avant | Après |
|---|---|---|
| Moteur (lecture MFT + index + envoi) | 3,51 s | 2,39 s |
| Lecture et décodage MFT | 1,94 s | 1,35 s (8 fils, un handle de volume par fil) |
| Index | 0,49 s | 0,33 s |
| De l'acceptation UAC au résultat complet côté client | non mesuré | **2,50 s** |
| De bout en bout, clic UAC compris | 6,1–6,2 s | 4,7–5,7 s (variable selon le clic) |

Le décodage côté client se fait pendant la réception (+0,1 s après le moteur). Nouvelle mesure simultanée avec WizTree non refaite.

## Arbitrages complémentaires (13/11/2026, après essais sur E:)

| Sujet | Décision du propriétaire | Mise en œuvre |
|---|---|---|
| Autorisation administrateur | **Une seule UAC au lancement**, pour toute la session (remplace « UAC à chaque analyse MFT » de D-06). Option retenue : composant admin de session, interface non élevée. | `optivance-mft-helper --session` : reste connecté jusqu'à la fermeture d'OPTIVANCE ; n'accepte que « analyser X: », « annuler », « quitter » (format exact) ; PID + image vérifiés ; s'arrête quand le canal se ferme. Aucun service. |
| Clic droit Windows | Entrée « OPTIVANCE » sur le fond du Bureau seulement, activable/désactivable. | `HKCU\Software\Classes\Directory\Background\shell\Optivance`, écrite uniquement au clic sur le bouton de la barre latérale ; retrait complet par le même bouton. |
| Privilège de sauvegarde | Accordé pour ouvrir `X:\$MFT`. | Essayé puis **retiré** : Windows refuse l'ouverture de `$MFT` même avec le privilège. Aucun privilège supplémentaire n'est activé. |

**Disque E: (disque dur WD 1 To, secteurs 4 Ko, enregistrements MFT de 1 Ko).** La lecture brute du volume y est refusée par Windows (erreur 50 dès le secteur de démarrage). Correctifs : taille d'enregistrement < secteur acceptée ; tampons alignés sur 4 Ko ; repli sur `FSCTL_GET_NTFS_FILE_RECORD` (parcours descendant, un appel par enregistrement occupé, même décodage). Mesure : **E: 0,68 s** (ScanProbe, après UAC) et 0,7 s dans l'interface, état `completed` ; alloué 235,72 Go pour 236,18 Go occupés (WizTree : 1,6 s, mesure de l'utilisateur). La lecture du volume est redevenue séquentielle (un fil) : les lectures simultanées pénalisaient ce disque mécanique.

## Autres modules — arbitrages du 13/11/2026

| Sujet | Décision du propriétaire | Écart aux références |
|---|---|---|
| Fermeture de la fenêtre | Réduit dans la zone de notification ; widget et raccourcis restent actifs ; « Quitter » arrête tout. Lancement avec Windows en option (désactivé par défaut, HKCU\Run). | D-14/D-17 : même processus conservé ; fonctionnement en arrière-plan explicite et réglable. |
| Capteurs | Sans pilote ; température CPU « indisponible » si Windows ne la fournit pas ; NVIDIA via `nvml.dll` du pilote installé (non redistribuée). | Conforme à D-18. Revue de licence NVML à consigner avant distribution. |
| FPS | Via ETW (sans injection), seulement si le coût mesuré est négligeable. | Nouveau ; à mesurer. |
| Nettoyage | Standard + approfondi (admin), avec aperçu, sélection, confirmation, Corbeille si possible, historique. | Conforme à D-11. |
| Optimisation | Plus large que D-11 (services, effets visuels, tâches planifiées) **avec avertissement explicite des conséquences avant tout réglage risqué**, sauvegarde et restauration. | Écart à D-11 (services en lecture seule) décidé par le propriétaire. |
| Historique / paramètres | Toutes les actions tracées localement (JSONL) ; paramètres JSON locaux. | SQLite (recommandation D-08) non utilisé : aucune dépendance tierce. |

Mesures : widget seul (fenêtre fermée) 0,10 % du processeur en moyenne sur 20 s, 112 Mo de mémoire privée (Windows 11, 13/11/2026).

## Non vérifié à ce jour

- Windows 10 Enterprise LTSC 2019/2021 (aucun environnement disponible).
- Volumes très fragmentés dont la `$MFT` est décrite par une liste d'attributs (repli prévu, non rencontré).
- Suppression déclenchée depuis l'interface (testée au niveau des fonctions, pas par un clic réel automatisé).
- Paquet self-contained / installateur (non concerné par cette étape).

## À reporter dans les références (après validation du propriétaire)

1. D-06 : backend MFT retenu = lecture directe `$MFT`, secours FSCTL.
2. D-11 / spécification Storage : suppression autorisée depuis Storage avec les garde-fous ci-dessus.
3. D-10 : protocole local passé en v3 (lignes compactes, date de modification) ; helper MFT : arguments stricts, `SecurityIdentification`, PID + image vérifiés, `FILE_FLAG_FIRST_PIPE_INSTANCE`, DLL limitées à System32.

## Capteurs AMD — revue de licence (14/11/2026)

- Bibliothèque : `atiadlxx.dll` (AMD Display Library), installée avec le pilote AMD, chargée dynamiquement, **non redistribuée**.
- En-têtes ADL (dépôt GPUOpen-LibrariesAndSDKs/display-library, `include/adl_sdk.h`) : **licence MIT**, © Advanced Micro Devices. Aucun fichier copié ; déclarations P/Invoke réécrites dans `AdlGpu.cs`.
- Conforme à D-18 (bibliothèque constructeur présente avec le pilote, sans pilote ajouté ni élévation).
- Vérifié réellement sur la puce graphique intégrée d'un Ryzen 5 9600X (température graphique 41 °C, consommation, fréquence) ; carte AMD dédiée non testée (aucune disponible).
- NVML (NVIDIA) : même principe ; revue de licence formelle toujours à consigner.
