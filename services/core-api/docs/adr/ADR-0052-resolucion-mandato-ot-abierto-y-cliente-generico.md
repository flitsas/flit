# ADR-0052: Resolución de mandato — modo del OT, nacimiento abierto y mandato cliente genérico

**Fecha**: 2026-08-26
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (pendiente de aceptación)
**Tags**: arquitectura, backend, frontend, modulo-admin-ot, modulo-tramites, documental

**Supersedes parcialmente**: [ADR-0036-mandatarios-multiples-y-mandato-por-ot] — deja de ignorarse el `assignment_mode` del OT cuando no hay regla compañía×OT. No deroga mandatarios múltiples, baúl ni identidad.

**Relacionados**: [ADR-0042] (PDF personalizado de compañía sigue primero), [ADR-0025] (baúl), [ADR-0034] (identidad admin).

## Contexto

La generación del mandato resolvía plantilla por OT y `assignment_mode` solo por `company_ot_mandate_rules`. Sin regla, el modo caía a `signer` e ignoraba el valor guardado en `transit_office_mandate_config`. Un OT nuevo no sembraba esa fila. El negocio pidió: nacer en **formato abierto** (bloque con líneas `___`, mandatario vacío, plantilla genérica); un solo modelo por **empresa que radica** para todas las familias; mandato cliente con plantilla **genérica**; institucional vivo; identidad y baúl conviven; Plataforma → Mandatos convive con el hub OT.

## Decisión

Honrar `assignment_mode` del OT cuando no hay regla de compañía. Al crear el tenant OT sembrar `admin.transit_office_mandate_config` con `generico` + `open` y campos institucionales nulos. Si la regla de la empresa que radica es `signer`, la redacción efectiva es `generico` (salvo PDF/editor propio, ADR-0042). OTs legado sin fila conservan fallback `signer`. No hay tablas por familia de trámite.

## Alternativas consideradas

### Opción 1: Nueva tabla de políticas por familia de trámite

**Pros:** granularidad TRASPASO vs MATRÍCULAS.  
**Cons:** el negocio cerró un solo modelo por empresa.  
**Esfuerzo:** L  
**Riesgos:** explosión de configuración.

### Opción 2: Reusar config OT + regla compañía×OT y dejar de ignorar el modo del OT (elegida)

**Pros:** sin tablas nuevas; misma API de Plataforma; hub OT como segunda superficie.  
**Cons:** OTs con `institutional` en la fila y sin regla de compañía cambian de `signer` a institucional.  
**Esfuerzo:** M  
**Riesgos:** cambio de comportamiento en organismos ya parametrizados; mitigado no reescribiendo filas existentes.

### Opción 3: Cuarto `assignment_mode` `client`

**Pros:** distingue mandato OT vs cliente en el enum.  
**Cons:** mandato cliente es `signer` + genérica; un código más obliga migración y UI.  
**Esfuerzo:** M  
**Riesgos:** duplicar `signer` en el generador.

## Tradeoff aceptado

Se elige la Opción 2 porque el modelo de datos ya expresa los tres modos y la regla compañía×OT. El costo es honrar el modo del OT (cambio observable en Sabaneta/Bello si su fila decía institucional y nunca se aplicaba). El nacimiento abierto queda en el alta del tenant, no en un default SQL masivo.

## Consecuencias

### Lo que se gana
- OT nuevo = abierto + genérico + mandatario vacío.
- Empresa que radica: un modo para todas las familias; cliente = genérica.
- Hub OT y Plataforma editan la misma persistencia.

### Lo que se pierde
- El default implícito “sin regla ⇒ signer” cuando el OT sí tiene fila.

### Cambios operacionales
- Semilla en `CreateTransitOffice`.
- API `/api/v1/admin/ot/offices/{officeId}/mandatos` (OtModulePolicy).
- Pestaña Mandatos en el hub.

## ADRs relacionados

- [ADR-0036] — mandatarios y config por OT
- [ADR-0042] — documentos personalizados
- [ADR-0025] — baúl de firmas
- [ADR-0034] — identidad admin

## Notas para agentes

- **Backend Agent**: no ignorar `assignment_mode` del OT; no forzar genérica si el `signer` es solo del OT (sin regla de compañía).
- **Frontend Agent**: no deprecar Plataforma → Mandatos; tab id `mandatos` (no `mandatarios`, HU #11202).
- **QA Agent**: regresionar institucional, abierto con recuadro, identidad y baúl.
- **Security Agent**: ot_admin acotado a su `transit_office_id`.
- **Infra Agent**: sin cambio de deploy; entidad mandate_config sigue ExcludeFromMigrations.

## Referencias externas

- Requerimiento de producto `mandatos.txt` (sesión 2026-08-26).
