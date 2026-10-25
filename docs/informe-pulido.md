# Pulido visual e identidad de Rastrear (24-oct-2026)

Encargo `pulido-visual.md` aplicado por Claude en la rama `pulido-visual`. El reproductor y la superposición conservan su lógica: solo cambia el aspecto.

## Qué cambió

- **Identidad «radar»:** acento verde-cian (`SystemAccentColor` y sus seis variantes en `App.xaml`, así botones, deslizadores, barra de progreso y foco lo heredan), fondo y paneles grises verdosos que siguen al tema de Windows (`RequestedTheme` quitado; diccionarios claro, oscuro y alto contraste), visor siempre oscuro. Título y cabeceras en **Bahnschrift** y cifras (metadatos, tiempo, objetivo, progreso) en **Cascadia Mono**: las dos vienen con Windows 10/11, sin fuente OFL que incluir. Icono propio: mira con trayectoria (`assets/marca/rastrear.svg`, versión pequeña para 16-32 px, `rastrear.ico` generado con `build/generar-iconos.ps1` sobre el ffmpeg de `.entorno`), en el exe, la barra de tareas y la ventana. Lema en la cabecera: «Sigue lo que importa».
- **Estado vacío:** mira con trayectoria punteada en XAML y dos frases que dicen qué hacer.
- **Progreso:** barra con el acento, etapa y «0:12 · quedan ~0:36» (`TaskExecutionController.FormatElapsedEstimate`, estimación a partir del 2 %); al terminar, tic verde que aparece con fundido y rebote (`Storyboard` en XAML) y «Hecho en 0:04».
- **Superposición:** retícula fina en sextos sobre el cuadro, objetivo con cruz de mira, trayectoria y cajas en el acento, marcador actual claro con borde verde, etiquetas de detección en mono sobre fondo oscuro, guía de reencuadre en blanco punteado.
- **Ritmo:** rejilla de 8 px (márgenes 24/16/8), paneles con radio 8 y `ThemeShadow`, un solo botón de acento («Ejecutar»).
- **Ventana:** mínimo 960×600 DIP (`OverlappedPresenter.PreferredMinimum*`), tamaño por defecto 1280×800, recuerda tamaño, posición y maximizado en `%LOCALAPPDATA%\Dathinel\Rastrear\ventana.json` (`Core/Ui/WindowPlacement`, que además encaja la ventana en el área de trabajo más cercana si el monitor cambió) y la barra de título sigue al tema de la app.

## Cifras

- `build src\Rastrear.slnx -c Release -warnaserror`: 0 advertencias. Publicación AOT: 0 avisos de recorte.
- `Rastrear.App.exe`: 7.835.648 bytes antes → 8.081.920 después (+3 %, animación, ventana y JSON de `ventana.json`; más 33 KB del `.ico` suelto).
- Pruebas: 20 de 20 (2 nuevas: formato de tiempos y `WindowPlacement`, con ida y vuelta al disco y archivo corrupto).
- Arranque (ventana visible sondeando `MainWindowHandle` desde fuera, PC con otras tareas): 267-301 ms en frío (primera ejecución tras publicar; antes, 393) y 188-220 ms en caliente (antes, 136); el exe mide 41-77 ms hasta `Application.Start`.
- Captura: `docs/capturas/pulido-vacio.png` (estado vacío) y `docs/capturas/pulido-video.png` (clip de prueba con objetivo marcado).

## Decisiones y qué queda

- Me ceñí a la propuesta del encargo; la única apartación: las fuentes son del sistema (Bahnschrift y Cascadia Mono) en vez de una OFL incluida, para no añadir 300 KB ni una licencia más.
- Con Native AOT, `AppWindow.Presenter is OverlappedPresenter` nunca acierta (el RCW no sale con el tipo derivado) y `AppWindow.Changed` no se recibía: el tamaño mínimo y el guardado fallaban en silencio en el exe publicado aunque funcionaban con JIT. Ahora se pregunta por `Kind` y `As<OverlappedPresenter>()`; comprobado con el exe publicado (cierra, reabre en el mismo sitio). Vale para Subtitular y Lanzador.
- Un solo icono: Rastrear no tiene tipo de archivo propio (exporta CSV, JSON y video), así que no hay icono de documento.
- La barra de progreso no interpola entre valores: con avisos por cuadro (cada 40 ms) ya se mueve seguida; si en operaciones lentas (módulos de IA) da saltos, se añade un `DoubleAnimation` sobre `Value`.
- Queda por juzgar a ojo (bloque «Identidad» de `docs/pruebas-a-mano.md`): si la retícula ayuda o distrae, y el rebote del tic.
