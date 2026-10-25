# Historial de cambios de Rastrear

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
