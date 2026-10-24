# Rastrear

Seguimiento y visión por IA para video en Windows, en tu PC y sin conexión: seguir un punto, quitar el fondo, detectar rostros y personas, reencuadrar un video horizontal a vertical siguiendo al sujeto y estabilizar.

> En construcción: fase 1 cerrada (CLI con pruebas, cifras en `docs/informe-fase-1.md`); la interfaz llega en la fase 2.

## Cómo funciona

- Decodifica el video con FFmpeg (LGPL).
- El seguimiento de puntos y la estabilización usan el análisis de movimiento de Rodaje (código C# propio, sin modelos).
- Quitar el fondo, detectar y reencuadrar van por **modulo-vision** de Rodaje, un proceso aparte que habla por una tubería con nombre (protocolo de módulos v1): MODNet o BiRefNet_lite para el fondo, YuNet para rostros y RT-DETRv2 para personas, en ONNX Runtime con DirectML si hay GPU.
- Los modelos se bajan una sola vez desde el catálogo firmado de Rodaje y se comprueba su SHA-256.

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
