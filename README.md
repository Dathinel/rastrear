# Rastrear

Seguimiento y visión por IA para video en Windows, en tu PC y sin conexión: seguir un punto, quitar el fondo, detectar rostros y personas, reencuadrar un video horizontal a vertical siguiendo al sujeto y estabilizar.

> En construcción: fase 1 (CLI) y fase 2 (ventana WinUI 3 con Native AOT) cerradas; cifras en `docs/informe-fase-1.md` e `informe-fase-2.md`. Falta pulir la ventana con las pruebas a mano de Daniel (`docs/pruebas-a-mano.md`).

## Cómo funciona

- Decodifica el video con FFmpeg (LGPL).
- El seguimiento de puntos y la estabilización usan el análisis de movimiento de Rodaje (código C# propio, sin modelos).
- Quitar el fondo, detectar y reencuadrar van por **modulo-vision** de Rodaje, un proceso aparte que habla por una tubería con nombre (protocolo de módulos v1): MODNet o BiRefNet_lite para el fondo, YuNet para rostros y RT-DETRv2 para personas, en ONNX Runtime con DirectML si hay GPU.
- Los modelos se bajan una sola vez desde el catálogo firmado de Rodaje y se comprueba su SHA-256.

## Cómo se usa

`dist\Rastrear\Rastrear.App.exe [video]`: suelta o elige un video, elige la operación (seguir, quitar fondo, detectar, reencuadrar, estabilizar), marca el punto o la región con un clic o un arrastre sobre el cuadro, pulsa «Ejecutar» y exporta. `Rastrear.Cli.exe` hace lo mismo desde la consola. Los dos buscan FFmpeg y el módulo de visión en `RODAJE_ENTORNO` / `RASTREAR_MODULOS`.

## Cómo se ejecuta (desarrollo)

```
git clone --recursive https://github.com/Dathinel/rastrear
cd rastrear
powershell -NoProfile -ExecutionPolicy Bypass -File build\preparar-entorno.ps1
build\dotnet.cmd build src\Rastrear.slnx
```

El SDK de .NET y FFmpeg se instalan en `.entorno\` con huella fijada, sin tocar nada global. Los scripts son los de Rodaje (submódulo `externo/rodaje`, fijado a un commit). Para no bajar el SDK otra vez se puede apuntar `RODAJE_ENTORNO` al `.entorno` de Rodaje.

## Licencia

GPLv3, solo la versión 3 (`GPL-3.0-only`). Va con GPLv3 porque reutiliza código de Rodaje (el protocolo de módulos, entre otras piezas) y sus módulos de IA, que son GPLv3. Ver [LICENSE](LICENSE) y [LICENCIAS.md](LICENCIAS.md).
