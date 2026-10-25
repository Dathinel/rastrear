# Rastrear

Seguimiento y visión por IA para video en Windows, en tu PC y sin conexión: seguir un punto, quitar el fondo, detectar rostros y personas, reencuadrar un video horizontal a vertical siguiendo al sujeto y estabilizar.

> Fase 1 (CLI), fase 2 (ventana WinUI 3 Native AOT) y fase 3 (distribución AOT, catálogo e instalador) completadas. Cifras en `docs/informe-fase-1.md`, `informe-fase-2.md` e `informe-fase-3.md`.

## Cómo funciona

- Decodifica el video con FFmpeg (LGPL).
- El seguimiento de puntos y la estabilización usan el análisis de movimiento de Rodaje (código C# propio, sin modelos).
- Quitar el fondo, detectar y reencuadrar van por **modulo-vision** de Rodaje, un proceso aparte que habla por una tubería con nombre (protocolo de módulos v1): MODNet o BiRefNet_lite para el fondo, YuNet para rostros y RT-DETRv2 para personas, en ONNX Runtime con DirectML si hay GPU.
- Los modelos se bajan una sola vez desde el catálogo firmado de Rodaje y se comprueba su SHA-256.

## Cómo se usa

`Rastrear.App.exe [video]`: suelta o elige un video, elige la operación (seguir, quitar fondo, detectar, reencuadrar, estabilizar), marca el punto o la región con un clic o un arrastre sobre el cuadro, pulsa «Ejecutar» y exporta. `Rastrear.Cli.exe` hace lo mismo desde la consola. Los dos buscan FFmpeg y el módulo de visión en `RODAJE_ENTORNO` / `RASTREAR_MODULOS`.

## Distribución y empaquetado

Para compilar de punta a punta y empaquetar la versión distribuible:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build\distribuir.ps1
```

Genera en `dist\`:
- `Rastrear-<versión>-win-x64.zip`: paquete autocontenido AOT (App, Cli, `herramienta.json`, licencias y guía).
- `Rastrear-<versión>-instalador.exe`: instalador por usuario compilado con Inno Setup 7.
- `catalogo-rastrear.json`: entrada `app` para el catálogo firmado de Rodaje con el tamaño y la huella SHA-256 del zip.
- `SHA256SUMS`: lista de comprobación de integridad para todos los archivos generados.
- `notas-version.md`: notas de la release extraídas de `CHANGELOG.md`.

### Catálogo firmado de Rodaje

`build\catalogo-entrada.ps1` deja la entrada lista en `dist\catalogo-rastrear.json` sin firmar. La firma es potestad de Daniel, quien la incorpora al índice oficial ejecutando `build\firmar-indice.ps1` en el repositorio privado `Dathinel/rodaje-catalogo`.

## Cómo se ejecuta (desarrollo)

```
git clone --recursive https://github.com/Dathinel/rastrear
cd rastrear
powershell -NoProfile -ExecutionPolicy Bypass -File build\preparar-entorno.ps1
build\dotnet.cmd build src\Rastrear.slnx
```

El SDK de .NET y FFmpeg se instalan en `.entorno\` con huella fijada, sin tocar nada global. Los scripts son los de Rodaje (submódulo `externo/rodaje`, fijado a un commit). Para no bajar el SDK otra vez se puede apuntar `RODAJE_ENTORNO` al `.entorno` de Rodaje.

## Licencia

GPLv3, solo la versión 3 (`GPL-3.0-only`). Va con GPLv3 porque reutiliza código de Rodaje (el protocolo de módulos, entre otras piezas) y sus módulos de IA, que son GPLv3. Ver [LICENSE](LICENSE), [LICENCIAS.md](LICENCIAS.md) y [docs/legal.md](docs/legal.md).
