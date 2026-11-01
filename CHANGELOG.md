# Historial de cambios de Rastrear

## [Sin publicar]

### Añadido
- La ventana recuerda la última carpeta (los diálogos de abrir y guardar se abren en ella), la última herramienta y los parámetros de cada una, en `%LOCALAPPDATA%\Dathinel\Rastrear\preferencias.json`.
- Se puede soltar un video en cualquier parte de la ventana, y varios a la vez: quedan en cola y «Ejecutar» aplica la herramienta a todos, uno tras otro, con progreso por archivo y global; el resultado de cada uno se escribe junto al video de entrada (`clip-fondo.webm`, `clip-seguimiento.csv`…) sin pisar archivos. «Cancelar» mata además los procesos de ffmpeg y el módulo de visión que sigan vivos (`ChildProcessRegistry`) y avisa si alguno no salió.
- Botón «Después» en el reproductor: vista previa del primer cuadro con los parámetros actuales, que se rehace al cambiarlos; para «Reencuadrar» el recorte centrado, para «Quitar fondo» la máscara de ese cuadro pedida al módulo, sobre un tablero de ajedrez o en grises (`FramePreview`, `VisionService.MatteFrameAsync`). Las demás herramientas lo dicen: dependen del video entero.
- Botones «Abrir carpeta» (el Explorador con el resultado seleccionado) y «Copiar ruta» (al portapapeles) al terminar (`ResultActions`).
- Aviso de versión nueva: al arrancar, como mucho una vez cada 24 h y sin bloquear la ventana, se consulta la última release pública de GitHub (`UpdateChecker`, `User-Agent: Rastrear/<versión>`, 5 s de tiempo límite; sin red o con una respuesta rara, silencio). Si hay una versión mayor, una franja bajo la cabecera dice «Rastrear 1.1.0 disponible» con «Ver novedades» (la página de la release), «Abrir el Lanzador» si está en el PC y «Más tarde» (no vuelve a avisar de esa versión). En «Acerca de», el interruptor «Avisar cuando haya una versión nueva» (activado por omisión) y el enlace «Buscar actualizaciones», que consulta al momento y también dice cuando no hay nada nuevo. Todo en `preferencias.json` (`avisarVersionNueva`, `ultimaConsultaVersion`, `versionDescartada`).
- Preferencia «Carpeta de salida» en el panel de la derecha («Elegir…» y «Junto al video»): los resultados de todas las herramientas van a esa carpeta, con el mismo esquema de nombres y sin pisar; por omisión siguen junto a cada video (`carpetaSalida` en `preferencias.json`, `OutputNaming`). Si la carpeta se fue (una unidad extraíble), el error queda en ese archivo de la cola.
- Atajos: `Ctrl+O` abrir, `Ctrl+Enter` empezar, `Esc` cancelar (`KeyboardShortcuts`). Si falta el módulo de visión, «Quitar fondo», «Detectar» y «Reencuadrar» lo dicen en claro antes de empezar (`VisionModuleMissingException`) con un botón «Abrir el Lanzador» si está en el PC (`LauncherLocator`: `RASTREAR_LANZADOR`, `%LOCALAPPDATA%\Programs\Lanzador`, donde lo deja su instalador); la Cli sigue listando las rutas probadas.

### Cambiado
- «Después» arranca el módulo de visión una sola vez y lo deja vivo mientras el botón esté pulsado (`ModuleSession`, `VisionService.CreateSession`): cambiar de herramienta o de parámetro ya no lo reinicia; se cierra al soltar el botón, al ejecutar, al abrir otro video y al cerrar la ventana. Si una máscara falla o el módulo muere, la siguiente arranca otro.
- La cola solo admite herramientas sin objetivo: con «Seguir» (que pide un clic en cada video) al soltar varios se abre solo el primero y, al cambiar a «Seguir» con varios en cola, queda solo el video cargado; la ventana lo dice en cada caso (`BatchQueue.SupportsTool`, `Enqueue(paths, toolIndex)`, `TrimToTool`).
- «Exportar resultado…» copia el archivo resultante donde se elija y el resultado sigue donde se dejó: «Abrir carpeta» y «Copiar ruta» no cambian de archivo. El seguimiento sigue pudiendo exportarse como JSON.
- Por dentro: `ChildProcessRegistry` sube a `Rastrear.Core`; `ToolParameters` (dispositivo, calidad, objetivo, aspecto, modo) normaliza en un solo sitio lo que guardan las preferencias y lo que recibe el módulo; `VisionService.StartModuleAsync` es el único arranque del módulo; `AtomicFile` escribe `preferencias.json` y `ventana.json` por temporal y Move; las pruebas con carpeta temporal comparten `TemporaryFolderTest` y la de cancelar arranca un solo proceso sin hijos.
- Arreglos de la revisión de bloque (#4): cierre esperado de la sesión de vista previa antes de ejecutar y al abrir otro video (sin procesos duplicados ni huérfanos), robustez frente a carreras al soltar «Después» y mapeo centralizado de calidad y dispositivo en `ToolParameters`.

## [1.0.0] - 2026-10-24

### Añadido
- **Rastrear.App**: interfaz de usuario moderna en WinUI 3 compilada de antemano con Native AOT.
  - Selección de video mediante diálogo nativo o arrastrar y soltar.
  - Reproductor integrado con decodificación fluida vía FFmpeg (`PlayerFrameBuffer`).
  - Superposición visual interactiva (`ViewportCoordinateTransformer`) para marcar puntos y regiones en el visor.
  - Cinco operaciones de análisis con IA y procesamiento de video:
    - **Seguir**: seguimiento de puntos y regiones con trayectorias exportables a CSV y JSON.
    - **Quitar fondo**: generación de video con canal alfa (VP9 WebM / ProRes 4444) o máscara en escala de grises.
    - **Detectar**: detección de rostros (YuNet) y personas (RT-DETRv2) con cuadros delimitadores.
    - **Reencuadrar**: reencuadre inteligente (9:16, 1:1, 4:5) que sigue automáticamente al sujeto principal.
    - **Estabilizar**: estimación de movimiento de cámara y suavizado de trayectoria para eliminar temblores.
  - Controlador de tareas (`TaskExecutionController`) con seguimiento de progreso, cancelación inmediata y reporte de errores en español sin trazas técnicas.
  - Exportación de resultados a formatos de datos y video.
- **Rastrear.Cli**: herramienta de línea de comandos para automatización y scripts (`seguir`, `quitar-fondo`, `detectar`, `reencuadrar`, `estabilizar`).
- **Rastrear.Core**: núcleo de procesamiento sin dependencias de UI, conectividad por tubería con nombre (`Rodaje.Modules.Protocol`) al módulo de visión (`Rodaje.Module.Vision`) y análisis nativo de Rodaje (`Rodaje.Analysis`).
- **Identidad «radar»**: acento verde-cian que sigue al tema de Windows, Bahnschrift en títulos y Cascadia Mono en cifras, icono propio, retícula en el visor, progreso con tiempo restante y tic de «hecho»; la ventana recuerda tamaño y posición.
- **Búsqueda del módulo de visión** en orden: `RASTREAR_MODULOS`, lo instalado por el Lanzador (`%LOCALAPPDATA%\Dathinel\Comun\modulos\modulo-vision\<última versión>`), los módulos de Rodaje (`%LOCALAPPDATA%\Rodaje\complementos\modulos` y `RODAJE_MODULOS`) y `modulos\` junto al exe; si falta, el error lista las rutas probadas.
- **Distribución**: paquete zip autocontenido AOT win-x64 (26,5 MB), entrada de catálogo firmado de Rodaje (`catalogo-rastrear.json`), instalador Inno Setup por usuario (16,9 MB) y `SHA256SUMS`; 23 pruebas xUnit.
