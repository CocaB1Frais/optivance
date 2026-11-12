# OPTIVANCE V0.2 — Storage

V0.2 est un prototype local du module Storage. Il analyse les dossiers avec le scan classique Rust; il n’utilise pas la MFT. Aucun autre module produit n’a été développé.

## Lancer le build Release local

Le binaire compilé se trouve dans :

```text
src/Optivance.Desktop/bin/Release/net10.0-windows/OPTIVANCE.exe
```

Il requiert le .NET 10 Desktop Runtime installé sur le poste. L’exécutable Rust `optivance-engine.exe` est placé à côté de l’application par le build. Aucun installateur, updater ou package de distribution n’est fourni; D-16 reste ouvert.

Pour reconstruire avec les outils déjà installés, depuis une invite configurée pour Rust MSVC et le SDK .NET 10 :

```powershell
dotnet build .\src\Optivance.Desktop\Optivance.Desktop.csproj -c Release --no-restore
```

Le projet appelle Cargo pour produire le worker Rust. Aucun package NuGet ou crate tiers supplémentaire n’a été ajouté pour V0.2.

## Portée

- Scan classique de dossiers et volumes, tailles logique/allouée, hardlinks reconnus quand les informations d’identité sont accessibles.
- Treemap squarified, couleurs par catégorie, infobulle, sélection et navigation synchronisées avec la grille.
- Grille virtualisée, tri, fil d’Ariane et menu contextuel d’analyse.
- Named Pipe local versionné v2 protégé par ACL.
- Aucun scan au démarrage, aucun service, driver, suppression, optimisation Windows ou accès MFT.

Les mesures et les limites sont dans [V0.2_REPORT.md](validation/v02/V0.2_REPORT.md) et [V0.2_BENCHMARK_REPORT.md](validation/v02/V0.2_BENCHMARK_REPORT.md). La proposition MFT sans code est dans [MFT_ENGINE_PROPOSAL.md](validation/v02/MFT_ENGINE_PROPOSAL.md).
