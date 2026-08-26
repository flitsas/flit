# ADR-0049 — La geometría del estampado del pie de página es propiedad del tipo de documento

- **Estado**: Propuesto · 2026-08-20
- **Módulo**: Trámites — Generación documental (`Flit.Infrastructure/Documents`, `Flit.Tramites.Application/Documents`)
- **Feature/Bug**: novedad 27 (sello del nombre del documento pisa la rejilla de "TIPO DE CARROCERÍA" en la hoja 2 del FUR AUTOMOTOR)
- **Deciders**: Líder Técnico FLIT
- **Tags**: arquitectura, backend, documental, pdf

## Contexto

`FlitPdfStamper.ApplyDocumentName` (ADR-0030, HU #10858) estampa el nombre del documento en el pie de
cada página del expediente consolidado, con un único margen inferior global
(`FlitDocumentTheme.DocNameBottomCm = 1,2 cm`) para **todas** las partes, sin importar su tipo. Ese
margen fue calibrado contra los documentos generados por FLIT con membrete (mandato, solicitud,
compraventa), donde la banda de color sólido del pie solo es segura desde ≈1,12 cm.

El FUR es un documento externo (formulario oficial de tránsito), con su propia diagramación y tamaño
de página, cuyo contenido oficial llega mucho más cerca del borde inferior que los documentos con
membrete de FLIT: en la hoja 2 (plantilla AUTOMOTOR), la rejilla de "TIPO DE CARROCERÍA" trae tinta
hasta y≈576 pt (de 612 pt de alto). Con el margen global de 1,2 cm, el sello del nombre del documento
invade esa rejilla — la colisión reportada en la novedad 27.

Un intento previo cubrió la colisión pintando un **fondo opaco blanco** detrás del sello
(`FlitPdfStamper.ComputeStampGeometry` devolvía además un rectángulo `Background`, con
`FlitDocumentTheme.DocNameBackgroundColor`/`DocNameBackgroundPaddingPt`). Verificado renderizando el
PDF, ese fondo:
- Borra la línea inferior de la rejilla y la base de las etiquetas verticales de tipo de carrocería
  (TRACTOCAMIÓN→"RACTO", VOLQUETA→"OLQUE", IMPROVISADO→"MPRO" quedan truncadas).
- En los documentos con membrete, recorta una muesca visible dentro de la banda negra sólida del pie.
- Asume que el papel bajo el sello es blanco — falso en cualquier adjunto del usuario que sea un
  escaneo (fondo gris, textura, otro color).

Ese intento se revierte como parte de esta decisión.

## Decisión

La geometría del estampado (dónde cae el margen inferior del sello) es una propiedad del **tipo de
documento**, no del tema visual global. Se introduce un **perfil de estampado por parte**
(`StampProfile`, en `MergePart`), con dos valores: `Default` (margen histórico 1,2 cm, sin cambios
para mandato/solicitud/compraventa/certificados) y `Formulario` (margen reducido 0,56 cm, para
**todas** las páginas del FUR — hoja 1 y hoja 2, en los tres formatos AUTOMOTOR/MAQUINARIA/
REMOLQUES; no hay resolución por número de página, basta por parte).

El valor 0,56 cm (16 pt) se eligió midiendo el perfil de tinta a 288 dpi sobre la franja horizontal
donde cae el sello (x ∈ [W−202, W−72] pt) en las cuatro geometrías FUR:

| Plantilla | Bandas con tinta (y, pt) | Libre |
|---|---|---|
| `fur-formulario-p1-blank.pdf` hoja 1 automotor (792×612) | 568,2–569,8 (regla) · 609,2–610,0 (marco) | 570,0–609,2 |
| `fur-instrucciones-p2-blank.pdf` automotor | 542,0–576,0 (rejilla carrocería) | 576,0–612 |
| `fur-maquinaria-p1` / `fur-remolques-p1` (1008×612) | 546,0–547,2 · 560,8–562,0 | 562,0–612 |
| `fur-maquinaria-p2` / `fur-remolques-p2` / `fur-matricula-full` | sin tinta | todo |

La intersección de toda la familia es 576,0–609,2 pt. Con ascent 9,2 pt / descent 2,8 pt (Poppins
Medium 8 pt), la línea base cabe en [585,2; 606,4] pt ⇒ `bottomPt ∈ [5,6; 26,8]` pt. Se eligió el
centro: 16 pt = 0,56 cm, con ±11 pt de holgura sobre cualquiera de las cuatro plantillas.

`ApplyDocumentName` gana un parámetro `bottomCm` con el valor por defecto actual
(`FlitDocumentTheme.DocNameBottomCm`), así que no rompe llamadores existentes.
`PdfExpedienteConsolidadoMerger.Compose` resuelve el margen según `MergePart.Profile` antes de
estampar cada parte. El mapeo `tipo → perfil` vive junto al diccionario curado de
`DocumentLabels.Display` (`DocumentLabels.ProfileFor`): solo `tipo == "fur"` obtiene
`StampProfile.Formulario`; el resto usa `Default`.

## Alternativas consideradas

### Opción 1: perfil de estampado por parte (`StampProfile`), margen propio para el FUR (RECOMENDADA)

**Pros:**
- No borra ni oculta contenido oficial de ningún documento.
- No asume nada sobre lo que hay debajo del sello — funciona igual sobre un PDF vacío o sobre un
  escaneo del usuario.
- El margen 0,56 cm cae dentro de la franja libre medida en las CUATRO plantillas FUR a la vez —
  no hace falta resolución por página ni por plantilla, basta el tipo de documento.
- Cambio de contrato acotado y hacia atrás compatible (`bottomCm` con valor por defecto).

**Cons:**
- Introduce un concepto nuevo (`StampProfile`) que hay que mantener si aparece un tercer tipo de
  documento con geometría propia.
- El margen es "best effort" para adjuntos del usuario clasificados como `fur` que no sean
  exactamente las plantillas medidas (ver más abajo).

**Esfuerzo:** S · **Riesgos:** bajo — cambio geométrico puro, cubierto por tests deterministas sin
render.

### Opción 2: fondo opaco detrás del sello (la implementada y revertida)

**Pros:** un solo margen global, sin perfiles ni mapeo por tipo.

**Cons:**
- Borra contenido oficial del documento bajo el sello (la propia rejilla que se quería librar, y la
  banda negra del pie de los documentos con membrete).
- Asume papel blanco: falso para cualquier adjunto escaneado del usuario.
- "Arregla" la colisión ocultándola, no evitándola — el texto real del sello sigue invadiendo la
  rejilla; solo cambia si eso se ve o no.

**Esfuerzo:** S · **Riesgos:** alto — pérdida de fidelidad documental, un producto FLIT no puede
alterar el contenido oficial de un formulario de tránsito ni de un adjunto de un tercero.

### Opción 3: metadato de perfil embebido dentro del propio PDF (p. ej. en el `Info` dictionary)

**Pros:** el merger no necesita conocer el tipo de documento; la decisión viaja con el archivo.

**Cons:**
- Fallo silencioso: si el generador del PDF olvida escribir el metadato, el merger no tiene forma de
  saber que debía usar el perfil `Formulario` — cae al `Default` sin ningún error visible hasta que
  alguien revisa el PDF impreso.
- Depende de que PdfSharpCore preserve ese metadato al reabrir/reescribir el documento — no
  verificado, y es una superficie adicional para que se pierda en un `Merge` intermedio.
- No aporta nada frente a pasar el perfil explícitamente en `MergePart`, que ya viaja junto al tipo
  técnico del documento (`DocumentLabels.ProfileFor`).

**Esfuerzo:** M · **Riesgos:** medio-alto (fallo silencioso).

### Opción 4: estampar el nombre del documento desde el propio generador del FUR, no desde el merger

**Pros:** el generador del FUR conoce su propia diagramación mejor que el merger genérico.

**Cons:**
- Duplica la lógica de estampado (fuente, color, posición) entre el generador del FUR y
  `FlitPdfStamper`, la misma deriva que ADR-0030 ya evitó centralizando el estampado en un solo lugar.
- El FUR es, en la mayoría de los flujos, un adjunto del usuario o un PDF externo — no siempre pasa
  por "el generador del FUR" de FLIT, así que esta opción no cubre todos los casos que sí cubre
  `MergePart.Profile`.
- Alteraría el documento oficial del FUR antes de llegar al consolidado, mezclando la generación del
  formulario con el estampado del expediente — dos responsabilidades que ADR-0030 separó a propósito.

**Esfuerzo:** M · **Riesgos:** medio (duplicación, acopla generación de FUR con estampado de expediente).

## Tradeoff aceptado

Se acepta mantener un pequeño mapeo `tipo → perfil` (hoy un solo caso: `fur`) a cambio de nunca
alterar el contenido de un documento oficial ni de un adjunto del usuario. La opción revertida
(fondo opaco) era más simple de implementar pero cambiaba lo que el usuario final ve impreso en un
documento que FLIT no genera — inaceptable para un formulario de tránsito.

## Consecuencias

### Lo que se gana
- El FUR (hoja 1 y hoja 2, en los tres formatos) lleva el sello del nombre del documento sin invadir
  contenido oficial, verificado con las cuatro geometrías medidas.
- Ningún otro documento cambia: mandato, solicitud, compraventa y certificados conservan
  `DocNameBottomCm = 1,2 cm` intacto.
- El mecanismo es extensible: un futuro documento con geometría propia solo necesita un nuevo valor
  de `StampProfile` y una entrada en `DocumentLabels.ProfileFor`.

### Lo que se pierde
- El perfil `Formulario` es **best effort sin garantía** sobre adjuntos que el usuario suba con
  `tipo == "fur"` pero que no sean ninguna de las plantillas oficiales medidas (por ejemplo, un FUR de
  otra secretaría de tránsito con diagramación distinta, o un escaneo con recorte irregular): FLIT no
  conoce el contenido bajo el sello de un adjunto de un tercero, así que no puede garantizar ausencia
  de colisión — solo se evitó la colisión conocida y reproducible contra las plantillas propias.

### Cambios operacionales
- Ninguna migración de esquema ni de configuración; cambio de código y de contrato interno
  (`MergePart.Profile`, valor por defecto `StampProfile.Default`).

## ADRs relacionados

- ADR-0030 — Módulo de marca documental compartido y merger compositor. Este ADR **refina** esa
  decisión (introduce el perfil de estampado por parte dentro del mismo compositor); no la
  reemplaza.

## Notas para agentes

- **Backend Agent**: `ApplyDocumentName` recibe `bottomCm` con valor por defecto
  `FlitDocumentTheme.DocNameBottomCm` — no romper llamadores existentes. `DocumentLabels.ProfileFor`
  es el único punto de mapeo `tipo → perfil`; si aparece un nuevo tipo de documento con geometría
  propia, agregar el caso ahí, no repartir `if`s por el merger.
- **Frontend Agent**: sin impacto (cambio interno del compositor de PDF).
- **QA Agent**: validar visualmente el FUR AUTOMOTOR (hoja 1 y hoja 2) y maquinaria/remolques en el
  consolidado — el sello no debe pisar la rejilla de carrocería ni el marco de hoja 1; validar que
  mandato/solicitud/compraventa no cambiaron su posición del sello.
- **Security Agent**: sin cambios de permisos; sin impacto en datos personales.
- **Infra Agent**: sin impacto (sin migración, sin variables nuevas).

## Referencias externas

- `docs/plan-firma-actor-nit-registro-radicacion.md` (contexto de la ola de novedades donde se
  reportó la colisión).
- Mediciones de perfil de tinta (PyMuPDF, 288 dpi) sobre `fur-formulario-p1-blank.pdf`,
  `fur-instrucciones-p2-blank.pdf`, `fur-maquinaria-p1/p2`, `fur-remolques-p1/p2`,
  `fur-matricula-full` y `membrete-hoja-footer.svg`.
