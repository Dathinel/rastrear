# Arma la distribución de Rastrear:
#   1. Publica Rastrear.App y Rastrear.Cli con Native AOT win-x64.
#   2. Arma dist\Rastrear-<versión>-win-x64.zip autocontenido (App, Cli, LICENSE, LICENCIAS.md, README.txt, herramienta.json).
#   3. Opcional: compila dist\Rastrear-<versión>-instalador.exe con Inno Setup si está disponible.
#   4. Genera dist\catalogo-rastrear.json con build\catalogo-entrada.ps1.
#   5. Genera dist\notas-version.md desde CHANGELOG.md.
#   6. Genera dist\SHA256SUMS con los hashes SHA-256 de los artefactos.
#
# Uso:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\distribuir.ps1 [-Version 1.0.0] [-Salida dist] [-SinPublicar] [-SinInstalador]
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$Salida = '',
    [switch]$SinPublicar,
    [switch]$SinInstalador
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$Raiz = Split-Path -Parent $PSScriptRoot
$Utf8 = New-Object System.Text.UTF8Encoding($false)

# 1. Determinar versión
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

$Etiqueta = "v$Version"
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Distribución de Rastrear $Version ($Etiqueta)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# Asegurar carpeta de salida
if (-not (Test-Path -LiteralPath $Salida)) {
    New-Item -ItemType Directory -Force -Path $Salida | Out-Null
}

$stagingDir = Join-Path $Salida 'staging'
$appStaging = Join-Path $stagingDir 'app'
$cliStaging = Join-Path $stagingDir 'cli'
$paqueteDir = Join-Path $stagingDir "Rastrear-$Version"

# Asegurar entorno RODAJE_ENTORNO
if (-not $env:RODAJE_ENTORNO) {
    $candidatosEntorno = @(
        (Join-Path $Raiz '.entorno'),
        (Join-Path $Raiz 'externo\rodaje\.entorno')
    )
    foreach ($cand in $candidatosEntorno) {
        if (Test-Path -LiteralPath $cand) {
            $env:RODAJE_ENTORNO = $cand
            break
        }
    }
}
Write-Host "Entorno .NET/herramientas: $env:RODAJE_ENTORNO" -ForegroundColor Gray

$dotnetCmd = Join-Path $Raiz 'build\dotnet.cmd'

# 2. Publicación Native AOT
if (-not $SinPublicar) {
    Write-Host "`n[1/5] Publicando Rastrear.Cli con Native AOT..." -ForegroundColor Yellow
    if (Test-Path -LiteralPath $cliStaging) { Remove-Item -LiteralPath $cliStaging -Recurse -Force }
    & $dotnetCmd publish (Join-Path $Raiz 'src\Rastrear.Cli\Rastrear.Cli.csproj') -c Release -r win-x64 -o $cliStaging
    if ($LASTEXITCODE -ne 0) { throw "Error al publicar Rastrear.Cli." }

    Write-Host "`n[2/5] Publicando Rastrear.App con Native AOT..." -ForegroundColor Yellow
    if (Test-Path -LiteralPath $appStaging) { Remove-Item -LiteralPath $appStaging -Recurse -Force }
    & $dotnetCmd publish (Join-Path $Raiz 'src\Rastrear.App\Rastrear.App.csproj') -c Release -r win-x64 -o $appStaging
    if ($LASTEXITCODE -ne 0) { throw "Error al publicar Rastrear.App." }
} else {
    Write-Host "`n[1-2/5] Omitiendo publicación (-SinPublicar)..." -ForegroundColor Gray
    if (-not (Test-Path -LiteralPath (Join-Path $appStaging 'Rastrear.App.exe'))) {
        throw "No se encontró $appStaging\Rastrear.App.exe (quita -SinPublicar)."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $cliStaging 'Rastrear.Cli.exe'))) {
        throw "No se encontró $cliStaging\Rastrear.Cli.exe (quita -SinPublicar)."
    }
}

# 3. Armar carpeta autocontenida de distribución
Write-Host "`n[3/5] Preparando paquete autocontenido..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $paqueteDir) { Remove-Item -LiteralPath $paqueteDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $paqueteDir | Out-Null

# Copiar archivos de Rastrear.App (exe, App SDK, pri, recursos); -Exclude de Get-ChildItem no filtra hijos sin -Recurse, así que se borra después
Copy-Item -Path (Join-Path $appStaging '*') -Destination $paqueteDir -Recurse -Force
Get-ChildItem -LiteralPath $paqueteDir -Recurse -File | Where-Object { $_.Extension -in '.pdb', '.xml' } | Remove-Item -Force

# Copiar Rastrear.Cli.exe
Copy-Item -LiteralPath (Join-Path $cliStaging 'Rastrear.Cli.exe') -Destination $paqueteDir -Force

# Copiar LICENSE y LICENCIAS.md
Copy-Item -LiteralPath (Join-Path $Raiz 'LICENSE') -Destination $paqueteDir -Force
Copy-Item -LiteralPath (Join-Path $Raiz 'LICENCIAS.md') -Destination $paqueteDir -Force

# herramienta.json para el Lanzador
$manifiestoHerramienta = [ordered]@{
    id = "rastrear"
    version = $Version
    modulos = @("modulo-vision")
}
[System.IO.File]::WriteAllText((Join-Path $paqueteDir 'herramienta.json'), ($manifiestoHerramienta | ConvertTo-Json -Depth 4), $Utf8)

# README corto de uso
$readmeUso = @"
Rastrear $Version - Seguimiento y vision por IA para video en Windows
======================================================================

Basado en el editor de video de código abierto de Dathinel / Rodaje (https://github.com/Dathinel/rodaje).

Uso de Rastrear.App:
  Ejecuta Rastrear.App.exe (o Rastrear.App.exe <ruta-del-video>).
  - Selecciona un video (arrastra o usa el boton de examinar).
  - Selecciona la operacion: Seguir, Quitar fondo, Detectar, Reencuadrar o Estabilizar.
  - Haz clic o arrastra sobre el visor para marcar el objetivo o region.
  - Pulsa «Ejecutar» y al terminar pulsa «Exportar resultado...».

Uso de Rastrear.Cli:
  Rastrear.Cli.exe <comando> <video> [opciones]
  Comandos disponibles:
    seguir <video>        Sigue un punto o region y genera CSV o JSON de trayectorias.
    quitar-fondo <video>  Genera video con alfa o mascara en escala de grises.
    detectar <video>      Detecta rostros o personas y genera JSON de cuadros y cajas.
    reencuadrar <video>   Recorte inteligente que sigue al sujeto (por defecto 9:16).
    estabilizar <video>   Estabiliza el movimiento de camara mediante suavizado de trayectoria.

Requisitos:
  - Windows 10 (19041+) o Windows 11 de 64 bits (x64).
  - FFmpeg (LGPL) en el PATH o en la carpeta del entorno.
  - Para quitar fondo, deteccion y reencuadre: Rodaje.Module.Vision (modulo-vision)
    con sus modelos correspondientes, disponible a traves del catalogo de Rodaje.

Licencia:
  GNU General Public License v3 (GPL-3.0-only).
  Ver LICENSE y LICENCIAS.md para terminos de licencia de terceros.
"@
[System.IO.File]::WriteAllText((Join-Path $paqueteDir 'README.txt'), $readmeUso.Replace("`r`n", "`n").Replace("`n", "`r`n"), $Utf8)

# Comprimir a zip
$zipSalida = Join-Path $Salida "Rastrear-$Version-win-x64.zip"
Write-Host "Comprimiendo $zipSalida..." -ForegroundColor Gray
if (Test-Path -LiteralPath $zipSalida) { Remove-Item -LiteralPath $zipSalida -Force }

$archivoZip = [System.IO.Compression.ZipFile]::Open($zipSalida, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $longitudPrefijo = $paqueteDir.TrimEnd('\').Length + 1
    Get-ChildItem -LiteralPath $paqueteDir -Recurse -File | ForEach-Object {
        $rutaRelativa = $_.FullName.Substring($longitudPrefijo).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archivoZip, $_.FullName, $rutaRelativa, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $archivoZip.Dispose()
}

$zipSize = (Get-Item -LiteralPath $zipSalida).Length
Write-Host "Zip creado: $zipSalida ($([math]::Round($zipSize / 1MB, 2)) MB)" -ForegroundColor Green

# 4. Compilar instalador con Inno Setup si está disponible
$instaladorSalida = Join-Path $Salida "Rastrear-$Version-instalador.exe"
if (-not $SinInstalador) {
    Write-Host "`n[4/5] Buscando Inno Setup para compilar instalador..." -ForegroundColor Yellow
    $isccCandidatos = @(
        (Join-Path $env:RODAJE_ENTORNO 'innosetup-7.1.0\ISCC.exe'),
        (Join-Path $Raiz 'externo\rodaje\.entorno\innosetup-7.1.0\ISCC.exe'),
        (Join-Path $Raiz '.entorno\innosetup-7.1.0\ISCC.exe')
    )
    $iscc = $null
    foreach ($cand in $isccCandidatos) {
        if ($cand -and (Test-Path -LiteralPath $cand)) {
            $iscc = $cand
            break
        }
    }
    if (-not $iscc) {
        $cmdIscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
        if ($cmdIscc) { $iscc = $cmdIscc.Source }
    }

    if ($iscc) {
        Write-Host "Compilando instalador con $iscc..." -ForegroundColor Gray
        $licenciaTemp = Join-Path $stagingDir 'licencia-instalador.txt'
        $textoLicencia = @"
Rastrear $Version
Copyright (C) 2026 Dathinel - https://github.com/Dathinel/rastrear

Rastrear es software libre: GNU General Public License version 3 (GPL-3.0-only).
Basado en el editor de video de código abierto de Dathinel / Rodaje (https://github.com/Dathinel/rodaje).

======================================================================
$((Get-Content -LiteralPath (Join-Path $Raiz 'LICENSE') -Raw -Encoding UTF8).TrimEnd())
"@
        [System.IO.File]::WriteAllText($licenciaTemp, $textoLicencia.Replace("`r`n", "`n").Replace("`n", "`r`n"), $Utf8)

        $issPath = Join-Path $PSScriptRoot 'instalador\Rastrear.iss'
        & $iscc '/Q' "/DVersion=$Version" "/DOrigen=$paqueteDir" "/DLicencia=$licenciaTemp" "/O$Salida" $issPath
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $instaladorSalida)) {
            $instSize = (Get-Item -LiteralPath $instaladorSalida).Length
            Write-Host "Instalador creado: $instaladorSalida ($([math]::Round($instSize / 1MB, 2)) MB)" -ForegroundColor Green
        } else {
            Write-Warning "Inno Setup fallo con codigo $LASTEXITCODE."
        }
    } else {
        Write-Host "Inno Setup no encontrado: se omite instalador." -ForegroundColor Gray
    }
}

# 5. Generar entrada de catálogo firmado (build\catalogo-entrada.ps1)
Write-Host "`n[5/5] Generando entrada de catálogo y notas de versión..." -ForegroundColor Yellow
$catalogoScript = Join-Path $PSScriptRoot 'catalogo-entrada.ps1'
& powershell -NoProfile -ExecutionPolicy Bypass -File $catalogoScript -Version $Version -Zip $zipSalida -Salida (Join-Path $Salida 'catalogo-rastrear.json')

# 6. Extraer notas de versión desde CHANGELOG.md
$changelogPath = Join-Path $Raiz 'CHANGELOG.md'
$notasTexto = ""
if (Test-Path -LiteralPath $changelogPath) {
    $lineas = Get-Content -LiteralPath $changelogPath -Encoding UTF8
    $capturando = $false
    $lineasNotas = New-Object System.Collections.Generic.List[string]
    foreach ($l in $lineas) {
        if ($l -match "^##\s+\[$([regex]::Escape($Version))\]") {
            $capturando = $true
            continue
        }
        if ($capturando -and $l -match '^##\s+\[') {
            break
        }
        if ($capturando) {
            $lineasNotas.Add($l)
        }
    }
    $notasTexto = ($lineasNotas -join "`n").Trim()
}
if (-not $notasTexto) {
    $notasTexto = @"
# Rastrear $Version

Herramienta de seguimiento y visión con IA para video en Windows (Native AOT).
Ver CHANGELOG.md para el detalle completo de cambios.
"@
}
# Término 2 de la licencia de Rodaje: aviso prominente en el canal de descarga (estas notas son la descripción de la release)
$notasTexto = "**Software libre y gratuito. Basado en el editor de video de código abierto de Dathinel / Rodaje (https://github.com/Dathinel/rodaje).**`n`n$notasTexto"

$rutaNotas = Join-Path $Salida 'notas-version.md'
$rutaNotasVersion = Join-Path $Salida "Rastrear-$Version-notas.md"
[System.IO.File]::WriteAllText($rutaNotas, $notasTexto, $Utf8)
[System.IO.File]::WriteAllText($rutaNotasVersion, $notasTexto, $Utf8)
Write-Host "Notas de versión guardadas en $rutaNotas" -ForegroundColor Green

# 7. Generar SHA256SUMS
$archivosParaSumas = @(
    "Rastrear-$Version-win-x64.zip",
    "Rastrear-$Version-instalador.exe",
    "catalogo-rastrear.json",
    "notas-version.md"
) | Where-Object { Test-Path -LiteralPath (Join-Path $Salida $_) }

$sumasLineas = foreach ($nombre in $archivosParaSumas) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $Salida $nombre) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $nombre"
}

$rutaSumas = Join-Path $Salida 'SHA256SUMS'
$rutaSumasTxt = Join-Path $Salida 'SHA256SUMS.txt'
[System.IO.File]::WriteAllText($rutaSumas, (($sumasLineas -join "`n") + "`n"), $Utf8)
[System.IO.File]::WriteAllText($rutaSumasTxt, (($sumasLineas -join "`n") + "`n"), $Utf8)
Write-Host "SHA256SUMS generado con $($archivosParaSumas.Count) archivos." -ForegroundColor Green

Write-Host "`nDistribución completada exitosamente en $Salida" -ForegroundColor Cyan
