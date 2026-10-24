# Fase 2: ventana WinUI 3 con Native AOT sobre Rastrear.Core (24-oct-2026)

Hecha por agy (gemini-3.8-flash) en unos 15 minutos; verificada por Claude (código completo leído, cifras medidas de nuevo, dos fallos corregidos).

- `Rastrear.App`: soltar o elegir video (diálogos COM por vtable, sin MSIX), reproductor (`WriteableBitmap` con BGRA de ffmpeg por `Ui/PlayerFrameBuffer`, deslizador, reproducir/pausar, paso a paso), superposición (punto/región elegidos, trayectoria, cajas de detección, guía de recorte 9:16, 1:1 y 4:5), selector de las cinco operaciones, barra de progreso, cancelar y exportar (CSV/JSON o copia del video). Llama a `FollowService`, `VisionService` y `StabilizeService` de la fase 1 con su `IProgress` y su `CancellationToken`; nada de análisis en la App. Admite `Rastrear.App.exe <video>`.
- Lógica sin WinUI en `Rastrear.Core/Ui`: `PlayerTimeModel`, `ViewportCoordinateTransformer` (clic ↔ píxel del video con barras laterales o superiores), `OverlayGeometry`, `TaskExecutionController` (estado, cancelación, errores en español sin traza), `PlayerFrameBuffer`; 8 pruebas xUnit nuevas.

## Corregido en la verificación

- `Program.cs` no instalaba `DispatcherQueueSynchronizationContext`: tras el primer `await`, la ventana tocaba la interfaz desde otro hilo y cargar un video fallaba en silencio (ningún cuadro, ningún aviso). Con el arreglo, el video carga y reproduce.
- El cuadro se copiaba al `IBuffer` con una interfaz `[ComImport]`, COM clásico que Native AOT no admite; ahora `CopyTo(PixelBuffer)` de CsWinRT, como en Rodaje.
- Los videos intermedios se escribían junto al exe; ahora en la carpeta temporal y «Exportar» los copia a donde se elija. Quitadas las cifras de arranque no medidas de `pruebas-a-mano.md`.

## Cifras (medidas por Claude, PC con otras compilaciones en marcha)

- `build src\Rastrear.slnx -c Release -warnaserror`: 0 advertencias. Publicación AOT: 0 avisos de recorte; `Rastrear.App.exe` 7.835.648 bytes (`dist\Rastrear\`, junto a `Rastrear.Cli.exe`, 4.519.424).
- Pruebas: 18 de 18, dos pasadas (2 s y 5 s).
- Arranque: ventana visible a los 393 ms en frío y 136 ms en caliente (`MainWindowHandle` sondeado cada 50 ms desde fuera); el propio exe mide 49-216 ms hasta `Application.Start`. Proceso vivo 12 s, sin archivo de error.
- Con un `testsrc` de 640×360, 25 fps y 3 s por UI Automation: metadatos «640×360 · 25 fps · 00:03.00», cuadro 1/75 al abrir; reproducir 1,5 s → cuadro 33/75 (decodificación por ffmpeg más allá de los 30 precargados); pausar, paso adelante → 35/75; «Ejecutar» sin objetivo → aviso en español en la `InfoBar`.

## Pendiente

- Máscara de fondo semitransparente sobre el cuadro para `quitar-fondo` (criterio 2): el módulo entrega un video, no máscaras por cuadro; hoy el resultado solo se exporta.
- Sin probar a mano: clic/arrastre, ejecución completa de las cinco operaciones con módulos reales y cancelación en la ventana (`docs/pruebas-a-mano.md`).
