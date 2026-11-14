<#
  Construit la version portable d'OPTIVANCE (aucun installateur, rien à installer pour l'utilisateur).

  Résultat :
    dist\OPTIVANCE\OPTIVANCE.exe          ← le seul fichier à lancer (.NET inclus)
    dist\OPTIVANCE\moteur\                ← moteur d'analyse et composant administrateur
    dist\OPTIVANCE-<version>-win-x64.zip  ← à distribuer (GitHub Releases)
    dist\OPTIVANCE-<version>-win-x64.zip.sha256

  Usage : .\build.ps1            (tests puis construction)
          .\build.ps1 -SkipTests
#>
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$app = Join-Path $dist 'OPTIVANCE'

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "OPTIVANCE $version" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host 'Tests…'
    dotnet run --project (Join-Path $root 'tests\Optivance.Core.Tests') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests en échec : construction annulée.' }
}

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
Write-Host 'Publication (autonome, un seul exécutable)…'
dotnet publish (Join-Path $root 'src\Optivance.Desktop') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false -o $app `
    --source https://api.nuget.org/v3/index.json  # paquets d'exécution .NET officiels (Microsoft, MIT), pour cette commande seulement
if ($LASTEXITCODE -ne 0) { throw 'Échec de la publication.' }

# Racine : OPTIVANCE.exe seul (+ dossier moteur). Tout autre fichier est une anomalie.
Get-ChildItem $app -File -Filter *.pdb | Remove-Item -Force
$unexpected = Get-ChildItem $app -File | Where-Object Name -ne 'OPTIVANCE.exe'
if ($unexpected) { throw "Fichiers inattendus à la racine : $($unexpected.Name -join ', ')" }
foreach ($engine in 'optivance-engine.exe', 'optivance-mft-helper.exe') {
    if (-not (Test-Path (Join-Path $app "moteur\$engine"))) { throw "Moteur manquant : moteur\$engine" }
}

$zip = Join-Path $dist "OPTIVANCE-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $app -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ascii

$exe = Get-Item (Join-Path $app 'OPTIVANCE.exe')
Write-Host ''
Write-Host "Prêt : $($exe.FullName) ($([math]::Round($exe.Length / 1MB, 1)) Mo)" -ForegroundColor Green
Write-Host "Archive : $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) Mo)" -ForegroundColor Green
