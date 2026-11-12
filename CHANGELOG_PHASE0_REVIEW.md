# OPTIVANCE — Changelog de la revue Phase 0

**Date :** 6 novembre 2026  
**Nature du travail :** révision de spécification et documentation seulement. Phase 1 non commencée.

> Les sections numérotées consignent des revues documentaires successives. En cas de divergence, la section la plus récente prévaut; les sections précédentes restent la trace des décisions et analyses à leur date.

## 1. Nouveaux livrables

- **`UPDATED_PRODUCT_SPECIFICATION.md`** — synthèse produit révisée du cahier initial, avec stockage, MFT/fallback, tree/treemap, recherche, filtres, doublons, nettoyage, monitoring, widget, optimisation, profils, historique, exports, distribution et confidentialité.
- **`ARCHITECTURE_DECISIONS.md`** — registre structuré des problèmes, options, avantages/inconvénients, recommandations, conséquences de développement et validations utilisateur attendues.
- **`CHANGELOG_PHASE0_REVIEW.md`** — présent document.
- **`ARCHITECTURE_VALIDATION_PLAN.md`** — matrice de prototypes à exécuter uniquement après accord explicite; aucune validation n’a été exécutée.

## 2. Documents réécrits

- **`PROPOSED_ARCHITECTURE.md`** — remplace l’architecture initiale par une proposition WPF + worker Rust + SQLite, protocole local, helper séparé et adaptateur Explorer facultatif. Ajoute le flux de lancement depuis Explorer, les frontières de confiance, l’historique local, le modèle de données, le widget et la distribution.
- **`IMPLEMENTATION_ROADMAP.md`** — séquence les phases de la fondation jusqu’à la distribution, ajoute prototypes package/IPC/widget, une phase pour le menu Windows 11, portes de phase et matrice de couverture des fonctions.

## 3. Documents Phase 0 mis à jour

- **`UI_ANALYSIS.md`** — précise que la recommandation WPF est provisoire; compare les verbes HKCU classiques au mécanisme moderne `IExplorerCommand` + identité de package; ajoute consentement, activation/désactivation et retrait.
- **`TECHNOLOGY_DECISION.md`** — rétrograde le stack WPF + Rust de décision baseline à recommandation provisoire; isole l’adaptateur Shell natif et précise que le menu moderne ne force pas une migration vers WinUI. *(Trace historique : le stack a ensuite été approuvé; voir les sections ultérieures.)*

## 4. Fonctionnalité ajoutée : menu contextuel Windows 11

Les cinq actions demandées sont inscrites : ouverture d’OPTIVANCE; flux générique d’analyse; scan direct d’un dossier; scan d’un disque; analyse préalable de nettoyage sans suppression. Le document précise que la différence entre « Analyser avec OPTIVANCE » et les commandes directes doit encore être définie.

Deux voies natives ont été documentées :

1. Verbes statiques HKCU, faciles à retirer et opérationnels application fermée, mais visibles dans « Afficher plus d’options » sous Windows 11.
2. `IExplorerCommand` moderne avec identité de package/sparse package, qui permet l’intégration au menu principal mais ajoute une DLL native, l’identité de package, la signature, l’activation explicite et le cycle enable/disable/uninstall.

La préférence recommandée dépend de l’exigence d’apparition dans le menu principal. Aucune voie n’est retenue à la place de l’utilisateur. Le menu est désactivé par défaut; aucune inscription ne se fait sans consentement explicite. Désactiver ou désinstaller retire uniquement les éléments propres à OPTIVANCE. Les commandes contextuelles n’offrent aucune suppression. Le désenregistrement peut demander le rechargement d’Explorer; OPTIVANCE ne le redémarre pas en silence.

## 5. Décisions et conséquences rendues explicites

La revue ajoute des recommandations à valider sur :

- minimum Windows 11/Windows 10 précis et éditions couvertes;
- menu moderne contre menu classique et compatibilité par OS;
- consentement par utilisateur ou tous les comptes;
- MSIX complet contre installateur classique + package sparse, et option portable;
- mises à jour manuelles uniquement en v1, sans vérification réseau;
- scanner MFT, permissions, prompt UAC ponctuel et fallback;
- taille logique/allouée en valeur principale;
- résumés de scans ou snapshots complets et rétention;
- cycle de vie du widget, même processus/service de mesures;
- validation du stack WPF/Rust et ajout éventuel d’un composant COM natif;
- nettoyage par catégories et valeurs initiales du scheduler.

Windows 10 Home/Pro est signalé comme hors support standard depuis le 14 octobre 2025; une compatibilité demeure possible mais nécessite des éditions/version/couverture limitées. Les références Microsoft officielles sont incluses dans les documents de décision et UI.

## 6. Vérification de couverture du cahier initial

Aucune fonctionnalité initiale n’a été retirée. La feuille de route garde toutes les capacités : analyse avancée, fallback, index, arbre, treemap, recherche/filtres, doublons, nettoyage prudent, monitoring, widget, profils, scheduler adaptatif, optimisation Windows, historique, exports, paramètres, tests/benchmarks futurs, sécurité, documentation, installateur, signature, désinstallation et publication.

Les fonctions sont phasées et accompagnées de portes de qualité; la feuille de route ne présente pas les fonctions futures comme déjà disponibles. Les garde-fous initiaux sont conservés : pas de scan au démarrage, télémétrie, cloud, auto-update, suppression silencieuse, élévation globale, arrêt automatique de processus/service, prise de propriété, modifications ACL ou driver noyau sans décision distincte.

## 7. Éléments non effectués

- Aucun code applicatif, script, dépendance, manifeste package, DLL ou fichier d’installation créé.
- Aucun changement de registre, de package Windows, de paramètre système ou d’intégration Explorer effectué.
- Aucun scan de disque ou accès MFT réalisé.
- Aucun test, benchmark ou prototype exécuté. Les plans de tests/mesures cités restent futurs.
- Aucune décision utilisateur requise n’est marquée comme approuvée.

La Phase 1 attend l’approbation explicite de la spécification révisée et des choix bloquants décrits dans `ARCHITECTURE_DECISIONS.md`.

## 8. Revue documentaire des décisions définitives — 6 novembre 2026

### Décisions produit intégrées

- D-01 : Windows 10 et Windows 11 sont confirmés. La cible précise proposée pour validation est Windows 10 22H2 x64, avec Windows 11 x64 maintenu à la date de publication. Les éditions Windows 10 et le runtime WPF restent à arbitrer; le périmètre OS n’est pas assimilé à une compatibilité prouvée.
- D-02 : le menu principal moderne Windows 11 via `IExplorerCommand` est confirmé au niveau produit. Une DLL COM native et un sparse package signé sont proposés pour l’implémentation; le fallback Win32 per-user pour Windows 10 ou en cas d’échec reste soumis au consentement et à la validation technique.
- D-04 : le format principal est l’installateur classique. Le sparse package est auxiliaire; l’outil et la portée per-user restent à confirmer.
- D-07 : taille logique comme valeur principale, taille allouée accessible secondairement. Politique hardlinks/streams encore ouverte.
- D-08 : résumé automatique des analyses terminées; inventaire détaillé uniquement sur demande. Rétentions proposées 365 jours pour résumé et 30 jours pour détails, à arbitrer.
- D-09 : le maintien en arrière-plan après fermeture de la fenêtre est une capacité réglable, jamais imposée. Les trois modes, l’icône tray, le widget, le démarrage, la confirmation de fermeture et le partage d’un seul collecteur sont inscrits comme exigences; défaut de fermeture et budgets ressources à décider/mesurer.
- Les contraintes de sécurité initiales restent valides : pas de modification de registre sans accord explicite, aucun nettoyage destructif depuis le menu, aucune élévation générale, pas de service permanent, aucun updater automatique en v1.

### Documents modifiés pendant cette passe

- **`ARCHITECTURE_DECISIONS.md`** — conserve l’historique des décisions, actualise le statut de compatibilité, et ajoute une comparaison explicite des options, avantages/inconvénients, recommandations et conséquences de développement pour les arbitrages encore ouverts.
- **`UPDATED_PRODUCT_SPECIFICATION.md`** — remplace les choix anciens laissés en suspens par les décisions utilisateur confirmées; ajoute les modes complets du cycle de vie et leur menu tray; précise taille logique, historique, installateur classique, `IExplorerCommand`, fallback opt-in et limites Windows 10.
- **`PROPOSED_ARCHITECTURE.md`** — précise la frontière du COM Shell, du sparse package, de l’installateur, du fallback Windows 10, du gestionnaire de cycle de vie, du sampler partagé, de l’historique et de l’OS/runtime.
- **`IMPLEMENTATION_ROADMAP.md`** — insère une étape préalable de validation technique planifiée mais non exécutée, comprenant le socle Windows, packaging, menu, widget/tray, collecteur, IPC, SQLite et MFT. La Phase 1 fonctionnelle attend validation des prototypes et accord exprès.
- **`ARCHITECTURE_VALIDATION_PLAN.md`** — nouveau document avec matrice risques/solutions/méthodes/critères/plateformes/dépendances/statut, références Microsoft, seuils proposés, limites, et décisions qui nécessitent un arbitrage. Aucun essai n’a été lancé.
- **`UI_ANALYSIS.md`** — remplace la question de build minimum Windows 10 par la cible de validation proposée et le risque édition/runtime; aligne le menu W11 sur `IExplorerCommand` confirmé et le fallback Win10 opt-in proposé.
- **`OPEN_SOURCE_AUDIT.md`** — retire de la liste des questions les décisions désormais tranchées (OS génériques, taille principale et mode d’historique) et conserve leurs détails techniques ouverts.
- **`TECHNOLOGY_DECISION.md`** — rend cohérent le choix `IExplorerCommand` Windows 11 et le fallback Win32 per-user Windows 10 proposé, sans valider leur implémentation.

### Points de compatibilité et incohérences clarifiés

- La compatibilité produit Windows 10/11 est maintenant confirmée, mais la version minimale Windows 10 précise reste une cible technique proposée, pas un engagement testé.
- Microsoft documente la fin du support standard Windows 10 au 14 octobre 2025; les éditions LTSC/ESU ont des conditions distinctes. La matrice .NET moderne ne recouvre pas toutes les éditions Windows 10 auxquelles la demande pourrait s’appliquer. Le couple édition/runtime est un bloqueur explicite au lieu d’une promesse silencieuse.
- L’extension moderne Windows 11 nécessite COM natif et identité package; Windows 10 utilise une voie Shell différente. L’installateur classique reste le format principal demandé. Activation, fallback, désactivation, mise à jour et désinstallation restent à éprouver après consentement.
- Les anciens documents qui demandaient encore de choisir la taille logique/allouée, résumé automatique/détails opt-in, Windows 10/11 ou menu principal Windows 11 ont été corrigés lorsque cette question était désormais résolue. Les limites techniques qui subsistent ne sont pas marquées comme validées.

### Éléments non effectués pendant cette passe

- Aucun code, prototype, script applicatif, DLL COM, manifeste sparse package, installateur, dépendance ou package créé.
- Aucun test, benchmark, mesure de consommation CPU/RAM, scan MFT ou test de disque exécuté.
- Aucune inscription, modification de registre, de package, de paramètre Windows ou d’Explorer effectuée.
- Aucun prototype de l’étape préalable ni travail de Phase 1 commencé. La matrice décrit uniquement les travaux futurs et des critères proposés.

La prochaine étape requiert l’arbitrage explicite des points listés dans `ARCHITECTURE_VALIDATION_PLAN.md` puis votre autorisation explicite d’exécuter les prototypes. La Phase 1 fonctionnelle attendra la revue des résultats et un accord séparé.

## 9. Confirmation de la politique Windows et revue .NET — 6 novembre 2026

### Décisions intégrées

- Windows 11 devient la plateforme prioritaire.
- Windows 10 est limité aux éditions LTSC/Enterprise compatibles avec le runtime retenu; Home/Pro est explicitement hors compatibilité garantie. Windows 10 22H2 n’est pas une cible supportée par défaut.
- La stack WPF + Rust est conservée. Le runtime moderne recommandé pour WPF est .NET 10 LTS x64 avec .NET Desktop Runtime 10. Ce choix est une recommandation de runtime; aucune implémentation ou packaging n’est validé.

### Vérification Microsoft et matrice mise à jour

- Microsoft indique .NET 10 LTS jusqu’en novembre 2028; .NET 9 STS et .NET 8 LTS arrivent en fin de support en novembre 2026. WPF est inclus dans .NET Desktop Runtime.
- La table .NET 10 x64 énumère Windows 10 21H2, 1809 et 1607 sous LTSC/Enterprise. Elle ne liste pas Windows 10 22H2. Cette compatibilité runtime ne garantit pas le maintien de sécurité de l’OS.
- Windows 10 Enterprise LTSC 2019 (1809, build 17763) demeure un candidat supporté par l’OS jusqu’en janvier 2029. Enterprise LTSC 2021 (21H2, build 19044) est supporté jusqu’en janvier 2027, puis seulement sous conditions d’ESU applicables. Windows 10 LTSC/LTSB 2016 (1607) a atteint la fin des mises à jour en octobre 2026.
- Windows 10 Enterprise non-LTSC 21H2 et 22H2 ont atteint la fin de service; 22H2 n’apparaît pas dans la matrice .NET 10. Ils ne sont pas garantis. IoT Enterprise LTSC 2021 a un cycle de vie distinct vers 2032 et reste conditionnel, car la matrice .NET ne nomme pas séparément cette SKU.
- Les éditions Windows 11 x64 répertoriées dans la matrice .NET 10 au moment de la vérification sont 26H1, 25H2 et 24H2; 23H2 est limité aux éditions Enterprise/Education. Revalider ces versions avant chaque release.

Sources Microsoft utilisées : [matrice .NET sur Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [versions et cycle de support .NET](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support), [release health Windows 10](https://learn.microsoft.com/en-us/windows/release-health/release-information), fiches Lifecycle de [Windows 10 Enterprise LTSC 2019](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2019), [Windows 10 Enterprise LTSC 2021](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2021) et [Windows 10 IoT Enterprise LTSC 2021](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-iot-enterprise-ltsc-2021).

### Documents modifiés dans cette mise à jour

- **`ARCHITECTURE_DECISIONS.md`** — remplace l’ancien blocage Home/Pro par la politique confirmée; actualise D-01/D-13, recommande .NET 10, ajoute la matrice des builds/éditions et leurs cycles de vie, et réduit les arbitrages restants à IoT LTSC, ESU et mode de déploiement.
- **`UPDATED_PRODUCT_SPECIFICATION.md`** — ajoute la matrice OS/runtime/servicing; exclut explicitement Home/Pro et Windows 10 22H2 de la garantie; donne les cibles LTSC candidates et les limites des cycles de vie.
- **`PROPOSED_ARCHITECTURE.md`** — cible WPF sur .NET 10 + Rust; consigne les Windows 10 compatibles en intersection avec .NET et lifecycle; conserve packaging .NET à valider.
- **`IMPLEMENTATION_ROADMAP.md`** — remplace l’essai Windows 10 22H2 par LTSC 2019/2021 et .NET 10; consigne Home/Pro/22H2 hors garantie et revalidation au moment des releases.
- **`ARCHITECTURE_VALIDATION_PLAN.md`** — ajoute la matrice officielle .NET/Windows, sépare runtime-compatible de OS-maintenu, et convertit les anciens choix Windows/runtime en validations d’installation, servicing et packaging.
- **`TECHNOLOGY_DECISION.md`** — actualise le runtime de la stack conservée WPF + Rust vers .NET 10 LTS x64 et explicite la cadence de maintien.
- **`UI_ANALYSIS.md`** et **`OPEN_SOURCE_AUDIT.md`** — retirent l’ancienne cible Windows 10 22H2 et reflètent l’exclusion Home/Pro ainsi que .NET 10 recommandé.
- **`CHANGELOG_PHASE0_REVIEW.md`** — présent addendum de traçabilité.

### Points restant ouverts et travaux non effectués

- Arbitrer l’inclusion de Windows 10 IoT Enterprise LTSC 2021 et le traitement d’Enterprise LTSC 2021 après janvier 2027 (ESU et politique de support).
- Prototyper le déploiement .NET Desktop Runtime 10 framework-dependent ou self-contained, son servicing, la signature, l’installation propre et le support WPF/IPC/COM sur les OS retenus.
- Aucun code, prototype, package, dépendance, test, paramètre Windows ou changement système n’a été créé/effectué. La Phase 1 reste non commencée et non autorisée.

## 10. Clarification finale de la politique Windows v1 — 6 novembre 2026

Cette entrée remplace toute formulation plus générale ou provisoire des sections précédentes sur les éditions Windows 10. Les anciennes sections restent un historique des décisions Phase 0; cette section définit la politique courante.

- **Windows 11 x64 maintenu** est confirmé comme cible principale et référence de développement.
- **Windows 10 Enterprise LTSC 2019 x64 (1809)** est confirmé comme cible secondaire de référence. La compatibilité technique de l’application reste à valider avant toute garantie.
- **Windows 10 Enterprise LTSC 2021 x64 (21H2)** est uniquement conditionnel tant que l’installation reçoit effectivement un support de sécurité applicable. La seule admissibilité théorique à l’ESU ne suffit pas; l’état doit être vérifié pour la version du produit à publier.
- **Windows 10 IoT Enterprise LTSC** est exclu du support général v1. Toute inclusion future exige une validation technique et une décision séparées.
- **Windows 10 Home/Pro, Enterprise non-LTSC et LTSB 2016** sont explicitement hors périmètre v1, indépendamment d’une ligne runtime générique ou d’une éventuelle éligibilité ESU.
- La stack **WPF + Rust** et la recommandation **.NET 10 LTS x64 / .NET Desktop Runtime 10** restent inchangées.

### Documents ajustés

- **`ARCHITECTURE_DECISIONS.md`** — précise D-01 et D-13, remplace la matrice Windows 10 par les statuts exacts, exclut IoT de l’arbitrage v1 et aligne les cibles de validation des autres décisions.
- **`UPDATED_PRODUCT_SPECIFICATION.md`** — remplace la politique générique et la matrice OS/runtime, exclut explicitement les éditions hors périmètre et retire IoT des arbitrages produit v1.
- **`PROPOSED_ARCHITECTURE.md`** — limite explicitement la matrice et les validations aux systèmes inclus, et ferme l’option IoT au niveau v1.
- **`ARCHITECTURE_VALIDATION_PLAN.md`** — met à jour la matrice, les plates-formes à valider et les critères de suivi du support actif; les éditions exclues ne sont pas des tests de compatibilité v1.
- **`IMPLEMENTATION_ROADMAP.md`** — applique la matrice finale aux jalons de compatibilité et au fallback du menu; IoT n’est plus un arbitrage v1.
- **`TECHNOLOGY_DECISION.md`**, **`UI_ANALYSIS.md`** et **`OPEN_SOURCE_AUDIT.md`** — remplacent les mentions générales Windows 10/Enterprise par la frontière de support v1 confirmée.
- **`MFT_ENGINE_ANALYSIS.md`** — limite le gate de couverture plateforme aux cibles v1.

Aucun code, prototype, test technique, dépendance ou changement système n’a été créé ou effectué dans cette révision documentaire. La Phase 1 n’a pas commencé.

## 11. Intégration des arbitrages produit et techniques — 10 novembre 2026

Cette entrée consigne les décisions validées dans la révision documentaire du 10 novembre. Elle complète et remplace les décisions Phase 0 antérieures qui étaient marquées « ouvertes », « proposées » ou « à arbitrer » sur ces sujets. Les sections antérieures restent dans le changelog comme historique; elles ne décrivent plus la politique courante lorsqu’elles contredisent cette entrée.

### Décisions intégrées

- **Stockage et hardlinks :** tous les chemins restent visibles; taille logique par défaut; une même identité de fichier partagée par hardlinks ne contribue qu’une fois au total de volume et au poids global du treemap. Les alias sont signalés et ne sont pas réadditionnés dans ces totaux.
- **États de scan :** Terminé = complet et intégré à l’historique/statistiques; Interrompu = partiel consultable, exclu des statistiques définitives; Échec = erreur signalée et résultat non fiable. Aucun scan incomplet n’est présenté comme inventaire complet.
- **Historique :** résumés automatiques 365 jours par défaut; inventaires détaillés 30 jours par défaut. Durées modifiables; conservation des inventaires détaillés désactivable.
- **Fermeture :** fermeture complète par défaut avec arrêt de l’application, widget, moteur, collecteurs et monitoring. Tray et widget-only restent optionnels. Aucun processus OPTIVANCE invisible après fermeture complète.
- **Runtime et architecture :** C# WPF + Rust conservés; .NET 10 LTS self-contained x64, runtime WPF embarqué. Un moteur Rust partagé communique avec l’application et le widget par Windows Named Pipes versionnés. Les messages, tailles, versions et commandes sont validés; une panne Rust laisse l’UI ouverte avec une erreur claire.
- **MFT/UAC :** MFT possible pour les analyses avancées; UAC ponctuelle uniquement si le scan demandé l’exige, sans UAC au démarrage ni service permanent. Le helper privilégié reste limité aux opérations autorisées; le reste de l’application reste non élevé.
- **Menu contextuel :** `IExplorerCommand` sous Windows 11; menu classique per-user sous Windows 10 Enterprise LTSC 2019. Le consentement reste requis pour toute inscription. En cas d’échec, l’interface principale reste utilisable.
- **Installation/désinstallation :** installateur classique per-user dans le profil, sans admin obligatoire. Désinstallation standard conserve paramètres/historique; désinstallation complète les supprime avec caches et données OPTIVANCE après confirmation. Raccourcis et intégrations OPTIVANCE sont retirés proprement.
- **Versions ultérieures :** désinstallation avancée de logiciels tiers (alternative envisagée à Revo Uninstaller) exclue de la V1. Une éventuelle version future étudiera désinstalleur officiel, analyse/aperçu des résidus et confirmation. Aucun résidu ni registre n’est nettoyé automatiquement.
- **Compatibilité Windows :** la politique de la section 10 est préservée; IoT LTSC ne revient pas dans les arbitrages V1.

### Documents modifiés

- **`ARCHITECTURE_DECISIONS.md`** — met à jour D-02, D-04, D-06 à D-10 et D-13; ajoute D-15; remplace les statuts ouverts par les arbitrages validés et garde les véritables validations techniques comme travaux futurs.
- **`UPDATED_PRODUCT_SPECIFICATION.md`** — précise hardlinks, états de scan, historique, MFT/UAC, Named Pipes, mode de fermeture, menu contextuel, déploiement self-contained, installation/désinstallation et fonctionnalité tierce hors V1.
- **`PROPOSED_ARCHITECTURE.md`** — harmonise modèle de données, moteur partagé, IPC/récupération après panne, cycle de vie, distribution et périmètre futur.
- **`ARCHITECTURE_VALIDATION_PLAN.md`** — met à jour les critères techniques sans les exécuter; retire les arbitrages déjà tranchés et garde uniquement les sujets réellement ouverts.
- **`IMPLEMENTATION_ROADMAP.md`** — aligne les phases 1–9 sur les décisions validées et inscrit le désinstalleur tiers comme travail post-V1.
- **`TECHNOLOGY_DECISION.md`** — fixe la distribution self-contained, Named Pipes, worker partagé, limites MFT/UAC et portée du Shell.
- **`UI_ANALYSIS.md`** et **`OPEN_SOURCE_AUDIT.md`** — retirent le choix de runtime de la liste des questions ouvertes et reflètent les décisions WPF/.NET 10, scan et hardlinks.
- **`MFT_ENGINE_ANALYSIS.md`** — consigne le périmètre UAC/helper approuvé et la règle hardlink; conserve uniquement le choix du backend comme point technique ouvert.
- **`LICENSE_REVIEW.md`** — remplace l’alternative framework-dependent/self-contained ouverte par la revue des notices du runtime self-contained retenu.
- **`CHANGELOG_PHASE0_REVIEW.md`** — ajoute cette entrée sans effacer l’historique antérieur.

Aucun code, prototype, test technique ou benchmark n’a été créé ou exécuté; aucune dépendance, configuration Windows, registre ni donnée système n’a été modifiée. La Phase 1 reste non autorisée et n’a pas commencé.

## 12. Finalisation documentaire des arbitrages Phase 0 — 10 novembre 2026

Cette entrée complète les sections antérieures sans les effacer. Les décisions plus anciennes qui contredisent les règles ci-dessous restent présentes comme historique et sont remplacées pour la politique courante par cette section et les documents actifs.

### Arbitrages définitivement approuvés

- **Optimization Windows V1 :** processus et services strictement informatifs/en lecture seule; entrées de démarrage prises en charge modifiables manuellement seulement, après confirmation et sauvegarde de l’état initial; restauration proposée si techniquement possible. Aucune action automatique/silencieuse, aucun arrêt silencieux de processus/service, aucun changement d’ACL, propriétaire ou permissions système.
- **Profils :** ECONOMY, BALANCED et MAXIMUM PERFORMANCE sont obligatoires en V1. Le scheduler adaptatif n’est pas garanti; l’inclure seulement si les benchmarks démontrent un bénéfice réel, une fiabilité suffisante et aucune régression significative. Sinon les réglages restent fixes, prévisibles et bornés.
- **Hardlinks NTFS :** tous les chemins demeurent visibles dans arbre et recherche; taille logique affichable pour chacun; un seul représentant stable et indépendant de l’ordre de scan porte le poids comptabilisé dans les agrégats de répertoires et la treemap; les autres chemins sont marqués alias sans seconde contribution. Convention recommandée et documentée : identifier par volume + file reference number + sequence number; choisir le chemin absolu minimal après normalisation de la racine et des séparateurs, comparaison ordinale insensible à la casse, puis ordinale sensible à la casse pour départager. Cette convention devra être validée sur NTFS.

### Règles complémentaires intégrées

- Les Alternate Data Streams sont exclus des totaux ordinaires; une présentation séparée comme métadonnées est possible seulement si leur lecture est fiable.
- Les commandes Explorer s’appliquent aux dossiers et volumes reconnus; les commandes non applicables sont masquées. « Analyser avec OPTIVANCE » ouvre Storage avec confirmation avant le scan.
- Widget, lancement avec Windows et Always-on-top sont désactivés par défaut. Aucun échantillonnage ne continue sans consommateur actif, sauf option explicite de fonctionnement en arrière-plan.
- Aucune vérification automatique de mise à jour ni notification de nouvelle version en V1; distribution manuelle par installateur signé et notes de version.
- L’historique résumé garde états et erreurs des scans terminés, interrompus et échoués. Les chemins partiels ne persistent que si l’inventaire détaillé est activé, soumis à sa rétention.
- Accessibilité : navigation clavier, libellés accessibles, respect des paramètres de contraste Windows et aucune information communiquée uniquement par la couleur.
- SQLite est la solution technique de référence recommandée, sous réserve de validation. Les obligations de redistribution et notices du pack .NET exact doivent être vérifiées avant toute distribution.

### Cohérence des documents et statuts

- La spécification distingue maintenant les arbitrages **approuvés**, les références **recommandées en attente de validation technique**, les éléments **ouverts** et les fonctions **reportées**.
- WPF + Rust et .NET 10 self-contained x64 demeurent approuvés; WinUI 3 est conservé uniquement comme comparaison Phase 0, pas comme choix ouvert. Compatibilité, performance, IPC, packaging et résilience restent non démontrés et doivent être validés avant toute garantie.
- Architecture, spécification, plan de validation et roadmap utilisent les mêmes règles de hardlinks/ADS, d’Optimization, de profils, d’historique, de menu et de distribution. Les validations n’ont pas été exécutées.
- Les notes d’audit datées du 6 novembre conservent la date de leurs sources; leur alignement avec les décisions du 10 novembre est indiqué. `OPEN_SOURCE_AUDIT.md` distingue désormais l’ancien état du dépôt de la documentation Phase 0 présente actuellement.

### Validations techniques encore nécessaires

- Vérifier le backend MFT (FSCTL/énumération et/ou lecture brute), sa couverture, exactitude, privilèges, sécurité et performance; le parcours non élevé reste le fallback.
- Valider la convention déterministe du chemin représentatif, la lecture des ADS séparés, la taille logique/allouée, les erreurs et la cohérence arbre/treemap/export sur des fixtures.
- Valider compatibilité WPF/.NET 10 self-contained sur Windows 11 x64 maintenu, LTSC 2019 x64 et LTSC 2021 seulement sous support de sécurité actif; aucun résultat de compatibilité n’est acquis.
- Valider Named Pipes, helper/UAC, crash/annulation, Shell `IExplorerCommand`, sparse package, signature, installateur per-user, activation/désactivation et les deux désinstallations.
- Valider SQLite (schéma, migrations, reprise, purge et rétention), restauration des entrées de démarrage, profils et budgets CPU/RAM. Évaluer le scheduler adaptatif contre le profil fixe; l’exclure de V1 si ses critères ne sont pas atteints.
- Vérifier les licences et notices du runtime .NET exact avant distribution; mesurer les capteurs matériels et confirmer les exigences d’accessibilité dans l’UI.

### État réel de la Phase 0

Les décisions produit incluses ci-dessus sont consolidées et la documentation est préparée pour validation finale. L’architecture n’est pas encore techniquement démontrée; les recommandations et validations en attente ne sont pas des résultats. Aucune phase de prototype ou Phase 1 n’a commencé. Aucun code, test, benchmark, prototype, dépendance, installateur, changement Windows, registre ou système n’a été créé ou réalisé.

### Fichiers modifiés dans cette mise à jour

- `ARCHITECTURE_DECISIONS.md`
- `UPDATED_PRODUCT_SPECIFICATION.md`
- `PROPOSED_ARCHITECTURE.md`
- `ARCHITECTURE_VALIDATION_PLAN.md`
- `IMPLEMENTATION_ROADMAP.md`
- `TECHNOLOGY_DECISION.md`
- `UI_ANALYSIS.md`
- `OPEN_SOURCE_AUDIT.md`
- `MFT_ENGINE_ANALYSIS.md`
- `PERFORMANCE_ANALYSIS.md`
- `LICENSE_REVIEW.md`
- `THIRD_PARTY_LICENSES.md`
- `CHANGELOG_PHASE0_REVIEW.md`

## 13. Revue finale de cohérence et préparation de la validation finale — 10 novembre 2026

Cette entrée consigne la dernière passe documentaire de Phase 0. Les sections précédentes restent l’historique des décisions à leur date; cette section, avec les documents courants, prévaut pour les statuts et l’état d’avancement. Les décisions produit définitives de la section 12 n’ont pas été rouvertes.

### Audit transversal

- Les 14 documents Markdown présents dans le dépôt ont été parcourus : décisions, plan de validation, architecture, spécification, roadmap, technologie, UI, MFT, performance, open source, comparaison, licences et changelog. Les liens Markdown locaux vers d’autres fichiers ont été vérifiés; aucun lien local cassé n’a été détecté.
- Les règles approuvées restent identiques entre spécification, architecture et roadmap : WPF + Rust, .NET 10 LTS self-contained x64, Named Pipes versionnés, SQLite recommandée en attente de validation, politique Windows, consentement Shell, privilèges UAC ponctuels, états/rétentions, hardlinks, profils, cycle de vie, optimisation, installation et mises à jour manuelles.
- Les décisions anciennes des sections précédentes n’ont pas été effacées. Leur statut historique est distingué de la politique courante; la légende du registre sépare maintenant plus clairement approbation produit, recommandation technique, validation en attente, arbitrage ouvert et élément reporté/rejeté.
- La comparaison de dépôts reste inchangée : c’est un relevé de références amont et non une source de décisions produit. La spécification et l’architecture proposées ne présentent pas l’application comme développée ou testée.

### Modifications apportées

- **`ARCHITECTURE_DECISIONS.md`** — harmonise les statuts de la synthèse D-01 à D-15, ajoute leurs dépendances et consigne en D-16 l’arbitrage de modèle de distribution/licence déjà identifié dans la revue licence. Cet arbitrage reste ouvert avant les engagements de licence et la distribution; il ne bloque pas la fondation sans dépendance tierce.
- **`ARCHITECTURE_VALIDATION_PLAN.md`** — conserve la matrice des risques et ajoute 18 dossiers de validation avec risque, objectif, méthode, conditions/prérequis, critères réussite/échec, conséquence architecturale, preuve attendue et porte. Chaque dossier est explicitement non exécuté; les seuils techniques doivent être fixés avant mesure.
- **`IMPLEMENTATION_ROADMAP.md`** — distingue le travail documentaire Phase 0 complété de sa validation finale, les tests techniques non exécutés, les gates pré-Phase 1, les gates par fonctionnalité/distribution et l’autorisation Phase 1, qui demeure distincte et explicite.
- **`PERFORMANCE_ANALYSIS.md`** — corrige la phrase erronée « dépôt vide » : le dépôt contient la documentation Phase 0, mais aucun exécutable ou moteur de scan; les mesures OPTIVANCE sont inexistantes.
- **`CHANGELOG_PHASE0_REVIEW.md`** — ajoute la présente entrée avec l’état et la liste exacte des fichiers de cette passe.

### Risques, validations et arbitrages restant à traiter

- Les validations des Named Pipes, du cycle worker, du parcours Win32, du backend MFT/helper, des hardlinks/ADS/tailles, de l’index/treemap, du sampler/widget, du Shell, de SQLite, des entrées de démarrage, des profils, du nettoyage, des capteurs, de l’accessibilité et des licences restent futures. Le plan indique leurs critères, conséquences et preuves attendues.
- Les seuils CPU/RAM/I/O, latence et taille des volumes ne sont pas présentés comme décidés ou atteints; ils doivent être préenregistrés avant les essais. Si aucune valeur n’est établie, un benchmark ne peut pas être déclaré concluant.
- La voie MFT et le wrapper SQLite restent des choix techniques recommandés/en attente de validation, pas des dépendances sélectionnées ni des implémentations testées. Les notices de redistribution du runtime .NET exact restent à vérifier avant distribution.
- Le modèle commercial/propriétaire final n’est pas approuvé dans les documents; il n’empêche pas la clôture documentaire ou le travail technique de fondation, mais doit être consigné avant l’approbation d’une dépendance ou d’une licence qui en dépend et avant la distribution.
- Les conditions d’autorisation de Phase 1 sont définies dans `IMPLEMENTATION_ROADMAP.md`. La documentation est prête pour validation finale du propriétaire; l’étape de prototypes et Phase 1 ne sont ni autorisées ni commencées.

### État réel après cette passe

Aucun code, prototype, test, benchmark, dépendance, package, installateur, modification de registre ou paramètre Windows n’a été créé/exécuté. Aucun résultat technique n’est revendiqué. Le travail de documentation de Phase 0 est consolidé et soumis à validation finale; cela ne constitue ni une preuve d’architecture ni une autorisation de Phase 1.

### Fichiers modifiés dans cette passe

- `ARCHITECTURE_DECISIONS.md`
- `ARCHITECTURE_VALIDATION_PLAN.md`
- `IMPLEMENTATION_ROADMAP.md`
- `PERFORMANCE_ANALYSIS.md`
- `CHANGELOG_PHASE0_REVIEW.md`

## 14. Arbitrages Master Product Vision, errata et gouvernance — 11 novembre 2026

**Auteur :** Claude Code, dans le périmètre documentaire autorisé par le propriétaire. Aucun code, prototype, test, installation, commit ni changement système.

### Arbitrages du propriétaire consignés

- **E-01, option A :** avant Phase 1, seul le volet contraintes de V-06 (UI non élevée, UAC après action explicite, refus → parcours classique, helper factice allowlisté, aucun service); choix du backend MFT en Phase 4.
- **E-06 :** choix du certificat réel et validation de la chaîne de signature reportés à V-02B (avant distribution); V-02A et V-12 utilisent une signature de test.
- **Q1–Q10 (Master Product Vision) :** navigation à six destinations sans Dashboard, accueil Storage, processus live dans Monitoring seul, widget dans le même processus, statistiques définies, personnalisation et FR + EN → **D-17**; capteurs par API Windows documentées et bibliothèques constructeur présentes avec le pilote après revue de licence, ni pilote ni élévation, historique de performances en mémoire seulement, graphes dessinés en WPF par défaut → **D-18**; Monitoring et widget (Phase 5) avant Cleaning (Phase 6).
- Restent ouverts : D-16, toolset d’installation (avant V-02A), backend MFT, budgets CPU/RAM, seuils de performance, catégories de nettoyage. SQLite reste une recommandation.

### Corrections documentaires

- **E-02 :** le plan de validation ne promet plus de menu contextuel sur LTSC 2021 (verbe classique limité à LTSC 2019, conforme à D-02).
- **E-03 :** le choix du toolset est déclaré arbitrage préalable à V-02A.
- **E-04 :** Windows 11 23H2 Enterprise/Education marqué hors périmètre (fin des mises à jour en novembre 2026, vérifiée le 11 nov. 2026 sur la page Microsoft de publication de Windows 11).
- **E-05 :** note de calendrier LTSC 2021 (12 janv. 2027) ajoutée à V-01.
- **E-07 :** numérotation de la roadmap corrigée, libellé de statut D-03 aligné, mention historique du changelog clarifiée.
- Suppression du Dashboard répercutée dans la spécification, l’architecture, l’analyse UI, D-07, le plan de validation et la roadmap; ajout de V-17A (étude documentaire des sources de capteurs); V-10 et V-17 étendus; V-02 scindé en V-02A/V-02B; Phases 5 et 6 interverties.

### Gouvernance ajoutée

- `CLAUDE.md` (instructions projet), `.claude/settings.json` (permissions, plugins défaillants ou inutiles désactivés au niveau projet uniquement), `.gitignore`.
- Skills projet `.claude/skills/optivance-guardrails` et `.claude/skills/optivance-evidence-report` (rédigés, sans évaluation exécutée).
- `CODEX_CLAUDE_HANDOFF.md` : répartition Codex/Claude, stratégie de branches proposée (`main`), prochaine mission Codex.
- Dossier `governance/` : errata, analyse d’écarts vision/dépôt, prérequis V-01/V-02.

### État réel après cette passe

Aucune validation technique n’a été exécutée. Phase 0 : clôture formelle toujours à prononcer. Étape de validation et Phase 1 : non autorisées. Le dépôt n’a toujours aucun commit.

### Fichiers modifiés dans cette passe

- `ARCHITECTURE_DECISIONS.md`, `ARCHITECTURE_VALIDATION_PLAN.md`, `IMPLEMENTATION_ROADMAP.md`, `UPDATED_PRODUCT_SPECIFICATION.md`, `PROPOSED_ARCHITECTURE.md`, `UI_ANALYSIS.md`, `CHANGELOG_PHASE0_REVIEW.md`
- Créés : `CLAUDE.md`, `CODEX_CLAUDE_HANDOFF.md`, `.gitignore`, `.claude/settings.json`, `.claude/skills/optivance-guardrails/SKILL.md`, `.claude/skills/optivance-evidence-report/SKILL.md`, `governance/ERRATA_PHASE0.md`, `governance/VISION_GAP_ANALYSIS.md`, `governance/V01_V02_PREREQUISITES.md`

## 15. Clarification des statuts de gouvernance — 11 novembre 2026

- `governance/ERRATA_PHASE0.md` conserve les constats et propositions originaux, mais ajoute une synthèse distinguant les corrections E-01 à E-07 appliquées, les décisions prises, et les validations techniques encore non exécutées. D-16 reste explicitement ouvert.
- `governance/VISION_GAP_ANALYSIS.md` identifie les anciennes questions Q1–Q10 comme un relevé antérieur aux arbitrages; ajoute leurs issues actuelles selon D-17/D-18 et sépare les décisions ouvertes des prérequis et validations techniques.
- Le Dashboard reste supprimé; la navigation à six destinations, l’accueil Storage et l’ordre de livraison Monitoring/widget avant Cleaning sont rappelés sans rouvrir D-17 ou D-18.
- Cette mise à jour est documentaire. Aucune validation technique, aucun prototype et aucun développement de Phase 1 n’en résultent.
