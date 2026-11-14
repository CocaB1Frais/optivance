# OPTIVANCE — Proposition pour une future voie MFT/USN

**Statut : proposition documentaire seulement — aucune implémentation ni validation de privilèges effectuée.** Le scan classique reste le seul moteur opérationnel V0.2 et doit demeurer le fallback.

## Voie candidate

Étudier l’API documentée `FSCTL_ENUM_USN_DATA` appelée avec `DeviceIoControl` sur un handle de volume local de forme `\\.\X:`. La documentation Microsoft décrit l’énumération des enregistrements MFT à l’aide d’`MFT_ENUM_DATA_V0`; le volume doit être NTFS et les appels successifs reprennent le numéro de référence retourné par le buffer. L’API renvoie un buffer contenant des enregistrements USN versionnés, pas un arbre de chemins prêt à afficher. Voir [FSCTL_ENUM_USN_DATA](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_enum_usn_data), [MFT_ENUM_DATA_V0](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-mft_enum_data_v0) et [parcours des buffers USN](https://learn.microsoft.com/en-us/windows/win32/fileio/walking-a-buffer-of-change-journal-records).

Le prototype futur devrait, dans cet ordre :

1. Détecter le système de fichiers, le volume local, l’édition Windows et le niveau de privilège; refuser proprement les systèmes non pris en charge.
2. Ouvrir le volume en lecture seule et tenter la voie documentée sans élévation. Ne pas créer, modifier, supprimer ou agrandir le journal USN.
3. Si et seulement si le test sur les systèmes cibles confirme qu’une élévation est nécessaire, présenter une demande UAC ponctuelle, limitée au scan choisi. Aucun service, pilote ou élévation au démarrage.
4. Lire et vérifier les buffers par `RecordLength`, `MajorVersion`, `FileNameOffset` et `FileNameLength`; ne pas supposer que le nom est nul-terminé. Rejeter les versions inconnues sans interpréter leur layout.
5. Construire une carte par référence de fichier et référence du parent, puis reconstruire les chemins; traiter les références périmées, les entrées supprimées, les renames pendant le scan, les parents absents, les cycles et les points de réanalyse.
6. Dédoublonner les tailles par identité de fichier/volume, tout en conservant les noms de tous les hardlinks. La portée de l’identité et la possibilité de plusieurs noms doivent être validées sur des volumes NTFS réels.
7. Obtenir les tailles logiques et allouées. Les enregistrements `USN_RECORD_V2` documentés contiennent notamment les références parent/enfant, les attributs et le nom, mais pas `EndOfFile`/`AllocationSize`; une voie candidate est d’ouvrir par identifiant avec `OpenFileById`, puis de lire `FILE_STANDARD_INFO`. Cela peut encore nécessiter des opérations par fichier et doit être mesuré. Sources : [USN_RECORD_V2](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-usn_record_v2), [OpenFileById](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-openfilebyid), [FILE_STANDARD_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_standard_info).
8. Calculer les tailles uniques et construire le même modèle d’index plat que le scan classique; comparer comptes, chemins, hardlinks et tailles sur un jeu contrôlé avant d’envisager un basculement.
9. Conserver le protocole Named Pipe versionné et son ACL. Toute variante d’élévation devra examiner le SID du processus élevé et l’ACL du canal, notamment lorsque l’utilisateur fournit des identifiants administrateur distincts. Ne jamais élargir une ACL par défaut pour rendre un test fonctionnel.
10. Revenir automatiquement au scan classique en cas de format, FSCTL, volume ou accès non pris en charge; afficher le motif et permettre à l’utilisateur de choisir le mode classique.

## Permissions et UAC — point non tranché techniquement

Microsoft documente le handle de volume, l’IOCTL et les conditions NTFS. La documentation générale des opérations de journal indique que les opérations de journal de modifications nécessitent des privilèges administrateur; elle ne constitue pas une preuve expérimentale du comportement exact de `FSCTL_ENUM_USN_DATA` dans chaque édition, état UAC et politique d’entreprise. La documentation indique également que l’accès direct aux volumes est restreint dans les versions modernes et que certaines opérations exigent des privilèges administrateur : [CreateFile — volumes physiques et volumes](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilea), [identifiant du journal](https://learn.microsoft.com/en-us/windows/win32/fileio/using-the-change-journal-identifier).

**Recommandation :** ne pas promettre de scan MFT non élevé avant test. Faire d’abord un test minimal en lecture seule non élevé sur Windows 11 x64 et Windows 10 Enterprise LTSC 2019 x64; noter `GetLastError`, le système de fichiers, les droits demandés et la politique UAC. Si l’accès échoue, tester séparément un helper ponctuel élevé après consentement explicite. L’élévation peut être un bloqueur d’architecture tant que le canal sûr avec éventuels identifiants distincts n’est pas défini.

## Cohérence, hardlinks et fallback

- Le MFT/USN est un instant de métadonnées du système de fichiers; le volume peut changer pendant l’énumération. Ne pas présenter un scan concurrent comme instantané atomique.
- Distinguer les enregistrements périmés/supprimés, les chemins accessibles et les objets encore ouverts. Comparer par ID de fichier et numéro de séquence pour éviter de réutiliser à tort une référence de fichier supprimée puis réattribuée.
- Les hardlinks partagent une identité de fichier mais peuvent avoir plusieurs noms/parents. Chaque chemin doit rester navigable; la taille allouée et logique ne compte qu’une fois dans les totaux uniques.
- Comparer les résultats MFT et classiques sur une fixture NTFS comprenant hardlinks, fichiers renommés/supprimés pendant le scan, chemins longs, ACL restrictives, nombreux petits fichiers, fichiers clairsemés/compressés et plusieurs volumes.
- En cas de différence inexpliquée, de version USN non gérée, d’accès refusé, d’ID parent inconnu ou de validation incohérente, marquer l’analyse MFT comme échouée/partielle et proposer le scan classique. Ne jamais fusionner silencieusement des données de sources incohérentes.

## Tests nécessaires avant décision

1. Énumération non élevée et élevée séparées sur NTFS Windows 11 et LTSC 2019; confirmer LTSC 2021 seulement si elle est encore supportée.
2. Matrice d’accès avec administrateur standard/compte standard, UAC activé et désactivé, stratégie d’entreprise et compte administrateur distinct.
3. Parsing de buffers versionnés : limites, alignement, noms Unicode, buffers tronqués, changement majeur de version.
4. Reconstruction de chemins, références de fichiers recyclées, parents absents, reparse points et mises à jour concurrentes.
5. Hardlinks, sparse/compressed files, allocation size, fichiers volumineux et accès refusé.
6. Contrôle de cohérence face au scan classique et benchmark identique à celui du [rapport V0.2](V0.2_BENCHMARK_REPORT.md).
7. Vérification que l’application reste non élevée au démarrage, que seul le helper de scan reçoit le consentement et qu’il n’y a ni service, ni driver, ni changement du journal.

**Conclusion :** le candidat documenté est une énumération USN/MFT avec récupération complémentaire des métadonnées par identifiant. Son avantage potentiel est d’éviter une partie de l’énumération arborescente classique; sa valeur réelle dépendra des accès aux volumes, du coût des tailles par fichier et de la reconstruction des chemins. Aucun gain de vitesse n’est établi à cette étape.
