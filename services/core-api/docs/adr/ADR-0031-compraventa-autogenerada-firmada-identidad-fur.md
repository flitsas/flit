# ADR-0031 — Compraventa autogenerada firmada con la validación de identidad del FUR (traspaso)

- **Estado**: Aceptado · 2026-07-23
- **Módulo**: Trámites — Traspaso (TR), generación documental / firmas
- **Feature**: #10852 (punto 2B)
- **Relacionado / continúa**: ADR-0028 (firma de compraventa no bloqueante — aplazó la "lógica ideal de firmas")
- **Deciders**: Líder Técnico FLIT + PO
- **Tags**: arquitectura, backend, documental, firmas, traspaso

## Contexto

ADR-0028 desbloqueó el traspaso haciendo **no bloqueante** la firma de compraventa y aplazó la "lógica ideal de firmas" hasta que negocio la definiera. El Feature #10852 (punto 2B) define esa lógica para el documento: la compraventa de traspaso se **genera automáticamente** firmada por comprador y vendedor con **la misma información de validación de identidad con que se firma el FUR**, va por defecto en traspasos, se genera **solo si el usuario no cargó** una compraventa autenticada, y si la validación aún no se ejecutó se muestra **sin firmas**.

Estado actual: `FurCompraventaDocumentGenerator` ya produce un PDF real pero **no pinta firmas** (ignora `SellosIdentidad`/`FirmaImagenes` aunque ya viajan en `FurDocumentData`), se genera **siempre** en traspaso y el loop de persistencia **sobrescribe** (clobber) la compraventa que sube el usuario (`Source="user"`), sin distinguir origen.

## Decisión

Generar la compraventa automáticamente **solo cuando no exista** un adjunto `compraventa` con `Source != "system"`; pintar dos bloques de firma (comprador/vendedor) consumiendo `FurDocumentData.SellosIdentidad[rol]`; renderizar **sin firmas** si `!IdentidadValidada` o falta el sello del rol; y proteger del borrado idempotente la compraventa subida por el usuario. La firma se mantiene **no bloqueante** (coherente con ADR-0028).

## Alternativas consideradas

### Opción 1: Generación condicional + firma reutilizando `FurDocumentData` (RECOMENDADA)
**Pros:**
- Los sellos de identidad ya se resuelven y viajan en la data (costura mínima).
- Corrige el bug de clobber de la compraventa del usuario.
- Respeta la parametrización obligatorio/opcional por compañía (`CompanyDocumentParam`).
- Sin dependencia externa; no bloqueante.

**Cons:**
- Requiere enriquecer el cuerpo jurídico del PDF (validación de negocio del texto/cláusulas).

**Esfuerzo:** M · **Riesgos:** contenido legal (mitigado con revisión de negocio).

### Opción 2: Firma electrónica externa (ZapSign/portal) sobre la compraventa
**Pros:** firma con validez de proveedor.
**Cons:** ADR-0028 lo aplazó; gran esfuerzo; introduce SLA externo y flujo async.
**Esfuerzo:** L · **Riesgos:** alto.

### Opción 3: Compraventa solo con datos, sin firmas
**Pros:** cero cambio en el generador.
**Cons:** incumple el requerimiento (firmada por ambas partes).
**Esfuerzo:** S · **Riesgos:** incumplimiento funcional.

## Tradeoff aceptado

Opción 1: cumple el requisito reutilizando la infraestructura de firma del FUR (sello de validación de identidad / baúl) sin depender de proveedor externo, y sin reintroducir un gate bloqueante que ADR-0028 eliminó.

## Consecuencias

### Lo que se gana
- Compraventa siempre presente en el consolidado de traspaso, firmada cuando hay identidad vigente.
- Se corrige la sobrescritura de la compraventa autenticada subida por el usuario.

### Lo que se pierde
- La firma de la compraventa autogenerada es un **sello de validación de identidad**, no una firma electrónica certificada de proveedor externo (decisión consciente, coherente con ADR-0028).

### Cambios operacionales
- Ninguno en gates de ciclo de vida (sigue no bloqueante).

## ADRs relacionados
- [ADR-0028] — Firma de compraventa no bloqueante (este ADR **continúa** su "lógica ideal de firmas" aplazada; **no** reintroduce el gate bloqueante).
- [ADR-0025] — Baúl de firmas (fuente de firma para actor jurídico/NIT).

## Notas para agentes
- **Backend Agent**: en `FurCommand`, condicionar la autogeneración a la ausencia de `compraventa` `Source!="system"` y excluir del borrado idempotente la subida por el usuario. En `FurCompraventaDocumentGenerator`, pintar firmas desde `SellosIdentidad`/`FirmaImagenes`; sin firmas si `!IdentidadValidada`. Aplicar el membrete de ADR-0030.
- **Frontend Agent**: NA (la sección de firma sigue informativa por ADR-0028).
- **QA Agent**: casos: usuario carga compraventa → no se sobrescribe ni se autogenera; no carga + identidad vigente → autogenerada firmada; no carga + identidad pendiente → autogenerada sin firmas.
- **Security Agent**: la compraventa incluye datos personales y sellos de identidad (Habeas Data) — mantener controles de acceso al adjunto.
- **Infra Agent**: sin migración.

## Referencias externas
- Template histórico de referencia (proyecto legado): `content.buyingselling.hbs` (adaptado a QuestPDF).
