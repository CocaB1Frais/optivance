# OPTIVANCE — Analyse historique d'écarts entre la Master Product Vision et le dépôt

**Date :** 11 novembre 2026
**Auteur :** Claude Code (analyse indépendante)
**Statut :** les recommandations Q1–Q10 ont été arbitrées le 11 novembre 2026 et leurs effets ont été intégrés aux documents de référence. Le contenu des sections 1 à 8 conserve l'analyse et les questions telles qu'elles se présentaient avant arbitrage; ses mentions « à acter », « à trancher » ou « ouvert » ne décrivent donc pas automatiquement l'état actuel. Les références actuelles sont `ARCHITECTURE_DECISIONS.md` (D-17, D-18 et D-16), `UPDATED_PRODUCT_SPECIFICATION.md` et `IMPLEMENTATION_ROADMAP.md`.

Légende historique de l'analyse initiale : **[A]** approuvé dans le dépôt à cette date · **[N]** nouveau dans la vision, à arbitrer · **[P]** proposition de Claude, alors non décidée · **[V]** validation technique requise. Ces marqueurs décrivent le relevé historique, pas le statut actuel.

## 0. État consolidé après arbitrage

- **Décisions actées :** le Dashboard est supprimé; la navigation a six destinations (Storage, Cleaning, Monitoring, Optimization, History, Settings); Storage est la page d'accueil; la liste des processus live est dans Monitoring seul; le widget reste dans le même processus; les choix de langues/statistiques sont consignés en D-17. Les règles de sources de capteurs, d'absence de pilote/élévation et d'historique de performances en mémoire seulement sont consignées en D-18.
- **Ordre de livraison acté :** Monitoring et le widget précèdent Cleaning dans `IMPLEMENTATION_ROADMAP.md` (Phase 5 avant Phase 6). Cet ordre de livraison ne change pas la liste ni l'ordre de navigation.
- **Recommandation conservée :** dessin des graphes en WPF par défaut (D-18). Une bibliothèque tierce n'est pas sélectionnée; toute proposition future exige une revue de licence et de dépendances.
- **Validations techniques encore dues :** V-17A pour les sources de capteurs; V-17 et V-10 pour couverture/accessibilité et coût de collecte/rendu; validations de cycle de vie, budget et stockage selon `ARCHITECTURE_VALIDATION_PLAN.md`. Elles ne sont pas des décisions déjà validées ni une autorisation de Phase 1.
- **Décisions ou choix encore ouverts :** D-16 (modèle de distribution/licence) reste ouvert. Le toolset d'installation et le certificat réel sont des prérequis/choix à régler avant V-02A et V-02B; les catégories de nettoyage publiables restent à établir. Le backend MFT, les budgets et seuils sont des points techniques à étudier, pas des arbitrages déjà approuvés.

Les décisions actuelles ne sont pas rouvertes par les questions ni les formulations proposées conservées plus bas.

---

## 1. Comparaison vision ↔ dépôt

### 1.1 Concordances (aucune action)

Scans classique/MFT, états Terminé/Interrompu/Échec, hardlinks/ADS/taille logique, doublons par hash intégral, nettoyage avec aperçu et Corbeille, absence d'effacement sécurisé, processus/services en lecture seule, entrées de démarrage réversibles, trois profils limités à OPTIVANCE, scheduler conditionnel, rétention 365/30 jours, exports CSV/JSON/TXT, trois modes de fermeture et menu tray, menu contextuel W11/LTSC 2019 sans garantie LTSC 2021, installation per-user, mises à jour manuelles, deux désinstallations, stack WPF/.NET 10/Rust/Named Pipes, SQLite recommandée, helper MFT, confidentialité, compatibilité, D-16 ouvert. Tous **[A]**.

### 1.2 Écarts relevés avant arbitrage — instantané historique

Le tableau ci-dessous décrit l'analyse antérieure aux arbitrages Q1–Q10. Les statuts [N]/[P] et les formulations « à trancher » ont depuis été résolus ou reclassés comme validations, comme résumé à la section 0.

| # | Sujet | Vision | Dépôt | Nature |
|---|---|---|---|---|
| G-01 | **Dashboard** | Supprimé; navigation à 6 entrées | 7 destinations dont Dashboard (spec l.41-47) | **[N]** décision produit à acter |
| G-02 | **Consommation électrique CPU/GPU (W)** | Demandée si capteurs fiables | Absente | **[N]** + **[V]** forte (cf. §4) |
| G-03 | Fréquences GPU | Demandées si disponibles | Spec : « température/fréquence si disponibles » | Concordant |
| G-04 | Valeurs actuelles **et maximales** dans les graphes | Demandées | Absentes | **[N]** détail UX |
| G-05 | Historique du trafic réseau | Demandé | Historique borné générique | Concordant (préciser) |
| G-06 | Widget redimensionnable, opacité réglable, températures/fréquences/débits disque | Demandés | Spec : transparence, taille/mouvement, métriques configurables | Concordant (préciser la liste) |
| G-07 | Processus dans Monitoring **et** dans Optimization | Les deux | Les deux (spec §6 et §7) | Redondance à trancher (§3) |
| G-08 | Statistiques | « Statistiques » sans contenu défini | Aucun contenu défini | Lacune (proposition §5.6) |
| G-09 | « Personnalisation de l'interface » | Paramètre listé | Absent | **[N]** périmètre à définir |
| G-10 | Animations discrètes | Demandées | Polissage après fiabilité | Concordant |
| G-11 | Tests automatisés | Exigés | Plans de tests, pas de stratégie d'automatisation | **[N]** à intégrer à la roadmap |
| G-12 | « Historique de son système » (§1) | Ambigu : historique des analyses ou historique de performances persistant ? | Seuls résumés scans/nettoyages/actions persistés | Ambiguïté à trancher (§6, Q4) |
| G-13 | Actions de fichier (ouvrir, Explorer, copier chemin, propriétés, Corbeille depuis Storage) | Non citées | Spec §3 « Actions de fichier » | Omission de la vision; je conserve la spec **[A]** |
| G-14 | Filtres caché/système/temporaire/protégé/inaccessible | Non cités | Spec §3 | Idem, conservés **[A]** |
| G-15 | Slogan | « envisagé » | Spec : nom de travail, identité hors revue | Concordant (pas une décision) |
| G-16 | Collecteur : impact énergie d'OPTIVANCE | Objectif explicite | Budgets ressources à mesurer | Concordant |
| G-17 | Widget « indépendant de la fenêtre principale » | Formulation UX | D-09 : même processus WPF, autre fenêtre | Précision à confirmer : indépendance d'affichage, **pas** de processus séparé |

## 2. Suppression du Dashboard — relevé historique avant correction

Le relevé suivant liste les occurrences trouvées dans l'état antérieur des documents et les corrections alors proposées. La suppression du Dashboard est désormais actée par D-17 et appliquée aux documents de référence.

| Fichier | Ligne | Contenu dans le relevé initial | Correction proposée à l'époque |
|---|---|---|---|
| `UPDATED_PRODUCT_SPECIFICATION.md` | 41 | Navigation à 7 entrées dont Dashboard | 6 entrées : Storage, Cleaning, Monitoring, Optimization, History, Settings; widget hors navigation |
| `UPDATED_PRODUCT_SPECIFICATION.md` | 45-47 | Section « Dashboard » | Supprimer; redistribuer son contenu (tableau ci-dessous) |
| `UI_ANALYSIS.md` | 15 | 7 destinations | 6 destinations |
| `PROPOSED_ARCHITECTURE.md` | 44 | « sept destinations : Dashboard… » | « six destinations » |
| `PROPOSED_ARCHITECTURE.md` | 78, 101 | Collecteur partagé avec Dashboard | Consommateurs : page Monitoring, widget (+ Optimization si Q2 retient une vue processus live) |
| `ARCHITECTURE_DECISIONS.md` | 131 (D-07) | « Arbre, treemap par défaut, Dashboard, historique… » | Retirer « Dashboard » (D-07 inchangée sur le fond) |
| `ARCHITECTURE_VALIDATION_PLAN.md` | 78 | Sampler partagé Dashboard/Monitoring/widget | Monitoring/widget |
| `ARCHITECTURE_VALIDATION_PLAN.md` | 85 | Taille logique partout « tree/treemap/dashboard/… » | Retirer dashboard |
| `ARCHITECTURE_VALIDATION_PLAN.md` | 265 (V-17) | Parcours « Dashboard, Storage… » | Retirer Dashboard; ajouter Monitoring, Cleaning, Optimization, History |
| `IMPLEMENTATION_ROADMAP.md` | 151 | Ligne de couverture « Dashboard » | Supprimer; ajouter « Navigation et page d'accueil » |

**Redistribution des fonctions de l'ancien Dashboard [P] :**

| Ancien contenu Dashboard | Nouvelle place |
|---|---|
| Vue d'ensemble du stockage | Storage, écran d'entrée (cartes de volumes) |
| CPU/RAM/GPU/réseau | Monitoring (vue d'ensemble) et widget |
| Scans récents | Storage (reprendre un résultat récent) et History |
| Bilan de nettoyage | Cleaning (dernier nettoyage) et History |
| Recommandations importantes | Optimization (onglet Recommandations); indicateur discret dans la navigation |

**Conséquence non triviale :** sans Dashboard, il faut une **page d'accueil** (Q1) et un **indicateur global d'activité** (scan, nettoyage, collecte en cours) visible depuis toute page, puisqu'aucune vue de synthèse ne le montre plus.

## 3. Redondances Monitoring / widget / autres pages

| Donnée | Pages concernées | Proposition **[P]** |
|---|---|---|
| CPU, GPU, RAM, réseau, débits disque | Monitoring, widget | Une seule source (collecteur), deux présentations; le widget n'affiche qu'un sous-ensemble choisi. Aucune redondance de collecte. |
| Liste des processus | Monitoring, Optimization | **Monitoring** possède la liste live (CPU/RAM/disque/GPU, tri). **Optimization** ne garde que Démarrage, Services et Recommandations; une recommandation « processus gourmand » renvoie vers Monitoring filtré. Évite deux listes et une double consommation. (Q2) |
| Volumes | Storage, Monitoring | Storage = **contenu/espace** (taille, libre, analyse). Monitoring = **activité** (débits, file d'attente, % actif). Libellés distincts : « Espace » vs « Activité ». |
| Historique | History, graphes Monitoring | History = événements persistés (scans, nettoyages, actions). Graphes Monitoring = mémoire bornée de session, non persistés en V1 **[P]** (Q4). |
| Statistiques | History, Storage | Statistiques consolidées dans History; Storage montre seulement le résultat courant. |

## 4. Conséquences architecturales

1. **Capteurs (point le plus risqué de la vision).** Les décisions existantes interdisent pilote noyau, installation de pilote et élévation hors MFT (D-06 limite le helper au MFT). Conséquences :
   - **Température et consommation CPU** : Windows n'expose pas d'API documentée fiable sans privilège; les outils tiers usuels s'appuient sur un pilote noyau. Avec les décisions actuelles, ces valeurs seront **probablement « indisponibles » sur la plupart des machines**. **[V]**
   - **GPU (utilisation, VRAM, températures, fréquences, puissance)** : sources candidates à évaluer — compteurs de performance Windows (moteurs GPU, mémoire dédiée/partagée), interface noyau-graphique utilisée par le Gestionnaire des tâches pour la température GPU, et bibliothèques constructeur livrées avec le pilote (NVIDIA NVML, AMD ADLX, Intel IGCL) chargées dynamiquement, sans redistribution. Disponibilité, précision, privilège requis et licence de chaque source : **non vérifiés**. **[V]**
   - Les bibliothèques constructeur sont des **dépendances tierces** au sens de la règle de licence : revue obligatoire et lien avec D-16.
   - Élargir le helper élevé aux capteurs contredirait D-06 et l'objectif « aucun UAC hors MFT » : **non retenu**.
2. **Emplacement du collecteur.** L'architecture le place dans le processus WPF (pas dans Rust). Le collecteur reste en C#; le moteur Rust n'est pas démarré pour le seul monitoring. Cette organisation est alignée avec D-09, D-17 et D-18; son coût reste à valider techniquement.
3. **Consommateurs du collecteur.** D-17 retient Monitoring et le widget dans le même processus. La liste live des processus n'est pas dupliquée dans Optimization; les recommandations y renvoient vers Monitoring.
4. **Graphiques temps réel.** D-18 retient le dessin WPF par défaut. V-10 doit mesurer le coût CPU/RAM; aucune bibliothèque tierce n'est sélectionnée.
5. **Navigation sans Dashboard.** Shell WPF à 6 destinations + indicateur d'activité global + routage des commandes du menu contextuel vers Storage/Cleaning. Aucun impact sur Rust/IPC.
6. **Persistance du monitoring.** D-18 approuve l'historique de performances en mémoire seulement en V1. La persistance n'est donc pas une option ouverte pour cette version; elle toucherait le stockage, les écritures et la confidentialité si elle était reconsidérée ultérieurement.
7. **Tests automatisés.** Exigent dès la fondation : harnais Rust headless, fixtures de scan générées, tests de protocole IPC, tests C# de view-models, et un mode « capteurs simulés » pour tester Monitoring sans matériel.

## 5. Conception globale de l'expérience utilisateur [P]

### 5.1 Cadre commun
- Fenêtre principale : **barre de navigation latérale** (6 entrées, icône + libellé, accessible clavier), zone de contenu, **barre d'état** basse : opération en cours (scan/nettoyage) avec progression et annulation, profil actif, état de la collecte (actif/en pause/arrêté), erreurs récentes.
- Page d'accueil : **Storage** (Q1). Aucun scan automatique : cartes de volumes avec bouton explicite « Analyser ».
- Indicateurs non chromatiques : chaque état coloré a aussi une icône et un libellé.
- Thème clair/sombre/système; contraste élevé Windows respecté.

### 5.2 Storage
1. **Entrée** : cartes de volumes (lettre, nom, FS, capacité, libre, accès), bouton « Analyser un dossier », derniers résultats.
2. **Analyse en cours** : progression, méthode (classique/MFT), profil, temps écoulé, erreurs d'accès, annuler; résultats progressifs.
3. **Résultat** : vue scindée **arborescence | treemap**, sélection synchronisée, fil d'Ariane, panneau de détails (tailles logique/allouée, alias hardlink, attributs, actions de fichier). Onglets : Plus gros fichiers · Plus gros dossiers · Extensions · Recherche/filtres · Doublons (analyse séparée, lancée explicitement).
4. Bandeau d'état du résultat : Terminé / Interrompu (partiel) / Échec, méthode, exhaustivité, nombre d'éléments inaccessibles.

### 5.3 Cleaning
Catégories (description, taille, risque) → détail par fichier avec cases à cocher et raison → récapitulatif (nombre, taille, destination Corbeille/définitif) → confirmation → résultat et lien History. Entrée depuis le menu contextuel « Nettoyer » = pré-analyse de la cible seulement.

### 5.4 Monitoring
- **Vue d'ensemble** : tuiles CPU, GPU, RAM, Stockage (activité), Réseau, chacune avec valeur actuelle, maximum de session et mini-courbe; « indisponible » explicite.
- **Vues détaillées** par tuile : grandes courbes (par cœur, par moteur GPU, par disque, par interface), valeurs actuelle/max, métriques sélectionnables.
- **Processus** : liste triable (CPU, RAM, disque, GPU si disponible), lecture seule.
- Contrôles globaux : pause, fréquence, durée de l'historique affiché, choix des métriques.

### 5.5 Optimization
Onglets **Démarrage** (entrées prises en charge, impact si mesurable, activer/désactiver avec confirmation, sauvegarde, restauration) · **Services** (lecture seule, explications) · **Recommandations** (fondées sur observation, chacune avec motif et lien vers la page concernée).

### 5.6 History
Chronologie filtrable des scans, nettoyages, actions de démarrage et exports, avec badge d'état. Onglet **Statistiques** (scans terminés seulement) — contenu proposé : évolution de l'espace utilisé par volume, espace récupéré par nettoyage, nombre de scans par état, top extensions sur la période. Onglet **Exports**. (Q7)

### 5.7 Settings
Général (fermeture, lancement avec Windows, icône tray) · Stockage (scan, profils, MFT) · Monitoring (fréquence, arrière-plan) · Widget · Historique (rétentions) · Intégration Windows (menu contextuel avec écran de consentement) · Apparence (thème, langue, personnalisation — Q8) · À propos/Licences hors ligne.

### 5.8 Widget
Fenêtre compacte, déplaçable, redimensionnable, opacité réglable, toujours au-dessus en option (désactivé par défaut), liste de métriques choisie, menu contextuel (ouvrir Monitoring, réglages, masquer). Même processus que l'application, même collecteur.

## 6. Proposition historique de roadmap jusqu'à la distribution

La séquence ci-dessous était une proposition de jalons. La règle produit désormais applicable est que Monitoring/widget est livré avant Cleaning, telle qu'inscrite aux Phases 5 et 6 de `IMPLEMENTATION_ROADMAP.md`; les autres dépendances restent celles de la roadmap de référence et de ses portes.

Jalons ordonnés par dépendances. Chaque jalon a une porte; aucun n'est autorisé par ce document.

| Jalon | Contenu | Dépend de | Porte |
|---|---|---|---|
| **J0 — Clôture Phase 0** | Arbitrages Q1-Q10; errata E-01…E-07; acter D-17 (navigation sans Dashboard) et D-18 (sources capteurs); CLAUDE.md/config; commit de référence sur `main` | — | Clôture formelle par le propriétaire |
| **J1 — Validation d'architecture** | V-01, V-02A (installation per-user, signature de test), V-03, V-04, V-05, V-06 volet contraintes, V-11, V-12 (signature de test), V-13; **V-17A étude documentaire des sources capteurs** (sans code) | J0 + autorisation | Preuves examinées |
| **J2 — Fondation (Phase 1)** | Shell 6 destinations + barre d'état; Settings; cycle de vie; IPC; schéma SQLite; harnais de tests automatisés; CI reproductible | J1 + autorisation Phase 1 | Tests de fondation verts |
| **J3 — Storage cœur** | Scanner classique, index, arbre, treemap, recherche, gros fichiers, exports, historique de scans (Phases 2-3) | J2 | Fixtures exactes, pas de freeze |
| **J4 — Monitoring et widget** (avancé) | Collecteur, page Monitoring, widget, processus; capteurs validés par V-17 | J2 (+V-17A) | Mesures sourcées, idle mesuré |
| **J5 — Doublons, MFT, performances** | Doublons; volet backend V-06; profils; benchmarks V-08/V-09/V-15 | J3 | Parité MFT, profils mesurés |
| **J6 — Cleaning** | Catégories une à une (V-16) | J3 | Chaque catégorie prouvée |
| **J7 — Optimization** | Démarrage (V-14), services, recommandations | J4 (données), J2 | Restauration prouvée |
| **J8 — Menu contextuel** | Implémentation Phase 8 | J3, J6 | Aucune trace orpheline |
| **J9 — Durcissement et distribution** | V-02B (certificat réel, signature), V-18 licences, audit sécurité, accessibilité V-17, tests complets | Tous, D-16 | Porte release |

**Évolution retenue depuis le relevé initial :** l'ancienne roadmap plaçait Monitoring/widget après Cleaning; la roadmap de référence place maintenant Monitoring/widget en Phase 5 et Cleaning en Phase 6, avec possibilité d'avancer en parallèle de Storage après la fondation. Cette séquence est actée par Q6 et D-17, et apparaît dans `IMPLEMENTATION_ROADMAP.md`.

**Tâches parallélisables :** J3 (Rust) ‖ J4 (C# collecteur/UI Monitoring); V-17A (documentaire, Claude) ‖ J1 techniques (Codex); revues de sécurité Claude en continu sur les livrables Codex.

## 7. Validations techniques

Existantes et toujours nécessaires : V-01 à V-18 (inchangées sur le fond).

Nouvelles ou étendues **[P]** :
- **V-02A / V-02B** : scission de V-02 selon l'arbitrage E-06 (A : installation/upgrade/désinstallation avec signature de test avant Phase 1; B : certificat réel et chaîne de signature avant distribution). V-12 utilise une signature de test en J1.
- **V-17A — Sources capteurs (documentaire)** : pour chaque métrique de la vision (CPU temp/fréquence/puissance, GPU usage/VRAM/temp/fréquence/puissance, RAM pression, disques, réseau) : API candidate, privilège requis, OS minimal, constructeurs couverts, licence, comportement sans capteur. Livrable : matrice « disponible / conditionnel / indisponible sans pilote ».
- **V-17 étendu** : essais sur matériel NVIDIA/AMD/Intel et sans GPU dédié; comparaison à une référence; aucune boucle en cas d'absence.
- **V-10 étendu** : coût CPU/RAM du rendu des graphes et du widget selon la fréquence.
- **V-19 — Navigation et accessibilité du shell à 6 destinations** (peut être fusionné dans V-17 accessibilité).

## 8. Questions historiques et réponses après arbitrage

Le tableau des options ci-dessous est conservé comme trace des questions posées avant décision. Les recommandations retenues ne sont plus en attente d'approbation : leur état actuel figure dans la colonne suivante et dans D-17/D-18.

| # | Question initiale | Options | Recommandation initiale | Conséquences | État actuel |
|---|---|---|---|---|
| Q1 | Page d'accueil sans Dashboard | A Storage · B dernière page visitée · C choix dans Settings | **A**, avec C en option ultérieure | Simple, cohérent avec l'inspiration WizTree/TreeSize | **Acté : Storage**, D-17. Dashboard supprimé. |
| Q2 | Où vit la liste des processus | A Monitoring seul · B les deux | **A** | Une liste, un coût de collecte; Optimization se concentre sur démarrage/services/recommandations | **Acté : Monitoring seul**, D-17. |
| Q3 | Sources des capteurs (températures, fréquences, puissance) | A API Windows documentées seulement · B A + bibliothèques constructeur présentes avec le pilote, chargées dynamiquement après revue de licence · C élargir le helper élevé · D pilote | **B** (C et D écartés : contraires à D-06 et à l'interdiction de pilote) | B demande V-17A, revue de licence par constructeur, et accepte que CPU temp/puissance soient souvent indisponibles | **Acté :** règles D-18; couverture et sources à vérifier par V-17A/V-17. |
| Q4 | Historique de performances | A mémoire de session seule en V1 · B persisté avec rétention | **A** | Pas d'écritures périodiques, idle et confidentialité préservés; B possible en V2 | **Acté : mémoire seulement en V1**, D-18. |
| Q5 | Graphiques | A dessin maison WPF · B bibliothèque MIT après revue | Décider après V-17A/V-10; **A** par défaut tant qu'aucune bibliothèque n'est revue | A = plus de code, zéro dépendance; B = dépendance et licence | **Recommandation retenue par D-18 : dessin WPF par défaut**; V-10 mesure le coût. Aucune bibliothèque choisie. |
| Q6 | Ordre de la roadmap | A Monitoring en J4 avant Cleaning · B ordre actuel | **A** | Monitoring livré plus tôt, parallélisable avec Storage | **Acté : Monitoring/widget avant Cleaning**, roadmap Phase 5 avant Phase 6. |
| Q7 | Contenu des statistiques | Liste du §5.6 ou autre | Liste §5.6 | Définit le schéma SQLite des résumés | **Acté :** contenu de History défini dans la spécification produit et D-17; schéma reste à valider. |
| Q8 | « Personnalisation de l'interface » et langues | Périmètre (disposition, colonnes, thème) et langues V1 (FR, EN, FR+EN) | Colonnes/thème/densité; **FR + EN** | La localisation dès J2 évite une refonte | **Acté : FR + EN et personnalisation retenue**, D-17/spécification. |
| Q9 | Acter D-17 « navigation sans Dashboard » et la redistribution §2 | Oui / ajuster | Oui | Corrections listées au §2 | **Acté en D-17**; ne pas rouvrir. |
| Q10 | Widget « indépendant » | Indépendance d'affichage dans le même processus (D-09) · processus séparé | **Même processus** | Conforme à D-09; un processus séparé ajouterait IPC et un second cycle de vie | **Acté : même processus**, D-17. Le widget lui-même est livré ultérieurement selon la roadmap. |

### Points réellement ouverts et validations restantes

- **Décision produit ouverte :** D-16, modèle final de distribution/licence. Aucune hypothèse commerciale ne le tranche.
- **Choix propriétaire/prérequis encore ouverts :** toolset d'installation avant V-02A; certificat réel avant V-02B; portée des catégories de nettoyage V1 selon les preuves de sécurité.
- **Validations techniques, pas des questions produit :** backend MFT et parité en Phase 4; sources de capteurs via V-17A/V-17; budgets et seuils à préenregistrer/mesurer; détails IPC, Shell, stockage et cycle de vie selon les dossiers concernés.
- **Errata E-01 à E-07 :** corrections documentaires appliquées et historisées dans `governance/ERRATA_PHASE0.md` et `CHANGELOG_PHASE0_REVIEW.md` §14. Les validations techniques restent non exécutées tant qu'un rapport probant ne dit pas le contraire.

Note de provenance : la phrase de l'analyse initiale indiquant que l'application des errata était en attente décrivait l'état antérieur à leur application; elle est remplacée par le statut ci-dessus. La validation documentaire ne constitue pas une preuve technique ni une autorisation de Phase 1.
