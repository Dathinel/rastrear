# Guía de pruebas manuales para Rastrear.App (Fase 2)

Esta guía describe paso a paso las comprobaciones visuales y de interacción que solo Daniel puede juzgar en la interfaz WinUI 3 Native AOT de `Rastrear.App`.

El ejecutable se encuentra listo para usar en:
`dist\Rastrear\Rastrear.App.exe`

---

## 1. Arranque en frío y carga de video

1. Haz doble clic en `dist\Rastrear\Rastrear.App.exe` (o ábrelo con un video: `Rastrear.App.exe clip.mp4`).
   - **Qué juzgar:** Comprueba la inmediatez de la apertura (cifras medidas en `informe-fase-2.md`).
2. **Arrastrar y soltar:** Toma cualquier video (`.mp4`, `.mov`, `.webm`, `.mkv`) desde el explorador y suéltalo en el área central.
   - Alternativa: Pulsa el botón **«Elegir video…»** en la barra superior.
   - **Qué juzgar:** En cuanto se suelta o elige, el cartel vacío desaparece, la barra superior muestra los metadatos (nombre, resolución, fps y duración), el contador de abajo el cuadro actual y el total, y el primer cuadro se visualiza nítido y centrado.

---

## 2. Fluidez del reproductor

1. Pulsa **«▶»**: el video comienza a reproducirse cuadro a cuadro.
2. Pulsa **«⏸»**: se pausa inmediatamente.
3. Arrastra el **deslizador de tiempo** horizontal hacia adelante y hacia atrás.
   - **Qué juzgar:** La fluidez con la que se actualiza la imagen y la sincronización del contador de tiempo (`mm:ss.cc`) y cuadro actual (`cuadro N / Total`).
4. Pulsa los botones de paso individual **«⏮»** y **«⏭»** para avanzar o retroceder cuadro a cuadro.

---

## 3. Precisión del clic y arrastre (Selección de objetivo para «Seguir»)

1. Con la operación **«Seguir (Punto o región)»** seleccionada en el panel derecho:
2. **Modo Punto:**
   - Deja marcado el selector «Punto».
   - Haz clic sobre un elemento visible del video (ej. la nariz de un rostro, un botón o un objeto en movimiento).
   - **Qué juzgar:** El marcador circular cian debe quedar centrado exactamente donde apuntó el cursor, sin desplazamiento por barras negras laterales o superiores (uniform scaling). La etiqueta derecha muestra las coordenadas nativas exactas en píxeles.
3. **Modo Región:**
   - Marca «Región».
   - Haz clic y arrastra sobre el video formando una caja alrededor del objeto.
   - **Qué juzgar:** Durante el arrastre se dibuja la caja punteada y al soltar queda fijado el recuadro con sus dimensiones exactas (`X, Y, Ancho×Alto`).

---

## 4. Superposición visual

1. **Trayectoria (`Seguir`):**
   - Con un punto o región seleccionada, pulsa **«Ejecutar»**.
   - Tras el análisis, reproduce el video o mueve el deslizador:
   - **Qué juzgar:** La línea continua amarilla traza la trayectoria recorrida y el marcador rojo señala la posición exacta en el cuadro actual. La línea no parpadea ni se deforma al cambiar el tamaño de la ventana.
2. **Detección (`Detectar`):**
   - Elige «Detectar (Rostros o personas)» y pulsa «Ejecutar».
   - **Qué juzgar:** Las cajas verdes delimitan los rostros/personas con su etiqueta de confianza en porcentaje (`Detectado (95%)`), legibles sobre fondos oscuros o claros.
3. **Guía de reencuadre (`Reencuadrar`):**
   - Selecciona «Reencuadrar».
   - Alterna entre los botones **`9:16`**, **`1:1`** y **`4:5`**.
   - **Qué juzgar:** El marco dorado punteado de recorte se adapta inmediatamente en pantalla mostrando cómo quedará el encuadre final.

---

## 5. Progreso, cancelación rápida y exportación

1. Durante cualquier procesamiento (ej. seguimiento de un clip de varios segundos), observa la barra de progreso:
   - Indica el porcentaje numérico y la etapa (`Siguiendo`, `Analizando movimiento`, etc.).
2. Pulsa **«Cancelar»**:
   - **Qué juzgar:** La detención debe ser prácticamente instantánea (< 1 segundo), liberando los subprocesos de decodificación y volviendo la interfaz al estado disponible con el aviso informativo en español.
3. Al finalizar un análisis completo:
   - Pulsa **«Exportar resultado…»**: permite guardar el CSV/JSON (para seguimiento y detección) o el video procesado (se genera en la carpeta temporal y se copia donde elijas).

---

## 6. Mensajes de error en español sin trazas

1. Si intentas pulsar «Ejecutar» en «Seguir» sin haber hecho clic sobre el video, la barra `InfoBar` avisa claramente: *«Selecciona un punto con un clic o una región arrastrando sobre el video.»*
2. Si un módulo o ejecutable no se encuentra en el equipo, el aviso indica claramente la ruta buscada en español, sin mostrar excepciones ni trazas técnicas.

---

## 7. Identidad (pulido visual, 24-oct)

Lo que solo tú puedes juzgar a ojo. Captura de referencia: `docs/capturas/pulido-vacio.png`.

1. **Primer vistazo (3 segundos):** icono de mira con trayectoria en la barra de tareas y en la ventana, «Rastrear» en Bahnschrift con el lema «Sigue lo que importa», acento verde-cian «radar». ¿Se distingue de Rodaje y de las otras dos herramientas sin leer nada?
2. **Tema:** cambia Windows entre claro y oscuro (Configuración › Personalización › Colores) y vuelve a abrir la app: el fondo, los paneles y los textos siguen al sistema; el visor sigue oscuro en los dos. ¿Se lee todo (texto apagado, cifras en verde) en los dos temas?
3. **Estado vacío:** sin video, el visor muestra la mira con la trayectoria punteada y dice qué hacer. ¿Invita a soltar un video o sobra?
4. **Cifras monoespaciadas:** metadatos de la cabecera, tiempo del reproductor, objetivo (X, Y) y tiempo de progreso van en Cascadia Mono. ¿Dejan de bailar al cambiar los dígitos?
5. **Retícula y superposición:** con un video cargado, la retícula fina (sextos) sobre el cuadro, el objetivo con cruz de mira, la trayectoria y las cajas en verde-cian, la guía de reencuadre en blanco punteado. ¿La retícula ayuda a apuntar o distrae? (Se puede quitar si molesta.)
6. **Progreso:** al ejecutar, barra con el acento, etapa y «0:12 · quedan ~0:36» a la derecha; al acabar, un tic verde que aparece con un rebote y «Hecho en 0:04». ¿El rebote es agradable o excesivo?
7. **Ventana:** mínimo 960×600; al cerrar recuerda tamaño, posición y si estaba maximizada (`%LOCALAPPDATA%\Dathinel\Rastrear\ventana.json`). Muévela, ciérrala y vuelve a abrirla; si desconectas un monitor, debe volver a aparecer dentro de la pantalla.
8. **Teclado:** recorre los controles con Tab: el foco se ve (anillo del sistema) y Espacio activa botones y casillas.

---

## 8. Calidad de vida (31-oct, rama `claude/calidad-de-vida`)

1. **Memoria:** abre un video de una carpeta, cambia a «Reencuadrar» con 1:1 y suavizado alto, cierra y vuelve a abrir: la herramienta y sus parámetros son los mismos y «Elegir video…» se abre en esa carpeta (`%LOCALAPPDATA%\Dathinel\Rastrear\preferencias.json`).
2. **Soltar en cualquier parte:** arrastra un video sobre la cabecera o el panel derecho, no solo sobre el visor. Suelta tres a la vez: aparece la cola bajo el reproductor con «pendiente» en cada uno; «Ejecutar» los procesa uno tras otro («Archivo 2 de 3 · 40 % · …») y deja `clip-fondo.webm`, `clip-seguimiento.csv`… junto a cada video. ¿Se entiende el estado de cada archivo? ¿Sobra «Vaciar»?
3. **Cancelar:** con «Quitar fondo» a medio camino pulsa «Cancelar» (o `Esc`) y mira el Administrador de tareas: no debe quedar `ffmpeg.exe` ni `Rodaje.Module.Vision.exe`. El aviso dice si algo siguió vivo.
4. **Después:** con «Reencuadrar», pulsa «Después» y cambia entre 9:16, 1:1 y 4:5: el visor muestra el recorte al instante. Con «Quitar fondo», tarda lo que tarde el módulo con un cuadro y muestra el sujeto sobre el tablero (o la máscara en grises). ¿El tablero se ve bien en los dos temas? Con «Seguir» el botón explica que no hay vista previa.
5. **Resultado:** al acabar, «Abrir carpeta» abre el Explorador con el archivo seleccionado y «Copiar ruta» deja la ruta completa en el portapapeles (pégala en el Ejecutar de Windows).
6. **Atajos:** `Ctrl+O` abre el diálogo, `Ctrl+Enter` ejecuta (solo con video cargado), `Esc` cancela (solo mientras corre). Las descripciones emergentes de los botones los muestran.
7. **Sin módulo:** renombra la carpeta de `modulo-vision` y pulsa «Ejecutar» con «Detectar»: el aviso dice qué falta y para qué, sin rutas, y el botón «Abrir el Lanzador» aparece solo si `Lanzador.exe` está en `%LOCALAPPDATA%\Programs\Lanzador` (donde lo deja su instalador) o donde apunte `RASTREAR_LANZADOR`.

---

## 9. Aviso de versión nueva (1-nov, rama `claude/pensive-wozniak-1bqxdy`)

Para probarlo sin esperar a una release real, pon en `%LOCALAPPDATA%\Dathinel\Rastrear\preferencias.json` una `"ultimaConsultaVersion"` de hace más de un día (o quítala) y baja la versión de `Directory.Build.props` a `0.9.0` antes de compilar: la 1.0.0 publicada pasa a ser «nueva».

1. **Arranque:** con red, la ventana abre igual de rápido (la consulta va aparte y tarda como mucho 5 s) y, si hay una versión mayor, aparece una franja bajo la cabecera: «Rastrear 1.0.0 disponible», en Bahnschrift con la raya de acento, y debajo qué versión tienes y quién instala la nueva. ¿Es discreta o estorba? ¿Se lee en los dos temas?
2. **Botones de la franja:** «Ver novedades» abre la página de la release en el navegador; «Abrir el Lanzador» solo aparece si `Lanzador.exe` está donde lo busca `LauncherLocator` y lo abre; «Más tarde» quita la franja y, al volver a abrir la app (con `ultimaConsultaVersion` borrada), ya no avisa de esa versión (`versionDescartada` en el JSON). Una versión más alta sí volvería a avisar.
3. **24 h:** cierra y vuelve a abrir enseguida: no consulta (`ultimaConsultaVersion` no cambia). Sin red (desconecta el Wi-Fi): nada, ningún aviso ni error.
4. **Acerca de:** el interruptor «Avisar cuando haya una versión nueva» viene activado; apágalo, cierra y abre: no hay franja aunque toque consultar (`avisarVersionNueva: false`). «Buscar actualizaciones» consulta al momento aunque esté apagado o se hayan consultado hace un minuto: «Consultando GitHub…» y después «No hay nada nuevo: la 1.0.0 es la última versión.», «Rastrear 1.1.0 disponible» (y la franja detrás del diálogo, aunque se hubiera dicho «Más tarde») o «No se pudo consultar GitHub…» sin red.
5. **Daniel:** el aviso solo dice que hay versión nueva; instalar sigue siendo cosa del Lanzador o del zip/instalador de la release. Si el Lanzador va a hacer algo con un argumento (abrir la ficha de Rastrear), hay que pasárselo en `OnAbrirLanzadorClick`.

---

## 10. Módulo vivo en «Después», cola sin «Seguir» y carpeta de salida (1-nov, rama `claude/quirky-hawking-c8tin7`)

1. **Módulo vivo:** carga un video, elige «Quitar fondo» y pulsa «Después»; en el Administrador de tareas aparece un `Rodaje.Module.Vision.exe`. Cambia a «Reencuadrar», vuelve a «Quitar fondo», alterna calidad y dispositivo: la máscara se rehace cada vez pero el proceso es el mismo (mismo PID), y la segunda máscara y las siguientes tardan menos que la primera. Suelta «Después»: el proceso desaparece en un par de segundos. Vuelve a pulsarlo: arranca otro. También se cierra al pulsar «Ejecutar», al abrir otro video y al cerrar la ventana (mira que no quede ninguno).
2. **Módulo que muere:** con «Después» pulsado, mata `Rodaje.Module.Vision.exe` desde el Administrador de tareas y cambia un parámetro: la vista previa se rehace con un módulo nuevo, sin error. Si en vez de eso falta el módulo (renombra `modulo-vision`), bajo el reproductor sale el mensaje de que falta, igual que al ejecutar.
3. **Cola sin «Seguir»:** con «Seguir» elegida suelta tres videos: se abre el primero, no aparece la cola y el aviso dice que «Seguir» necesita un clic en cada video y cuántos no entraron. Suelta los tres con «Detectar»: cola de tres; cambia a «Seguir»: la cola desaparece, queda el video cargado y el aviso lo dice. ¿Convendría más poder encolar y pedir el clic video a video?
4. **Carpeta de salida:** en el panel de la derecha, «Carpeta de salida» dice «Junto al video». Pulsa «Elegir…», elige una carpeta, ejecuta «Estabilizar»: el resultado aparece en esa carpeta (`clip-estabilizado.mp4`, y `(2)` si ya existía), «Abrir carpeta» va allí y el aviso de varios en cola dice «en <carpeta>». Cierra y abre: la carpeta sigue elegida (`carpetaSalida` en `preferencias.json`). «Junto al video» vuelve a lo de antes. Elige una carpeta en un pendrive, quítalo y ejecuta: el archivo queda «con error» en la cola y la ventana no se cae.
5. **Exportar copia:** tras «Quitar fondo», «Exportar resultado…» a otra carpeta: el aviso dice «Copia exportada a:»; «Abrir carpeta» sigue abriendo el resultado original, no la copia. Exporta el seguimiento como `.json` y como `.csv`; elige como destino el propio resultado: no pasa nada ni da error.
