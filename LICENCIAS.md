# Licencias

Qué lleva Rastrear y con qué licencia. Ningún binario ni modelo de terceros va en este repositorio: los modelos se descargan con huella fijada.

## Código

| Pieza | Licencia | Cómo entra |
|---|---|---|
| Código propio (`src/`, `build/`) | GPL-3.0-only | Este repositorio |
| Rodaje (`externo/rodaje`, submódulo fijado a un commit) | GPL-3.0-only con los términos adicionales de la sección 7 de su `LICENSE` | Se compilan sus proyectos; el código no se copia |
| SDK de .NET 10 y runtime de Native AOT | MIT | `.entorno\`, con huella SHA-512 |
| Windows App SDK 2.5.1 (WinUI 3): `Microsoft.WindowsAppSDK.WinUI` 2.3.9, `.Foundation` 2.3.12, `.InteractiveExperiences` 2.1.9 | Términos de licencia de software de Microsoft para el Windows App SDK (no MIT); detalle y pendiente para el abogado en el `LICENCIAS.md` de Rodaje | Unas 240 DLL, `.pri`, `.winmd` y carpetas de idioma (unos 55 MB sin comprimir) junto a `Rastrear.App.exe` en el zip |
| Microsoft.Web.WebView2 (dependencia de WinUI) | Propia de Microsoft, de tipo BSD de 3 cláusulas | `Microsoft.Web.WebView2.Core.dll` y `WebView2Loader.dll` en el zip |
| FFmpeg, compilación LGPL de Rodaje | LGPL v3 | `.entorno\ffmpeg`, con huella SHA-256; se distribuye como DLL y ejecutables sin renombrar |
| ONNX Runtime con DirectML | MIT | Dentro de modulo-vision |

Atribución que exige el término 1 de la licencia de Rodaje: **Basado en el editor de video de código abierto de Dathinel / Rodaje (https://github.com/Dathinel/rodaje)**.

## Módulos y modelos (se descargan aparte)

| Módulo | Modelo | Licencia del modelo |
|---|---|---|
| modulo-vision (GPLv3) | MODNet | Apache-2.0 |
| | BiRefNet_lite fp16 | MIT |
| | YuNet 2023mar | MIT |
| | RT-DETRv2-R18 | Apache-2.0 |

El seguimiento de puntos y la estabilización son código de Rodaje (`src/Rodaje.Analysis`), sin modelos. El detalle de cada modelo (URL fijada a un commit, tamaño y SHA-256) está en `externo/rodaje/modules/Rodaje.Module.Vision/paquete.json`.
