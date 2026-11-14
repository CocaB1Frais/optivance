# OPTIVANCE — Registre des décisions d’architecture

**Révision :** 11 novembre 2026 (arbitrages Master Product Vision Q1–Q10, errata E-01 à E-07)  
**Statut global :** les décisions produit approuvées fixent le besoin; elles ne valident pas automatiquement les mécanismes techniques. Ce registre est documentaire et n’autorise aucun développement ni prototype.

**Légende des statuts :** **Approuvé (produit)** = arbitrage confirmé; **Recommandé — validation technique en attente** = choix de référence sans preuve d’implémentation; **À valider (technique)** = preuve ou comportement non établi; **Ouvert (arbitrage)** = décision produit nécessaire; **Reporté / rejeté** = respectivement hors V1 ou option écartée. « Validé » dans les notes historiques signifie « décision produit approuvée », jamais « mécanisme techniquement testé ». Une validation technique ne rouvre pas un arbitrage produit.

## Synthèse des statuts

| ID | Décision consolidée | Statut produit | État technique |
|---|---|---|---|
| D-01 | Windows 11 x64 maintenu principal; Enterprise LTSC 2019 x64 référence secondaire; LTSC 2021 conditionnel sous support de sécurité actif; autres éditions Windows 10 exclues V1 | Approuvé | Compatibilité applicative à valider sur les cibles incluses |
| D-02 | IExplorerCommand sur Windows 11; menu classique per-user sur LTSC 2019; consentement requis; l’UI reste utilisable en cas d’échec | Approuvé | Identité sparse, signature et cycle de vie à valider |
| D-03 | Aucune inscription Windows/registre sans consentement explicite | Approuvé | Comportement refus/acceptation/désactivation à valider |
| D-04 | Installateur classique per-user sans admin obligatoire; désinstallation standard ou complète après confirmation | Approuvé | Toolset, signature et packaging auxiliaire à valider |
| D-05 | Pas d’updater, de vérification ou notification de nouvelle version en V1; distribution manuelle signée | Approuvé | Chaîne de publication et upgrade manuel à valider |
| D-06 | MFT facultative; UAC ponctuelle uniquement pour une opération demandée qui la nécessite; aucun service permanent | Approuvé | Backend MFT et helper à valider |
| D-07 | Taille logique principale; tous les chemins de hardlinks visibles; poids unique par représentant; ADS hors totaux ordinaires | Approuvé | Identité, convention représentative et agrégats à valider |
| D-08 | États distincts; résumés 365 jours et inventaires détaillés 30 jours par défaut; chemins partiels seulement avec opt-in | Approuvé | SQLite recommandée; schéma/intégrité/rétention à valider |
| D-09 | Fermeture complète par défaut; tray et widget-only configurables | Approuvé | Transitions et budgets de ressources à valider |
| D-10 | C# WPF + Rust; Named Pipes Windows versionnés; moteur partagé UI/widget | Approuvé | Robustesse IPC et récupération après panne à valider |
| D-11 | Processus/services informatifs; entrées de démarrage prises en charge modifiées manuellement après confirmation/sauvegarde | Approuvé | APIs, périmètre pris en charge et restauration à valider |
| D-12 | ECONOMY, BALANCED et MAXIMUM PERFORMANCE obligatoires; scheduler adaptatif conditionnel | Approuvé | Valeurs à mesurer; inclusion du scheduler soumise aux critères |
| D-13 | .NET 10 LTS WPF self-contained x64; runtime embarqué | Approuvé | Publication, signature, taille et notices à valider |
| D-14 | Widget, lancement Windows et always-on-top désactivés par défaut; collecte sans consommateur seulement sur opt-in d’arrière-plan | Approuvé | Mécanismes et transitions à valider |
| D-15 | Désinstallation avancée des logiciels tiers hors V1 | Reporté | Décision de version future seulement |
| D-16 | Modèle de distribution/licence final | Ouvert avant approbation des dépendances et distribution; non bloquant pour la fondation technique | À confirmer avant tout engagement de licence ou release |
| D-17 | Navigation à six destinations sans Dashboard; accueil Storage; processus live dans Monitoring seul; widget dans le même processus; FR + EN | Approuvé (11 nov. 2026) | Shell, indicateur d’activité et accessibilité à valider |
| D-18 | Capteurs : API Windows documentées + bibliothèques constructeur présentes avec le pilote après revue de licence; ni pilote ni élévation; historique de performances en mémoire seulement en V1 | Approuvé (11 nov. 2026) | Disponibilité et fiabilité de chaque source à établir (V-17A, V-17) |

## D-01 — Compatibilité Windows 10 / Windows 11

**Décision utilisateur — validée :** Windows 11 x64 maintenu est la cible principale et la référence de développement. Windows 10 Enterprise LTSC 2019 x64 (1809) est la cible secondaire de référence. Windows 10 Enterprise LTSC 2021 x64 (21H2) est compatible conditionnellement uniquement tant que l’installation reçoit effectivement un support de sécurité applicable; une simple éligibilité à l’ESU ne suffit pas. Windows 10 IoT Enterprise LTSC est exclu du support général v1 jusqu’à validation technique et décision séparées. Windows 10 Home/Pro, Enterprise non-LTSC et LTSB 2016 sont hors périmètre v1.

**Justification.** Cette cible respecte la priorité Windows 11 et limite le support Windows 10 à deux éditions/version précises. La matrice Microsoft .NET et le cycle de sécurité Windows sont des conditions techniques, mais ne remplacent pas la politique produit qui exclut les autres éditions.

**Recommandation technique.** Conserver WPF + Rust et cibler .NET 10 LTS x64. La table Microsoft énumère Windows 10 21H2, 1809 et 1607 LTSC/Enterprise, mais seules Enterprise LTSC 2019 (1809, x64) comme référence secondaire et LTSC 2021 (21H2, x64) sous la condition de support de sécurité effectivement actif entrent dans le périmètre Windows 10 v1. La release Windows 11 x64 doit être maintenue et apparaître dans la matrice .NET 10 au moment de la release. IoT Enterprise LTSC, LTSB 2016, Enterprise non-LTSC et Home/Pro sont exclus du support général v1. Le menu moderne reste Windows 11; proposer un verbe classique Windows 10 après consentement.

**Conséquences.** Chaque API, runtime, package et dépendance native doit déclarer son plancher OS. Les fonctions communes doivent avoir un chemin Windows 10; les fonctions Windows 11 doivent être détectées et désactivables sans panne. Le cycle de vie Windows 10 doit être expliqué au téléchargement.

**Compatibilité / limites.** Microsoft liste .NET 10 x64 pour Windows 10 21H2, 1809 et 1607 sous les catégories LTSC/Enterprise, mais cette liste ne change pas le périmètre produit v1. Windows 10 Enterprise LTSC 2019 demeure dans son support de sécurité jusqu’en janvier 2029. LTSC 2021 atteint sa fin de support standard en janvier 2027; après cette échéance, sa compatibilité OPTIVANCE reste conditionnée à la réception effective d’un support de sécurité applicable par l’installation, et non à sa seule admissibilité à l’ESU. IoT LTSC, Enterprise non-LTSC, LTSB 2016 et Home/Pro restent exclus du support général v1.

**Validations techniques nécessaires avant toute garantie.** Vérifier installation propre, démarrage, UI WPF, scan classique, IPC WPF/Rust, Settings, widget, menu/fallback, upgrade manuel et désinstallation sur Enterprise LTSC 2019 x64 et Windows 11 x64 maintenu; tester Enterprise LTSC 2021 x64 uniquement pendant une période où l’installation reçoit effectivement le support de sécurité applicable. IoT LTSC n’est pas une cible de validation v1; son inclusion future exigerait une revue technique et décision distinctes. Aucun résultat n’est acquis. Références : [OS pris en charge par .NET moderne](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [support .NET](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support), [cycle Windows 10](https://learn.microsoft.com/en-us/windows/release-health/release-information), [Enterprise LTSC 2019](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2019), [Enterprise LTSC 2021](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2021).

**Statut :** politique de compatibilité v1 validée; compatibilité technique des cibles incluses reste à valider avant toute garantie.

## D-02 — Menu contextuel moderne et fallback Windows 10

**Décision utilisateur — validée :** intégrer OPTIVANCE au menu principal moderne de Windows 11 au moyen d’IExplorerCommand et, sur Windows 10 Enterprise LTSC 2019, au menu contextuel classique. Le composant Shell transmet une action typée et une cible validée à OPTIVANCE; aucun scan n’est effectué dans Explorer. Une intégration indisponible ou en échec ne limite pas l’utilisation de l’application principale.

**Justification.** Microsoft exige IExplorerCommand et une identité de package pour que l’extension apparaisse dans le menu principal Windows 11. Le handler est sollicité lors de la construction du menu; il doit répondre vite. L’application doit pouvoir être fermée lorsque la commande est invoquée.

**Solution proposée.** Composant COM natif minimal dans une DLL indépendante (C++ est la voie Microsoft illustrée), inscrit avec une extension de manifeste windows.fileExplorerContextMenus. Le programme principal reste WPF. L’installateur classique fournit un sparse package signé pour donner l’identité nécessaire sur Windows 11. Le menu reste désactivé tant que l’utilisateur ne l’a pas activé dans Settings après consentement. Activation/désactivation inscrit/désinscrit le package ou son extension pour l’utilisateur courant.

**Windows 10.** Sur Enterprise LTSC 2019, fournir un verbe classique per-user pour ouvrir/lancer OPTIVANCE, après consentement. Le support de menu contextuel n’est pas étendu par cette décision à LTSC 2021 ou à une autre édition Windows 10; toute extension de ce périmètre nécessite validation séparée. Si une intégration échoue ou est indisponible, afficher une explication et laisser l’application entièrement utilisable depuis son interface principale. Aucune inscription HKCU avant accord explicite.

**Conséquences.** L’installateur classique gère binaires, signature, identité sparse, activation au niveau OS, mise à niveau et désinstallation. Settings gère le consentement et l’activation par utilisateur. L’extension Shell valide dossier/disque, lance ou active OPTIVANCE et transmet une enum fermée; le worker réalise ensuite l’opération. Elle n’analyse, ne supprime, ne démarre pas de collecteur et n’exécute pas de ligne de commande arbitraire.

**Compatibilité / inconnues.** Les sparse packages et extensions Shell ont des limites OS différentes; l’identité auxiliaire vise le menu Windows 11 uniquement. Pour les OS Windows 10 retenus, le fallback est un composant Win32 distinct dans le menu classique, après consentement. Le package doit être signé et son Publisher correspondre au package. Le rechargement d’Explorer peut être nécessaire après inscription; OPTIVANCE ne redémarre jamais Explorer silencieusement.

**Validations techniques nécessaires.** Sur Windows 11, vérifier le menu principal sur dossier/disque, cinq commandes, app fermée/ouverte, activation/désactivation, upgrade et désinstallation. Sur Windows 10 Enterprise LTSC 2019 seulement, valider le verbe classique per-user. Vérifier aucun changement registre/package en cas de refus; un échec du menu laisse l’application principale utilisable. Vérifier callbacks rapides, aucune lecture/suppression depuis Explorer et une seule instance/action validée. Références : [IExplorerCommand et menu Windows 11](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer), [compatibilité MSIX 10/11](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/msix-windows10-windows11), [sparse package](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps).

**Statut :** périmètre et comportement de repli validés; identity package, signature, inscription per-user, désactivation et cycle de vie à valider techniquement.

## D-03 — Consentement et changements registre/package

**Décision utilisateur — validée :** aucun changement de registre sans consentement explicite. L’intégration Explorer est facultative, activable/désactivable dans Settings et retirée proprement.

**Justification.** Une inscription de verbe, COM ou package modifie l’intégration Windows et doit être visible, facultative et réversible.

**Solution proposée.** Désactivé par défaut; écran de consentement avant la première activation et toute inscription Shell, startup ou package, qui explique les valeurs et effets touchés. L’installateur évite ces inscriptions facultatives par défaut. Si une inscription registre/package est indispensable au fonctionnement ou à la désinstallation, elle est explicitée et consentie avant écriture. Inscription par utilisateur courant uniquement. Un refus n’effectue aucune écriture concernée. Désactiver retire les inscriptions OPTIVANCE. Le désinstallateur retire les mêmes inscriptions et conserve préférences/historique selon le choix de l’utilisateur.

**Conséquences.** L’installateur ne préactive pas l’intégration. Settings déclenche l’action d’activation après consentement; le désinstallateur dispose d’un chemin de nettoyage idempotent. Distinguer la configuration applicative locale des inscriptions Explorer/Startup.

**Compatibilité.** Installer et désinstaller dans un compte utilisateur standard sous Windows 10 Enterprise LTSC 2019 x64, LTSC 2021 x64 seulement pendant réception effective d’un support de sécurité applicable, et Windows 11 x64 maintenu. Aucun HKLM global dans la proposition par défaut.

**Tests nécessaires.** Capturer avant/après les clés HKCU, packages enregistrés et entrées Startup; refus = aucune différence; acceptation = uniquement les entrées attendues; désactivation et uninstall les retirent sans toucher aux autres utilisateurs/valeurs. Tester un uninstall après suppression manuelle d’une partie de l’intégration.

**Statut :** exigence utilisateur validée; périmètre exact d’inscription **à valider (technique)**.

## D-04 — Installateur classique

**Décision utilisateur — validée :** installer OPTIVANCE avec un installateur classique x64 par utilisateur, dans le profil utilisateur, sans droits administrateur obligatoires. Le déploiement WPF .NET est self-contained x64. Pas de MSIX complet comme format principal.

**Justification.** Cela garde le contrôle de l’installation WPF, du helper, des fichiers locaux et de la mise à jour manuelle. Le menu moderne conserve son package sparse auxiliaire; ce package ne remplace pas l’installateur.

**Décisions complémentaires — validées.** Le runtime .NET 10 et WPF sont embarqués; aucun .NET Desktop Runtime séparé n’est requis sur le poste. Une installation standard n’élève pas les privilèges. Les actions qui nécessitent un accès élevé conservent leur UAC ponctuelle. La désinstallation classique retire l’application, raccourcis et intégrations OPTIVANCE, mais conserve réglages et historique. La désinstallation complète supprime aussi réglages, historique, caches et données OPTIVANCE après confirmation explicite. Le toolset (WiX/Inno/NSIS/MSI), les signatures et la conception idempotente restent à choisir/valider. Le choix du toolset est un arbitrage préalable à V-02A. Le choix du certificat réel et la validation de la chaîne de signature sont reportés à V-02B (avant distribution); V-02A et V-12 utilisent une signature de test limitée aux VM.

**Conséquences.** L’installateur classique doit prendre en charge architecture x64, upgrade versionnée, raccourcis, entrée Startup optionnelle, package sparse, retour arrière et nettoyage. Un package sparse n’offre pas à lui seul le modèle complet de désinstallation MSIX.

**Compatibilité.** Valider l’installation depuis un compte standard sur les systèmes inclus en D-01. L’installation par défaut n’exige aucune élévation; toute élévation résiduelle est justifiée et limitée à une action séparée explicitement demandée.

**Validations techniques nécessaires.** Installation, mise à niveau, réparation, désinstallation standard et complète, avec intégration activée/désactivée; retirer uniquement les raccourcis/inscriptions OPTIVANCE; confirmer que standard préserve réglages/historique et que le complet les efface seulement après confirmation; vérifier signatures et Publisher concordants.

**Statut :** format, portée per-user, absence d’admin et deux options de désinstallation validés; toolset, signature et format sparse à valider techniquement.

## D-05 — Mises à jour et réseau

**Décision utilisateur — approuvée :** aucune vérification automatique ni notification de nouvelle version, aucun updater automatique ou réseau en V1. Distribution manuelle par installateur signé et notes de version.

**Solution approuvée.** V1 sans contrôle de version réseau, notification de nouvelle version ni updater. L’utilisateur télécharge et lance manuellement un nouvel installateur signé; changelog et version sont livrés avec la publication. Mise à niveau met à jour la DLL Shell et sparse package sans laisser une ancienne extension.

**Justification / conséquence.** Évite transfert silencieux et dépendance à un endpoint; impose un processus de publication/version/signature et peut retarder les correctifs.

**Compatibilité.** Mise à niveau manuelle doit fonctionner sur les deux OS x64. Ne pas activer App Installer auto-update ou feed appinstaller par défaut.

**Tests nécessaires.** Installer version N puis N+1 hors réseau, conserver l’historique prévu, mettre à jour identité/DLL et nettoyer anciens composants; vérifier fonctionnement si signature, package ou binaire manque.

**Statut :** politique produit approuvée; chaîne de publication signée et parcours N→N+1 à valider techniquement.

## D-06 — MFT et permissions administrateur

**Décision utilisateur — validée :** une analyse avancée peut utiliser la MFT. L’UAC est ponctuelle et n’apparaît que si le scan demandé nécessite la voie MFT. Aucune demande UAC au démarrage, aucun service Windows permanent; le reste de l’application reste non élevé. Le composant privilégié est limité aux opérations explicitement autorisées.

**Problème.** FSCTL_ENUM_USN_DATA, lecture brute de $MFT et journal USN ne sont pas interchangeables. Le chemin de privilèges et l’exhaustivité des tailles/records doivent être démontrés.

**Recommandation.** Garder le parcours classique non élevé comme baseline et fallback. Le backend MFT n’est pas encore choisi. Si un scan MFT le nécessite, afficher l’UAC au moment de l’action et lancer un helper allowlisté limité au volume et aux requêtes explicitement approuvés; l’UI et le scan fallback ne sont jamais élevés.

**Conséquences.** Le refus UAC revient au scan classique clairement indiqué. Helper à durée de vie limitée, lecture seule, pipe authentifié, volume validé, aucune écriture MFT/journal, aucun service permanent, aucune commande arbitraire, annulation et données bornées.

**Compatibilité.** NTFS uniquement pour MFT; Windows 11 x64 maintenu prioritaire, Windows 10 Enterprise LTSC 2019 x64 et LTSC 2021 x64 uniquement tant que ce dernier reçoit effectivement un support de sécurité applicable. Tester volumes pris en charge et états de privilège. Les autres filesystems restent sur traversal classique.

**Validations techniques nécessaires.** Parité avec fixtures et volumes contrôlés : parent/file/sequence, chemins hardlink visibles et agrégat volume unique, tailles, sparse/compression/reparse, records corrompus, mutations, erreurs, permissions et annulation; aucune écriture au volume/journal. Fuzzing ultérieur.

**Statut :** politique de privilèges validée; choix backend et annonce de couverture MFT restent bloqués jusqu’aux validations techniques.

## D-07 — Taille logique principale

**Décision utilisateur — validée :** la taille logique est la valeur affichée par défaut; la taille allouée est accessible comme information secondaire.

**Justification.** La taille logique (EOF) exprime le contenu nominal et correspond au choix produit explicite. L’espace alloué reste distinct et utile pour estimer l’espace disque attribué.

**Conséquences.** Arbre, treemap par défaut, historique, recherche et export utilisent le même réglage logique par défaut. L’utilisateur peut basculer vers la taille allouée. Dossiers cumulés/physique estimé restent étiquetés.

**Décision utilisateur — approuvée :** conserver tous les chemins de hardlink visibles dans l’arborescence et la recherche; afficher la taille logique par chemin; ne compter l’identité physique qu’une fois dans les totaux du volume, agrégats de répertoires et poids du treemap. Un seul chemin représentatif porte ce poids; les autres sont marqués comme alias et ont une contribution d’agrégat nulle.

**Convention déterministe recommandée :** identifier le fichier par volume + file reference number + sequence number. Normaliser seulement le chemin absolu (racine de volume canonique et séparateurs), puis choisir le chemin minimal par ordre ordinal insensible à la casse, avec ordre ordinal sensible à la casse comme départage. Cette méthode ne dépend pas de l’ordre d’énumération; ne pas normaliser les noms Unicode au point de fusionner des noms distincts. Valider cette convention et les limites des API sur NTFS.

Les Alternate Data Streams NTFS sont exclus des totaux ordinaires. Ils peuvent être affichés séparément comme métadonnées si leur lecture est fiable. Ne pas suivre symlinks, jonctions ni points de montage par défaut. Le poids dédupliqué ne doit pas être présenté comme une mesure exacte de l’espace physique du support.

**Validations techniques nécessaires.** Fixtures à tailles EOF/allocation connues et hardlinks multi-dossiers; vérifier la sélection déterministe du représentant quel que soit l’ordre d’énumération, la visibilité de chaque chemin, l’identification des alias, et l’unicité de contribution dans volume/parent/treemap. Vérifier qu’aucun ADS n’entre dans les totaux ordinaires et que les éventuelles métadonnées ADS sont qualifiées. Arbre, treemap, recherche et exports utilisent les mêmes règles; tester aussi symlink/junction/cycle, sparse et compression.

**Statut :** règles produit de taille logique, hardlinks, représentant unique et exclusion des ADS des totaux ordinaires approuvées; réalisation et couverture technique à valider.

## D-08 — Historique et SQLite

**Décision utilisateur — validée :** les scans ont trois états explicites. **Terminé** : résultats complets, ajoutés à l’historique et aux statistiques définitives. **Interrompu** : résultats partiels consultables, exclus des statistiques définitives et jamais présentés comme complets. **Échec** : erreur signalée, résultats non fiables et exclus des statistiques définitives. Les résumés des scans terminés sont automatiques; les inventaires détaillés sont conservés seulement sur demande explicite.

**Décision de rétention — approuvée :** résumés conservés 365 jours par défaut; inventaires détaillés demandés explicitement conservés 30 jours par défaut. Les durées sont modifiables et l’utilisateur peut désactiver la conservation des inventaires détaillés. L’historique résumé garde l’état et les informations d’erreur des scans terminés, interrompus et échoués. Les chemins partiels ne sont conservés que si l’inventaire détaillé est activé, dans la limite de sa rétention.

**Conséquences.** SQLite est la solution technique de référence recommandée, sous réserve de validation technique; prévoir schéma versionné, migrations transactionnelles, ACL utilisateur et écritures groupées. Les états et erreurs restent dans le résumé sans ligne par fichier. Les chemins partiels sont consultables pendant la session; ils ne sont persistés que si l’inventaire détaillé est activé, soumis à sa rétention. Les scans interrompus et échoués n’entrent pas dans les statistiques définitives. Le stockage détaillé est une action distincte, jamais automatique.

**Compatibilité.** Données dans l’emplacement utilisateur non virtualisé par MSIX/installer; migration 10->11 et upgrade sans réseau. Les chemins personnels sont minimisés.

**Tests nécessaires.** Historique exact après complet/partiel/annulé/erreur; scan ordinaire n’ajoute aucun record par fichier; rétention efface au bon seuil; migrations anciennes; coupure pendant transaction; récupération DB; mesures d’écritures et volume disque sur analyse de millions d’entrées.

**Statut :** états, durées par défaut et règles de conservation approuvés; SQLite recommandé en attente de validation technique; schéma et rétention à valider.

## D-09 — Widget et cycle de vie en arrière-plan

**Décision utilisateur — validée :** la fermeture complète est le comportement par défaut. Les modes tray et widget-only sont optionnels et configurables dans Settings.

**Comportements requis dans Settings > General > Application Lifecycle :**
1. Close application completely (défaut) : fermer fenêtre et widget, arrêter collecteurs, worker et composants.
2. Minimize to system tray and keep running : masquer la fenêtre, rester dans la tray; widget peut continuer si activé.
3. Close main window but keep only the floating widget active : garder le widget et seules les mesures dont il a besoin; arrêter le reste.

La fermeture complète arrête l’application principale, le widget, le worker Rust, les collecteurs, le monitoring et les opérations actives de façon contrôlée; aucun processus OPTIVANCE invisible ne reste. Les deux autres modes demeurent configurables : (2) réduction dans la zone de notification avec fonctionnement en arrière-plan; (3) fermeture de la fenêtre principale et maintien du seul widget avec ses collecteurs requis. Le widget, le lancement avec Windows et Always-on-top sont désactivés par défaut. Aucune mesure n’est échantillonnée sans consommateur actif, sauf si l’utilisateur configure explicitement le fonctionnement en arrière-plan. L’icône tray et l’arrêt du monitoring lorsque le widget est masqué restent réglables. Quit force toujours la fermeture complète.

**Menu tray requis :** Open OPTIVANCE, Show/Hide Widget, Settings, Quit OPTIVANCE. Quit arrête réellement collecteurs, jobs et worker.

**Recommandation technique.** Garder WPF, window manager et sampler partagé dans un processus utilisateur; aucun service Windows. Le widget ne crée pas de deuxième collecteur. Démarrage auto recommandé via raccourci per-user Startup plutôt que Run registry; l’activation Settings reste explicite.

**Conséquences / compatibilité.** Le processus WPF reste résident uniquement dans les modes tray/widget; ce n’est pas un service. Aucune collecte sans consommateur actif, sauf réglage explicite de monitoring arrière-plan. À la fermeture complète, annuler/terminer les opérations, fermer le moteur et le helper, puis quitter tous les processus OPTIVANCE. En mode widget-only, ne collecter que les métriques utilisées par le widget.

**Tests nécessaires.** Toutes transitions close/minimize/show/hide/quit/startup/logoff/shutdown; app fermé ne laisse aucun processus; tray idle aucun worker ni collector; widget-only un collector unique; Quit arrête tous les processus dans ≤5 s après cancellation; mesure CPU/RAM sur 15 min idle/tray/widget-only. Budgets CPU/RAM finaux restent à approuver.

**Statut :** fermeture complète par défaut et deux autres modes configurables validés; budgets ressources et transitions techniques à valider.

## D-10 — Architecture WPF, Rust et named pipes

**Statut :** architecture WPF + Rust, named pipes versionnés et moteur Rust partagé validés; détails IPC et comportement de panne à valider techniquement.

**Décision utilisateur — validée.** Conserver C# WPF pour l’interface et un seul moteur Rust x64 partagé par l’application principale et le widget. Communiquer via Windows Named Pipes avec protocole local versionné. Aucun serveur réseau n’est requis. Ajouter le COM natif Explorer comme composant isolé.

**Justification.** Rust garde les gros index et le parsing séparés des modèles UI; WPF offre virtualisation et dessin custom; le menu IExplorerCommand exige une DLL native de toute façon, indépendante de la fenêtre WPF.

**Conséquences.** Cibler WPF sur .NET 10 LTS self-contained x64; embarquer le runtime. Le worker Rust unique traite les requêtes et jobs du shell principal et du widget. Valider les limites de tailles, versions, commandes allowlistées, ACL et erreurs. Si le moteur Rust plante, l’interface reste ouverte et affiche un message compréhensible; sa relance éventuelle ne doit pas masquer l’échec.

**Compatibilité.** Le choix WPF + Rust et .NET 10 LTS x64 est maintenu. Cibles v1 : Windows 11 x64 maintenu et Windows 10 Enterprise LTSC 2019 x64; LTSC 2021 x64 uniquement sous support de sécurité effectivement actif. IoT LTSC et les autres éditions exclues en D-01 ne sont pas des cibles générales v1. COM DLL x64 correspondant au shell; named pipes standard Windows. Le package sparse ne remplace pas l’installateur classique.

**Validations techniques nécessaires.** Compatibilité des versions de messages, ACL current-user, commandes allowlistées, validation systématique des messages/tailles/versions, annulation, singleton et plantage du moteur isolé; l’interface doit rester utilisable et afficher une erreur. Aucun prototype exécuté.

**Statut :** stack et frontières de communication validées; robustesse IPC et récupération après panne à valider. Aucun changement de stack sans justification technique solide.

## D-11 — Nettoyage et optimisation

**Décision utilisateur — approuvée pour V1 :** les informations sur les processus et services Windows restent strictement en lecture seule. Les entrées de démarrage prises en charge peuvent être activées ou désactivées manuellement, après confirmation explicite et sauvegarde de leur état initial; proposer une restauration lorsque techniquement possible. Aucun arrêt silencieux de processus/service, aucune modification automatique, aucun changement d’ACL, de propriétaire ou de permissions système. Pour Cleaning : analyse, sélection par fichier, aperçu, confirmation et Corbeille par défaut; suppression définitive séparée et explicite. L’action contextuelle « Nettoyer avec OPTIVANCE » exécute une pré-analyse seulement.

**Justification.** Limite suppression accidentelle et rend chaque action compréhensible.

**Conséquences.** Analyseur, aperçu, approbation, exécution et historique sont séparés. Seules les entrées de démarrage prises en charge sont modifiables en V1 selon la règle ci-dessus; processus et services restent informatifs. Toute écriture est ponctuelle, confirmée et précédée d’une sauvegarde.

**Compatibilité.** APIs Shell/Recycle Bin testées sur les cibles v1 : Windows 11 x64 maintenu, Windows 10 Enterprise LTSC 2019 x64 et LTSC 2021 x64 uniquement sous support de sécurité effectivement actif. Catégories qui exigent API différente cachées ou indiquées non disponibles.

**Tests nécessaires.** Répertoires jetables; vérifier liste avant/après, annulation, permissions, fichiers déplacés, cible déjà disparue; vérifier clic de menu « Nettoyer » ne supprime pas et ne crée aucun effet de bord.

**Statut :** périmètre et garde-fous produit approuvés; catégories, APIs de démarrage, sauvegarde/restauration et actions prises en charge à valider techniquement.

## D-12 — Profils de performance

**Décision utilisateur — approuvée pour V1 :** les trois profils ECONOMY, BALANCED et MAXIMUM PERFORMANCE sont obligatoires. Les valeurs fixes de workers restent des hypothèses techniques à mesurer.

**Recommandation en attente de validation technique.** BALANCED comme profil initial et réglages fixes, prévisibles et bornés. Le scheduler adaptatif est conditionnel : l’intégrer seulement si des benchmarks démontrent un bénéfice réel, une fiabilité suffisante et l’absence de régression significative; sinon, conserver les réglages fixes. Il n’est pas une fonctionnalité garantie de V1.

**Conséquences.** Moteur enregistre profil, concurrence, limites cache et mesures; aucune promesse de speedup sans comparaison.

**Compatibilité.** Mesurer sur les cibles v1 (Windows 11 x64 maintenu, Windows 10 Enterprise LTSC 2019 x64, LTSC 2021 x64 uniquement sous support de sécurité effectivement actif), HDD/SATA/NVMe, différentes RAM/CPU; les profils ne dépendent pas d’une API Win11 only.

**Tests nécessaires.** Exactitude identique avec chaque profil; mémoire maximale définie; annulation; charge CPU et I/O; comparaisons froid/chaud; gains et interférence mesurés.

**Statut :** trois profils approuvés; valeurs et budgets à mesurer. Scheduler adaptatif conditionnel et non garanti; validation technique requise avant toute inclusion V1.

## D-13 — Runtime .NET/WPF

**Décision de plateforme liée à D-01 — validée :** Windows 11 x64 maintenu est la cible principale; Enterprise LTSC 2019 x64 est la référence secondaire Windows 10; Enterprise LTSC 2021 x64 est conditionnel sous support de sécurité effectivement actif. IoT LTSC, Home/Pro, Enterprise non-LTSC et LTSB 2016 sont hors support général v1. Préférer un runtime moderne, maintenu et durable, même si cela exclut des éditions Windows 10.

**Faits Microsoft vérifiés le 6 novembre 2026.** Les versions modernes actuellement supportées sont .NET 10 LTS (jusqu’en novembre 2028), .NET 9 STS (jusqu’en novembre 2026) et .NET 8 LTS (jusqu’en novembre 2026). La matrice Windows 10 commune à .NET 10/9/8 liste les versions 21H2, 1809 et 1607 sous les catégories LTSC/Enterprise, en x64/x86/Arm64. WPF est pris en charge via le .NET Desktop Runtime. Windows 10 22H2, toutes éditions, n’apparaît pas dans cette matrice; Home/Pro est de plus exclu par la décision produit.

**Décision utilisateur — validée :** conserver WPF + Rust et publier l’application WPF en .NET 10 LTS self-contained x64. Le .NET Desktop Runtime 10 (`Microsoft.WindowsDesktop.App`) est embarqué; l’utilisateur n’installe pas séparément le runtime. Ne pas remplacer cette décision par un déploiement framework-dependent. Planifier une migration manuelle vers la prochaine LTS avant la fin de support de .NET 10, sans updater automatique dans l’application.

**Matrice Windows 10 intersectée avec runtime et cycle de l’OS :**

| Windows 10 | .NET 10 x64 selon Microsoft | Cycle de l’OS au 6 nov. 2026 | Statut OPTIVANCE v1 |
|---|---|---|---|
| Enterprise LTSC 2019 x64 — version 1809, build 17763 | Oui, ligne 1809 LTSC/Enterprise | Mises à jour de sécurité jusqu’au 9 janv. 2029 (date Microsoft peut apparaître au 10 janv. PT) | **Référence secondaire Windows 10**; compatibilité technique à valider avant garantie. |
| Enterprise LTSC 2021 x64 — version 21H2, build 19044 | Oui, ligne 21H2 LTSC/Enterprise | Support standard jusqu’au 12 janv. 2027; toute période ultérieure doit avoir une couverture active | **Conditionnelle uniquement tant que l’installation reçoit effectivement un support de sécurité applicable**; simple éligibilité ESU insuffisante. |
| IoT Enterprise LTSC | La ligne .NET générique ne nomme pas séparément cette SKU | Cycle de vie distinct de l’édition Enterprise LTSC | **Exclu du support général v1**; inclusion future après validation technique et décision séparées uniquement. |
| Enterprise non-LTSC 21H2 / 22H2 | 21H2 figure dans le groupe .NET LTSC/Enterprise; 22H2 non listé | Fin de service | **Hors périmètre de support général v1**. |
| Enterprise LTSB 2016 — version 1607, build 14393 | Oui, ligne 1607 LTSC/Enterprise | Fin des mises à jour le 13 oct. 2026 | **Hors périmètre de support général v1**. |
| Home/Pro | Ne constitue pas une cible produit Windows 10 | Indépendant de la politique v1 | **Hors périmètre de support général v1**. |

Les dates de fin de support distinguent compatibilité du runtime et maintien de sécurité de l’OS. Les dates Microsoft sont affichées selon leur fuseau de référence; la table release-health peut différer d’un jour des fiches Lifecycle en heure du Pacifique. La matrice doit être réévaluée à la sortie.

**Windows 11.** La table .NET 10 x64 courante liste les versions documentées dans cette révision; OPTIVANCE vise la version Windows 11 x64 maintenue et compatible au moment de chaque release, comme cible principale et référence de développement. Les versions/builds pris en charge sont revérifiés avant chaque release.

**Conséquences / validations nécessaires.** Garder WPF + Rust; fixer le TFM `net10.0-windows` après revue d’API minimale; produire et valider un paquet self-contained x64 contenant `Microsoft.WindowsDesktop.App` 10. Valider WPF, named pipes, `IExplorerCommand`/COM x64, treemap, installateur classique, sparse package Windows 11, signature/licences, taille, démarrage, patch du runtime embarqué, upgrade et uninstall. Valider installation/exécution sur Enterprise LTSC 2019 x64 et Windows 11 x64 maintenu; tester LTSC 2021 uniquement tant que le support de sécurité applicable est effectivement reçu. Aucun prototype n’a été exécuté.

**Références Microsoft :** [matrice des OS pour .NET](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [versions et cycle de support .NET](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support), [Windows 10 release health](https://learn.microsoft.com/en-us/windows/release-health/release-information), [Enterprise LTSC 2019](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2019), [Enterprise LTSC 2021](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2021), [Enterprise 21H2](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-and-education).

## D-14 — Démarrage, tray et fonctionnement explicite en arrière-plan

**Décision produit — approuvée :** widget flottant, lancement avec Windows et mode toujours au premier plan désactivés par défaut. Aucun échantillonnage ou monitoring sans consommateur actif, sauf si l’utilisateur active explicitement un fonctionnement en arrière-plan.

**Recommandation technique en attente de validation.** Settings conserve ses préférences localement. Étudier un raccourci Startup per-user pour éviter une clé Run; l’icône tray et les modes restent réglables. Tout mécanisme nécessitant une inscription Windows/registre/package suit le consentement explicite de D-03.

**Conséquences.** Lifecycle manager possède états Closed, Main, Tray, WidgetOnly et transitions explicites. Les opérations de scan et monitoring sont des sous-états orthogonaux. Pas de service Windows; worker Rust ne reste pas actif sans scan/analyse.

**Compatibilité.** Startup folder, notification area et comportement shutdown/logoff sur Windows 11 x64 maintenu, Windows 10 Enterprise LTSC 2019 x64 et LTSC 2021 x64 uniquement pendant réception effective d’un support de sécurité applicable. Si un mécanisme choisi nécessite un registre/package, obtenir consentement selon D-03.

**Tests nécessaires.** Démarrage/disable, sortie tray, close avec job actif, masquage widget, arrêt OS, logoff, relaunch et état local; zéro processus orphelin; collecteurs arrêtés lorsque aucun consommateur/option explicite.

**Statut :** règles produit **approuvées**; mécanismes et transitions **à valider techniquement**.

## D-15 — Désinstallation avancée des logiciels tiers

**Décision utilisateur — validée :** hors périmètre V1 et prévue pour une version ultérieure. La piste envisagée comprend l’usage des désinstalleurs officiels, la recherche de résidus fichiers/registre, une analyse approfondie, un aperçu détaillé et une confirmation avant suppression; les protections anti-faux-positifs/composants partagés et une restauration restent à étudier. Aucune suppression de résidu ni aucun nettoyage automatique du registre.

## D-16 — Modèle de distribution et cadre de licence

**Statut :** arbitrage ouvert avant sélection des composants distribués et publication; il ne bloque ni la revue documentaire ni la fondation technique sans dépendances tierces.

**État actuel :** `LICENSE_REVIEW.md` mentionne une hypothèse de distribution propriétaire/commerciale et précise que le modèle final n’est pas approuvé. Aucune licence de produit, politique de contribution ou dépendance tierce n’est choisie par cette hypothèse.

**Options à conserver ouvertes :**

- **Propriétaire/commercial :** contrôle de la distribution et du produit; impose une sélection stricte des licences compatibles, conservation des notices et revue des restrictions/conditions commerciales.
- **Open source :** transparence et contributions possibles; nécessite de choisir une licence de projet et de définir les obligations applicables aux dépendances copyleft, notices, marques et contributions.
- **Modèle mixte/autre :** peut séparer composants ou offres, mais ajoute des frontières, obligations et coûts de maintenance/licence à examiner.

**Recommandation technique en attente :** ne pas incorporer de code tiers ni approuver de dépendance avant que le modèle de distribution pertinent soit fixé; garder la provenance et notices exactes des composants Microsoft .NET. Le code original peut être développé sans dépendance tierce sélectionnée.

**Conséquences :** ce choix détermine l’éligibilité de composants GPL/AGPL et les documents de distribution; il n’impose pas de changer la stack approuvée. Il devient un bloqueur avant approbation de dépendances dont la licence est incompatible ou inconnue, et avant release.

**Statut :** aucune réponse utilisateur implicite ou choix commercial n’est déduit.

## D-17 — Navigation, page d’accueil et répartition des fonctions

**Décision utilisateur — approuvée le 11 novembre 2026 (Master Product Vision, arbitrages Q1, Q2, Q6–Q10).**

- Pas de Dashboard dédié. Navigation principale à six destinations : Storage, Cleaning, Monitoring, Optimization, History, Settings. Le widget flottant est hors navigation.
- Page d’accueil : Storage (cartes de volumes, aucun scan automatique).
- Redistribution de l’ancien Dashboard : vue d’ensemble du stockage → Storage; métriques CPU/GPU/RAM/disques/réseau → Monitoring et widget; scans récents → Storage et History; bilan de nettoyage → Cleaning et History; recommandations → Optimization.
- Un indicateur d’activité global (opération en cours, progression, annulation, état de la collecte) est visible depuis toutes les pages.
- La liste live des processus vit uniquement dans Monitoring; Optimization conserve Démarrage, Services (lecture seule) et Recommandations, qui renvoient vers Monitoring.
- Le widget est indépendant à l’affichage mais s’exécute dans le même processus WPF, avec le même collecteur (conforme à D-09).
- Statistiques (scans terminés seulement) : évolution de l’espace utilisé par volume, espace récupéré par les nettoyages, nombre de scans par état, extensions principales sur la période.
- Personnalisation V1 : colonnes, thème (clair/sombre/système), densité d’affichage. Langues V1 : français et anglais, localisation prévue dès la fondation.
- Ordre de livraison : Monitoring et widget sont livrés avant Cleaning (voir roadmap).

**Conséquences.** Shell WPF à six destinations et barre d’état d’activité; routage des commandes du menu contextuel vers Storage/Cleaning; aucun impact sur le moteur Rust ni sur le protocole IPC. Les documents qui citaient le Dashboard sont alignés.

**Validations techniques nécessaires.** Navigation clavier et lecteur d’écran sur les six destinations et le widget; indicateur d’activité exact (V-17 accessibilité, V-11 cycle de vie).

**Statut :** approuvé (produit); réalisation à valider.

## D-18 — Sources des capteurs et historique de performances

**Décision utilisateur — approuvée le 11 novembre 2026 (arbitrages Q3, Q4, Q5).**

- Sources autorisées : API Windows documentées, puis bibliothèques constructeur déjà présentes avec le pilote graphique installé (par exemple NVIDIA, AMD, Intel), chargées dynamiquement, sans redistribution, après revue de licence de chacune.
- Interdits : installation ou embarquement d’un pilote; élargissement du helper élevé aux capteurs (D-06 le réserve au MFT); toute élévation pour lire un capteur.
- Températures, fréquences et consommations électriques (W) du CPU et du GPU ne sont affichées que si une source autorisée et fiable les fournit; sinon « indisponible ». Il est attendu que la température et la consommation CPU soient souvent indisponibles avec ces règles.
- Historique de performances : mémoire de session bornée uniquement en V1; aucune écriture périodique en base.
- Graphiques : dessin WPF maison par défaut; une bibliothèque de graphes n’est envisagée qu’après revue de licence et mesure de coût (V-10).
- Le collecteur partagé reste dans le processus WPF; le moteur Rust n’est pas démarré pour le seul monitoring.

**Conséquences.** Une étude documentaire des sources (V-17A) précède la conception détaillée du Monitoring. Chaque bibliothèque constructeur est une dépendance tierce soumise à la revue de licence et à D-16.

**Statut :** approuvé (produit); disponibilité, précision, privilèges et licences de chaque source à établir.

## Dépendances entre décisions

Ces dépendances orientent l’ordre des validations; elles ne constituent pas de nouvelles décisions produit :

- **D-01 → D-02, D-04, D-13 :** la matrice OS borne les plateformes du Shell, de l’installateur et du runtime .NET; toute qualification doit respecter l’état de support conditionnel LTSC 2021.
- **D-03 → D-02, D-04, D-14 :** le consentement précède toute inscription Explorer, package ou mécanisme de démarrage qui touche le profil/Windows.
- **D-04 + D-05 + D-13 → livraison :** le runtime self-contained et les composants signés doivent être intégrés au cycle manuel install/upgrade/uninstall, sans updater ni vérification réseau.
- **D-10 → D-02, D-06, D-09 :** Shell et UI communiquent par le contrat local versionné; helper MFT, instance applicative, widget et fermeture doivent respecter ses limites de commande et de vie.
- **D-06 → scan MFT :** le backend choisi détermine si UAC ponctuelle est nécessaire; le parcours classique non élevé reste le fallback.
- **D-07 → index, treemap, recherche, historique et exports :** toutes ces vues doivent appliquer la même identité, convention de représentant, taille et non-double-comptage.
- **D-08 → SQLite :** le schéma et les transactions doivent préserver états, erreurs, rétention et opt-in des chemins partiels.
- **D-09 + D-14 → monitoring/widget :** le cycle de vie des fenêtres gouverne l’existence du sampler; aucune collecte ne continue sans consommateur sauf option explicite d’arrière-plan.
- **D-11 → actions Optimization :** une entrée de démarrage n’est modifiable que si l’API permet confirmation, état initial récupérable et restauration vérifiée; le reste des informations demeure en lecture seule.
- **D-12 → performances :** les trois profils sont obligatoires; seule la preuve comparative peut satisfaire la condition d’inclusion du scheduler adaptatif.
- **D-15 :** la fonction est reportée et n’est pas une dépendance de la V1.
- **D-16 → approbation des dépendances et distribution :** le modèle de licence/distribution applicable doit être établi avant d’approuver les composants dont les obligations en dépendent et avant publication.
- **D-17 → shell, Monitoring, Optimization :** la navigation et la répartition des fonctions gouvernent la fondation UI et les consommateurs du collecteur (D-09).
- **D-18 → Monitoring/widget :** les sources de capteurs dépendent de V-17A/V-17 et de la revue de licence (D-16); D-06 interdit d’y employer le helper élevé.

## Décisions et validations restant nécessaires

Les politiques OS, WPF + Rust, .NET 10 self-contained x64, Named Pipes, privilèges MFT, optimisation Windows (processus/services informatifs; entrées de démarrage manuelles et réversibles), trois profils, scheduler adaptatif conditionnel, hardlinks/ADS, états/rétention, cycle de vie, installation per-user et modes de désinstallation sont approuvés au niveau produit. SQLite est la base locale de référence recommandée, en attente de validation technique. Les validations restent à effectuer avant toute garantie; LTSC 2021 doit encore recevoir effectivement un support de sécurité applicable à chaque release. IoT LTSC et la désinstallation avancée de logiciels tiers sont reportés/hors V1. La navigation sans Dashboard (D-17) et les règles de capteurs (D-18) sont approuvées. Les travaux ouverts sont techniques : sources de capteurs, backend MFT, toolset (arbitrage préalable à V-02A), certificat (V-02B), sparse package, détails Shell, couverture ADS comme métadonnées, budgets et valeurs mesurés, compatibilité, IPC, stockage et transition de cycle de vie.

## Comparatif des arbitrages techniques non résolus

Les décisions produit déjà validées (politique OS v1, .NET 10 self-contained x64, IExplorerCommand W11 et menu classique LTSC 2019, installation per-user sans admin, états/rétention/hardlinks, fermeture complète par défaut et deux options de désinstallation) ne sont pas rouvertes ici. Le comparatif porte uniquement sur les éléments techniques restant à valider. Les recommandations ne valent pas approbation supplémentaire.

| Sujet | Options, avantages et inconvénients | Recommandation | Conséquences sur le développement |
|---|---|---|---|
| Windows 10 et runtime WPF | .NET 10 LTS : durée de support et WPF via Desktop Runtime; la matrice runtime n’élargit pas le périmètre produit. Le framework-dependent demanderait une installation distincte du runtime et est écarté. | Cibler .NET 10 LTS self-contained x64. Référence secondaire : Enterprise LTSC 2019 (1809), sous validation technique; LTSC 2021 conditionnel seulement tant que la sécurité applicable est effectivement reçue. Exclure du support général v1 IoT LTSC, Enterprise non-LTSC, LTSB 2016 et Home/Pro. | TFM .NET 10, runtime WPF embarqué, servicing via nouveaux paquets signés et migration manuelle vers la prochaine LTS; revalider l’OS cible et LTSC 2021 à chaque release. |
| Accès MFT et permissions | Parcours Win32 non élevé : plus simple et sans UAC; couverture/latence potentiellement moins bonnes. FSCTL_ENUM_USN_DATA : API structurée pouvant nécessiter un processus administrateur. Lecture brute MFT : couverture/performance à mesurer, parser bas niveau et risque accru. | MFT peut être utilisé pour une analyse avancée. Le backend reste ouvert; tout UAC est ponctuel et n’apparaît qu’après action explicite si le scan MFT le nécessite. Aucune élévation au démarrage ou service permanent; walk non élevé en fallback. | Scanner multi-backends, méthode/couverture explicites, refus UAC avec repli, helper allowlisté limité, aucune écriture MFT/USN. Valider parité et sécurité avant d’annoncer le backend. |
| Taille logique, chemins et hardlinks | Taille logique (EOF) affichée par chemin; un poids unique par identité physique dans les agrégats. | Garder tous les chemins; sélectionner un représentant par ordre de chemin absolu normalisé déterministe; alias sans poids secondaire; ADS exclus des totaux ordinaires. | Valider identité volume/file ID/sequence, ordre indépendant du scan et cohérence arbre/treemap/recherche/exports; ADS éventuels affichés séparément. |
| Historique et états de scan | Résumé d’état léger; inventaire détaillé opt-in. | État/erreurs des trois résultats dans le résumé; chemins partiels seulement si détails activés; résumé 365 jours et détails 30 jours, modifiables. | SQLite recommandée en attente de validation; schéma versionné, rétention et purge selon opt-in; seuls les scans complets alimentent les stats définitives. |
| Widget et cycle de vie | Fenêtre/widget/collecteur dans le processus UI : moins de complexité; processus/service séparés ajoutent IPC, mémoire et maintenance. | Un processus WPF utilisateur, un moteur Rust partagé, un collecteur unique et aucun service. Fermeture complète par défaut; tray et widget-only optionnels. | Machine d’état Main/Tray/WidgetOnly/Closed, Quit complet, arrêt des jobs/collecteurs/processus à la fermeture complète; budgets de ressources encore à valider. |
| Menu contextuel | `IExplorerCommand` moderne exige identité package, COM native et signature; un verbe Win32 classique est adapté au menu Windows 10 mais demande inscription consentie. | `IExplorerCommand` Windows 11; verbe classique per-user Windows 10 Enterprise LTSC 2019 seulement. En cas d’échec/absence, l’UI principale reste utilisable. | Settings opt-in, aucun changement registre avant consentement, opérations hors Explorer, retirer proprement l’intégration. LTSC 2021 menu support non promis par cette décision. |
| Installation et désinstallation | Installateur classique donne contrôle sur upgrade et nettoyage; MSIX complet est écarté comme format principal. | Installation classique per-user dans le profil, sans admin, self-contained x64. Désinstallation standard conserve réglages/historique; option complète les supprime après confirmation. | Toolset/signature/package sparse à valider; raccourcis et inscriptions OPTIVANCE retirés proprement; aucune donnée conservée supprimée par défaut. |
| Mises à jour | Updater automatique : correctifs rapides, mais introduit réseau, vérification/signature et service/planification. Mise à jour manuelle : pas de contrôle réseau en app, moins de complexité, mais l’utilisateur peut retarder les correctifs. | Respecter la contrainte v1 : publication manuelle d’un installateur signé, sans updater ni vérification de version réseau. | Versioning et changelog inclus; tester upgrade N vers N+1 hors réseau, mise à jour DLL/package et nettoyage de l’ancienne version; plan de support/notification est à part. |

Les décisions produit approuvées ne constituent pas une preuve technique ni une autorisation de développement. La tâche actuelle reste documentaire; les validations et prototypes définis dans [ARCHITECTURE_VALIDATION_PLAN.md](ARCHITECTURE_VALIDATION_PLAN.md) attendent une approbation ultérieure.
