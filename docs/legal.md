# Fondo legal de Rastrear

No es asesoría legal: resume las licencias y lo que hay que comprobar antes de entregar un ejecutable a terceros. Las licencias de cada pieza están en [LICENCIAS.md](../LICENCIAS.md).

## Licencia

GPL-3.0-only, con el texto oficial completo en [LICENSE](../LICENSE). La razón: Rastrear compila código de Rodaje (submódulo `externo/rodaje`), que es GPLv3, y trabaja con sus módulos de IA, también GPLv3. Rodaje y este programa tienen un solo titular (Dathinel), así que la elección de licencia es libre; GPLv3 es la coherente con el resto.

**Términos adicionales de Rodaje.** El `LICENSE` de Rodaje añade, por la sección 7, un aviso de atribución en la interfaz («Basado en el editor de video de código abierto de Dathinel / Rodaje», con enlace). Ese término acompaña al código de Rodaje que se compila aquí. Recomendación provisional: mostrar ese crédito en «Acerca de», que no cuesta nada y deja cerrada la duda. El titular puede decidir eximirlo.

**Marca.** El nombre «Rodaje», su logotipo y su icono no se licencian con el código (`TRADEMARK.md` de Rodaje). Rastrear usa nombre e icono propios.

## Lista de comprobación antes de publicar

- [ ] `LICENSE` con la GPLv3 completa y la cabecera de copyright y URL.
- [x] Crédito de Rodaje en «Acerca de» (enlace discreto en la cabecera; `HyperlinkButton` al repositorio con el texto literal del término 1), junto a nombre, versión y licencia.
- [ ] Nombre e icono propios; nada de recursos con la marca Rodaje.
- [ ] `LICENCIAS.md` al día: cada dependencia, módulo y modelo con versión, licencia y huella.
- [ ] Modelos: su licencia permite redistribución y uso comercial; se descargan con URL fijada a un commit y SHA-256, no van en el repositorio.
- [ ] FFmpeg: solo la compilación LGPL de Rodaje (sin `--enable-gpl` ni `--enable-nonfree`), DLL y ejecutables sin renombrar, enlace al código fuente correspondiente y línea de configuración (`externo/rodaje/docs/ffmpeg-configure.txt`).
- [ ] Sin código GPL ajeno copiado: de los proyectos de terceros solo se estudia el diseño.
- [ ] Sin datos personales ni secretos en el historial (gitleaks, como en Rodaje).
- [ ] La release se reconstruye desde su etiqueta con `build\preparar-entorno.ps1` y publica el SHA-256 de cada archivo.
