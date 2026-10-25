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
