# ADR-0082: PPT como tipo de documento de actores OT y observación FUR sin casilla

**Fecha**: 2026-11-10  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT, Producto OT  
**Tags**: arquitectura, backend, frontend, tramites, fur, identidad  
**Feature**: #14497

## Contexto

Los actores de trámites OT solo admiten `CC|CE|NIT|PAS|TI`. El PPT (Permiso por Protección Temporal) aparece de forma fragmentada (bulk, prevalidación, biometría, Quipux) pero `PutActors` lo rechaza y el select de actores no lo ofrece. El Anexo 46 del FUR no tiene casilla tipológica para PPT; el PO dictaminó no marcar casilla y declarar el tipo en OBSERVACIONES con un literal fijo. El portal Persona Natural queda fuera de alcance. No hay catálogo BD de tipos de ID (`document_type` es `string`).

## Decisión

Adoptar PPT como tipo de documento de primera clase en el contrato de actores OT (API + UI), con número alfanumérico; en el FUR no marcar ninguna casilla `*_document_type_*` para PPT y concatenar una vez por FUR el literal `tipo de documento (PPT) Permiso por Protección Temporal` en el párrafo 23; mantener Quipux `PPT→7`; en Kyverum RUNT omitir el tipo cuando sea PPT (no colapsar a pasaporte `P`).

## Alternativas consideradas

### Opción 1: PPT de primera clase + FUR observación sin casilla (elegida)

**Pros:** Cumple el Feature; alinea bulk/UI/API; dictamen normativo limpio; datos auditable hacia radicación.  
**Cons:** Toca API, UI, FUR e integraciones.  
**Esfuerzo:** M  
**Riesgos:** Consulta conductor con PPT nativo puede ser frágil; se mitiga en HU de integraciones.

### Opción 2: Capturar como CE/PAS y nota manual en observaciones

**Pros:** Cambio mínimo de código.  
**Cons:** Tipo incorrecto en actores/Quipux/RUNT; no auditable; incumple criterios del Feature.  
**Esfuerzo:** S  
**Riesgos:** Alto en calidad de radicación y FUR.

### Opción 3: Marcar casilla NUIP + observación

**Pros:** Reutiliza casilla existente; Quipux ya usa código 7.  
**Cons:** Contradice dictamen PO/ExpertDocEngine; confunde PPT con NUIP en el PDF.  
**Esfuerzo:** M  
**Riesgos:** Normativo (casilla incorrecta en Anexo 46).

## Tradeoff aceptado

Se acepta el esfuerzo multi-capa de la Opción 1 para no mentir en la casilla tipológica del FUR ni en el tipo persistido del actor. Quipux conserva el código 7 ya mapeado; Kyverum deja de tratar PPT como pasaporte.

## Consecuencias

### Lo que se gana
- Actores OT con PPT seleccionable y validable.
- FUR alineado al dictamen: sin X tipológica; literal en observaciones.
- Bulk que ya lista PPT deja de fallar al materializar actores.

### Lo que se pierde
- No se unifica aún el portal Persona Natural (explícitamente fuera de alcance).
- Consulta RUNT/Kyverum con PPT nativo puede omitir filtro de tipo.

### Cambios operacionales
- Actualizar `docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md` en el mismo PR de la HU FUR.
- Tests: actores, mapper FUR, composición de observaciones, KyverumDocType, Quipux.

## ADRs relacionados

- Reglas canónicas FUR: `docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md` (no es ADR; artefacto normativo).

## Notas para agentes

- **Backend Agent**: ampliar `ValidDocumentTypes`; `ResolveDocTypeCheckbox("PPT")=null`; `FurPptObservation`; `MapDoc` mandato/compraventa; Kyverum omitir PPT.
- **Frontend Agent**: `ActorDocumentType`, `fieldRules`, `ActorsForm` / `TramiteWizard` con etiqueta «Permiso por Protección Temporal (PPT)».
- **QA Agent**: TCs de actores PPT + generación FUR (sin casilla + literal).
- **Security Agent**: PII de documento PPT igual que otros tipos; sin logs de número completo.
- **Infra Agent**: sin cambios de deploy específicos; sin DDL.

## Referencias externas

- Feature ADO #14497
- Resolución 20233040017145 de 2023 (Mintransporte) — Anexo 46 FUR
- Dictamen ExpertDocEngine sesión 2026-11-10: sin casilla PPT; literal en observaciones
