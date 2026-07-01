# Fuentes embebidas del FUR (HU #10256)

`DejaVuSans.ttf` y `DejaVuSans-Bold.ttf` se embeben como recursos (`EmbeddedResource`) y los
sirve `FurFontResolver` a PdfSharpCore al generar el FUR por overlay.

## Por qué

PdfSharpCore no trae fuentes: resuelve las familias (`XFont("Arial", ...)`) contra las del
sistema operativo. El contenedor runtime (`aspnet:10.0-alpine`) no tiene ninguna fuente
instalada, por lo que el primer `DrawString`/`MeasureString` lanzaba y el endpoint de generación
del FUR respondía **HTTP 500**. Embeber la fuente + registrar un `IFontResolver` hace la
generación determinista e independiente del SO.

## Origen y licencia

- **Fuente:** DejaVu Sans 2.37 (`dejavu-fonts-ttf-2.37`).
- **Descarga:** https://github.com/dejavu-fonts/dejavu-fonts/releases/tag/version_2_37
- **Licencia:** Bitstream Vera Fonts + Arev — permisiva, permite redistribución y embebido.
  Texto completo en `DejaVu-LICENSE.txt`.

DejaVu Sans se eligió por ser open, ampliamente disponible y sin dependencias de red en build.
