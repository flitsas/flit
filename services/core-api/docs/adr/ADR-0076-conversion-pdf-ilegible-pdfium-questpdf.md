# ADR-0076: Conversión de PDF ilegible vía PDFium → QuestPDF antes del placeholder

**Fecha**: 2026-11-05  
**Status**: Propuesto  
**Deciders**: Jorman Copete (solicitante), backend-agent (propuesta). Aceptación: Líder Técnico (regla FLIT 15).  
**Tags**: arquitectura, backend, documentos, consolidado, pdf  
**Épica / Feature**: HU #14312 (continuación de #14209 / #14240)

## Contexto

Tras #14209 (N1 Import-rewrite / N2 raster de XObject) y #14240 (página de aviso Import-ok), QA sigue
viendo solo el aviso en consolidados con PDFs “hostiles” (p. ej. Token `'18'` de PdfSharp). El negocio
pide **conservar el contenido visual** del adjunto en ver expediente / radicar OT, no solo evitar el 5xx.

Restricciones:

1. El Merge del consolidado exige `PdfDocumentOpenMode.Import` (PdfSharpCore).
2. Runtime de `core-api`: `aspnet:10.0-alpine` (musl); nativos PDFium/Skia en musl tienen historial de
   builds frágiles upstream.
3. QuestPDF + ImageSharp 2.1.11 ya están pineados; no ampliar superficie PdfSharp.
4. Placeholder (#14240) permanece como **último recurso**.

## Decisión

Usar **PDFtoImage 5.4.0** (PDFium + SkiaSharp) para rasterizar páginas a PNG @150 DPI y reconstruir un
PDF Import-ok con QuestPDF (`TryConvertViaPdfium`) **antes** de `CreateIncompatiblePlaceholder` en
`EnsureImportable` [ADR-0076].

## Alternativas consideradas

### Opción 1: PDFtoImage / PDFium → QuestPDF (elegida)

**Pros:** Conserva aspecto visual; API simple; Windows CI confiable; degrada a placeholder si nativos fallan.  
**Cons:** Dependencia nativa; tamaño de imagen/PDF mayor; texto deja de ser seleccionable.  
**Esfuerzo:** M  
**Riesgos:** Carga de `libpdfium` en Alpine/musl; si falla, se mantiene el aviso.

### Opción 2: Solo placeholder (#14240) sin conversión

**Pros:** Cero nativos nuevos; ya en producción.  
**Cons:** QA/negocio rechazan pérdida de contenido.  
**Esfuerzo:** S  
**Riesgos:** Rechazo funcional / tickets recurrentes.

### Opción 3: Cambiar runtime a `aspnet:10.0` (Debian/glibc) + PDFium

**Pros:** Binarios PDFium glibc más maduros.  
**Cons:** Imagen más pesada; cambio infra transversal; fuera del alcance mínimo de #14312.  
**Esfuerzo:** L  
**Riesgos:** Regresiones de deploy/size; requiere ADR + infra-agent.

## Tradeoff aceptado

Preferimos conservar contenido con nativos y **fallback al placeholder** frente a perder el adjunto o
mover el runtime a Debian en la misma HU. Si Alpine/musl falla en QA, escalamos Opción 3 en ADR aparte.

## Consecuencias

### Lo que se gana
- Consolidados con páginas rasterizadas del PDF hostil cuando PDFium abre el archivo.
- Logs distintos Converted vs Placeholder (HU14312 / HU14240).

### Lo que se pierde
- Texto seleccionable / capas vectoriales en la parte convertida.
- Dependencia nativa adicional en el publish de Infrastructure.

### Cambios operacionales
- Validar en contenedor Alpine que `libpdfium` carga; si no, degradación silenciosa a placeholder.
- Monitorear tamaño de consolidados (DPI 150).

## ADRs relacionados

- [ADR-0030] — marca documental compartida y merger compositor
- HU #14209 / #14240 — normalización Modify / EnsureImportable placeholder

## Notas para agentes

- **Backend Agent**: `PdfModifyNormalizer.TryConvertViaPdfium` + `PdfImportRecovery`; no abrir PDFs con PdfSharp si ya falló Import.
- **Frontend Agent**: N/A (mismo flujo ver expediente / radicar).
- **QA Agent**: Caso positivo con PDF Token `'18'` real: páginas con contenido, no solo aviso; caso negativo irrecuperable: aviso.
- **Security Agent**: Sin secretos; binarios nativos de NuGet auditados vía SCA.
- **Infra Agent**: Si Alpine falla de forma sistemática → evaluar `aspnet:10.0` (glibc) o empaquetar musl verificado.

## Referencias externas

- [PDFtoImage (NuGet)](https://www.nuget.org/packages/PDFtoImage/)
- [sungaila/PDFtoImage](https://github.com/sungaila/PDFtoImage/)
