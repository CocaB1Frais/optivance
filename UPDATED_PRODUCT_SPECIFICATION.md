# OPTIVANCE — Spécification produit révisée

**Révision :** 11 novembre 2026 — arbitrages Master Product Vision (D-17 navigation sans Dashboard, D-18 capteurs), errata E-04  
**Statut :** les arbitrages marqués approuvés sont des décisions produit confirmées. Les solutions marquées recommandées restent soumises à validation technique; les points ouverts et reportés sont distingués dans la section 12. Aucun développement applicatif n’a commencé.

## 1. Vision du produit

OPTIVANCE est une application Windows de gestion du stockage, de nettoyage contrôlé, de monitoring et d’optimisation réversible. Elle doit aider l’utilisateur à comprendre l’occupation de ses disques, à trouver de gros fichiers et des doublons, à nettoyer des éléments compris, à observer les ressources de sa machine et à appliquer des améliorations Windows explicables.

La priorité est la confiance : chiffres qualifiés, opérations annulables, aucune suppression implicite, aucun réglage silencieux et séparation nette entre observation, recommandation et action.

Le nom OPTIVANCE demeure un nom de travail. La recherche de marque, le slogan et le travail d’identité visuelle ne font pas partie de cette revue technique, conformément à la demande initiale.

## 2. Plateforme et principes non négociables

- Politique v1 approuvée : Windows 11 x64 maintenu est la cible principale et référence de développement; Windows 10 Enterprise LTSC 2019 x64 (1809) est la référence secondaire; Windows 10 Enterprise LTSC 2021 x64 (21H2) est conditionnel à la réception effective d’un support de sécurité applicable. IoT LTSC est exclu du support général v1 jusqu’à validation technique et décision distinctes; Home/Pro, Enterprise non-LTSC et LTSB 2016 sont hors périmètre. WPF + Rust et .NET 10 LTS self-contained x64 sont approuvés; les couples OS/runtime et le paquet restent à valider avant toute garantie ([OS supportés par .NET](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [support .NET](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)).

| Plateforme x64 | Compatibilité documentée .NET 10 | Cycle de support de l’OS au 6 novembre 2026 | Statut OPTIVANCE proposé |
|---|---|---|---|
| Windows 11 x64 maintenu à la release | Vérifier la version dans la matrice .NET 10 | Dépend de l’édition et du servicing Windows 11 | Cible principale et référence de développement; valider les versions maintenues. |
| Windows 11 23H2 Enterprise/Education | Oui selon la matrice .NET 10 | Fin des mises à jour en novembre 2026 (vérifié le 11 nov. 2026) | Hors périmètre : plus de servicing. |
| Windows 10 Enterprise LTSC 2019 x64 (1809, build 17763) | Oui, 1809 LTSC/Enterprise | Mises à jour de sécurité jusqu’en janvier 2029 | Cible secondaire de référence; compatibilité technique à valider avant garantie. |
| Windows 10 Enterprise LTSC 2021 x64 (21H2, build 19044) | Oui, 21H2 LTSC/Enterprise | Support standard jusqu’en janvier 2027; toute couverture ultérieure doit être effectivement active | Compatibilité conditionnelle uniquement tant que l’installation reçoit le support de sécurité applicable; vérifier à chaque release. |
| Windows 10 IoT Enterprise LTSC | La matrice .NET générique ne nomme pas séparément cette SKU | Cycle de vie distinct | Exclu du support général v1; toute inclusion future exige validation technique et décision séparées. |
| Windows 10 Enterprise non-LTSC, LTSB 2016, Home / Pro | Versions éventuellement listées par .NET ne créent pas de support produit | Cycles de support différents ou terminés | Hors périmètre v1, sans exception ESU pour réintégrer ces éditions. |

La matrice .NET indique aussi que .NET 9 STS et .NET 8 LTS arrivent en fin de support en novembre 2026; .NET 10 LTS est maintenu jusqu’en novembre 2028. Les fiches Windows peuvent afficher les dates de fin de support avec un décalage d’un jour selon le fuseau horaire. Références de cycle de vie : [Windows 10 release health](https://learn.microsoft.com/en-us/windows/release-health/release-information), [Enterprise LTSC 2019](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2019), [Enterprise LTSC 2021](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2021).
- Application de bureau locale, sans compte obligatoire, cloud, abonnement, télémétrie ni transmission automatique des données.
- Pas de scan automatique au lancement, de service résident inutile, de vérification réseau des mises à jour ou d’updater automatique dans la v1.
- Ressources presque nulles au repos : pas de boucle active, scan de fond, timer fréquent, animation continue, écriture DB inutile ou polling matériel non demandé.
- Aucun lancement intégral en administrateur. N’élever qu’une opération précise si elle le nécessite, après action explicite de l’utilisateur.
- MFT possible pour les analyses avancées; l’UAC ponctuelle n’apparaît qu’après action explicite si le scan MFT le nécessite. Aucune UAC au démarrage ni service Windows permanent; l’interface et le reste de l’application restent non élevés.
- C# WPF communique avec un seul moteur Rust partagé par l’application et le widget via Windows Named Pipes et un protocole local versionné. Validation des commandes, tailles, versions et messages; aucun serveur réseau. Un plantage du moteur Rust laisse l’interface ouverte avec une erreur compréhensible.
- Toutes les opérations longues sont annulables et présentent progrès, méthode, erreurs et état partiel.
- Aucun changement silencieux de permissions, propriétaire, configuration, registre, processus ou service.
- Ne jamais modifier le MFT. Pas de driver noyau sans nécessité démontrée et accord distinct.
- Ne pas inventer mesure, capteur, estimation de temps restant, économie d’espace ou amélioration de performance.

## 3. Navigation et expérience

Navigation principale (D-17) : Storage, Cleaning, Monitoring, Optimization, History et Settings. Il n’y a pas de Dashboard dédié. Le widget flottant est hors navigation. La première interface est fonctionnelle et claire; les animations restent discrètes et le polissage graphique final attend la fiabilité des fonctions. Langues V1 : français et anglais.

Accessibilité requise : navigation clavier, libellés accessibles, respect des réglages de contraste Windows et aucune information transmise uniquement par la couleur.

### Page d’accueil et activité globale

La page d’accueil est Storage : cartes de volumes, analyse d’un dossier, derniers résultats; aucun scan automatique. Une barre d’état visible depuis toutes les pages indique l’opération en cours (progression, annulation), le profil actif et l’état de la collecte. Le contenu de l’ancien Dashboard est réparti : stockage → Storage; métriques → Monitoring et widget; scans récents → Storage et History; bilan de nettoyage → Cleaning et History; recommandations → Optimization.

### Storage — analyse avancée du stockage

- Détecter les volumes et afficher lettre, nom, système de fichiers, capacité totale, utilisé/libre, pourcentage, type et état d’accès.
- Proposer l’analyse d’un disque, de plusieurs disques ou d’un répertoire. Inclure par défaut les fichiers cachés et système, en signalant les éléments inaccessibles.
- NTFS : évaluer les voies `FSCTL_ENUM_USN_DATA` et parsing brut du MFT séparément; lecture seule, noms Unicode, références parent/fichier/séquence, fragmentation, hardlinks, reparse points, sparse/compression, enregistrements corrompus, volumes volumineux et mutations concurrentes.
- Hardlinks : conserver tous les chemins dans l’arborescence et la recherche et afficher la taille logique de chaque chemin. Un seul chemin représentatif, sélectionné par un ordre déterministe indépendant de l’ordre du scan, porte le poids comptabilisé dans les agrégats de répertoires et la treemap; les autres sont signalés comme alias et ne contribuent pas une seconde fois. Identité : volume + file reference number + sequence number. Convention recommandée : racine de volume canonique et séparateurs normalisés, puis chemin absolu minimal en comparaison ordinale insensible à la casse, avec comparaison ordinale sensible à la casse pour départager. Cette règle doit rester cohérente dans les exports et être validée sur NTFS.
- Alternate Data Streams NTFS : exclus des totaux ordinaires; ils peuvent être présentés séparément comme métadonnées si leur lecture est fiable. Le total de contenus logiques sans double comptage n’est pas une mesure exacte de l’espace physique consommé.
- Ne pas confondre les enregistrements supprimés avec l’inventaire courant; une éventuelle vue forensique future reste séparée.
- Autres systèmes de fichiers : parcours Win32 cancellable avec gestion des chemins longs, Unicode, disparitions, accès refusés et erreurs récupérables. Ne pas suivre par défaut les liens symboliques, jonctions ou points de montage.
- Chaque résultat précise sa méthode, son profil, son niveau d’exhaustivité, ses erreurs et les exclusions éventuelles.
- État obligatoire : **Terminé** signifie résultats complets, intégrés à l’historique et aux statistiques définitives; **Interrompu** signifie résultats partiels consultables pendant la session, clairement signalés et exclus des statistiques définitives; **Échec** signifie erreur signalée et résultats non fiables, exclus des statistiques définitives et jamais présentés comme inventaire complet. L’historique résumé conserve l’état et les erreurs des trois états. Les chemins partiels ne sont persistés que si l’inventaire détaillé est activé, dans la limite de sa rétention.
- Construire un index compact possédé par le moteur, avec résultats progressifs et vues paginées; ne pas instancier un objet UI par fichier.
- Arbre configurable : nom, taille logique, taille allouée rapportée, part du parent/volume, fichiers/dossiers, extension, date, attributs et accès; navigation, tri, recherche, filtre, dossiers/fichiers les plus grands.
- Distinguer systématiquement taille logique (EOF), taille allouée, cumul de dossier et estimation physique. Une estimation n’est jamais présentée comme une mesure exacte du support.

### Treemap interactive

Rectangles proportionnels à une taille choisie; couleurs par extension; regroupement par dossier; survol avec informations; sélection synchronisée à l’arbre; clic/double-clic, zoom/drill-down, fil d’Ariane, recherche surlignée, infobulles et menu contextuel. Utiliser dessin personnalisé, lots, index spatial et agrégation pour les grands volumes, sans widget par fichier ni blocage de l’interface.

La taille logique est la valeur principale affichée par défaut, conformément à D-07. La taille allouée reste accessible comme valeur secondaire. Les deux valeurs, les cumuls de dossier et les estimations physiques ne doivent jamais être confondus.

### Recherche et filtres

Nom, extension, chemin complet, type, tailles min/max, dates, cachés/système/temporaire/protégés/inaccessibles, grands fichiers. Recherche indexée, réactive et annulable; ne pas reparcourir le disque à chaque frappe.

### Doublons

Analyse facultative et annulable : regroupement préliminaire par taille, hash partiel si utile, puis hash intégral pour confirmer le contenu. Jamais conclure sur le nom ou la taille seuls. Éviter les lectures complètes lors d’un scan normal. Les doublons et rapports sont séparables des totals d’espace qui doivent respecter la politique de hardlinks.

### Actions de fichier

Ouvrir; ouvrir le répertoire contenant; sélectionner dans Explorer; copier chemin/nom; propriétés; analyser le répertoire contenant; supprimer vers la Corbeille; supprimer définitivement uniquement par action distincte et confirmation. Aucune promesse d’effacement sécurisé sur SSD. Les fichiers protégés donnent une explication et une élévation seulement si justifiée; aucune prise de propriété ou modification ACL automatique.

## 4. Menu contextuel facultatif Windows 11

L’intégration est désactivée par défaut et doit pouvoir être activée/désactivée depuis Settings. Elle doit fonctionner si la fenêtre OPTIVANCE est fermée; une commande lance l’application ou active une instance existante et lui transmet une action typée ainsi que la cible validée. Windows 11 utilise `IExplorerCommand`; Windows 10 Enterprise LTSC 2019 utilise une intégration adaptée au menu classique. L’intégration est facultative et n’effectue aucune inscription système/registre sans consentement explicite. En cas d’échec ou d’indisponibilité, l’application reste entièrement utilisable depuis son interface principale.

| Commande proposée | Cible | Comportement demandé |
|---|---|---|
| Ouvrir OPTIVANCE | Dossier ou disque | Ouvre l’application; le traitement n’est pas automatique. |
| Analyser avec OPTIVANCE | Dossier/disque reconnu | Ouvre Storage et présente la cible avec confirmation avant le lancement du scan. |
| Analyser ce dossier | Dossier | Lance à la suite du clic explicite un scan du dossier sélectionné; progrès/annulation affichés dans l’application. |
| Analyser ce disque | Disque/volume | Lance le scan du volume sélectionné; une demande UAC peut apparaître uniquement si la voie MFT retenue la nécessite. |
| Nettoyer avec OPTIVANCE | Cible sélectionnée et prise en charge par Cleaning | Ouvre Cleaning et démarre une analyse préalable de la cible. Aucune suppression, mise en Corbeille ou modification du système depuis cette commande. |

Les commandes ne s’appliquent qu’aux dossiers et volumes reconnus; les commandes non applicables sont masquées (notamment pour les fichiers et sélections multiples non pris en charge). Les commandes sont groupées sous OPTIVANCE lorsque le mécanisme le permet. « Analyser avec OPTIVANCE » ouvre toujours Storage avec confirmation avant le scan. L’action « Nettoyer » est toujours non destructive à ce stade.

### Voie produit validée et détails techniques à valider

La voie produit validée pour Windows 11 est l’extension moderne `IExplorerCommand` dans le menu principal. Le format principal de distribution validé est un installateur classique; un sparse package signé est la piste auxiliaire pour accorder l’identité de package nécessaire, pas le format principal d’installation. Une DLL COM native séparée répond rapidement aux demandes d’Explorer et transmet une commande typée avec une cible validée. Aucun scan, nettoyage ou accès prolongé au disque n’est réalisé dans Explorer. L’application peut être fermée au moment du clic : l’action démarre OPTIVANCE ou est transmise à une instance existante.

Windows 10 ne propose pas le menu principal moderne Windows 11. Pour Windows 10 Enterprise LTSC 2019, la voie prévue est un verbe Win32 per-user dans le menu contextuel classique, après consentement explicite. Le support du menu contextuel n’est pas étendu par cette décision aux autres éditions Windows 10; LTSC 2021 reste soumis à une validation distincte de ce chemin. Le fonctionnement du sparse package, la signature, la désactivation, la désinstallation et le fallback restent à valider techniquement. Si l’intégration échoue ou est indisponible, OPTIVANCE explique la situation et reste entièrement utilisable dans son interface principale; aucune inscription cachée ni redémarrage silencieux d’Explorer.

Toute inscription est précédée d’un consentement explicite. Un refus laisse l’intégration inchangée. La désactivation retire les inscriptions OPTIVANCE; la désinstallation les retire proprement sans effacer l’historique ou les préférences sans choix distinct. La compatibilité des extensions Shell empaquetées diffère selon Windows 10/11 ([extension File Explorer](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer), [comparaison MSIX Windows 10/11](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/msix-windows10-windows11), [verbes Shell](https://learn.microsoft.com/en-us/windows/win32/shell/fa-verbs)).

Microsoft documente le mécanisme `IExplorerCommand`, l’identité de package et l’option sparse pour une application desktop non empaquetée ([extension moderne](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer), [identité sparse](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)); les verbes Shell classiques sont décrits [ici](https://learn.microsoft.com/en-us/windows/win32/shell/fa-verbs).

## 5. Cleaning — nettoyage sûr

Analyser sans supprimer : fichiers temporaires Windows/utilisateur, caches applicatifs ou navigateur identifiables, Corbeille, vieux logs, temporaires d’installation, candidats de nettoyage Windows Update si pris en charge, grands/anciens fichiers, doublons et applications volumineuses. Une catégorie n’apparaît que si ses règles sont comprises, testées et documentées; ne pas qualifier arbitrairement un fichier de déchet.

Pour chaque catégorie : description, nombre, taille, chemins, risque et raison. L’utilisateur inspecte et désélectionne des fichiers. Avant action : récapitulatif et confirmation explicite; Corbeille par défaut; suppression définitive séparée. Aucune suppression de Downloads, fichier personnel, cache actif ou fichier système par règle générique. Élévation seulement au besoin. Historique : date, catégorie, éléments traités, espace récupéré, erreurs et résultat.

### Désinstallation avancée des logiciels tiers — versions ultérieures, hors V1

Une version ultérieure pourra étudier une alternative intégrée à Revo Uninstaller : lancer le désinstalleur officiel, rechercher fichiers/dossiers et clés de registre potentiellement associés, effectuer une analyse approfondie, afficher un aperçu détaillé, protéger les composants partagés/faux positifs et étudier une restauration. Toute suppression de résidus exigerait une confirmation explicite. Aucune suppression automatique de résidus ou nettoyage automatique du registre. Cette fonction est exclue du périmètre V1.

## 6. Monitoring et widget flottant

- CPU global/par cœur, modèle, cœurs/threads, fréquence, température et consommation électrique (W) si disponibles.
- RAM totale/utilisée/disponible, pourcentage et pression mémoire si accessible.
- GPU/modèle/utilisation, VRAM dédiée/partagée, température, fréquence et consommation électrique (W) si disponibles; prise en charge à évaluer pour NVIDIA/AMD/Intel.
- Sources (D-18) : API Windows documentées et bibliothèques constructeur présentes avec le pilote, après revue de licence; aucun pilote installé, aucune élévation pour un capteur. Les températures et la consommation CPU seront souvent indisponibles avec ces règles; elles sont alors affichées « indisponible ».
- Stockage : activité, lecture/écriture, file d’attente, type (l’espace occupé relève de Storage); réseau : débit entrant/sortant, historique du trafic, interface optionnelle.
- Processus : nom, PID, CPU, RAM, disque/GPU quand disponible, chemin si accessible; tri par ressources, classement des plus gourmands. Aucun arrêt automatique. Cette liste vit uniquement dans Monitoring (D-17).
- Vue d’ensemble par tuiles (valeur actuelle, maximum de session, mini-courbe) et vues détaillées par composant. Graphiques temps réel dessinés en WPF par défaut, historique borné en mémoire de session seulement (aucune persistance en V1), fréquence configurable, pause/reprise et métriques activables séparément.
- Widget facultatif, désactivé par défaut, dans le même processus que l’application : compact, déplaçable, redimensionnable, opacité réglable, toujours au-dessus en option et désactivé par défaut, métriques configurables (CPU, GPU, RAM, températures, fréquences, réseau, débits disque et autres métriques disponibles), ouvrir Monitoring, fermer séparément. Il réutilise exactement la source de mesures principale; pas de double polling. Aucun échantillonnage lorsque ni page ni widget ne l’utilisent, sauf option explicite de surveillance en arrière-plan.
- Cadence 2–3 secondes comme point de départ à mesurer, pas comme garantie; pas de valeur de température inventée.

Par défaut, la fermeture de la fenêtre principale ferme complètement OPTIVANCE : application principale, widget, moteur Rust, jobs, collecteurs et monitoring s’arrêtent; aucun processus OPTIVANCE invisible ne subsiste. `Settings > General > Application Lifecycle` permet de choisir : (1) fermeture complète (défaut); (2) réduction dans la zone de notification avec fonctionnement en arrière-plan; (3) fermeture de la fenêtre principale en conservant uniquement le widget. Le menu de notification comporte `Open OPTIVANCE`, `Show/Hide Widget`, `Settings` et `Quit OPTIVANCE`; `Quit` ferme toujours complètement l’application. Widget et lancement avec Windows sont désactivés par défaut; Always-on-top est désactivé par défaut. L’icône tray, la confirmation avant fermeture et l’arrêt de la surveillance lorsque le widget est masqué restent réglables. Aucun service Windows ni second collecteur.

## 7. Optimization — optimisation Windows contrôlée

- Démarrage : nom, éditeur, chemin, emplacement, état et métadonnées; les entrées prises en charge peuvent être activées/désactivées manuellement, après confirmation explicite et sauvegarde préalable de l’état initial; proposer une restauration lorsque techniquement possible.
- Processus : consultés dans Monitoring (D-17); les recommandations d’Optimization y renvoient. Services : nom, nom affiché, description, type de démarrage, état et exécutable/éditeur si disponible. Processus et services restent strictement informatifs et en lecture seule.
- Aucune modification automatique, aucun arrêt silencieux de processus ou de service, aucune modification d’ACL, de propriétaire ou de permissions système. Ne jamais conseiller de désactiver antivirus, firewall ou services Windows essentiels.
- Recommandations fondées sur une observation : application de démarrage coûteuse, disque presque plein, processus exceptionnellement consommateur, volume de temporaires détecté. Chaque proposition donne son motif; pas de promesse arbitraire de FPS.
- Aucune modification silencieuse ou irréversible; pas de changement de registre comme raccourci de nettoyage.

## 8. Profils de performance

Trois profils exigés : ECONOMY (interférence minimale), BALANCED (profil initial proposé), MAXIMUM PERFORMANCE (terminer plus vite dans les limites du matériel). Ils pilotent une politique bornée de concurrence, priorité, blocs, cache, prefetch et rafraîchissement UI. Valeurs proposées dans le cahier initial (p. ex. 1–2 workers Economy, 2–4 Balanced) sont des hypothèses à benchmarker, pas des constantes garanties. Ne jamais supposer qu’augmenter les threads accélère tous les supports.

Scheduler adaptatif conditionnel et non garanti en V1 : l’intégrer uniquement si les benchmarks démontrent un bénéfice réel, une fiabilité suffisante et l’absence de régression significative. Sinon, utiliser les réglages fixes, prévisibles et bornés des trois profils. Afficher profil, travailleurs, rythme, temps écoulé et estimation restante seulement lorsque significative.

Détecter le modèle CPU, cœurs/threads, RAM, type de disque, système de fichiers et charge courante pour informer le scheduler; conserver des limites sûres et ne pas présumer qu’un matériel se comporte comme un autre.

## 9. History, exports et base locale

Historique des scans, nettoyages, recommandations/actions et benchmarks; dates, résumés, méthode, erreurs et résultats. Un résumé de chaque analyse terminée est enregistré automatiquement; l’inventaire complet n’est conservé que sur demande explicite. Exports CSV, JSON et TXT documentés pour résumé disque, listes fichiers, dossiers, grands fichiers, doublons, nettoyages et performances. Tout reste local.

SQLite est la solution technique de référence recommandée, sous réserve de validation : schéma versionné, migrations, transactions, rétention, nettoyage, réparation/récupération et ACL utilisateur. Ne pas historiser des millions de chemins à chaque scan par défaut. Rétention approuvée : résumés pendant 365 jours par défaut et inventaires détaillés demandés explicitement pendant 30 jours par défaut. Les deux durées sont modifiables; l’utilisateur peut désactiver la conservation des inventaires détaillés.

Les résumés d’historique conservent l’état et les erreurs des scans terminés, interrompus et échoués. Les chemins partiels ne sont persistés que si l’inventaire détaillé est activé, soumis à sa rétention; un résultat interrompu reste consultable pendant la session. Seuls les scans terminés alimentent les statistiques définitives. Aucun scan incomplet n’est présenté comme inventaire complet.

Statistiques (History, scans terminés seulement) : évolution de l’espace utilisé par volume, espace récupéré par les nettoyages, nombre de scans par état, extensions principales sur la période.

Settings : comportement général, cycle de vie de l’application, démarrage, scan, profil, rafraîchissement monitoring, widget, durée d’historique, base, langue (français, anglais), thème (clair, sombre, système), personnalisation (colonnes, densité d’affichage), exports et activation du menu contextuel. La base peut contenir résumés de scans, métadonnées de volumes, préférences, profil, historique de nettoyage et paramètres de monitoring; chemins et listes détaillées ne sont stockés que selon la politique explicitement choisie.

## 10. Distribution et mises à jour

Le format produit validé est un installateur classique x64, per-user, installé dans le profil utilisateur sans droits administrateur obligatoires. Le paquet de l’application WPF est .NET 10 LTS self-contained x64 : le runtime .NET est embarqué et l’utilisateur n’installe pas séparément .NET Desktop Runtime 10. Les opérations nécessitant une élévation conservent leur demande UAC ponctuelle. Un sparse package signé est le composant auxiliaire envisagé pour l’identité nécessaire à l’extension moderne Windows 11. Le choix du toolset, la signature et le format technique du package restent à valider.

La désinstallation classique supprime l’application et ses intégrations/raccourcis OPTIVANCE, tout en conservant paramètres et historique. La désinstallation complète supprime aussi paramètres, historiques, caches et données propres à OPTIVANCE, seulement après confirmation explicite. Ces deux choix doivent retirer proprement les raccourcis et intégrations créés par l’application. MSIX complet et version portable ne sont pas le format principal retenu.

Pour V1 : aucune vérification automatique de mise à jour ni notification de nouvelle version dans l’application. Distribution manuelle par installateur signé et notes de version. Ne pas exiger les environnements Python/Rust sur le PC de l’utilisateur.

## 11. Exigences de qualité et validation future

La v1 ne revendique une fonction comme complète qu’après implémentation et tests. Le plan futur comprend fixtures de scan (dossiers vides/gros, hardlinks, liens, sparse/compression, Unicode/chemins longs, fichiers cachés/système, refus d’accès, fichiers verrouillés/disparus, records supprimés/corrompus/fragmentés, multiples volumes), tests d’actions sur répertoires temporaires, réactivité/annulation UI, sync tree/treemap, widget, sécurité IPC/helper/package, base corrompue, panne de capteur/API et arrêt inattendu, benchmarks HDD/SATA/NVMe et mesure idle/active. Aucun test d’application n’a été effectué pendant cette revue documentaire.

Tout rapport de benchmark précise machine, build Windows, filesystem, volume, nombre de fichiers/dossiers, cache, méthode, privilège, durée, RAM, CPU, I/O et erreurs. Aucun résultat n’est présumé.

Les comparaisons avec WizTree, TreeSize et WinDirStat ne sont publiées qu’avec conditions comparables et résultats exacts. La documentation de livraison comprend README, architecture, développement/build, performances, sécurité, tests, licences, changelog et roadmap. Le paquet final comprend icône, dépendances nécessaires et instructions; les postes utilisateurs n’ont pas à installer Python ou Rust.

## 12. Décisions et points encore ouverts

Les décisions produit approuvées comprennent la politique Windows, WPF + Rust, .NET 10 LTS self-contained x64, les limites MFT/UAC, Named Pipes, les règles hardlinks/ADS, les états et rétentions de scan, les modes de fermeture, l’optimisation limitée aux entrées de démarrage manuelles et réversibles, les trois profils avec scheduler adaptatif conditionnel, l’accessibilité, le menu contextuel, les choix de distribution/mise à jour, les deux modes de désinstallation, la navigation sans Dashboard (D-17) et les règles de capteurs et d’historique de performances (D-18). Les recommandations techniques en attente sont SQLite, le chemin représentatif stable et les mécanismes d’installation/Shell. Restent à valider techniquement : compatibilité sur les OS inclus, sources de capteurs (V-17A), backend MFT, IPC/helper, toolset/signature/format sparse, détails Shell, restauration des entrées de démarrage, couverture capteurs, budgets ressources, schémas et performances. La désinstallation avancée de logiciels tiers est reportée hors V1. La Phase 1 n’est pas commencée.
