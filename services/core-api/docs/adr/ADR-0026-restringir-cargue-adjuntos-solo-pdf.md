# ADR-0026 — Restringir el cargue de documentos de trámite a solo PDF

- **Estado**: Propuesto · 2026-07-07
- **Módulo**: Trámites (adjuntos / cargue de documentos) — Matrícula (MI) y Traspaso (TR)
- **Requerimientos**: Pendiente C8 (`PendientesFLIT2.0MI-TR.xlsx`): *"Permite cargar JPG (validar solo PDF)"*
- **Decide**: Líder Técnico

## Contexto

En el flujo de trámites (MI y TR) el usuario adjunta documentos de soporte. Hoy la validación de tipo
de archivo es **uniforme y permisiva**: acepta PDF **e imágenes** (JPEG/PNG/WEBP), sin distinguir el
tipo de documento. El defecto reportado (C8) es que se pueden cargar imágenes (p. ej. un JPG) donde el
negocio requiere **PDF**.

El tipo permitido está definido en **dos únicos puntos**, uno por capa:

- **Backend:** `Flit.Tramites.Application/UseCases/ProcedureInstances/AttachmentsCommand.cs` →
  `ValidMimetypes = { application/pdf, image/jpeg, image/png, image/webp }`. Los mensajes de error
  correspondientes están en `Flit.Api/.../AttachmentEndpoints.cs`.
- **Frontend:** `frontend/components/operacion/DocumentChecklist.tsx` →
  `ALLOWED_MIME = ['application/pdf','image/jpeg','image/png','image/webp']` y el atributo `accept`
  del input de archivo.

Además, el consolidado (`PdfExpedienteConsolidadoMerger` / `IsMergeableMime`) hoy sabe **normalizar
imágenes a PDF** al fusionar; esto es relevante porque pueden existir adjuntos-imagen **ya cargados**
en datos históricos.

## Decisión

**Restringir el cargue a `application/pdf` únicamente**, en backend y frontend, de forma **global**
para todos los tipos de documento.

1. **Backend (fuente de verdad):** `ValidMimetypes` pasa a contener solo `application/pdf`. El
   endpoint de subida rechaza cualquier otro MIME con un error claro (HTTP 4xx, código legible del
   estilo `formato_no_permitido_solo_pdf`). La validación server-side es la que **manda** (el
   frontend es conveniencia de UX, no seguridad).
2. **Frontend:** `ALLOWED_MIME` pasa a solo PDF y el `accept` del input a `.pdf,application/pdf`,
   para que el selector de archivos no ofrezca imágenes y el usuario reciba feedback inmediato.
3. **Compatibilidad hacia atrás:** **no se migran** adjuntos ya cargados como imagen; la regla aplica
   solo a **cargas nuevas**. El merger del consolidado (`IsMergeableMime`) se deja **tolerante** con
   imágenes para no romper la generación de consolidados de expedientes que ya contengan imágenes
   históricas.
4. **Alcance:** la restricción es transversal (MI y TR) y no toca el preflight, el wizard de
   traspaso, ni las consultas externas (zona de otro desarrollador).

## Alternativas consideradas

### Alternativa A — Solo PDF, global, back + front (RECOMENDADA)
- (+) Cumple el requisito literal; cambio mínimo y localizado (una lista por capa).
- (+) Sin migración de datos; consolidado sigue tolerante con imágenes históricas.
- (+) Cero solape con el flujo de traspaso/preflight de otro desarrollador.
- (−) Si a futuro algún documento debe admitir foto, habría que reintroducir excepciones.
- Esfuerzo: **muy bajo**. Riesgo: bajo.

### Alternativa B — PDF-only configurable por tipo de documento
Permitir definir, por `DocTipo`, qué MIME acepta (algunos solo PDF, otros también imagen).
- (+) Flexible; respeta documentos que legítimamente sean fotografías.
- (−) Sobre-ingeniería para lo que pide C8; requiere modelo/config nuevo, UI y datos semilla.
- Esfuerzo: medio-alto. Riesgo: medio (agranda el alcance de un bug simple).

### Alternativa C — Validar solo en frontend
Restringir el `accept`/`ALLOWED_MIME` y dejar el backend como está.
- (+) Cambio trivial.
- (−) **Inseguro**: un cliente que salte la UI (API directa) seguiría subiendo JPG. No corrige el
  defecto de fondo.
- Esfuerzo: mínimo. Riesgo: **alto** (validación evadible).

## Consecuencias por agente

- **Backend:** reducir `ValidMimetypes` a `application/pdf`; ajustar mensajes de error en
  `AttachmentEndpoints.cs` (código y texto claros); **no** tocar `IsMergeableMime` del consolidado.
- **Frontend:** `ALLOWED_MIME` solo PDF; `accept=".pdf,application/pdf"`; mensaje de rechazo claro.
- **QA:** casos — subir JPG/PNG/WEBP → rechazado con mensaje "solo PDF"; subir PDF válido →
  aceptado; verificar que un expediente histórico con imágenes aún genera consolidado.
- **Security:** endurece la superficie de subida (menos tipos aceptados). Mantener validación
  server-side como autoritativa.
- **Infra:** sin cambios (sin migración, sin despliegue especial).

## Requisito vs decisión (trazabilidad)

| Pendiente | Estado con esta decisión |
|-----------|--------------------------|
| C8 — "Permite cargar JPG (validar solo PDF)" | **Cubierto** — solo PDF, validado en backend (autoritativo) y frontend |

## Estado y aceptación

Este ADR queda en **Propuesto**. Pasa a **Aceptado** solo mediante PR de aceptación del Líder
Técnico humano (regla FLIT 15).
