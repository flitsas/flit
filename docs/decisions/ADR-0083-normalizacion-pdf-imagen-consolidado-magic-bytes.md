# ADR-0083: Normalización PDF/imagen del consolidado por magic bytes + ImageSharp

**Fecha**: 2026-11-10  
**Status**: Propuesto  
**Deciders**: Jorman Copete (solicitante), architecture-agent / orchestrator-agent (propuesta). Aceptación: Líder Técnico humano (regla FLIT 15).  
**Tags**: arquitectura, backend, documentos, consolidado, pdf, imagen  
**Épica / Feature**: Feature #14504 (incidente PDN FT1-0000372)

## Contexto

`PdfExpedienteConsolidadoMerger.NormalizeToPdf` decide el camino solo con el `mimetype` declarado. En PDN (FT1-0000372) una impronta era **PDF real** (`%PDF-1.7`) etiquetada como `image/jpeg`; QuestPDF falló con `DocumentComposeException: Cannot decode the provided image`, invalidó regeneraciones y en admin `limpiar` llegó a 500.

Restricciones:

1. No ampliar códecs ImageSharp más allá de PNG/JPEG (HU #13371 / `PngJpegImageDecoding`).
2. El merge sigue exigiendo PDF Import-ok (PdfSharpCore); PDFs hostiles ya tienen [ADR-0076].
3. Sin cambios de schema ni migraciones.
4. Preferir reusar la puerta de decodificación existente frente a nuevas dependencias nativas.

## Decisión

Clasificar el contenido por **magic bytes** (precedencia sobre MIME), tratar PDF con el pipeline actual, y las imágenes PNG/JPEG vía `PngJpegImageDecoding.ReencodeToCleanPng` antes de QuestPDF; si no hay decodificación viable, **página de aviso** + bitácora sin tumbar el consolidado [ADR-0083].

## Alternativas consideradas

### Opción 1: Magic bytes + ReencodeToCleanPng + placeholder (elegida)

**Pros:** Reusa HU #13371; corrige MIME mentiroso; superficie de seguridad acotada; arregla FT1-0000372.  
**Cons:** WebP declarado hoy no pasa por ImageSharp restringido → placeholder.  
**Esfuerzo:** M  
**Riesgos:** Imágenes no PNG/JPEG quedan como aviso (aceptable vs 500).

### Opción 2: Solo try/catch alrededor de QuestPDF

**Pros:** Cambio mínimo.  
**Cons:** No normaliza ICC/CMYK; MIME mentiroso PDF↔imagen sigue roto.  
**Esfuerzo:** S  
**Riesgos:** Recurrencia del incidente.

### Opción 3: Magick.NET / conversión multi-formato

**Pros:** Más formatos (HEIC/WebP).  
**Cons:** Dependencia pesada; más superficie de seguridad; fuera del mínimo.  
**Esfuerzo:** L  
**Riesgos:** Tamaño de imagen, auditoría SCA, complejidad operativa.

## Tradeoff aceptado

Preferimos **robustez con códecs ya auditados** y fallback a aviso frente a ampliar formatos o confiar en el MIME del cliente.

## Consecuencias

### Lo que se gana
- Regeneración estable con adjuntos mal etiquetados o JPEG “sucios”.
- Fail-safe: el expediente se entrega aunque una parte imagen falle.
- Misma puerta de seguridad que firmas/impronta (ImageSharp PNG/JPEG only).

### Lo que se pierde
- Soporte de primera clase para WebP/HEIC en consolidado (queda placeholder).

### Cambios operacionales
- Logs/bitácora cuando una parte se sustituye por aviso.
- Tests de magic bytes y MIME mentiroso obligatorios.

## ADRs relacionados

- [ADR-0076] — conversión PDF ilegible PDFium→QuestPDF (partes PDF hostiles).
- [ADR-0078] — coherencia temporal consolidado en estado final (entrega JIT).

## Notas para agentes

- **Backend Agent**: implementar en `PdfExpedienteConsolidadoMerger.NormalizeToPdf` / `ImageToPdf`; no tocar schema.
- **Frontend Agent**: N/A (avisos cascada/bitácora ya existen).
- **QA Agent**: TCs regenerar consolidado con adjunto PDF mal MIME e imagen ilegible.
- **Security Agent**: no registrar TIFF/otros códecs en ImageSharp; reusar `PngJpegImageDecoding`.
- **Infra Agent**: N/A.

## Referencias externas

- Incidente PDN FT1-0000372 (EV MOTOS SAS).
- QuestPDF `DocumentComposeException: Cannot decode the provided image`.
