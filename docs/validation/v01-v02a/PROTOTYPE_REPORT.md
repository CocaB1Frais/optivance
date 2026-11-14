# OPTIVANCE V0.1 — Compte rendu de validation du prototype

Date : 2026-11-11 (heure locale du poste)

Autorisation : prototype local V0.1 explicitement autorisé par le propriétaire. Cette note ne clôture aucun dossier formel V-01 ou V-02.

## Environnement réellement observé

| Élément | Valeur observée |
|---|---|
| Architecture | x64 (`dotnet` RID `win-x64`) |
| OS rapporté par .NET | Windows, version `10.0.26200` |
| Registre Windows | `ProductName=Windows 10 Pro`, `EditionID=Professional`, `DisplayVersion=25H2`, build `26200.9457` |
| Vérification WMI | `Get-CimInstance Win32_OperatingSystem` refusé par l’environnement |
| .NET SDK | `10.0.404`, MSBuild `18.9.15` |
| .NET runtime installé | `Microsoft.NETCore.App 10.0.15`, `Microsoft.WindowsDesktop.App 10.0.15` |
| Rust | `1.99.1`, cible `x86_64-pc-windows-msvc` |
| MSVC | `14.51.36231` (Build Tools `18.11.3`) |
| Windows SDK | `10.0.28000.2957` installé; en-têtes/libs `10.0.28000.0` |

La désignation Windows est contradictoire entre les interfaces disponibles. Le poste n’est pas une machine Windows 10 Enterprise LTSC 2019; aucune compatibilité LTSC n’a été validée.

## Résultats observés

| Contrôle | Résultat | Preuve/limite |
|---|---|---|
| Compilation Rust x64 MSVC | Réussie | `cargo build --release`, code de sortie 0, sans crate tierce |
| Compilation WPF/C# | Réussie | `dotnet build ... -p:UseSharedCompilation=false`, 0 avertissement, 0 erreur |
| Démarrage du shell | Réussi sur ce poste | Au dernier relevé, `OPTIVANCE.exe` avait un handle de fenêtre non nul et `Responding=True` (PID `25648`). La capture visuelle automatisée n’a pas abouti; l’état WPF est donc vérifié par le handle et la réponse du processus, pas par inspection de pixels. |
| Démarrage worker + connexion Named Pipe ACL utilisateur | Réussi pour un aller-retour simple hors du bac à sable | La première tentative dans le bac à sable Codex a échoué avec `Accès refusé. (os error 5)`. Le même client a ensuite établi le pipe protégé et reçu le résultat du worker hors bac à sable; aucun pipe sans ACL n’a été utilisé. |
| Analyse réelle d’un dossier | Réussie sur le petit dossier source du prototype | `engine\src` : état `Completed`, 1 dossier, 1 fichier, 0 avertissement, 17 462 octets logiques uniques, 20 480 octets alloués, durée observée 27 ms; 2 entrées transmises. Ce résultat valide un parcours élémentaire sur ce dossier seulement. |
| Hardlinks, accès refusés, annulation UI | Non testés | Le fixture hardlink n’a pas pu être créé sous les permissions disponibles. Les chemins refusés et l’annulation depuis l’interface n’ont pas été exercés. |
| Exactitude générale des tailles allouées et déduplication des hardlinks | Non validée | Le scanner a renvoyé une valeur allouée pour un fichier, mais aucun jeu de référence multi-fichiers/hardlinks n’a permis de comparer les calculs. |
| Absence de processus orphelin | Confirmée après cette exécution de probe uniquement | `ScanEngineClient` attend la fin normale du worker avant de retourner; le worker n’était plus présent au relevé suivant. La fermeture de la fenêtre, l’arrêt forcé et les scénarios d’échec restent à vérifier. |
| Self-contained / machine propre / dépendances natives | Non testés | Le build actuel est framework-dependent et s’exécute sur un poste où le runtime .NET 10 est installé. |
| Windows 11 x64 maintenu / Windows 10 Enterprise LTSC 2019 | Non testés comme cibles | Un build sur le poste courant n’est pas une preuve de compatibilité de ces systèmes. |
| Ordre des destinations | Écart documentaire identifié | Le prototype suit la demande Storage → Monitoring → Cleaning; les documents D-17 conservent Storage → Cleaning → Monitoring. Ce rapport ne transforme pas l’ordre de prototype en nouvelle décision produit. |

## Protocole reproductible

Depuis un Developer Command Prompt x64 pour Visual Studio 2026, à la racine du dépôt :

```bat
set PATH=%USERPROFILE%\.dotnet;%USERPROFILE%\.cargo\bin;%PATH%
set DOTNET_CLI_HOME=%TEMP%\optivance-dotnet-cli-home
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet restore src\Optivance.Desktop\Optivance.Desktop.csproj --ignore-failed-sources -p:NuGetAudit=false -p:RestoreBuildInParallel=false -p:RestoreUseStaticGraphEvaluation=false -m:1
dotnet build src\Optivance.Desktop\Optivance.Desktop.csproj -c Debug --no-restore -m:1 -p:UseSharedCompilation=false
```

Puis :

```powershell
Start-Process .\src\Optivance.Desktop\bin\Debug\net10.0-windows\OPTIVANCE.exe
```

Probe IPC/scan :

```bat
dotnet restore validation\v01-v02a\ScanProbe\ScanProbe.csproj --ignore-failed-sources -p:NuGetAudit=false -p:RestoreBuildInParallel=false -p:RestoreUseStaticGraphEvaluation=false -m:1
dotnet build validation\v01-v02a\ScanProbe\ScanProbe.csproj -c Debug --no-restore -m:1 -p:UseSharedCompilation=false
dotnet validation\v01-v02a\ScanProbe\bin\Debug\net10.0-windows\ScanProbe.dll
```

Le probe lit seulement `engine\src` par défaut. Résultat exécuté hors du bac à sable Codex :

```text
state=Completed; directories=1; files=1; warnings=0; logical=17462; allocated=20480; elapsedMs=27
```

La tentative isolée dans le bac à sable a renvoyé `Accès refusé. (os error 5)` avant le scan. Une tentative sur un fichier unique a ensuite confirmé l’établissement du pipe mais le worker l’a rejetée comme cible invalide (le scanner attend un dossier ou volume). Le succès ci-dessus porte sur le scan du seul petit dossier `engine\src`.

## Verdict et risques restants

- Le shell WPF x64 et le binaire Rust se compilent; le shell démarre sans demande UAC observée.
- Un scan élémentaire fonctionne via le client .NET, le pipe protégé par ACL utilisateur et le worker Rust sur le poste courant lorsque le probe est exécuté hors du bac à sable Codex. Le refus observé uniquement dans le bac à sable n’est pas une incompatibilité générale de l’IPC, mais signale que ce bac à sable ne peut pas servir d’environnement pour valider ce flux.
- La vérification a porté sur un seul petit dossier; elle ne valide ni les performances, ni l’exactitude générale des tailles, ni hardlinks, annulation, erreurs d’accès ou intégration de la commande depuis le bouton WPF.
- Les statuts formels V-01 et V-02 restent **NON TESTÉS** : aucun environnement Windows 11 cible ou LTSC 2019 isolé, aucune machine propre et aucun déploiement autonome n’ont été utilisés.
- Pas d’intégration du menu contextuel, widget, MFT, suppression, optimisation, persistance de l’historique, export, telemetry ou service Windows dans ce prototype.
- D-16 reste ouverte. Aucun choix de distribution ou de licence n’a été effectué.
- La Phase 1 n’a pas commencé et n’est pas autorisée.
