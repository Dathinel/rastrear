# Comprueba que los artefactos de dist\ están completos y cumplen con los requisitos.
# Uso:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\probar-distribucion.ps1 [-Version 1.0.0] [-Salida dist]
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$Salida = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$Raiz = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    $propsPath = Join-Path $Raiz 'Directory.Build.props'
    if (Test-Path -LiteralPath $propsPath) {
        $props = [xml](Get-Content -LiteralPath $propsPath -Raw -Encoding UTF8)
        $Version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
    }
    if (-not $Version) { throw 'No se pudo determinar la versión desde Directory.Build.props.' }
}

if (-not $Salida) {
    $Salida = Join-Path $Raiz 'dist'
}

Write-Host "Comprobando distribución de Rastrear $Version en $Salida..." -ForegroundColor Cyan

# 1. Comprobar zip
$zip = Join-Path $Salida "Rastrear-$Version-win-x64.zip"
if (-not (Test-Path -LiteralPath $zip)) { throw "Falta el zip de distribución: $zip" }
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$zipSize = (Get-Item -LiteralPath $zip).Length
Write-Host "  [OK] Zip encontrado: $zip ($([math]::Round($zipSize / 1MB, 2)) MB)" -ForegroundColor Green

# 2. Comprobar catálogo JSON
$catalogo = Join-Path $Salida 'catalogo-rastrear.json'
if (-not (Test-Path -LiteralPath $catalogo)) { throw "Falta el catálogo: $catalogo" }
$catData = Get-Content -LiteralPath $catalogo -Raw -Encoding UTF8 | ConvertFrom-Json

if ($catData.id -ne 'rastrear') { throw "El id en catalogo debe ser 'rastrear', obtenido '$($catData.id)'" }
if ($catData.version -ne $Version) { throw "La versión no coincide: $($catData.version) vs $Version" }
if ($catData.kind -ne 'app') { throw "El kind en catalogo debe ser 'app', obtenido '$($catData.kind)'" }
if ($catData.sha256 -ne $zipHash) { throw "La huella sha256 del catálogo no coincide con el zip real" }
if ($catData.size -ne $zipSize) { throw "El tamaño del catálogo no coincide con el zip real" }
if (-not ($catData.modulos -contains 'modulo-vision')) { throw "El catálogo debe declarar modulo-vision" }
Write-Host "  [OK] Catálogo JSON validado contra el zip y contrato SignedIndex" -ForegroundColor Green

# 3. Comprobar notas de versión
$notas = Join-Path $Salida 'notas-version.md'
if (-not (Test-Path -LiteralPath $notas)) { throw "Faltan las notas de versión: $notas" }
$notasContenido = Get-Content -LiteralPath $notas -Raw -Encoding UTF8
if ($notasContenido.Trim().Length -lt 20) { throw "Notas de versión vacías o demasiado cortas" }
Write-Host "  [OK] Notas de versión encontradas" -ForegroundColor Green

# 4. Comprobar SHA256SUMS
$sumas = Join-Path $Salida 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $sumas)) { throw "Falta SHA256SUMS: $sumas" }
$lineasSumas = Get-Content -LiteralPath $sumas -Encoding UTF8 | Where-Object { $_.Trim() }
foreach ($linea in $lineasSumas) {
    if ($linea -match '^([0-9a-f]{64})\s+(.+)$') {
        $hashEsperado = $Matches[1]
        $archivoNom = $Matches[2].Trim()
        $rutaArch = Join-Path $Salida $archivoNom
        if (-not (Test-Path -LiteralPath $rutaArch)) { throw "Archivo en SHA256SUMS no existe: $archivoNom" }
        $hashReal = (Get-FileHash -LiteralPath $rutaArch -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hashEsperado -ne $hashReal) { throw "Huella incorrecta en SHA256SUMS para $archivoNom" }
    }
}
Write-Host "  [OK] SHA256SUMS verificado ($($lineasSumas.Count) archivos verificados)" -ForegroundColor Green

# 5. Comprobar instalador si existe
$instalador = Join-Path $Salida "Rastrear-$Version-instalador.exe"
if (Test-Path -LiteralPath $instalador) {
    $instSize = (Get-Item -LiteralPath $instalador).Length
    Write-Host "  [OK] Instalador verificado ($([math]::Round($instSize / 1MB, 2)) MB)" -ForegroundColor Green
}

Write-Host "Todas las comprobaciones de distribución pasaron exitosamente." -ForegroundColor Cyan
