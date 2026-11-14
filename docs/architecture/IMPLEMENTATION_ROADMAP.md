# OPTIVANCE — Feuille de route révisée

**Révision :** 11 novembre 2026 — errata E-01 à E-07; arbitrages Master Product Vision (D-17, D-18; Monitoring avant Cleaning)  
**Statut :** plan proposé. La revue reste documentaire; aucune validation prototype ni Phase 1 n’a commencé. Aucun code applicatif, prototype, package ou test n’a été produit pour cette révision.

## 1. Ordre de livraison proposé

Établir la correction avec un scan sans élévation, puis construire les vues et l’index; ajouter les opérations destructives et privilégiées seulement quand leurs règles et interfaces sont fiables. Garder les fonctions avancées dans le périmètre produit sans les présenter comme livrées avant implémentation et validation.

Le Monitoring et le widget (Phase 5) ne dépendent pas du moteur de stockage : après la Phase 1, ils peuvent avancer en parallèle des Phases 2–3 (collecteur et UI en C# d’un côté, scanner Rust de l’autre, fichiers disjoints). Cleaning (Phase 6) vient ensuite.

## Phase 0 — Audit et révision produit (travail documentaire complété; validation finale en attente)

- Inspecter le dépôt initial et les documents/source existants.
- Étudier les projets, méthodes MFT/USN, comparatifs, licences et données de performance.
- Réviser la spécification avec la demande de menu contextuel Windows 11.
- Écrire les options, avantages/inconvénients, recommandations et choix bloquants.
- Mettre à jour architecture et feuille de route; documenter les modifications.
- Réaliser la revue transversale finale et harmoniser les statuts, preuves requises, risques et portes de phase.
- L’état « travail documentaire complété » ne signifie pas que les mécanismes techniques sont validés, ni que le propriétaire a prononcé la clôture formelle de Phase 0.
- Aucun développement applicatif, prototype ou essai technique n’est lancé par cette feuille de route.

Livrables : [spécification révisée](UPDATED_PRODUCT_SPECIFICATION.md), [registre de décisions](ARCHITECTURE_DECISIONS.md), [architecture proposée](PROPOSED_ARCHITECTURE.md), [plan de validation](ARCHITECTURE_VALIDATION_PLAN.md), [changelog](CHANGELOG_PHASE0_REVIEW.md), et les audits Phase 0 existants.

**État actuel :** documentation prête à la validation finale du propriétaire. La Phase 1 reste non autorisée. Le plan de validation distingue les gates techniques pré-Phase 1 des validations ultérieures propres à chaque fonctionnalité et à la distribution.

## Étape préalable à la Phase 1 — Validation de l’architecture (planifiée, non exécutée, autorisation distincte requise)

Cette étape est une préparation documentaire de validations techniques ciblées, pas une autorisation de les exécuter. Les essais/prototypes s’exécutent uniquement après autorisation explicite distincte. Ils précèdent la fondation fonctionnelle. Chaque dossier comporte risques, objectif, méthode, environnement, critères réussite/échec, effet architectural et preuve attendue dans [ARCHITECTURE_VALIDATION_PLAN.md](ARCHITECTURE_VALIDATION_PLAN.md). Aucun résultat n’est acquis.

Prototypes à prévoir :

1. **V-01, V-02A :** démontrer le runtime et le démarrage WPF + Rust sur Windows 11 x64 maintenu et LTSC 2019 x64; LTSC 2021 seulement sous support actif (avant le 12 janv. 2027 ou absence consignée); vérifier l’installation per-user de base et le runtime self-contained avec une signature de test. Le toolset d’installation doit être choisi avant; le certificat réel relève de V-02B avant distribution.
2. **V-03, V-04 :** contrat Named Pipe versionné, ACL et messages bornés, erreurs, annulation, panne worker, état de scan et absence de processus orphelin.
3. **V-05, V-06 (volet contraintes) :** confirmer le parcours classique non élevé; démontrer avec un helper factice allowlisté que l’UI reste non élevée, que l’UAC n’apparaît qu’après action explicite, qu’un refus revient au parcours classique avec méthode annoncée, que le helper quitte après l’opération et qu’aucun service n’est installé. Le choix du backend MFT et ses preuves de parité relèvent de la Phase 4.
4. **V-11 :** valider fermeture complète par défaut, TrayResident, WidgetOnly, lancement Windows, Quit, logoff/shutdown et collecteurs arrêtés sans consommateur.
5. **V-13 :** vérifier que la recommandation SQLite peut respecter états, intégrité, migrations, rétention, reprise et opt-in, ou documenter une autre solution technique sans changer les règles produit.
6. **V-12 :** établir la faisabilité de IExplorerCommand sous Windows 11 et du verbe classique sous LTSC 2019, le consentement, le cycle de vie et le comportement sans application ouverte, avec un sparse package signé par certificat de test. LTSC 2021 ne reçoit pas de garantie menu.
7. **V-17A (documentaire, sans code) :** recenser les sources de capteurs autorisées par D-18 pour chaque métrique du Monitoring. Elle n’est pas une condition de Phase 1 mais précède la conception de la Phase 5.

Les V-07 à V-10 et V-14 à V-18 sont des gates avant les fonctions, mesures ou claims correspondants; elles ne sont pas présentées comme résultats exigés avant toute ligne de fondation si la roadmap n’en dépend pas encore.

Les critères et seuils de performance doivent être préenregistrés avant leur mesure. La politique OS et les décisions produit restent inchangées. Aucun prototype n’est lancé par la présente revue.

## Phase 1 — Fondation technique (non autorisée; après validations préalables et accord explicite seulement)

- Consigner les résultats de validation des décisions déjà arrêtées : paquet .NET self-contained, installation/désinstallation, IPC, états/historique, hardlinks, cycle de vie, menu et contraintes MFT/UAC. Les arbitrages techniques encore ouverts portent sur backend MFT (Phase 4), toolset (avant V-02A), certificat (V-02B), sparse package, détails Shell, sources de capteurs, seuils et budgets. La politique IoT LTSC est exclue de V1.
- Geler l’architecture seulement après passage des critères de la matrice et accord explicite de l’utilisateur.
- Structurer un seul worker Rust partagé par l’application et le widget, avec protocole Named Pipe versionné, corrélation, progression bornée, annulation, erreurs structurées, allowlist et interface résiliente à un crash du worker.
- Structurer le shell à six destinations (Storage, Cleaning, Monitoring, Optimization, History, Settings; D-17), page d’accueil Storage, indicateur d’activité global, Settings et affichage d’erreurs, sans scan automatique. Prévoir la localisation français/anglais dès la fondation.
- Mettre en place le harnais de tests automatisés : tests Rust headless, fixtures de scan générées, tests du protocole IPC, tests des view-models C#, mode « capteurs simulés ».
- Valider SQLite comme base de référence, puis définir configuration locale et schéma versionné pour les trois états : résumés (état/erreur inclus, 365 jours par défaut) et chemins détaillés opt-in (30 jours par défaut); rendre les durées réglables et la rétention détaillée désactivable.
- Installer environnement de build reproductible et documentation de développement.
- Définir signature, permissions et déploiement de test; désactiver la télémétrie de CLI de build dans les environnements de développement.

**Conditions nécessaires à l’autorisation de Phase 1 :** les gates préalables V-01, V-02A, V-03 à V-05, le volet contraintes de V-06, V-11, V-12 et V-13 doivent avoir des preuves examinées; les échecs doivent être corrigés ou leur impact documenté; aucun blocage majeur de compatibilité, sécurité, IPC, privilèges, cycle de vie ou stockage ne doit rester sans résolution. Le propriétaire doit ensuite autoriser expressément Phase 1 dans une décision distincte. Ce document ne constitue pas cette autorisation. Pas d’écriture registre/package sans consentement explicite lors d’une activation ultérieure.

## Phase 2 — Volumes et scanner classique

- Détecter volumes, lettre/nom, système de fichiers, type, capacité, disponibilité et statut d’accès.
- Implémenter parcours Win32 unicode, chemins longs, annulation, bornage de concurrence et erreurs partielles.
- Gérer fichiers supprimés/déplacés durant le scan, accès refusé, verrouillage et disques déconnectés.
- Appliquer la politique de reparse point sans suivre symlink/jonction/mount par défaut.
- Appliquer la taille logique comme valeur principale validée; garder visibles tous les chemins hardlink, sélectionner un représentant déterministe indépendant de l’ordre de scan pour le poids d’agrégat, marquer les autres comme alias et ne compter l’identité qu’une fois dans total/parents/treemap. ADS NTFS exclus des totaux ordinaires, métadonnées séparées seulement si fiables.
- Marquer explicitement chaque scan Terminé, Interrompu ou Échec. État et erreurs restent dans l’historique résumé; chemins partiels consultables pendant la session et persistés seulement si l’inventaire détaillé est activé, selon sa rétention. Aucun incomplet dans les statistiques définitives.
- Supporter NTFS, FAT32, exFAT et autres filesystems seulement là où les API permettent une couverture correcte.
- Présenter la méthode et l’exhaustivité pour chaque résultat.

**Porte de phase :** scans reproductibles de fixtures et volumes de test; annulation et erreur d’un fichier ne font pas tomber le job complet.

## Phase 3 — Index, analyses et interface Storage

- Index compact moteur, liens/noms séparés, agrégats cohérents et livraison progressive.
- Conserver toutes les entrées de chemins hardlink dans l’arbre et la recherche; sélectionner le représentant par la convention de chemin stable documentée, signaler les alias et éviter leur double comptage dans les agrégats volume/répertoires/treemap.
- Arbre à colonnes, navigation, tri, fichiers/dossiers les plus grands, métadonnées et accès.
- Recherche nom/extension/chemin, tailles, dates et filtres cachés/système/temporaire/protégés/inaccessibles.
- Treemap interactive : couleur extension, regroupement, hover, selection sync, double-clic, zoom, breadcrumbs, surlignage, tooltips, hit test et agrégation grands volumes.
- Analyse des doublons : regroupement par taille, éventuel pré-hash, hash intégral final, cancellable, sans surcoût de lecture pendant le scan normal.
- Export streamé CSV/JSON/TXT des résumés, listes, grands fichiers, dossiers, doublons et rapports.
- Actions non destructives de fichier : ouvrir, Explorer, copier chemin/nom, propriétés, analyse du parent.

**Porte de phase :** grands résultats sans freeze ni objet UI par fichier, chiffres cohérents arbre/treemap/exports, doublons confirmés par contenu.

## Phase 4 — Prototype MFT et performance

- Garder `FSCTL_ENUM_USN_DATA`, parsing raw `$MFT` et journal USN comme fonctions distinctes.
- Comparer API Windows + requêtes taille avec un lecteur brut sur les mêmes volumes et fixtures.
- N’ajouter le helper élevé ponctuel qu’après revue de sécurité; aucune élévation au démarrage ni service permanent. L’UAC ne paraît que si l’analyse MFT demandée la nécessite et après action explicite.
- Exécuter le volet backend de V-06 : comparer les routes MFT au parcours V-05, choisir le backend ou limiter explicitement le support avancé, sous autorisation distincte.
- Valider FRN + séquence, parenté, hardlinks, streams, tailles, records fragmentés, corrompus et mutations.
- Produire benchmarks reproductibles : scan, index, recherche, treemap, annulation, mémoire, CPU, I/O et idle sur HDD/SATA/NVMe.
- Mesurer les trois profils obligatoires ECONOMY, BALANCED, MAXIMUM PERFORMANCE. N’ajouter un scheduler adaptatif à la V1 que si les résultats démontrent bénéfice réel, fiabilité suffisante et absence de régression significative; sinon garder des réglages fixes, prévisibles et bornés. Le scheduler adaptatif n’est pas garanti en V1.
- Émettre ETA/débit seulement après fenêtre stable; publier hardware, build OS, filesystem, cache, privilège, erreurs et outil.

**Porte de phase :** pas d’affirmation MFT complète ni de supériorité à WizTree/TreeSize/WinDirStat avant parité, gestion erreurs et résultats mesurés.

## Phase 5 — Monitoring et widget flottant

- Prérequis : matrice des sources V-17A (D-18). Peut démarrer après la Phase 1, en parallèle des Phases 2–3.
- Mesurer CPU global/par cœur, mémoire/pression, disques, réseau, processus et GPU lorsque l’OS expose une donnée fiable. La liste live des processus vit uniquement dans Monitoring (D-17).
- Températures, fréquences et consommations électriques (W) CPU/GPU uniquement depuis une source autorisée par D-18 (API Windows documentées, bibliothèques constructeur présentes avec le pilote après revue de licence); ni pilote ni élévation; sinon « indisponible ».
- Vue d’ensemble par tuiles (valeur actuelle, maximum de session, mini-courbe) et vues détaillées; graphes live dessinés en WPF par défaut, sélection de métriques, pause, intervalle, ring buffers; historique de performances en mémoire seulement (aucune écriture périodique en base).
- Widget compact, désactivé par défaut, dans le même processus WPF : opacité réglable, déplaçable, redimensionnable, always-on-top désactivé par défaut, indicateurs configurables, accès page Monitoring, fermeture indépendante. Le comportement Close par défaut reste la fermeture complète de l’application; tray et widget-only sont des options. Ajouter navigation clavier, libellés accessibles, contraste Windows et indices redondants à la couleur.
- Faire partager page/widget à un seul sampler; arrêter l’échantillonnage quand aucun consommateur n’est actif, sauf option de fonctionnement en arrière-plan explicitement activée. Le lancement avec Windows est désactivé par défaut.
- Mesurer coût idle, widget visible, widget masqué et paramètres d’actualisation.

**Porte de phase :** chaque mesure est sourcée/qualifiée; valeurs manquantes affichées indisponibles; aucune boucle supplémentaire du widget.

## Phase 6 — Cleaning

- Développer analyseurs catégorie par catégorie : temporaires Windows/utilisateur, caches sûrs identifiables, Corbeille, logs, temporaires d’installation, candidats Windows Update pris en charge, anciens/gros fichiers, doublons et applications volumineuses.
- Chaque résultat détaille description, fichiers, taille, risques, justification et chemin.
- Permettre sélection et désélection par fichier; séparer prévisualisation, confirmation, exécution et historique.
- Corbeille par défaut; suppression permanente action distincte, résumée et confirmée.
- Ajouter opérations Shell et élévation uniquement lorsque catégorie le justifie.
- Exclure règle générique sur Downloads, fichiers personnels/caches actifs, suppression de système risquée et promesse secure erase.
- Tester actions uniquement sur fixtures jetables et environnements contrôlés.

**Porte de phase :** aucune catégorie publiée comme sûre avant critères, erreurs, annulation et restauration appropriés documentés.

## Phase 7 — Optimization Windows contrôlée

- Services strictement informatifs/en lecture seule; la liste live des processus est dans Monitoring et les recommandations y renvoient (D-17). Les entrées de démarrage prises en charge peuvent être activées ou désactivées manuellement, uniquement après confirmation et sauvegarde de l’état initial; proposer la restauration si techniquement possible.
- Recommandations fondées sur mesure (startup coûteux, volume presque plein, processus consommateur, temporaires détectés), avec explication.
- Aucune modification automatique, aucun arrêt silencieux de processus/service et aucune modification d’ACL, de propriétaire ou de permissions système. Pas d’autre action de réglage en V1 sans arbitrage distinct.
- Mesurer et ne pas promettre d’augmentation arbitraire des FPS.

**Porte de phase :** chaque changement a portée, prérequis, confirmation, valeur antérieure et restauration quand possible.

## Phase 8 — Menu contextuel Windows 11

- Implémenter la voie produit déjà choisie : `IExplorerCommand` avec identité de package sur Windows 11; le sparse package reste à valider techniquement. Prévoir un verbe Win32 per-user pour Windows 10 Enterprise LTSC 2019 uniquement après consentement explicite. L’échec du menu ne bloque pas l’application principale.
- Ajouter uniquement les commandes approuvées : ouvrir; « Analyser avec OPTIVANCE » ouvre Storage et demande confirmation; analyser directement dossier ou volume reconnu; pré-analyse de nettoyage. Masquer toute commande pour les autres types de cible/sélections non pris en charge.
- Menu opt-in, preuve de consentement préalable, inscription uniquement après accord, désactivation depuis Settings et retrait propre à uninstall.
- La DLL/handler du menu répond vite; traitement envoyé au programme principal; app fermée = activation avec action/cible validée.
- Vérifier dossier, disque, espaces/Unicode/longs chemins, permissions, instance déjà ouverte, annulation et cible devenue indisponible.
- Confirmer que « Nettoyer » ne peut qu’ouvrir Cleaning et démarrer une analyse sans suppression.
- Vérifier fonctionnement après upgrade, désactivation/réactivation, crash/restart Explorer et uninstall. Si Windows doit recharger les commandes, l’indiquer clairement; ne jamais tuer/redémarrer Explorer en silence.

**Porte de phase :** aucune inscription avant opt-in, pas de commande de suppression, aucune analyse dans l’UI de construction du menu, absence de trace orpheline après retrait.

## Phase 9 — Durcissement et distribution

- Audit sécurité IPC, paths, helper, shell COM, base, logs et file operations.
- Compatibilité Windows sur la matrice explicitement approuvée : Windows 11 x64 maintenu, Windows 10 Enterprise LTSC 2019 x64 et LTSC 2021 x64 uniquement tant que ce dernier reçoit effectivement un support de sécurité applicable. IoT LTSC, Home/Pro, Enterprise non-LTSC et LTSB 2016 sont hors périmètre v1. Revalider les versions à la sortie selon .NET 10 et le cycle Microsoft.
- Exécuter V-02B : choix du certificat réel, chaîne de signature et concordance du Publisher.
- Finaliser l’installateur classique x64 signé, per-user dans le profil et sans admin obligatoire; inclure le runtime WPF .NET 10 self-contained x64 et le sparse package auxiliaire Windows 11. Valider les désinstallations standard (données conservées) et complète (suppression des données après confirmation), ainsi que raccourcis/intégrations retirés. Ne pas remplacer le format principal par MSIX complet ou portable sans nouvelle décision.
- Fournir dépendances natives/runtime, licences et notices complètes, About/Licenses offline, icon, doc et versioning.
- Aucune vérification automatique de mise à jour, notification de nouvelle version, télémétrie ou connexion obligatoire en V1. Distribuer manuellement l’installateur signé et les notes de version. Vérifier les obligations de redistribution et notices du runtime .NET exact avant publication.
- Lancer plan complet de tests et benchmarks; enregistrer connus/limitations sans données inventées.

**Porte release :** nettoyage de l’extension et données, signature, documentation, offline behavior et notices revus sur installation propre/mise à niveau/désinstallation.

## 2. Couverture des fonctionnalités de la spécification

| Fonctionnalité | Phase principale |
|---|---|
| Navigation à six destinations, page d’accueil Storage, indicateur d’activité (pas de Dashboard, D-17) | 1 |
| Analyse avancée, détection de volumes, MFT/fallback, index et recherche | 2–4 |
| Arbre et treemap interactive | 3 |
| Détection de fichiers en double | 3–4 |
| Exports, historique local et statistiques | 1, complété en 3 et 5–7 |
| Monitoring CPU/GPU/RAM/disque/réseau/températures/fréquences/consommation/processus | 5 |
| Widget flottant | 5 |
| Nettoyage prévisualisé et sûr | 6 |
| Trois profils obligatoires; scheduler adaptatif conditionnel | 4 |
| Optimisation Windows et recommandations réversibles | 7 |
| Menu contextuel Windows 11 facultatif | Validation préalable planifiée; implémentation en Phase 8 |
| Installation, signature, désinstallation, mise à jour manuelle | Validation préalable planifiée; finalisation en 9 |
| Désinstallation avancée de logiciels tiers (alternative à Revo Uninstaller) | Version ultérieure, hors V1 |

Toutes les fonctionnalités listées restent au périmètre cible. La séquence est phasée, pas un retrait de fonctionnalités.

## 2A. Périmètre prévu après la V1

Étudier dans une version ultérieure la désinstallation avancée des logiciels tiers : lancer le désinstalleur officiel, rechercher les fichiers/dossiers et clés de registre potentiellement associés, analyser les résidus, en présenter un aperçu détaillé, protéger les composants partagés et faux positifs, et étudier une restauration. Toute suppression nécessitera une confirmation explicite. Aucun effacement automatique de résidus ni nettoyage automatique du registre; cette fonction n’appartient pas à la V1.

## 3. Estimation et limites

L’estimation Phase 0 initiale — 24–36 mois-personnes pour une version initiale de qualité production et 5–8 pour un MVP scan/arbre/treemap — est grossière et ne détaillait pas l’extension Windows 11 moderne ni le cycle d’identité/package. Elle doit être révisée après l’étape préalable de validation. Aucun chiffre révisé n’est inventé dans cette revue. Les éditions/runtime Windows 10, la voie MFT, les catégories de nettoyage, le menu moderne et le mode d’installation peuvent modifier les coûts.

## 4. Règle d’arrêt

Le plan n’autorise pas l’exécution de l’étape préalable ni de Phase 1. Les prototypes attendent une autorisation explicite de validation. Phase 1 fonctionnelle attend ensuite l’approbation explicite des résultats et des décisions nécessaires dans [ARCHITECTURE_DECISIONS.md](ARCHITECTURE_DECISIONS.md). Les tests détaillés sont des plans, pas des tests exécutés ici.
