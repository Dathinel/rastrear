# Rastrear

Herramienta de IA aparte de Rodaje, con interfaz propia. Repositorio público `Dathinel/rastrear`, GPL-3.0-only. El plan común de las tres herramientas está en `..\PLAN.md` (carpeta `Herramientas-IA`, que no se versiona).

## Cómo se trabaja aquí

- **.NET:** siempre con `build\dotnet.cmd <args>`; `build\preparar-entorno.ps1` deja el SDK y FFmpeg en `.entorno\`. Los dos llaman a los scripts de Rodaje del submódulo `externo/rodaje`: no se copian.
- **Código de Rodaje:** por `ProjectReference` a `externo/rodaje/src/...`, nunca copiado. Mover el commit fijado del submódulo es un commit propio.
- **Worktrees** en `.claude\worktrees\` (ignorado). Un commit por tarea y push.

## Estado (24-oct)

- Fase 0 cerrada: esqueleto, licencia y `src\Rastrear.slnx`.
- Fase 1 cerrada: `Rastrear.Core` (FFmpeg por tubería, `Rodaje.Analysis` para seguir y estabilizar, `Rodaje.Module.Vision` por el protocolo para detectar, quitar fondo y reencuadrar) y `Rastrear.Cli` con AOT (4,5 MB); 10 pruebas, 3 de ellas largas con `RODAJE_PRUEBAS_LARGAS=1` y el módulo publicado desde `externo/rodaje/modules/Rodaje.Module.Vision` (se pasa con `--modulo` o `RASTREAR_MODULOS`). Cifras en `docs/informe-fase-1.md`.
- **Siguiente:** fase 2, interfaz WinUI 3 sobre `Rastrear.Core` (`..\PLAN.md` y `..\encargos\`).
