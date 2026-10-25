# Rastrear

Herramienta de IA aparte de Rodaje, con interfaz propia. Repositorio público `Dathinel/rastrear`, GPL-3.0-only. El plan común de las tres herramientas está en `..\PLAN.md` (carpeta `Herramientas-IA`, que no se versiona).

## Cómo se trabaja aquí

- **.NET:** siempre con `build\dotnet.cmd <args>`; `build\preparar-entorno.ps1` deja el SDK y FFmpeg en `.entorno\`. Los dos llaman a los scripts de Rodaje del submódulo `externo/rodaje`: no se copian.
- **Código de Rodaje:** por `ProjectReference` a `externo/rodaje/src/...`, nunca copiado. Mover el commit fijado del submódulo es un commit propio.
- **Worktrees** en `.claude\worktrees\` (ignorado). Un commit por tarea y push.

## Estado (24-oct)

- Fase 0 cerrada: esqueleto, licencia y `src\Rastrear.slnx`.
- Fase 1 cerrada: `Rastrear.Core` (FFmpeg por tubería, `Rodaje.Analysis` para seguir y estabilizar, `Rodaje.Module.Vision` por el protocolo para detectar, quitar fondo y reencuadrar) y `Rastrear.Cli` con AOT (4,5 MB); 10 pruebas, 3 de ellas largas con `RODAJE_PRUEBAS_LARGAS=1` y el módulo publicado desde `externo/rodaje/modules/Rodaje.Module.Vision` (se pasa con `--modulo` o `RASTREAR_MODULOS`). Cifras en `docs/informe-fase-1.md`.
- Fase 2 cerrada (verificada por Claude): `Rastrear.App`, ventana WinUI 3 con Native AOT (7,8 MB) sobre `Rastrear.Core`: reproductor por ffmpeg (`Ui/PlayerFrameBuffer`), superposición, clic/arrastre → coordenadas del video (`Ui/ViewportCoordinateTransformer`), progreso y cancelación (`Ui/TaskExecutionController`), exportar; 18 pruebas. Admite `Rastrear.App.exe <video>`. Cifras y pendientes en `docs/informe-fase-2.md`; lo que juzga Daniel, en `docs/pruebas-a-mano.md`.
- Pulido visual (24-oct, rama `pulido-visual`): identidad «radar» (acento verde-cian, Bahnschrift en títulos, Cascadia Mono en cifras, icono mira+trayectoria en `assets/marca/`, tema que sigue a Windows, retícula en el visor, tic de «hecho», ventana que recuerda tamaño y posición en `%LOCALAPPDATA%\Dathinel\Rastrearentana.json`); 20 pruebas. Cifras en `docs/informe-pulido.md`; bloque «Identidad» en `docs/pruebas-a-mano.md`.
- **Siguiente:** correcciones de las pruebas a mano de Daniel; máscara de fondo semitransparente en el visor (pendiente de la fase 2); fases comunes de `..\PLAN.md`.
