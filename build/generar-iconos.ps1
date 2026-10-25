# Genera assets\marca\rastrear.ico (16, 20, 24, 32, 40, 48, 64, 128 y 256 px, PNG dentro del .ico) y rastrear-256.png desde los
# SVG de assets\marca con el ffmpeg (librsvg) de .entorno. De 16 a 32 px se usa rastrear-pequeno.svg. Los resultados se versionan.
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\generar-iconos.ps1
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$marca = Join-Path $raiz 'assets\marca'
$entorno = if ($env:RODAJE_ENTORNO) { $env:RODAJE_ENTORNO } else { Join-Path $raiz '.entorno' }
$ffmpeg = Join-Path $entorno 'ffmpeg\bin\ffmpeg.exe'
if (-not (Test-Path -LiteralPath $ffmpeg)) { throw "Falta $ffmpeg (corre build\preparar-entorno.ps1)." }
$tamanos = 16, 20, 24, 32, 40, 48, 64, 128, 256
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('rastrear-iconos-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

function Rasterizar([string]$svg, [int]$lado, [string]$png) {
    & $ffmpeg -hide_banner -v error -y -width $lado -height $lado -i $svg -frames:v 1 -pix_fmt rgba $png
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $png)) { throw "ffmpeg no pudo rasterizar $svg a $lado px." }
}

try {
    $pngs = foreach ($t in $tamanos) {
        $png = Join-Path $tmp "rastrear-$t.png"
        Rasterizar (Join-Path $marca $(if ($t -le 32) { 'rastrear-pequeno.svg' } else { 'rastrear.svg' })) $t $png
        $png
    }
    $datos = foreach ($p in $pngs) { , [IO.File]::ReadAllBytes($p) }
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter($ms)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    for ($i = 0; $i -lt $pngs.Count; $i++) {
        $l = if ($tamanos[$i] -ge 256) { 0 } else { $tamanos[$i] }
        $w.Write([byte]$l); $w.Write([byte]$l); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$datos[$i].Length); $w.Write([uint32]$offset)
        $offset += $datos[$i].Length
    }
    foreach ($d in $datos) { $w.Write($d) }
    $w.Flush()
    [IO.File]::WriteAllBytes((Join-Path $marca 'rastrear.ico'), $ms.ToArray())
    Copy-Item -LiteralPath (Join-Path $tmp 'rastrear-256.png') -Destination (Join-Path $marca 'rastrear-256.png') -Force
    Write-Host '  rastrear.ico, rastrear-256.png'
}
finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
