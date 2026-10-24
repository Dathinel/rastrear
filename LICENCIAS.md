# Licencias

Qué lleva Rastrear y con qué licencia. Ningún binario ni modelo de terceros va en este repositorio: los modelos se descargan con huella fijada.

## Código

| Pieza | Licencia | Cómo entra |
|---|---|---|
| Código propio (`src/`, `build/`) | GPL-3.0-only | Este repositorio |
| Rodaje (`externo/rodaje`, submódulo fijado a un commit) | GPL-3.0-only con los términos adicionales de la sección 7 de su `LICENSE` | Se compilan sus proyectos; el código no se copia |
| SDK de .NET 10 y runtime de Native AOT | MIT | `.entorno\`, con huella SHA-512 |
| FFmpeg, compilación LGPL de Rodaje | LGPL v3 | `.entorno\ffmpeg`, con huella SHA-256; se distribuye como DLL y ejecutables sin renombrar |
| ONNX Runtime con DirectML | MIT | Dentro de modulo-vision |

## Módulos y modelos (se descargan aparte)

| Módulo | Modelo | Licencia del modelo |
|---|---|---|
| modulo-vision (GPLv3) | MODNet | Apache-2.0 |
| | BiRefNet_lite fp16 | MIT |
| | YuNet 2023mar | MIT |
| | RT-DETRv2-R18 | Apache-2.0 |

El seguimiento de puntos y la estabilización son código de Rodaje (`src/Rodaje.Analysis`), sin modelos. El detalle de cada modelo (URL fijada a un commit, tamaño y SHA-256) está en `externo/rodaje/modules/Rodaje.Module.Vision/paquete.json`.
