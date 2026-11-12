# OPTIVANCE — Plan de validation technique de l’architecture

**Révision :** 11 novembre 2026 — errata E-01 à E-07, arbitrages Q1–Q10 (D-17, D-18)  
**Statut :** planification documentaire uniquement. Aucun prototype, code applicatif, package, test ou changement Windows n’a été réalisé. L’exécution de cette étape nécessite une autorisation explicite ultérieure.

## 1. Objet et règles de lecture

Ce plan prépare les prototypes indispensables avant le gel de l’architecture et avant le développement fonctionnel. Les choix produit validés ne valent pas preuve de fonctionnement de leur implémentation.

Les arbitrages produit marqués **Approuvé** ne sont pas à rouvrir. **Recommandé — validation technique en attente** désigne un choix de référence dont le fonctionnement doit encore être prouvé; **À valider (technique)** désigne un comportement ou une preuve non établi; **Ouvert (arbitrage)** signale une décision produit réellement nécessaire; **Reporté** est hors V1. **Validé (produit/fait)** ne signifie jamais implémentation prouvée si aucun essai n’a été exécuté. Les seuils numériques proposés sont des valeurs de départ à mesurer, pas des exigences acceptées ni des résultats.

États utilisés dans les tableaux et fiches :

- **Approuvé (produit/fait)** : choix explicitement confirmé par l’utilisateur ou fait appuyé par une source primaire.
- **Recommandé — validation technique en attente** : référence proposée qui n’est pas encore techniquement démontrée.
- **À valider (technique)** : méthode, compatibilité, sécurité, performance ou comportement dont la preuve manque.
- **À prototyper** : ancien libellé de la matrice récapitulative, équivalent à « à valider (technique) »; il décrit un travail futur, pas une autorisation ni un résultat.
- **Bloquant avant [porte]** : preuve requise avant la porte Phase 1, fonctionnalité ou release indiquée.
- **Reporté / rejeté** : hors V1 ou option écartée; elle n’est pas une alternative active.

## 2. Faits documentés et limites connues

- Microsoft distingue compatibilité .NET d’une version de Windows et son propre cycle de service. Politique v1 : Windows 11 x64 maintenu est prioritaire; Windows 10 Enterprise LTSC 2019 x64 est la référence secondaire; LTSC 2021 x64 est conditionnel uniquement pendant que l’installation reçoit effectivement un support de sécurité applicable. IoT Enterprise LTSC, Home/Pro, Enterprise non-LTSC et LTSB 2016 sont exclus du support général v1 ([informations de version Windows 10](https://learn.microsoft.com/en-us/windows/release-health/release-information)).
- Au 6 novembre 2026, .NET 10 est LTS jusqu’en novembre 2028; .NET 9 STS et .NET 8 LTS arrivent en fin de support en novembre 2026. .NET Desktop Runtime fournit WPF ([versions/support .NET](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support), [installation .NET Windows et matrice runtime](https://learn.microsoft.com/en-us/dotnet/core/install/windows)).
- La matrice Microsoft .NET 10 x64 énumère Windows 10 21H2, 1809 et 1607 dans la catégorie LTSC/Enterprise. Elle ne liste pas Windows 10 22H2. L’appartenance à la liste ne garantit pas que la version de Windows reçoive encore les mises à jour de sécurité.
- Windows 10 Enterprise LTSC 2019 (1809) reste dans son cycle de support jusqu’en janvier 2029. Enterprise LTSC 2021 (21H2) arrive en fin de support standard en janvier 2027; après cette date, seule une couverture de sécurité effectivement applicable et reçue par l’installation permet de conserver le statut conditionnel, pas la seule admissibilité à l’ESU. LTSB 2016 (1607) a atteint sa fin des mises à jour le 13 octobre 2026. IoT Enterprise LTSC est hors support général v1, même si son cycle de vie OS est distinct et plus long ([Lifecycle LTSC 2019](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2019), [Lifecycle LTSC 2021](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2021), [Windows 10 release health](https://learn.microsoft.com/en-us/windows/release-health/release-information), [cycle IoT Enterprise LTSC 2021](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-iot-enterprise-ltsc-2021)).
- L’extension du menu moderne Windows 11 utilise `IExplorerCommand`, une DLL COM native et une identité de package. Les callbacks de construction du menu doivent rester rapides et les opérations coûteuses commencent après `Invoke` ([extension moderne File Explorer](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)).
- Un sparse package peut donner une identité à une application Win32 non empaquetée; les capacités Shell ne sont pas identiques sous Windows 10 et Windows 11. Microsoft indique une extension Shell empaquetée Windows 11 seulement et un fallback Win32 séparé pour Windows 10 ([comparaison MSIX Windows 10/11](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/msix-windows10-windows11), [identité sparse](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)).

## 3. Matrice de compatibilité Microsoft vérifiée

Le runtime approuvé est **.NET 10 LTS self-contained x64**, incluant **.NET Desktop Runtime 10** pour WPF. La matrice distingue l’admissibilité documentée par .NET de l’état de support de sécurité Windows. La compatibilité OPTIVANCE demeure à confirmer par validation sur image propre; la redistribution du pack et ses notices doivent être vérifiées avant distribution.

| OS / édition / version | Build indicatif | .NET 10 x64 documenté | Cycle de support Windows au 6 nov. 2026 | Statut pour la matrice produit |
|---|---:|---|---|---|
| Windows 11 x64 maintenu à la release | Variable par mise à jour | Vérifier la version dans la matrice courante | Selon release/édition; prendre la version maintenue au moment de la release | Cible principale et référence de développement, après validation. |
| Windows 11 x64 23H2 Enterprise/Education | 22631 | Oui, ces éditions seulement dans la table .NET | Fin des mises à jour : dernière mise à jour 2026-11 B du 10 nov. 2026 (vérifié le 11 nov. 2026) | **Hors périmètre** : plus de servicing. Versions Windows 11 x64 listées par .NET 10 et encore maintenues au 11 nov. 2026 : 26H1, 25H2, 24H2. |
| Windows 10 Enterprise LTSC 2019 x64 (1809) | 17763 | Oui (ligne 1809 LTSC/Enterprise) | Sécurité jusqu’en janvier 2029 | Cible secondaire de référence; compatibilité à valider. |
| Windows 10 Enterprise LTSC 2021 x64 (21H2) | 19044 | Oui (ligne 21H2 LTSC/Enterprise) | Fin standard janvier 2027; période ultérieure seulement sous support de sécurité effectivement actif | Compatibilité conditionnelle pendant la réception effective du support de sécurité applicable; simple éligibilité ESU insuffisante. |
| Windows 10 IoT Enterprise LTSC | Variable selon édition | La matrice .NET générique ne nomme pas cette SKU distinctement | Cycle de vie distinct | Exclu du support général v1; ajouter seulement après validation technique et décision séparées. |
| Windows 10 Enterprise non-LTSC 21H2 / 22H2 | 19044 / 19045 | 21H2 figure dans le groupe LTSC/Enterprise; 22H2 n’est pas listé | Fin de service le 12 juin 2024 / 14 octobre 2025 | Hors périmètre de support général v1. |
| Windows 10 Enterprise LTSB 2016 (1607) | 14393 | Oui (ligne 1607 LTSC/Enterprise) | Fin des mises à jour le 13 octobre 2026 | Hors périmètre de support général v1. |
| Windows 10 Home/Pro, notamment 22H2 | 19045 pour 22H2 | Hors cible produit | Fin de support standard octobre 2025; aucune incidence sur exclusion v1 | Explicitement hors périmètre v1, sans exception ESU. |

Les dates officielles peuvent différer d’un jour entre tableaux de release-health et fiches Lifecycle du fait du fuseau horaire affiché. Pour un engagement supporté, seule l’intersection OS encore entretenu + édition admise par .NET + architecture x64 est prise en compte. .NET 10 est lui-même maintenu jusqu’en novembre 2028; son remplacement LTS doit être planifié avant sa fin de support.

## 4. Matrice de validation

Dans les tableaux suivants, les mentions génériques « Windows 10 » désignent uniquement Enterprise LTSC 2019 x64 et, sous condition de support de sécurité effectivement actif, Enterprise LTSC 2021 x64. Elles n’incluent jamais IoT LTSC, Home/Pro, Enterprise non-LTSC ou LTSB 2016.

### A. Compatibilité Windows 10 / Windows 11

| Risque ou question | Solution envisagée | Méthode de validation | Critères de réussite mesurables | Plateformes | Dépendances | Statut |
|---|---|---|---|---|---|---|
| Définir et vérifier le plancher Windows cible | Windows 11 x64 maintenu prioritaire; Windows 10 Enterprise LTSC 2019 comme référence secondaire; LTSC 2021 seulement pendant support de sécurité effectivement actif. IoT LTSC et les autres exclusions de D-01 ne sont pas supportées par v1. | Matrice VM propre; installer, lancer et quitter l’app; parcourir destinations et fonctions; vérifier upgrade/uninstall sur LTSC 2019, LTSC 2021 seulement si la sécurité applicable est effectivement reçue, et Windows 11 x64 maintenu. | Installation, démarrage, Settings, scan classique, Monitoring, widget, historique, export, uninstall sans erreur non gérée; rapport consigne édition, build, runtime et résultat pour chaque couple OS/runtime ciblé. | Windows 11 x64 maintenu; W10 Enterprise LTSC 2019 x64; LTSC 2021 x64 sous condition | Images VM, installateur x64, .NET Desktop Runtime 10, Win SDK, outils d’observation | Choix produit **validé**; compatibilité implémentée **à valider** |
| Windows 10 arrive en fin de service sur certaines cibles | Pour LTSC 2021, ne conserver la compatibilité conditionnelle que pendant la réception effective d’un support de sécurité applicable; réévaluer l’état réel à chaque sortie. Les autres exclusions de D-01 restent exclues même si un programme ESU existe. | Vérifier l’état de servicing de l’installation et distinguer compatibilité applicative du maintien de sécurité dans la documentation de release. | Le téléchargement/spécification nomme seulement les cibles v1; LTSC 2021 n’est mentionné comme conditionnel que si support actif reçu. L’éligibilité théorique à l’ESU ne suffit pas. | Windows 11 x64 maintenu; W10 Enterprise LTSC 2019; LTSC 2021 sous condition | Matrice OS .NET, Microsoft Lifecycle, politique support | **À valider par release** |
| Déploiement WPF .NET 10 self-contained x64 | Embarquer `Microsoft.WindowsDesktop.App` 10; l’utilisateur n’installe pas séparément le .NET Desktop Runtime. Aucune édition Windows 10 exclue ne doit être récupérée par un runtime ancien. | Après autorisation : valider publication/install/démarrage WPF, mises à jour manuelles du runtime embarqué, upgrade/uninstall sur les OS inclus; vérifier notices/licences, signature, taille et démarrage. | Sur chaque OS inclus, installation propre et lancement réussis sans runtime préinstallé; le runtime WPF est inclus; les correctifs entrent dans le paquet applicatif signé; aucun fallback silencieux vers un runtime différent. | Windows 11 x64 maintenu; W10 Enterprise LTSC 2019; LTSC 2021 sous support actif | .NET 10 SDK, publication self-contained, installateur | Décision de déploiement **validée**; empaquetage **à valider** |
| Fonctionnalités différentes selon l’OS | Garder fonctions communes; offrir le menu moderne seulement W11 et un verbe classique facultatif sur Windows 10 Enterprise LTSC 2019 seulement (aucune garantie menu sur LTSC 2021); masquer ou expliquer une capacité indisponible. | Exécuter la même matrice fonctionnelle sur les OS inclus et comparer résultats, exceptions et options présentées. | Tous les scénarios ont résultat pass/fail documenté; aucune commande Windows 11 n’apparaît comme disponible si le Shell ne la fournit pas; l’indisponibilité ne rend pas l’app inutilisable. | Windows 11 x64 maintenu; W10 Enterprise LTSC 2019; LTSC 2021 sous condition | APIs OS, .NET Desktop Runtime 10, composant Shell, installer | **À valider** |

### B. Menu contextuel moderne et installateur classique

| Risque ou question | Solution envisagée | Méthode de validation | Critères de réussite mesurables | Plateformes | Dépendances | Statut |
|---|---|---|---|---|---|---|
| Extension moderne `IExplorerCommand` et identité sparse | Installer classique validé comme format principal; sparse package signé auxiliaire proposé; DLL COM native isolée. | Spike COM/package: enregistrer/activer, afficher le menu principal, désactiver, réactiver, mettre à jour et retirer; tester installation propre, compte standard et Explorer relancé normalement. | 500 ouvertures/invocations de menu sans crash ou blocage d’Explorer; callbacks `GetTitle/GetIcon/GetState` p95 ≤100 ms (seuil technique provisoire à calibrer lors de la validation, pas un arbitrage produit); activation/désactivation affecte uniquement OPTIVANCE; aucun résultat avant consentement. | W11 x64; tester au moins la release courante et une précédente maintenue si elle est dans la matrice de support | Windows SDK, C++ COM, manifest/package sparse, certificat de signature, installer classique; outil de packaging à choisir | **Bloquant** pour menu / **à prototyper** |
| Installation classique et identité sparse, signature et Publisher | Installer classique per-user validé comme format principal; identité/sparse package signé auxiliaire et DLL COM native réservés à l’extension Windows 11. Sur LTSC 2019, l’intégration utilise le verbe classique per-user. | Installer sur compte standard; vérifier signature binaire/package/Publisher; upgrade N→N+1, réparation, rollback et uninstall. Vérifier la voie d’intégration propre à chaque OS, activée ou désactivée, et répéter hors réseau. | Signature valide pour chaque composant; après install/upgrade uniquement les entrées attendues existent; après uninstall aucune inscription OPTIVANCE orpheline; données utilisateur conservées sauf choix; aucune élévation non justifiée. | Windows 11 x64 maintenu; Windows 10 Enterprise LTSC 2019 x64; LTSC 2021 x64 sous condition de support de sécurité actif | WiX/Inno/NSIS/MSI candidats non sélectionnés; certificat, sparse package W11, runtime | **À prototyper** |
| Consentement et aucun changement registre sans accord explicite | Désactivé par défaut; Settings décrit l’effet; opt-in explicite avant inscription Shell/startup/package; refus sans mutation. L’installateur doit aussi divulguer et faire consentir toute entrée de registre/package indispensable à l’installation/désinstallation. Disable/uninstall retirent les seules inscriptions OPTIVANCE. | Capturer état registre/package/Startup avant et après install, refus, acceptation, disable et uninstall en compte standard; tester aussi un nettoyage partiellement présent. | Refus : différence nulle sur les inscriptions non consenties. Acceptation : différence limitée aux valeurs/package documentés, avec consentement enregistré. Disable/uninstall : zéro entrée OPTIVANCE résiduelle et aucune valeur d’autres applications modifiée. | Windows 11 x64 maintenu; Windows 10 Enterprise LTSC 2019 x64; LTSC 2021 x64 sous condition de support de sécurité actif, profil utilisateur standard | API de package, HKCU Shell verbs si fallback, installer, Settings | **Validé (exigence)**; réalisation **à prototyper** |
| L’application est fermée au clic et doit recevoir une action sûre | DLL Shell ne transmet qu’une enum fermée et cible canonique; l’app est activée/démarrée et reçoit une requête versionnée via IPC local; aucune analyse dans Explorer. | Tester app fermée, en tray, ouverte, plusieurs clics, cible disparue, chemin Unicode/espaces/long, sélection multiple non supportée et pipe indisponible. | 100 % des actions reçues correspondent à la cible validée; une seule instance applicative; cible invalide rejetée; aucune ligne de commande arbitraire, scan I/O dans Explorer, ou crash; ouverture de l’app ≤5 s (seuil proposé). | W11 x64; verbe classique W10 Enterprise LTSC 2019 après consentement | COM, named pipe, singleton, WPF/Rust, API Shell | **À prototyper** |
| Sémantique et cible des cinq commandes | Les commandes s’appliquent aux dossiers et volumes reconnus; les autres sont masquées. `Analyser avec` ouvre Storage avec confirmation; dossier/volume déclenchent le scan après le clic explicite; `Nettoyer` lance seulement le pré-scan. | Matrice dossiers, volumes, fichiers, arrière-plan de dossier et sélection multiple; vérifier qu’une commande non applicable est masquée et que la cible passe correctement à l’app. | Chaque commande n’apparaît que pour une cible supportée; scan après confirmation là où requis; « Nettoyer » n’a jamais d’effet destructif ou système. | W11 moderne; verbe classique W10 Enterprise LTSC 2019 | `IExplorerCommand`, validation IShellItem, IPC, UI | Décision produit **approuvée**; comportement **à valider** |
| Menu classique Windows 10 / échec d’intégration | Verbe Win32 per-user pour Windows 10 Enterprise LTSC 2019 après opt-in; aucune inscription cachée. L’application principale reste utilisable si le menu échoue ou est absent. | Valider l’activation/refus, app fermée, désactivation et désinstallation sur LTSC 2019; simuler extension indisponible et vérifier l’accès complet à l’interface principale. | Refus = aucun changement registre; intégration réussie uniquement après accord; en cas d’échec, interface principale reste fonctionnelle et donne une explication. | W10 Enterprise LTSC 2019 x64; IExplorerCommand W11 x64 | Shell verbs Win32, HKCU, installer, consentement | Politique **validée**; réalisation **à valider** |
| Explorer doit parfois recharger ses commandes | Ne jamais terminer/redémarrer Explorer; informer l’utilisateur ou attendre le rechargement normal. | Activer/désactiver avec Explorer ouvert; vérifier mise à jour après fermeture/réouverture normale ou nouvelle session; observer absence de processus forcé. | Le Shell retrouve les commandes selon le cycle documenté; aucun process `explorer.exe` tué/redémarré par OPTIVANCE; état Settings reflète une activation réussie ou son échec. | Windows 11 x64 maintenu; Windows 10 Enterprise LTSC 2019 x64 (LTSC 2021 sans garantie menu, hors essai) | Package/Shell, Settings | **À prototyper** |

### C. Widget, zone de notification et cycle de vie

| Risque ou question | Solution envisagée | Méthode de validation | Critères de réussite mesurables | Plateformes | Dépendances | Statut |
|---|---|---|---|---|---|---|
| Fermeture complète par défaut et deux modes optionnels | Close par défaut arrête app, widget, workers, collecteurs et monitoring. Settings peut choisir tray en arrière-plan ou widget-only. | Valider les transitions Close/Quit/window/widget/tray, jobs actifs, fermeture de session et arrêt Windows; la fermeture complète annule/termine les jobs puis quitte. | Fermeture complète = zéro processus OPTIVANCE résiduel; tray/widget-only persistent seulement selon choix explicite; Quit force toujours l’arrêt complet. | Windows 11 x64 maintenu; Enterprise LTSC 2019 x64; Enterprise LTSC 2021 x64 sous support de sécurité actif | WPF, API notification area, lifecycle manager, worker IPC | Décision **validée**; transitions **à valider** |
| Widget, tray et démarrage | Widget, lancement Windows et Always-on-top désactivés par défaut. Un sampler partagé ne fonctionne que pour un consommateur actif sauf option explicite d’arrière-plan. | Vérifier valeurs/persistance, démarrage désactivé, activation opt-in, arrêt à la fermeture/masquage, logon/logoff/shutdown et menu tray. | Aucun prélèvement sans consommateur ou option explicite; aucune tâche Startup si désactivée; tray Quit ferme tout; un seul sampler. | Cibles v1 selon matrice | WPF notification area, Startup per-user, lifecycle manager | Décision produit **approuvée**; transitions **à valider** |
| Plusieurs collecteurs ou polling inutile | Un sampler partagé pour la page Monitoring et le widget (D-17; pas de Dashboard); un collecteur au plus; aucune collecte sans consommateur, sauf option explicite d’arrière-plan. Pas de Windows service. | Instrumenter instances, fréquence, CPU, RAM, handles, réveils et I/O dans les modes pris en charge; vérifier widget et lancement Windows désactivés par défaut. | Aucun sampler sans consommateur/option; jamais plus d’un collecteur; aucun service ou worker résiduel. Les seuils CPU/RAM proposés sont des hypothèses d’essai, pas des exigences utilisateur; les fixer après mesure sur machine de référence. | Cibles v1 selon matrice; machine de référence consignée | ETW/Performance Monitor, API compteurs, WPF | Politique **approuvée**; budgets **à mesurer** |
| Fermeture widget, tâche active, shutdown et logoff | Transitions explicites : Closed, MainVisible, TrayResident, WidgetOnly; scans/monitoring sont des états de travail orthogonaux. Fermer widget peut arrêter monitoring selon option; Quit annule les jobs puis arrête composants. | Tests de transitions table-driven; arrêt forcé de la session; perte IPC; fenêtre cachée; worker en annulation. | Aucun état inaccessible ou invisible; annulation termine dans le budget proposé; reprise après fermeture suit Settings; redémarrage OS ne restaure pas un mode background non demandé. | Windows 11 x64 maintenu; Enterprise LTSC 2019 x64; Enterprise LTSC 2021 x64 sous support de sécurité actif | Lifecycle manager, shutdown notifications, IPC | **À prototyper** |

### D. Tailles, hardlinks et cohérence de l’index

| Risque ou question | Solution envisagée | Méthode de validation | Critères de réussite mesurables | Plateformes | Dépendances | Statut |
|---|---|---|---|---|---|---|
| Confusion logique/allouée | Décision utilisateur: taille logique (EOF) par défaut, allocation en information secondaire. Chaque champ porte une unité et un type explicite. | Fixtures NTFS/ReFS si cible, FAT/exFAT, fichiers sparse/compressés, zéro octet; comparer métadonnées Windows et résultats. | 100 % fixtures contrôlées présentent valeur attendue et libellé de méthode; valeur par défaut partout logique dans tree/treemap/search/history/export; unavailable ≠ 0. | Cibles v1 : Windows 11 x64 maintenu, Enterprise LTSC 2019 x64, LTSC 2021 x64 sous support de sécurité actif; volumes compatibles | Win32 file info, scanner walk/NTFS, index | **Validé (choix produit)**; cohérence **à prototyper** |
| Hardlinks, Alternate Data Streams et reparse points | Tous les chemins visibles; un représentant stable et indépendant de l’ordre du scan porte le poids des agrégats; alias sans double poids; taille logique par chemin. ADS exclus des totaux ordinaires; pas de suivi des liens/jonctions/mount points par défaut. | Fixtures multi-répertoires avec ordre d’énumération varié, collisions de casse, liens cycliques, ADS, sparse/compression et parenté. | Représentant toujours identique; chaque chemin visible; alias signalés; un ID compte une fois; ADS absents des totaux; arbre/treemap/exports concordent. | Cibles v1 et filesystems supportés | File IDs/FRN, FileStandardInfo, reparse tags, API native | Politique **approuvée**; réalisation **à valider** |
| États de scan et mutation pendant l’analyse | Garder état Terminé/Interrompu/Échec, complétude et erreurs; ne jamais prétendre un instantané atomique si non garanti. | Valider changements, renommages, disparitions, refus et annulation sur données de test après autorisation. | Terminé = complet; Interrompu = partiel consultable mais hors statistiques; Échec = erreur et résultats non fiables; aucun scan incomplet n’est présenté comme complet. | Cibles v1 | APIs traversal, journal de scan et index compact | Politique **validée**; comportement technique **à valider** |

### E. Historique SQLite et exports

| Risque ou question | Solution envisagée | Méthode de validation | Critères de réussite mesurables | Plateformes | Dépendances | Statut |
|---|---|---|---|---|---|---|
| Historique, états et rétention | Résumé d’état/erreur des trois statuts; chemins partiels persistés seulement si détails activés. Résumés 365 jours, inventaires détaillés opt-in 30 jours; réglables, détail désactivable. SQLite recommandée sous validation. | Vérifier états, stats filtrées aux terminés, comportement pendant/après session, opt-in détail, migrations, arrêt durant transaction et purge. | État/erreurs conservés; partiel temporairement consultable; aucun chemin partiel persisté sans opt-in détail; seuil de purge respecté; seuls terminés dans statistiques. | Cibles v1 | SQLite, schéma versionné, wrapper à choisir | Politique **approuvée**; SQLite/schema **à valider techniquement** |
| Écritures/stockage excessifs | Agréger en mémoire et écrire transactionnellement en fin de scan; séparer snapshots détaillés/export; retention transactionnelle. | Instrumenter quantité d’écritures, octets DB/WAL et temps commit sur fixture volumineuse (au moins 1M d’entrées synthétiques si matériel disponible); vérifier DB intègre après interruption. | Pas de transaction par fichier; écritures standards bornées à métadonnées/résumé; aucun détail inventaire sans opt-in; définir une borne technique de commit après mesures de référence, avant son implémentation. | Cibles v1 : Windows 11 x64 maintenu, Enterprise LTSC 2019 x64, LTSC 2021 x64 sous support de sécurité actif; tailles de bases documentées | SQLite, système de fichiers, profiler | **À prototyper** |

### F. Frontières WPF/Rust, MFT et distribution

| Risque ou question | Solution envisagée | Méthode de validation | Critères de réussite mesurables | Plateformes | Dépendances | Statut |
|---|---|---|---|---|---|---|
| Contrat WPF/Rust par Windows Named Pipes | Protocole local versionné, ACL compte courant, tailles/versions/commandes validées et allowlist; un seul moteur Rust partagé UI/widget. Un plantage Rust ne ferme pas l’interface; l’UI affiche une erreur compréhensible. | Après autorisation, valider messages inconnus/oversized, pipe refusé/usurpé, worker mort, annulation et plusieurs consommateurs UI/widget. | Commandes hors allowlist rejetées; messages invalides bornés/rejetés; UI reste ouverte sur panne Rust et affiche une erreur; moteur unique; aucun serveur réseau; aucun blocage UI. | Cibles v1 | .NET Named Pipes, Rust, sérialisation/ACL à sélectionner | Architecture **validée**; robustesse **à valider** |
| Accès MFT et UAC | Baseline traversal non élevé; MFT utilisable si choisi; helper ponctuel allowlisté et UAC seulement lorsqu’un scan MFT le requiert après action explicite. Aucun UAC au démarrage ni service permanent; walk en fallback. | Après autorisation, valider parité MFT/walk, permissions, UAC accepté/refusé, annulation, limites helper et absence d’écriture. | UI reste non élevée; refus UAC revient au walk; helper n’exécute que les commandes autorisées et quitte après l’opération; aucun service; aucune écriture MFT/USN. | Cibles v1, NTFS | APIs `DeviceIoControl`, helper, Named Pipe sécurisé | Contraintes privilèges **validées**; choix backend **bloquant** |
| Installateur per-user et désinstallation | Installateur classique x64 dans profil utilisateur, sans admin; .NET 10 self-contained; standard conserve données, complet les supprime après confirmation. Retirer raccourcis et intégrations OPTIVANCE. | Valider install/repair/upgrade/deux types d’uninstall et audit des inscriptions/données après consentement. | Installation depuis compte standard; aucune élévation au setup; standard conserve paramètres/historique; complet efface les données uniquement après confirmation; aucun raccourci/intégration orphelin. | Cibles v1 | Toolset non choisi, signature, COM/sparse package | Politique **validée**; outillage **à valider** |
| Mises à jour dans la v1 sans updater automatique | Publication et installation manuelles d’un nouvel installateur signé; pas de check réseau, feed, tâche ou service de mise à jour. | Test hors réseau: installer N, appliquer manuellement N+1, vérifier version, DLL/package et données locales. Vérifier aucun trafic au démarrage et absence de composant updater installé. | N→N+1 réussit avec signature valide; version apparaît dans About/changelog; capture réseau ne montre aucun trafic d’update; uninstall ne laisse ni updater ni identité Shell orpheline. | Cibles v1 : Windows 11 x64 maintenu, Enterprise LTSC 2019 x64, LTSC 2021 x64 sous support de sécurité actif | Installateur, signature, procédure release manuelle | **Validé (contrainte produit)**; chaîne release **à prototyper** |
| Stack UI/moteur/composants natifs, annulation et erreurs | WPF + Rust, .NET 10 self-contained x64 et Named Pipes versionnés sont approuvés. Le test porte sur frontières, worker, COM native isolée, helper ponctuel, annulation et erreurs; aucun changement de stack sans justification solide et décision explicite de réouverture. | Après autorisation, vérifier IPC/package, injection d’erreurs, fermeture worker, annulation et erreurs OS; crash du worker sans fermeture UI. | UI non élevée; helper allowlisté; données bornées; erreur item n’arrête pas scan; jobs annulables; callbacks Shell rapides. | OS de la matrice de section 3 | WPF/.NET Desktop Runtime 10, Rust stable, Win SDK/C++, SQLite | Architecture **approuvée**; robustesse **à valider** |

### G. Optimization, profils et accessibilité

| Risque ou question | Solution envisagée | Méthode de validation | Critères de réussite mesurables | Plateformes | Dépendances | Statut |
|---|---|---|---|---|---|---|
| Entrées de démarrage réversibles | Processus/services en lecture seule; seules les entrées prises en charge sont modifiables manuellement après confirmation, sauvegarde préalable et restauration lorsque possible. | Fixtures/données d’essai; vérifier avant/après, annulation, échec de sauvegarde, permissions/refus et restauration. | Aucune mutation avant confirmation; seul l’élément choisi change; état initial conservé/restaurable; aucune action sur processus/services/ACL/propriétaire. | Cibles v1 selon matrice | API Startup/entrée ciblée, sauvegarde, Settings | Périmètre **approuvé**; API/restauration **à valider** |
| Profils et scheduler adaptatif conditionnel | Trois profils obligatoires, BALANCED défaut recommandé; réglages fixes et bornés en baseline. Adaptive scheduler seulement si bénéfice/fiabilité sans régression significative. | Benchmarks reproductibles HDD/SATA/NVMe avec CPU/RAM/charge; comparer profils fixes à l’option adaptative et vérifier correction/annulation. | Résultats exacts identiques pour les trois profils; adaptation retenue seulement sur preuve mesurée répondant aux critères; sinon exclue de la V1. | Cibles v1 selon matrice | Scheduler, instrumentation CPU/I/O/RAM, datasets contrôlés | Profils **approuvés**; valeurs à mesurer; scheduler **conditionnel** |
| Accessibilité arbre/treemap/widget | Navigation clavier, libellés accessibles, contraste Windows et information redondante au-delà de la couleur. | Vérifier clavier, lecteur d’écran, réglages de contraste/couleur et focus sur tree/treemap/widget. | Actions primaires utilisables au clavier; éléments nommés accessible; aucune distinction uniquement chromatique; contraste configuré respecté. | Cibles v1 selon matrice | WPF/UI Automation, contrôles personnalisés | Exigence produit **approuvée**; conformité **à valider** |

## 4A. Dossiers de validation détaillés

Les lignes de la matrice ci-dessus sont le sommaire des risques. Les dossiers suivants ajoutent, pour chaque famille d’essais, l’objectif, le mode opératoire, l’environnement, les résultats d’échec, la conséquence architecturale et le livrable exigé. **Tous les dossiers sont non exécutés.** Les environnements destructibles sont des VM/volumes de test avec snapshot, jamais le poste personnel ou un volume de production.

### V-01 — Compatibilité OS et runtime .NET 10

- **Risque / objectif :** l’application x64 WPF + Rust ou une fonction indispensable ne démarre pas sur une cible V1. Vérifier le paquet self-contained sans runtime préinstallé.
- **Méthode et conditions :** installation propre et vérification des vues de base, démarrage/arrêt du worker et opérations de base sur Windows 11 x64 maintenu et Windows 10 Enterprise LTSC 2019 x64. Tester LTSC 2021 uniquement si le système reçoit alors effectivement le support de sécurité applicable; reconsulter la matrice Microsoft .NET à chaque release. Home/Pro, non-LTSC, LTSB 2016 et IoT LTSC ne sont pas des plateformes de qualification V1.
- **Réussite :** setup et lancement réussissent sans runtime Desktop préinstallé; runtime WPF embarqué détecté; version, édition, build, servicing et résultat consignés pour chaque cible admise.
- **Échec / conséquence :** bloquer la garantie de compatibilité de la cible concernée et diagnostiquer une voie compatible sans rétrograder .NET 10 de façon implicite. Toute modification de la politique OS exige un nouvel arbitrage.
- **Preuve attendue :** matrice datée, journaux install/lancement, version/hash du paquet, inventaire du runtime embarqué et anomalies.
- **Porte :** avant gel d’architecture/Phase 1 pour Windows 11 et LTSC 2019; LTSC 2021 est vérifié conditionnellement par release.
- **Calendrier :** LTSC 2021 sort du support standard le 12 janv. 2027. Le tester avant cette date, ou consigner dans le rapport qu’aucun support de sécurité actif n’a pu être établi.

### V-02 — Installation, upgrade, désinstallation et signatures

V-02 est scindé (arbitrage E-06, 11 nov. 2026) : **V-02A** couvre installation per-user, upgrade N→N+1 hors réseau, réparation et les deux désinstallations avec une **signature de test** limitée aux VM, avant Phase 1; **V-02B** couvre le choix du certificat réel, la chaîne de signature, la concordance du Publisher en conditions de distribution et l’horodatage, avant toute distribution. Le choix du toolset est un arbitrage préalable à V-02A.

- **Risque / objectif :** élévation non prévue, paquet non signé, données perdues, inscription orpheline ou désinstallation qui touche une autre application.
- **Méthode et conditions :** compte standard dans VM réinitialisable; install, repair, upgrade manuel N→N+1 hors réseau, rollback si offert, uninstall normal, puis uninstall complet avec confirmation/refus. Vérifier chaque voie OS; sparse identity uniquement pour Shell Windows 11.
- **Réussite :** aucune élévation obligatoire; signature/Publisher valides; normal conserve préférences/historique; complet ne supprime qu’après confirmation; les raccourcis et inscriptions propres à OPTIVANCE sont retirés; aucune mise à jour réseau automatique.
- **Échec / conséquence :** distribution bloquée; revoir toolset, opérations idempotentes, signature ou séparation installateur/package. Ne pas modifier le format principal sans décision.
- **Preuve attendue :** logs setup/uninstall/upgrade, signatures, états avant/après du profil et des inscriptions, résultat hors réseau.
- **Porte :** V-02A (installateur de base, signature de test) avant Phase 1; V-02B (certificat réel, cycle complet signé) avant distribution.

### V-03 — Named Pipes versionnés et frontière C# / Rust

- **Risque / objectif :** messages invalides, usurpés, non bornés ou hors version déclenchant une commande non autorisée, divulgation ou blocage.
- **Méthode et conditions :** protocole et limites définis à l’avance; builds test isolés, comptes Windows distincts; essais avec version inconnue, messages tronqués/dupliqués/volumineux, commande hors allowlist, pipe absent/recréé et requêtes simultanées.
- **Réussite :** seuls messages/version/commandes autorisés sont acceptés; refus des autres comptes et charges hors limites; corrélation/progression bornée; aucune écoute réseau; UI réactive.
- **Échec / conséquence :** revoir ACL, cadrage, validation ou versionnement; bloquer toute fonction IPC tant qu’une faille ou un blocage subsiste.
- **Preuve attendue :** spécification du protocole, limites retenues, matrice pass/fail, traces et mesures de latence.
- **Porte :** contrat et frontière de sécurité bloquants avant Phase 1.

### V-04 — Annulation, erreurs, déconnexion et crash du worker

- **Risque / objectif :** worker orphelin, UI bloquée ou scan incomplet présenté comme terminé après annulation/crash/erreur.
- **Méthode et conditions :** fixtures synthétiques et harness d’injection; refuser l’accès, renommer/supprimer un fichier durant le scan, déconnecter un volume/pipe, arrêter le worker, annuler à plusieurs points de lot, fermer l’UI et provoquer des erreurs d’I/O.
- **Réussite :** UI reste utilisable et explique l’erreur; annulation arrête l’émission au prochain point sûr borné; worker/handles se ferment; l’historique distingue Terminé/Interrompu/Échec, et seul Terminé entre dans les statistiques définitives.
- **Échec / conséquence :** revoir supervision, annulation, états ou transactions; aucune fonction dépendante ne passe sa porte tant que le comportement est ambigu.
- **Preuve attendue :** chronologie, journaux, statuts d’historique, inventaire des processus/handles après fin et reproductibilité.
- **Porte :** cycle worker avant Phase 1; comportement scan avant publication de Storage.

### V-05 — Parcours classique non élevé et fallback

- **Risque / objectif :** omissions silencieuses, liens suivis à tort, ou résultats partiels déclarés complets lorsque MFT est indisponible/refusée.
- **Méthode et conditions :** session standard, volumes et répertoires de test; fixtures chemins Unicode/longs, cachés/système, verrouillés, disparus, refus d’accès, plusieurs filesystems pris en charge, reparse points, symlinks/jonctions/mount points.
- **Réussite :** résultats attendus exacts; refus/omissions et méthode visibles; aucune traversée des liens exclus; erreur d’un item n’arrête pas tout le scan; annulation et état partiel conformes à la spécification.
- **Échec / conséquence :** corriger la couverture/gestion d’erreurs ou limiter explicitement la portée annoncée; le parcours reste la référence de fallback, même si la route MFT existe.
- **Preuve attendue :** fixture avec résultat attendu, diff d’inventaire, erreurs et état final par run.
- **Porte :** avant scanner classique et fallback MFT.

### V-06 — MFT, UAC ponctuelle et helper limité

- **Risque / objectif :** FSCTL_ENUM_USN_DATA et parsing brut ont couvertures et permissions différentes; un helper élevé pourrait exécuter trop de choses.
- **Méthode et conditions :** comparer les routes MFT séparément au parcours V-05 sur volumes NTFS jetables; couvrir records fragmentés/corrompus, accès refusé, mutation concurrente, UAC accepté/refusé et annulation. Observer écritures et journal.
- **Réussite :** routes, couverture et omissions qualifiées; aucune écriture/réparation/changement du journal; UI reste non élevée; helper allowlisté s’arrête après l’action; UAC uniquement après demande explicite quand requis; refus revient au scan classique avec méthode annoncée.
- **Échec / conséquence :** écarter le backend incapable de passer exactitude, sécurité ou couverture; garder le fallback. Aucun claim MFT avant parité et preuves.
- **Preuve attendue :** comparaison aux fixtures, audit d’accès, liste des omissions, logs UAC/helper et mesures de couverture/performance.
- **Porte (arbitrage E-01, option A, 11 nov. 2026) :** volet **contraintes** — UI non élevée, UAC uniquement après action explicite, refus → parcours classique avec méthode annoncée, helper allowlisté à durée limitée, aucun service — démontré avec un helper factice, avant Phase 1; volet **backend** — parité, couverture, absence d’écriture — en Phase 4, avant toute promesse MFT et sous autorisation séparée.

### V-07 — Tailles, hardlinks, ADS et agrégats

- **Risque / objectif :** double comptage, représentant dépendant de l’ordre, confusion EOF/allocation ou ADS inclus dans les totaux.
- **Méthode et conditions :** volume NTFS temporaire avec hardlinks multi-dossiers, casse/Unicode, ordre d’énumération inversé, ADS, sparse et compression; comparer file identity volume+FRN+sequence, tailles attendues, arbre, recherche, agrégats, treemap et exports.
- **Réussite :** chaque chemin visible avec taille logique; un représentant déterministe selon la convention documentée porte le poids unique; alias marqués sans seconde contribution; total/parents/treemap cohérents; taille allouée distincte; ADS hors totaux ordinaires et métadonnées séparées seulement si fiables.
- **Échec / conséquence :** revoir l’identité/normalisation et les règles d’agrégation avant l’index canonique; si la convention ne tient pas sur NTFS, documenter une alternative compatible avec le besoin approuvé puis la revalider.
- **Preuve attendue :** manifeste attendu, répétitions dans différents ordres, tableau de rapprochement des sommes, sortie d’export et résultat ADS.
- **Porte :** avant index canonique et version Storage publiée.

### V-08 — Gros volumes, index, recherche et treemap

- **Risque / objectif :** OOM, freeze UI ou résultats incohérents sur des inventaires à forte cardinalité.
- **Méthode et conditions :** fixtures graduées, jusqu’à au moins un million d’entrées si le matériel le permet; distributions de tailles uniformes/asymétriques/zéro; mesurer séparément scan, index, agrégation, recherche paginée, layout, hit-test, zoom, scroll, annulation et RSS/allocations.
- **Réussite :** aucune corruption/OOM; mêmes totaux et choix du représentant à travers les vues; aucun objet UI par fichier; seuils chiffrés latence/mémoire/rendu fixés avant l’essai et respectés.
- **Échec / conséquence :** revoir structures d’index, pagination, agrégation ou rendu; limiter explicitement la taille annoncée si nécessaire; pas de claim de scalabilité sans mesure.
- **Preuve attendue :** corpus/configuration, profils mémoire/CPU, latences p50/p95, critères préenregistrés, écarts et résultats de cohérence.
- **Porte :** avant les garanties de capacité Storage/treemap; les budgets précis sont techniques et restent à établir avant benchmark.

### V-09 — Mesure de performance des scans et profils

- **Risque / objectif :** comparaisons ou ETA non reproductibles, dues à un état de cache, hardware ou privilège différent.
- **Méthode et conditions :** runs répétés et ordre randomisé sur HDD/SATA SSD/NVMe disponibles; comparer cache chaud/froid et méthodes; même cible, privilège, exclusion et build; enregistrer durée, p50/p95/variabilité, CPU, RAM, I/O et justesse.
- **Réussite :** critères préenregistrés respectés, résultats exacts, données de contexte complètes et variation dans la tolérance fixée avant essai.
- **Échec / conséquence :** aucune revendication vitesse/ETA; revoir la concurrence ou méthode; pas de résultat présenté comme benchmark s’il manque le contexte ou la justesse.
- **Preuve attendue :** protocole, données brutes, configuration de chaque run, rapport de justesse, médiane/p95 et limites.
- **Porte :** avant claims publics ou insertion du scheduler adaptatif; aucun benchmark exécuté ici.

### V-10 — Repos, collecteur partagé et arrière-plan

- **Risque / objectif :** polling sans consommateur, plusieurs collecteurs ou activité cachée de l’application.
- **Méthode et conditions :** capturer CPU, mémoire, threads/handles, réveils, disque et réseau après stabilisation, au moins 15 minutes par mode : app inactive, Monitoring visible, widget visible/masqué, aucun consommateur, arrière-plan explicitement activé. Mesurer aussi le coût du rendu des graphiques (dessin maison par défaut, D-18) et du widget selon la fréquence de rafraîchissement; vérifier qu’aucune écriture périodique en base n’a lieu (historique de performances en mémoire seulement).
- **Réussite :** un sampler maximum partagé; aucun échantillonnage sans consommateur ni option; aucun trafic réseau applicatif; ressources dans les budgets fixés avant essai; capteur absent ne provoque pas de boucle serrée.
- **Échec / conséquence :** corriger cycle de vie, cadence ou propriétaire du sampler; désactiver le mode/capteur fautif jusqu’à preuve.
- **Preuve attendue :** traces ETW/compteurs et réseau, état des consommateurs, budget préalable, rapport comparatif par mode.
- **Porte :** avant Monitoring/widget et toute garantie de ressources; le budget précis reste à mesurer.

### V-11 — Fermeture complète, TrayResident et WidgetOnly

- **Risque / objectif :** fermeture laissant un processus invisible ou arrêt prématuré d’un job sans statut correct.
- **Méthode et conditions :** table de transitions ouverture/fermeture/Quit, widget, tray, job actif/annulation, logoff et shutdown; compte standard et paramètres propres sur OS V1.
- **Réussite :** fermeture complète par défaut = aucun processus OPTIVANCE résiduel; tray et widget-only persistent uniquement après choix explicite; Quit arrête tout; pas de collecte sans consommateur/option; résultat du job garde le statut correct.
- **Échec / conséquence :** revoir machine d’états et superviseur worker avant la fondation/widget.
- **Preuve attendue :** table pass/fail, arbre de processus avant/après, chronologie jobs et valeurs Settings.
- **Porte :** cycle de vie avant Phase 1; transitions UI avant widget.

### V-12 — IExplorerCommand, verbe classique, consentement et commandes

- **Risque / objectif :** handler Shell lent, cible incorrecte, inscription sans accord, commande destructive ou integration orpheline.
- **Méthode et conditions :** Windows 11 x64 pour IExplorerCommand et LTSC 2019 uniquement pour le verbe classique; VM et compte test, snapshots package/registre. Tester cible dossier/volume/fichier/arrière-plan/sélection multiple, application ouverte/fermée, Unicode/chemins longs, pipe absent, activation, refus, disable, upgrade et uninstall. LTSC 2021 n’a pas de support de menu contextuel garanti par la décision actuelle. Le sparse package est signé avec un certificat de test approuvé sur la VM uniquement; le certificat réel relève de V-02B.
- **Réussite :** refus = aucune mutation; seules commandes applicables aux dossiers/volumes reconnus sont visibles; « Analyser avec » ouvre Storage avec confirmation; scan direct seulement après action explicite; « Nettoyer » ouvre Cleaning et lance une pré-analyse sans suppression; l’app fermée reçoit action typée et cible validée; pas d’I/O long dans callbacks; aucun crash/redémarrage forcé d’Explorer; inscription propre retirée.
- **Échec / conséquence :** désactiver l’intégration fautive, revoir handler/package/installateur; UI principale reste utilisable sans Shell.
- **Preuve attendue :** matrice action×cible×OS, snapshots consentement avant/après, logs invocations, p95 callback avec seuil préétabli, audit des entrées après retrait.
- **Porte :** prototype et packaging Shell avant Phase 1 selon roadmap; test sans mutation hors VM de test.

### V-13 — SQLite, migration, rétention et intégrité

- **Risque / objectif :** corruption, perte en migration ou conservation de chemins sans opt-in.
- **Méthode et conditions :** bases de chaque schéma; trois états scan; crash pendant transaction; DB verrouillée/corrompue; migrations; purge aux bornes 365/30 jours; détail activé/désactivé et reprise.
- **Réussite :** intégrité SQLite vérifiée; migration préserve les champs garantis ou échoue sans perte; état/erreur résumé conservés; chemins partiels jamais persistés sans détails activés; seuls scans terminés alimentent les statistiques; rétention respectée.
- **Échec / conséquence :** revoir schéma/transactions/migration; si critères impossibles, évaluer autre technologie sans changer les règles produit d’historique.
- **Preuve attendue :** jeux de DB, schémas, logs de migration/panne, vérification d’intégrité, compte avant/après purge et contrôle opt-in.
- **Porte :** validation de la recommandation SQLite avant fondation/historique; recommandation actuelle non prouvée.

### V-14 — Entrées de démarrage, confirmation, sauvegarde et restauration

- **Risque / objectif :** entrée tierce modifiée, état initial perdu ou restauration destructive.
- **Méthode et conditions :** VM jetable avec entrée de test; confirmation/refus, échec de sauvegarde, désactivation/réactivation, redémarrage et restauration; entrée absente ou modifiée extérieurement.
- **Réussite :** aucun changement avant consentement; une entrée supportée seulement change; état initial sauvé; restauration reproduit l’état initial si supportée; changement externe détecté et non écrasé; processus/services/ACL/propriétaires restent en lecture seule/intacts.
- **Échec / conséquence :** exclure l’API ou classe d’entrée non sûre; ne pas offrir sa modification et conserver l’affichage informatif.
- **Preuve attendue :** diff avant/après, version sauvegardée, journaux confirmation/restauration et état post-reboot.
- **Porte :** avant de déclarer modifiable une classe d’entrée; aucun test sur profil réel.

### V-15 — ECONOMY, BALANCED, MAXIMUM PERFORMANCE et scheduler

- **Risque / objectif :** profils incohérents ou scheduler adaptatif sans bénéfice fiable.
- **Méthode et conditions :** runs répétés, ordre randomisé, mêmes données sur HDD/SATA SSD/NVMe disponibles; mesurer justesse, durée, CPU/RAM/I/O, annulation et stabilité; seuil de gain et tolérance de régression préétablis.
- **Réussite :** trois profils donnent des résultats exacts identiques et paramètres bornés; scheduler inclus seulement si amélioration reproductible et significative selon seuil fixé, sans régression significative de correction, fiabilité ou ressources.
- **Échec / conséquence :** garder les trois profils fixes et omettre le scheduler adaptatif de V1; réviser les valeurs de profils si résultats imprévisibles.
- **Preuve attendue :** protocole préenregistré, données brutes/répétitions, variabilité, comparaison baseline et décision inclusion/exclusion motivée.
- **Porte :** profils avant leur livraison; scheduler conditionnel/non garanti en V1.

### V-16 — Cleaning et Optimization : périmètres d’action

- **Risque / objectif :** suppression/changement hors aperçu confirmé, ou contradiction avec le mode consultatif pour processus/services.
- **Méthode et conditions :** fixtures temporaires et VM réinitialisable; tester annulation/refus, sélections, Corbeille, suppression permanente séparée, erreurs, restauration startup; vérifier ACL/propriétaire/permissions.
- **Réussite :** aucun effet avant confirmation; seuls items prévisualisés et sélectionnés changent; suppression permanente est distincte; chaque catégorie a raisons/risques/historique; processus/services en lecture seule; modification startup manuelle, sauvegardée et restaurable quand possible; aucune mutation ACL/propriétaire.
- **Échec / conséquence :** désactiver la catégorie/action jusqu’à correction; aucune catégorie n’est réputée sûre sans preuve propre.
- **Preuve attendue :** manifeste fixture, diff avant/après, rendu d’aperçu/confirmation, logs d’action et restauration.
- **Porte :** avant publication de chaque catégorie; aucun essai destructif réel.

### V-17A — Sources des capteurs (étude documentaire, sans code)

- **Risque / objectif :** concevoir le Monitoring sur des métriques qu’aucune source autorisée par D-18 ne fournit.
- **Méthode et conditions :** pour chaque métrique de la spécification (CPU utilisation/par cœur/fréquence/température/consommation; GPU utilisation/VRAM/température/fréquence/consommation; RAM et pression mémoire; disques; réseau; processus), recenser les API Windows documentées et les bibliothèques constructeur présentes avec le pilote : privilège requis, OS minimal parmi les cibles v1, constructeurs couverts, licence et conditions d’usage, comportement sans capteur. Exclure toute source exigeant un pilote ou une élévation.
- **Réussite :** matrice complète « disponible / conditionnel / indisponible sans pilote » par métrique, sources primaires citées, licences identifiées.
- **Échec / conséquence :** métrique sans source fiable → affichée « indisponible » et retirée du widget par défaut; aucune promesse.
- **Preuve attendue :** matrice datée avec références.
- **Porte :** avant la conception détaillée du Monitoring (Phase 5); peut être menée pendant la validation d’architecture.

### V-17 — Capteurs matériels et accessibilité

- **Risque / objectif :** mesure inventée ou fonction inaccessible sans souris/contraste standard.
- **Méthode et conditions :** matériel avec/sans capteurs, GPU NVIDIA, AMD, Intel et sans GPU dédié selon disponibilité; comparer données à une référence (source OS ou outil constructeur); injecter absence/erreur; parcourir les six destinations (Storage, Cleaning, Monitoring, Optimization, History, Settings), la treemap, l’indicateur d’activité et le widget avec clavier, lecteur d’écran et paramètres de contraste.
- **Réussite :** valeur absente indiquée indisponible; aucune boucle supplémentaire; contrôles et actions principaux nommés et atteignables au clavier; aucune information par couleur seule; contraste et focus Windows respectés.
- **Échec / conséquence :** exclure le capteur non fiable ou revoir le contrôle/UI Automation avant livraison du module concerné.
- **Preuve attendue :** registre métrique/source, captures capteur absent, matrice clavier/lecteur/contraste, anomalies et version UI testée.
- **Porte :** avant Monitoring/UI finale; aucun capteur ou contrôle vérifié dans cette passe.

### V-18 — Licences, dépendances, runtime et notices

- **Risque / objectif :** redistribuer un composant dont version, provenance, licence ou obligation de notice n’est pas satisfaite.
- **Méthode et conditions :** pour chaque version exacte sélectionnée, auditer manifestes/verrous, transitives, assets, code généré, notices, runtime .NET Desktop 10 et composants natifs; produire SBOM et inventaire de notices.
- **Réussite :** chaque composant a version, URL, titulaire, licence et obligations identifiés; licences/notices .NET exactes incluses; aucune condition inconnue ou incompatible n’est expédiée.
- **Échec / conséquence :** bloquer la distribution, retirer/remplacer la dépendance ou effectuer revue juridique. L’audit actuel de candidats ne constitue pas une clearance de build.
- **Preuve attendue :** lockfiles/build exact, SBOM, copies de licences/NOTICE, manifestes de runtime et revue datée.
- **Porte :** avant tout installateur distribuable; dépendances exactes encore non sélectionnées.

## 5. Points techniques à valider avant les prototypes

Les décisions produit approuvées restent acquises. Les validations ci-dessous vérifient les mécanismes et preuves techniques; elles ne rouvrent pas le périmètre approuvé :

1. **Politique OS v1 :** décisions validées, pas d’arbitrage produit restant. Windows 11 maintenu principal; Enterprise LTSC 2019 secondaire; LTSC 2021 conditionnel sous support actif; exclusions v1 consignées. La couverture de sécurité active de LTSC 2021 est à revérifier par release.
2. **Installation/distribution :** .NET self-contained et installation per-user sans admin validés; le choix du toolset est un arbitrage préalable à V-02A; V-02A et V-12 utilisent une signature de test; le certificat réel et la chaîne de signature relèvent de V-02B; le format sparse reste à valider.
3. **Shell :** périmètre validé (IExplorerCommand W11, menu classique LTSC 2019); détails d’inscription/activation et signature restent à valider. Toute inscription nécessite consentement; l’UI principale reste utilisable en cas d’échec.
4. **MFT :** UAC ponctuelle uniquement si un scan MFT demandé la nécessite; aucun UAC au démarrage/service. Avant Phase 1, seul le volet contraintes de V-06 est requis; le backend est sélectionné en Phase 4 après revue technique.
4bis. **Capteurs (D-18) :** V-17A recense les sources autorisées avant la conception du Monitoring; aucun pilote, aucune élévation pour les capteurs.
5. **Autres validations :** fiabilité de lecture des ADS uniquement pour métadonnées séparées (ils restent exclus des totaux), valeurs des trois profils obligatoires, preuve avant toute inclusion éventuelle du scheduler adaptatif, budgets ressources, règles de démarrage réversible et accessibilité. IoT LTSC et le désinstalleur tiers restent reportés hors V1.

Les recommandations techniques, avantages/inconvénients et conséquences figurent aussi dans [ARCHITECTURE_DECISIONS.md](ARCHITECTURE_DECISIONS.md). Ce plan décrit des validations futures et ne les autorise pas. Les seuils de performance proposés seront traités comme hypothèses de mesure, pas comme arbitrages utilisateur individuels.

## 6. Hors périmètre de cette révision

Cette révision n’exécute aucun des tests décrits. Aucun prototype, code applicatif, script d’installation, manifeste, DLL, package, signature, changement registre, paramètre Windows, scan de stockage ou benchmark n’a été produit. Les critères proposés ne constituent pas des résultats et attendent votre validation avant le lancement des travaux.
