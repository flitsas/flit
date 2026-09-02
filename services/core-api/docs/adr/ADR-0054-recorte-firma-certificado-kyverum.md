# ADR-0054: Recorte y custodia de la firma del certificado Kyverum

**Fecha**: 2026-09-02  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT  
**Tags**: arquitectura, backend, identidad, PII

## Contexto

Kyverum no expone la imagen de la firma manuscrita por API. El trazo solo existe en el PDF del certificado. FLIT necesita estampar esa rúbrica en documentos cuando la parte firma por validación de identidad, sin recortar el PDF en cada generación y sin alargar el webhook de aprobación.

## Decisión

Extraer la rúbrica **una vez** (XObject `/Image` vía PdfSharpCore, ya en el árbol), **decodificar el stream a PNG de archivo** (DCT JPEG o ráster Flate + ImageSharp 2.x; el stream crudo no es pintable), persistir el binario en S3 vía `IAttachmentStorage` (agrupador `tenantId`, tipo `identity_signature`) y guardar solo `signature_image_path` + `signature_image_sha256` en `tramites.procedure_instance_biometric_validations`. La captura corre en la outbox **antes** del auto-FUR y se completa por backfill en el GET del certificado y en la descarga del FUR. Un artefacto ya guardado que no sea PNG/JPEG se recaptura. No se añade PdfPig (el feed NuGet del entorno no lista 0.1.x estable).

## Alternativas consideradas

### Opción 1: Recortar on-demand en cada FUR/GET

**Pros:** Sin columnas nuevas.  
**Cons:** Kyverum + CPU en cada documento; recorte no determinista; PII en memoria a cada rato.  
**Esfuerzo:** S  
**Riesgos:** Latencia y layout Kyverum.

### Opción 2: Extraer solo en el primer download, sin worker

**Pros:** Menos piezas.  
**Cons:** El auto-FUR de la outbox casi nunca tendría imagen.  
**Esfuerzo:** M  
**Riesgos:** Primera emisión documental sin rúbrica.

### Opción 3: Worker + backfill (elegida)

**Pros:** Webhook delgado; reuso 30 días; alineado al baúl (ADR-0025).  
**Cons:** Carrera PDF vs auto-FUR; si Kyverum aplana la página, no hay XObject y queda el sello.  
**Esfuerzo:** M  
**Riesgos:** Kyverum sin PDF al instante (mitigado con backfill, no con rollback de la aprobación).

## Tradeoff aceptado

Se acepta que el primer FUR automático pueda salir con sello de texto si el PDF aún no existe. Regenerar documentos al completar un backfill tardío queda fuera de este ADR.

## Consecuencias

### Lo que se gana

- Rúbrica reutilizable por persona/validación, no por trámite.
- Misma custodia que el baúl (path + hash, no bytea).

### Lo que se pierde

- Dependencia de que Kyverum embeba la rúbrica como imagen (si la aplana, el extractor devuelve null y queda el sello).

### Cambios operacionales

- Comentario `@pii:high` en `signature_image_path`.
- No loguear bytes ni paths completos con PII de nombre.

## ADRs relacionados

- [ADR-0025] — baúl de firmas, custodia S3
- [ADR-0031] — firma por identidad en el flujo documental

## Notas para agentes

- **Backend Agent**: no extraer en `IdentityValidationResultApplier` ni en el HTTP del webhook.
- **Frontend Agent**: sin cambio de contrato del GET certificado.
- **QA Agent**: fixture PDF sintético (no PII real).
- **Security Agent**: PII alta; no exponer el PNG en listados.
- **Database Agent**: ALTER de columnas nullable + CHECK ambos null o ambos not null.

## Referencias externas

- Plan: `docs/plan-tecnico-extraccion-firma-identidad-kyverum.md`
- Feature local: `docs/feature-extraccion-firma-identidad-kyverum.md`
