# OPTIVANCE V0.3 — Plan d’intégration du scan MFT et de l’UAC

**Date :** 12 novembre 2026  
**Statut :** plan préparatoire; aucun code applicatif modifié. La Phase 1 reste non autorisée.

## 1. Cohérence avec les décisions existantes

- **D-06** autorise un helper ponctuel et limité pour le MFT; l’UI et le scan classique restent non élevés. La méthode de backend reste ouverte à validation. Cette mission autorise un prototype `FSCTL_ENUM_USN_DATA`, sans réécrire D-06 ni le présenter comme backend final déjà approuvé.
- **D-10** conserve WPF + Rust et des Named Pipes locaux versionnés. L’échange avec le helper sera une session distincte, bornée et authentifiée; aucun serveur réseau.
- **D-07** reste prioritaire : taille logique principale, taille allouée secondaire; les chemins hardlink restent visibles et un seul représentant déterministe contribue aux agrégats.
- **D-08** garde les états de scan Terminé / Interrompu / Échec. Le statut de méthode/resultat précise « MFT partiel », « MFT échoué », « classique après refus UAC » ou « classique après échec MFT » sans présenter une analyse incomplète comme terminée.
- Aucun document d’architecture de référence n’est modifié par ce plan.

## 2. Changements envisagés

1. **Moteur Rust** : ajouter un exécutable helper distinct, sans crate tiers. Il valide une lettre de volume locale, vérifie le système de fichiers NTFS, ouvre le volume sans droit d’écriture, boucle sur `FSCTL_ENUM_USN_DATA` et ne traite que les formats d’enregistrements explicitement supportés. Les offsets/longueurs et versions inconnues sont contrôlés avant lecture.
2. **Métadonnées et index** : utiliser `OpenFileById` en droits de lecture d’attributs pour recueillir EOF/allocation/LastWrite disponibles; ignorer les flux de contenu. Construire l’index par référence de fichier et référence parent; détecter versions inconnues, entrées périmées, parents absents, cycles, noms multiples et accès refusés. Si une information nécessaire manque, marquer le résultat partiel/échoué ou rebasculer; ne pas fabriquer un chemin ou une taille.
3. **Hardlinks** : conserver toutes les liaisons nom/parent si l’énumération les expose. Identifier l’objet par volume + référence/sequence NTFS, trier les chemins normalisés selon la convention D-07 et n’agréger qu’un chemin. Une fixture hardlink vérifie les noms exposés; si la couverture MFT ne permet pas de prouver tous les alias, le résultat ne peut pas être qualifié complet.
4. **Lancement** : le clic explicite « Analyse rapide (MFT) » crée d’abord le canal local de cette analyse puis appelle le helper avec `UseShellExecute=true` et `Verb=runas`. La fenêtre principale ne s’élève jamais. Chaque analyse provoque un nouveau consentement UAC; l’erreur de refus est reconnue séparément et propose le scan classique. Le scan classique conserve sa commande et ne produit aucun UAC.
5. **Canal local** : nom aléatoire par session, rejet des clients distants, DACL limitée à l’utilisateur courant, administrateurs locaux et SYSTEM (pour supporter l’entrée d’identifiants administrateur différents), une seule instance, trames versionnées et taille maximale. Le client valide le PID, le jeton élevé, le chemin d’image du helper attendu, la version, les ID/ranges et la taille de chaque message. Le helper ne reçoit ni chemin arbitraire, ni commande générique, et ne prend aucune commande du pipe autre que l’annulation.
6. **Arrêt/erreurs** : UAC refusé, non-NTFS, échec/plantage helper, timeout et annulation sont différenciés. Le processus helper est arrêté à la fermeture/annulation et est attendu avant retour; le fallback classique signale explicitement la cause et la méthode réellement utilisée.
7. **UI Storage** : ajouter une action MFT distincte, le mode utilisé, la progression, compteurs, temps, avertissements et état final. Les interactions existantes restent dans la fenêtre standard. Le scan classique reste disponible à côté, pas derrière un choix implicite.
8. **Validation** : tests Rust sans dépendance pour records valides/invalides et reconstruction; tests des métadonnées via fixtures temporaires NTFS; tests UI/IPC/UAC avec refus, acceptation, annulation et panne. Benchmark comparatif sur `C:` en consignant les modes/consentements, comptes, temps, mémoire et rapprochement sans conserver de chemins locaux ou CSV personnels dans Git.

## 3. Limites de sécurité à résoudre avant une revendication de production

- Le projet n’a pas de certificat de publication fixé (D-16/V-02B demeure ouvert). Un helper élevé depuis un dossier d’installation accessible en écriture à l’utilisateur ne doit pas être décrit comme une chaîne de confiance de production sur la seule base d’un pipe ACL. Les essais locaux devront afficher le vrai éditeur/signature (ou « éditeur inconnu ») et conserver cette limite dans le rapport; aucune signature/certificat ou installation système ne sera créée dans cette mission.
- D-06 exige une allowlist étroite : le helper ne doit pas exposer d’API d’administration générale, d’écriture de fichier, de contrôle de journal, de service ou de privilège persistant.
- L’énumération FSCTL seule ne contient pas EOF/allocation. Les ouvertures `OpenFileById` ajoutent du coût et peuvent manquer des objets changés pendant le scan; le seuil de performance et l’exhaustivité seront mesurés, pas inférés du nom « MFT ».

## 4. Vérification du SDK et blocage actuel

`dotnet --list-sdks` et `dotnet --version` détectent **10.0.404**. Le projet `Optivance.Core` compile en Release avec 0 warning et 0 erreur.

Le projet WPF ne démarre pas sa compilation dans le shell Codex : le diagnostic MSBuild échoue à résoudre `Microsoft.NET.SDK.WorkloadAutoImportPropsLocator` et `Microsoft.NET.SDK.WorkloadManifestTargetsLocator` (`MSB4276`), car leurs dossiers attendus sont absents de `C:\Program Files\dotnet\sdk\10.0.404\Sdks`. `dotnet --info` échoue également quand il tente d’interroger le Service Control Manager. Le répertoire CLI temporaire a été placé sous `validation/v03/tmp`; la variable ponctuelle `MSBuildEnableWorkloadResolver=false` n’a pas résolu l’échec. Aucun dossier SDK n’a été créé, réparé ou modifié; aucun contournement de sandbox n’est proposé.

Le prochain changement de code WPF doit attendre soit un environnement .NET SDK/WPF correctement initialisé et accessible, soit une autorisation explicite de réparer/réinstaller le SDK. La réparation peut toucher `Program Files` ou les enregistrements de workloads; elle ne sera pas exécutée sans cette autorisation.

## 5. Préconditions de reprise

1. Résoudre le blocage SDK dans l’environnement de développement autorisé; vérifier `dotnet build` pour `Optivance.Desktop` avant d’éditer l’UI.
2. Le test bout en bout du helper nécessite que l’utilisateur accepte réellement le dialogue UAC. Codex ne peut ni confirmer automatiquement ni contourner le dialogue; un refus sera un scénario de test normal.
3. Exécuter d’abord les tests parser/fixtures, puis une analyse contrôlée sur NTFS et enfin les benchmarks. Aucune validation Windows LTSC n’est inférée d’un test Windows 11.
