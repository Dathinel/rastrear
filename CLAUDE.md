# Rastrear

Herramienta de IA aparte de Rodaje, con interfaz propia. Repositorio público `Dathinel/rastrear`, GPL-3.0-only. El plan común de las tres herramientas está en `..\PLAN.md` (carpeta `Herramientas-IA`, que no se versiona).

## Cómo se trabaja aquí

- **.NET:** siempre con `build\dotnet.cmd <args>`; `build\preparar-entorno.ps1` deja el SDK y FFmpeg en `.entorno\`. Los dos llaman a los scripts de Rodaje del submódulo `externo/rodaje`: no se copian.
- **Código de Rodaje:** por `ProjectReference` a `externo/rodaje/src/...`, nunca copiado. Mover el commit fijado del submódulo es un commit propio.
- **Worktrees** en `.claude\worktrees\` (ignorado). Un commit por tarea y push.

## Estado (24-oct)

- Fase 0 cerrada: esqueleto, licencia y `src\Rastrear.slnx` con un `Rastrear.Cli` vacío que compila y publica con AOT.
- **Siguiente:** fase 1, la CLI que hace el trabajo, con pruebas (`..\PLAN.md` y `..\encargos\`).
