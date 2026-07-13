# build-installer.ps1 — empaqueta el .exe Velopack en una pasada.
# Uso (Windows):
#   pwsh ./scripts/build-installer.ps1 -Version 0.1.0
# Requiere: dotnet SDK 10, vpk tool (se instala si falta).

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 1) instalar vpk si falta
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "Installing vpk tool..."
    dotnet tool install --global vpk
}

# 2) publish del Api (Windows Service host)
$publishDir = Join-Path $root "publish"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Path $publishDir | Out-Null

Write-Host "Publishing PacCollector.Api..."
dotnet publish src/PacCollector.Api/PacCollector.Api.csproj `
    -c $Configuration -r $Runtime --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del Api fallo con exit code $LASTEXITCODE" }
if (-not (Test-Path (Join-Path $publishDir "PacCollector.Api.exe"))) {
    throw "PacCollector.Api.exe NO se encontro en $publishDir post-publish"
}

# 3) publish del Shell (Photino) en la misma carpeta para que queden colaterales
Write-Host "Publishing PacCollector.Shell..."
dotnet publish src/PacCollector.Shell/PacCollector.Shell.csproj `
    -c $Configuration -r $Runtime --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del Shell fallo con exit code $LASTEXITCODE" }
if (-not (Test-Path (Join-Path $publishDir "PacCollector.Shell.exe"))) {
    throw "PacCollector.Shell.exe NO se encontro en $publishDir post-publish"
}

# 4) (opcional) copiar el frontend buildeado al wwwroot del Api
$frontendDist = Join-Path $root "frontend/dist"
if (Test-Path $frontendDist) {
    $wwwroot = Join-Path $publishDir "wwwroot"
    if (Test-Path $wwwroot) { Remove-Item -Recurse -Force $wwwroot }
    Copy-Item -Recurse $frontendDist $wwwroot
    Write-Host "wwwroot/ poblado desde frontend/dist/"
}

# 5) empaquetar con vpk
$releasesDir = Join-Path $root "releases"
if (-not (Test-Path $releasesDir)) { New-Item -ItemType Directory -Path $releasesDir | Out-Null }

Write-Host "Packing with vpk..."
# --framework webview2,net10.0-x64-desktop: el Setup.exe va a verificar y bootstrap-instalar
# Microsoft Edge WebView2 Runtime + .NET 10 Desktop Runtime si no estan presentes.
# Sin esto, Photino abre ventana en blanco en PCs que no tengan WebView2 (Win10 sin update,
# Servers, etc). YPF no quiere instalar nada manualmente.
$iconPath = Join-Path $root "branding/pac-icon.ico"
vpk pack `
    --packId PacCollector `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe PacCollector.Shell.exe `
    --packTitle "PAC Collector" `
    --packAuthors "PAC Collector" `
    --icon $iconPath `
    --outputDir $releasesDir

Write-Host "Installer listo en: $releasesDir"
Get-ChildItem $releasesDir | Format-Table Name, Length

# 6) pac-mock: simulador de equipo PAC (console standalone). Self-contained
# single-file paralelo al installer. Sirve para que YPF lo use desde linea
# de comandos para validar el setup sin necesidad de un equipo PAC real.
Write-Host "Publishing pac-mock (standalone console)..."
$mockOutDir = Join-Path $releasesDir "pac-mock"
if (Test-Path $mockOutDir) { Remove-Item -Recurse -Force $mockOutDir }
dotnet publish src/PacCollector.MockDevice/PacCollector.MockDevice.csproj `
    -c $Configuration -r $Runtime --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $mockOutDir

# renombrar para que tenga el nombre versionado
$mockExe = Join-Path $mockOutDir "pac-mock.exe"
if (Test-Path $mockExe) {
    $versionedExe = Join-Path $releasesDir "pac-mock-$Version-$Runtime.exe"
    Copy-Item $mockExe $versionedExe -Force
    Write-Host "pac-mock listo: $versionedExe"
}
