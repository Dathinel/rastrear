# Prepara .entorno\ (SDK de .NET y FFmpeg LGPL con huella fijada) llamando al script de Rodaje del submódulo, sin copiarlo.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File build\preparar-entorno.ps1 [-Forzar]
# Si RODAJE_ENTORNO ya está definido se usa ese .entorno (por ejemplo el de Rodaje, para no bajar el SDK otra vez).
param([switch]$Forzar)

$ErrorActionPreference = 'Stop'
$Raiz = Split-Path -Parent $PSScriptRoot
$Script = Join-Path $Raiz 'externo\rodaje\build\preparar-entorno.ps1'

if (-not (Test-Path -LiteralPath $Script)) {
    git -C $Raiz submodule update --init --depth 1 externo/rodaje
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if (-not $env:RODAJE_ENTORNO) { $env:RODAJE_ENTORNO = Join-Path $Raiz '.entorno' }

& $Script -Forzar:$Forzar
exit $LASTEXITCODE
