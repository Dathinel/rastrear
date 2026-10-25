# Fondo legal de Rastrear

No es asesoría legal: resume las licencias y lo que hay que comprobar antes de entregar un ejecutable a terceros. Las licencias de cada pieza están en [LICENCIAS.md](../LICENCIAS.md).

## Licencia

GPL-3.0-only, con el texto oficial completo en [LICENSE](../LICENSE). La razón: Rastrear compila código de Rodaje (submódulo `externo/rodaje`), que es GPLv3, y trabaja con sus módulos de IA, también GPLv3. Rodaje y este programa tienen un solo titular (Dathinel), así que la elección de licencia es libre; GPLv3 es la coherente con el resto.

**Términos adicionales de Rodaje.** El `LICENSE` de Rodaje añade, por la sección 7, un aviso de atribución en la interfaz («Basado en el editor de video de código abierto de Dathinel / Rodaje», con enlace). Ese término acompaña al código de Rodaje que se compila aquí. La atribución literal va en `LICENCIAS.md`, en el `README.txt` del zip, en la licencia que muestra el instalador y en las notas de la release (término 2: aviso en el canal de descarga). **Falta en la interfaz** (término 1 pide un enlace funcional en «Acerca de», barra de estado o pantalla de inicio): la añade el worktree `pulido`; mientras, solo el titular (Dathinel, el mismo de Rodaje) puede distribuirla así.

**Windows App SDK.** Las DLL de WinUI 3 van en el zip bajo los términos de Microsoft (no MIT): redistribución permitida, pero con el mismo choque con la GPLv3 que ya tiene Rodaje pendiente para el abogado (su `LICENCIAS.md` y `docs/legal.md`). Rastrear hereda esa consulta.

**Marca.** El nombre «Rodaje», su logotipo y su icono no se licencian con el código (`TRADEMARK.md` de Rodaje). Rastrear usa nombre e icono propios, con identificador y GUID de instalación independientes.

## Módulos y modelos que se descargan aparte

Los modelos de IA no forman parte del repositorio de Rastrear ni de su archivo comprimido principal; se descargan de forma diferida o mediante el catálogo firmado:
- **modulo-vision** (`Rodaje.Module.Vision`, GPLv3):
  - **MODNet**: arquitectura de matting para personas, licencia Apache-2.0 (permite redistribución y uso comercial con aviso).
  - **BiRefNet_lite fp16**: segmentación bilateral de alta resolución, licencia MIT.
  - **YuNet (2023mar)**: detector de rostros ultraligero de OpenCV Zoo, licencia MIT.
  - **RT-DETRv2-R18**: detector de personas en tiempo real, licencia Apache-2.0.
- El seguimiento de puntos (`PointTracker`, `RegionTracker`) y la estabilización (`MotionEstimator`, `CameraPath`) son algoritmos numéricos en C# nativo de `Rodaje.Analysis`, sin modelos de pesos neuronales.

Cada modelo se descarga exclusivamente con URL fijada a un commit inmutable y con verificación de huella SHA-256 antes de guardarse en disco.

## Lista de comprobación para publicar

- [x] `LICENSE` con la GPLv3 completa y la cabecera de copyright y URL (`https://github.com/Dathinel/rastrear`).
- [x] Crédito y atribución de Rodaje en `LICENCIAS.md`, `README.txt` del zip, instalador y notas de versión.
- [x] Atribución a Rodaje con enlace en la interfaz de `Rastrear.App` (término 1 de su licencia): «Acerca de» en la cabecera con `HyperlinkButton` al repositorio (24-oct).
- [ ] Consulta al abogado sobre Windows App SDK y GPLv3 (compartida con Rodaje).
- [x] Nombre e icono propios; ningún recurso visual con marca o logos de Rodaje.
- [x] `LICENCIAS.md` al día: dependencias (SDK .NET, Windows App SDK, WebView2, FFmpeg LGPL) y modelos con licencia; versión y huella en el `paquete.json` del módulo.
- [x] Modelos: licencias permisivas (MIT y Apache-2.0) que permiten redistribución y uso comercial; URLs fijadas a commit y SHA-256 verificado.
- [x] FFmpeg: solo la compilación LGPL de Rodaje (sin `--enable-gpl` ni `--enable-nonfree`), ejecutables sin renombrar y enlace a fuentes.
- [x] Sin código GPL ajeno copiado sin atribución: referencias directas por submódulo o proyectos propios.
- [x] Sin credenciales, datos personales ni secretos en el repositorio ni en el historial.
- [x] La release se genera de forma determinista desde la etiqueta `v<versión>` con `build\distribuir.ps1` publicando `SHA256SUMS`.
