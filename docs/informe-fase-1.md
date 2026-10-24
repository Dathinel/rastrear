# Fase 1: CLI de seguimiento, fondo, detección, reencuadre y estabilización (24-oct-2026)

Hecha por agy (gemini-3.8-flash) en unos 15 minutos; verificada por Claude (código completo leído, cifras medidas de nuevo).

- `Rastrear.Core` (`IsAotCompatible`): `FfmpegTool` (ffprobe + ffmpeg → BGRA crudo por tubería; codifica h264_mf, VP9 con alfa, ProRes 4444), `FollowService` (`PointTracker`/`RegionTracker` de `Rodaje.Analysis`), `StabilizeService` (`MotionEstimator` + `CameraPath` de `Rodaje.Analysis`; la deformación bilineal de cada cuadro es propia) y `VisionService` (`ModuleProcess` + `SharedMemoryRegion` contra `Rodaje.Module.Vision`: `detectar`, `quitar-fondo`, `reencuadre`). JSON con generadores de código, sin reflexión.
- `Rastrear.Cli seguir|quitar-fondo|detectar|reencuadrar|estabilizar <video> [--salida] [--ffmpeg] [--modulo] [--device]`; progreso en stderr, Ctrl+C cancela. Salida 0 bien, 1 error del usuario, 2 error de módulo o sistema; mensajes en español sin trazas.

## Cifras

- `build\dotnet.cmd build src\Rastrear.slnx -c Release -warnaserror`: 0 advertencias. AOT sin avisos de recorte: `Rastrear.Cli.exe` 4.519.424 bytes.
- Pruebas rápidas (7 de 10; las 3 de Vision se omiten sin `RODAJE_PRUEBAS_LARGAS=1`): `seguir` error medio 0,018 px, máximo 0,039 px en 30 cuadros de un círculo sintético (límite 2 px); región 0,253 px en el último cuadro; `estabilizar` varianza del movimiento −99,9 % medida al redecodificar el video de salida (límite −50 %); Echo: llamada con progreso, cancelación en 16 ms, módulo que muere detectado en 32 ms con `ModuleDisconnectedException`.
- Pruebas largas con el módulo Vision publicado (CPU): IoU de la máscara 0,9981 sobre `corpus\vision\retrato.png` compuesto en gris (límite 0,90); rostro detectado (confianza 0,945); `reencuadrar` 16:9 → 9:16 sobre un clip sintético de 20 cuadros hecho con el retrato del corpus: «dentro» 100 % según el módulo y 100 % según una detección independiente sobre el video recortado (límite 95 %). 10 de 10 en 7,6 s.
- Exe publicado a mano: sin argumentos → uso y salida 1; archivo inexistente → salida 1 con mensaje en español; `seguir` sobre `testsrc` de 3 s a 25 fps → CSV con 75 filas, una por cuadro; `estabilizar` y `detectar` → salida 0.

## Pendiente

- No hay clip de video en el corpus: el reencuadre se mide sobre un clip sintético con el retrato. Añadir uno real cuando exista.
- Fase 2: interfaz WinUI 3 sobre `Rastrear.Core`.
