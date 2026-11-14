<#
  Construit OPTIVANCE (version portable : aucun installateur, .NET inclus).

  Résultat :
    OPTIVANCE.exe + moteur\            ← à la racine de ce dossier : double-clic pour lancer
    dist\OPTIVANCE-win-x64.zip         ← à distribuer (publié automatiquement dans GitHub Releases)
    dist\OPTIVANCE-win-x64.zip.sha256

  Usage : .\build.ps1             tests puis OPTIVANCE.exe à la racine
          .\build.ps1 -Package    + archive dist\OPTIVANCE-win-x64.zip (publication)
          .\build.ps1 -Clean      + supprime ensuite tous les fichiers de compilation (dossier minimal)
          .\build.ps1 -SkipTests
#>
param([switch]$SkipTests, [switch]$Package, [switch]$Clean)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$root = $PSScriptRoot
$source = Join-Path $root 'source'
$dist = Join-Path $root 'dist'
$app = Join-Path $dist 'OPTIVANCE'

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "OPTIVANCE $version" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host 'Tests…'
    dotnet run --project (Join-Path $source 'tests\Optivance.Core.Tests') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests en échec : construction annulée.' }
}

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
Write-Host 'Publication (autonome, un seul exécutable)…'
dotnet publish (Join-Path $source 'Optivance.Desktop') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false -o $app `
    --source https://api.nuget.org/v3/index.json  # paquets d'exécution .NET officiels (Microsoft, MIT), pour cette commande seulement
if ($LASTEXITCODE -ne 0) { throw 'Échec de la publication.' }

# Contenu attendu : OPTIVANCE.exe seul + dossier moteur.
Get-ChildItem $app -File -Filter *.pdb | Remove-Item -Force
# Raccourci de désinstallation, à côté de l'application.
"@echo off`r`nstart """" ""%~dp0OPTIVANCE.exe"" --uninstall`r`n" | Set-Content (Join-Path $app 'Desinstaller OPTIVANCE.cmd') -Encoding ascii -NoNewline
$unexpected = Get-ChildItem $app -File | Where-Object Name -notin 'OPTIVANCE.exe', 'Desinstaller OPTIVANCE.cmd'
if ($unexpected) { throw "Fichiers inattendus : $($unexpected.Name -join ', ')" }
foreach ($engine in 'optivance-engine.exe', 'optivance-mft-helper.exe') {
    if (-not (Test-Path (Join-Path $app "moteur\$engine"))) { throw "Moteur manquant : moteur\$engine" }
}

# Archive à distribuer (nom fixe : le lien « Télécharger » du README vise toujours la dernière version).
if ($Package) {
    $zip = Join-Path $dist 'OPTIVANCE-win-x64.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $app -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ascii
    Write-Host "Archive : $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) Mo)" -ForegroundColor Green
}

# Copie à la racine du dossier (non versionnée) pour lancer OPTIVANCE d'un double-clic.
if (-not $env:CI) {
    try {
        Copy-Item (Join-Path $app 'OPTIVANCE.exe') $root -Force
        Copy-Item (Join-Path $app 'moteur') $root -Recurse -Force
        Copy-Item (Join-Path $app 'Desinstaller OPTIVANCE.cmd') $root -Force
        Write-Host "Prêt : $(Join-Path $root 'OPTIVANCE.exe')" -ForegroundColor Green
    }
    catch {
        Write-Warning "OPTIVANCE est ouvert : quittez-le (icône près de l'horloge › Quitter) puis relancez .\build.ps1 pour mettre à jour la copie à la racine."
    }
}

# Pas de copie en double : le dossier intermédiaire disparaît (l'archive reste si -Package).
if (-not $Package -and (Test-Path $dist)) { Remove-Item $dist -Recurse -Force }

# Dossier minimal : fichiers de compilation supprimés (recréés à la prochaine construction, ~2 min).
if ($Clean) {
    # Seulement bin/obj des projets .NET (jamais engine\src\bin, qui contient du code source Rust).
    Get-ChildItem $source -Filter *.csproj -Recurse | ForEach-Object {
        foreach ($output in 'bin', 'obj') { Remove-Item (Join-Path $_.DirectoryName $output) -Recurse -Force -ErrorAction SilentlyContinue }
    }
    Remove-Item (Join-Path $source 'engine\target') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Fichiers de compilation supprimés.' -ForegroundColor Green
}