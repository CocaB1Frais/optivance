# OPTIVANCE — Prototype local V0.1

Ce prototype limité vérifie le démarrage du shell WPF et pose la première structure de l’analyse Storage avec un moteur Rust séparé. Il ne constitue pas la Phase 1 ni un livrable produit.

## État

- Shell WPF x64 avec six destinations dans l’ordre demandé : Storage, Monitoring, Cleaning, Optimization, History, Settings. Storage est la page d’accueil; aucun Dashboard n’existe.
- Storage permet de choisir un volume prêt ou un dossier, de lancer/annuler une analyse, d’afficher des lignes triables, tailles logiques et allouées, progression par compteurs et avertissements.
- Moteur Rust x64 : parcours classique non élevé, récursif, tolérance aux erreurs de lecture, exclusion des points de réanalyse et agrégation des tailles de dossiers.
- Contrat local JSON à lignes, version 1, via Named Pipe; worker isolé dans un processus enfant. L’ACL est limitée au SID du compte courant et les clients distants sont rejetés.
- Monitoring expose une page réelle, sans métrique simulée; les capteurs sont marqués indisponibles.
- Les autres destinations indiquent explicitement leur état en développement. Aucun widget, MFT, nettoyage, optimisation, historique persistant, export ou intégration Explorer n’est inclus.

L’ordre visible Storage → Monitoring → Cleaning suit l’instruction de ce prototype. Les documents D-17 conservent l’ordre produit antérieur Storage → Cleaning → Monitoring; cet écart d’ordre reste à arbitrer avant la Phase 1 et n’est pas enregistré ici comme une nouvelle décision produit.

## Compiler

Prérequis installés pour le prototype : .NET SDK 10 x64, Rust MSVC x64, Visual Studio Build Tools avec MSVC, Windows SDK. Depuis le dépôt, dans un **Developer Command Prompt x64 pour Visual Studio 2026** :

```bat
set PATH=%USERPROFILE%\.dotnet;%USERPROFILE%\.cargo\bin;%PATH%
set DOTNET_CLI_HOME=%TEMP%\optivance-dotnet-cli-home
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet restore src\Optivance.Desktop\Optivance.Desktop.csproj --ignore-failed-sources -p:NuGetAudit=false -p:RestoreBuildInParallel=false -p:RestoreUseStaticGraphEvaluation=false -m:1
dotnet build src\Optivance.Desktop\Optivance.Desktop.csproj -c Debug --no-restore -m:1 -p:UseSharedCompilation=false
```

La cible MSBuild compile le moteur Rust sans dépendance Cargo tierce et le copie à côté du shell. Les options de compilation sérialisent la restauration et désactivent le serveur partagé Roslyn, qui s’appuie lui aussi sur un Named Pipe dans l’environnement de validation.

## Lancer

Depuis PowerShell, à la racine du dépôt :

```powershell
Start-Process .\src\Optivance.Desktop\bin\Debug\net10.0-windows\OPTIVANCE.exe
```

Le chemin de sortie actuel est :

`src\Optivance.Desktop\bin\Debug\net10.0-windows\OPTIVANCE.exe`

Ce build Debug est **framework-dependent** et utilise le runtime .NET Desktop 10 présent sur la machine de développement. Aucun package autonome ni démarrage sur une machine propre n’a été validé.

Le probe CLI reproductible (hors UI) se construit avec :

```bat
dotnet restore validation\v01-v02a\ScanProbe\ScanProbe.csproj --ignore-failed-sources -p:NuGetAudit=false -p:RestoreBuildInParallel=false -p:RestoreUseStaticGraphEvaluation=false -m:1
dotnet build validation\v01-v02a\ScanProbe\ScanProbe.csproj -c Debug --no-restore -m:1 -p:UseSharedCompilation=false
dotnet validation\v01-v02a\ScanProbe\bin\Debug\net10.0-windows\ScanProbe.dll
```

## Limite connue

Le build réussit et le shell démarre. Le scan du petit dossier `engine/src` a été vérifié via le client .NET, le Named Pipe protégé par ACL utilisateur et le worker Rust, hors du bac à sable Codex. Le bac à sable lui-même refusait cette connexion (code Windows 5); le prototype ne retire pas l’ACL et n’élève pas les privilèges pour contourner cette limite. Le scan testé ne valide pas encore les hardlinks, l’annulation UI, les erreurs d’accès ni l’exactitude générale des tailles. Voir [le compte rendu de validation du prototype](validation/v01-v02a/PROTOTYPE_REPORT.md).

Les décisions de distribution/licence D-16 restent ouvertes. Aucun développement de Phase 1 n’est autorisé par ce prototype.
