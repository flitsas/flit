# ADR-0085: Veredicto OCR del adjunto — persistido, declarado por el cliente y nunca compuerta

**Fecha**: 2026-11-12
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT
**Tags**: arquitectura, backend, frontend, modulo-tramites, ocr
**Bug**: #14559 (PDN)
**Diseño detallado**: diseño del fix en el PR del Bug #14559

## Contexto
El veredicto OCR (verified/rejected/skipped) de los documentos del wizard se calcula solo en el cliente
(evaluateOcr) y vive en estado de React; al cambiar de paso se pierde y el documento rechazado se ve
«Cargado» (Bug #14559, PDN). El backend no guarda el veredicto. El análisis (/tramites/ocr/{tipo}) es
stateless y por HU #11996 el OCR nunca impide cargar. Hoy ningún gate usa el veredicto.

## Decisión
Persistir el veredicto como columnas `ocr_status`/`ocr_reason` (códigos cerrados, CHECK) en
`tramites.procedure_instance_attachments`, declaradas por el cliente en `POST .../attachments/register`
y expuestas en `AttachmentDto`. El veredicto es informativo: NO puede usarse como compuerta.

## Alternativas consideradas
### Opción 1: Cliente declara en register + columnas en la tabla de adjuntos (elegida)
**Pros:** diff mínimo; sin costo extra de IA; reset natural al reemplazar (fila nueva).
**Cons:** el servidor no puede verificar lo declarado.
**Esfuerzo:** S  **Riesgos:** integridad; mitigada porque no gatea.
### Opción 2: Servidor recalcula (port de evaluateOcr o re-análisis del binario)
**Pros:** veredicto confiable, usable como compuerta.
**Cons:** duplica costo/latencia del modelo o duplica lógica; viola el espíritu de HU #11996 si el proveedor cae.
**Esfuerzo:** L  **Riesgos:** PR > 800 líneas; dos fuentes de verdad.
### Opción 3: Tabla aparte de resultados OCR
**Pros:** historial y JSON extraído a futuro.
**Cons:** entidad/RLS/join nuevos para un valor; diseño anticipado.
**Esfuerzo:** M  **Riesgos:** superficie de RLS nueva.

## Tradeoff aceptado
Se acepta un veredicto no verificable a cambio de un fix S que no duplica IA, porque el veredicto solo
cambia una etiqueta para el mismo operador que subió el archivo.

## Consecuencias
### Lo que se gana
- La UI muestra el mismo veredicto tras remontar, cambiar de paso o recargar.
### Lo que se pierde
- El veredicto no es evidencia confiable para auditoría ni para reglas de negocio.
### Cambios operacionales
- Adjuntos previos al cambio quedan con NULL («Cargado»); sin backfill.
- Cualquier HU que quiera bloquear por OCR debe crear un ADR que haga Supersedes de este y mover el cálculo al servidor.

## ADRs relacionados
- Ninguno superseded.

## Notas para agentes
- **Backend Agent**: normalizar contra whitelist; desconocido → NULL, nunca 400. No leer ocr_status en validaciones de avance/envío.
- **Frontend Agent**: rehidratar desde AttachmentDto; la sesión gana; no persistir texto libre del modelo.
- **QA Agent**: probar remount y reemplazo; NULL ⇒ «Cargado».
- **Security Agent**: confirmar que ningún gate consume ocr_status; ocr_reason no lleva PII.
- **Infra Agent**: migración aditiva; sin impacto de despliegue.
