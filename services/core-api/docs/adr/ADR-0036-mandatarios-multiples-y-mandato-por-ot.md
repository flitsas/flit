# ADR-0036: Mandatarios múltiples por compañía y mandato configurable por OT

**Fecha**: 2026-07-24
**Status**: Aceptado
**Fecha de aceptación**: 2026-07-24
**Status previo**: Propuesto (2026-07-24)
**Deciders**: Líder Técnico FLIT (aceptado), Product Owner
**Tags**: arquitectura, backend, modulo-tramites, modulo-admin-ot, documental

**Supersedes**: [ADR-0023-firmante-mandato-exclusividad-modelo] — deroga la exclusividad "un solo mandatario activo por (OT, compañía)".

## Contexto

El expediente debe incluir el **Mandato** (contrato privado de mandato), documento autogenerado por OT + compañía gestora, exigido cuando el radicador es persona jurídica (NIT) y también para persona natural en ciertos OT (hoy Sabaneta). El requerimiento (`mandato-req.txt`) introduce tres necesidades que chocan con el modelo vigente:

1. **Varios mandatarios por compañía.** [ADR-0023] fija un índice único parcial (`uq_mandate_signer_companies_active` sobre `transit_office_id, company_tenant_id` WHERE `is_active`) que garantiza **un solo** mandatario activo por par. El negocio (validado con el PO) requiere **N mandatarios activos**, un mandatario aplicable a **varias** compañías, y elegir/filtrar el firmante al aprobar.
2. **Aplicabilidad del mandato por OT × tipo de persona.** Hoy ningún componente cruza "OT" con "PN/PJ": `TramiteDocumentContext` no conoce el OT. Sabaneta exige mandato también a PN; el resto solo a PJ.
3. **Firmante = usuario que se autentica.** El mandatario que firma debe resolverse contra el usuario autenticado. Pero `identity.users` **no captura documento** (solo email + display_name; la invitación es email + rol), así que no hay documento contra el cual cotejar.

Restricciones: no romper trámites en producción; reutilizar las costuras existentes ([ADR-0025] baúl, [ADR-0034] validación de identidad admin); mantener RLS por tenant y auditoría.

## Decisión

**(1)** Sustituir la exclusividad de [ADR-0023] por **multiplicidad**: varios mandatarios activos por `(OT, compañía)`, relación mandatario↔compañía M:N. **(2)** La **aplicabilidad del mandato** se resuelve por configuración **del OT** (tabla propia llaveada por `transit_office_id`), inyectada al motor de reglas documentales como un puerto. **(3)** El firmante se auto-resuelve **vinculando el mandatario a su cuenta de usuario** (`mandate_signers.user_id` → `identity.users`) y cotejando por `user_id == usuario autenticado`; sin match, selección manual al aprobar.

## Alternativas consideradas

### Opción 1: Multiplicidad + config por OT en tabla propia + enlace por `user_id` (elegida)

**Pros:**
- Deroga la exclusividad tocando **solo el índice** (`(transit_office_id, company_tenant_id, mandate_signer_id)` WHERE `is_active`); sin migración de datos.
- Config de mandato en `admin.transit_office_mandate_config` (llave `transit_office_id`, catálogo RUNT, `ON DELETE RESTRICT`, **sin `tenant_id`**): la regla pertenece al OT y la consumen todas las compañías gestoras. Se expone al motor de reglas con el mismo patrón de puerto que `ISignatureVaultPolicy`/`IRnmcRequirementPolicy`.
- Enlace `user_id` da match **exacto** por identidad de cuenta; reutiliza el `changedByUserId` que la transición ya recibe. Cero cambios en login/claims.
- Reusa [ADR-0034] (validación de identidad admin agnóstica del sujeto) con `subject_type='mandate_signer'`: **cero cambios de esquema** en `admin_identity_validations`.

**Cons:**
- Deroga una decisión ya aceptada; obliga a revisar `ListActiveCompanyResolutionsAsync` (cambia de cardinalidad 1→N) y sus 3 tests.
- Un mandatario sin `user_id` nunca auto-firma (siempre cae en selección manual).

**Esfuerzo:** L
**Riesgos:** cambio de cardinalidad en la vista consolidada RF34; mitigable con tests de agregación.

### Opción 2: Mantener exclusividad, "varios" vía compañías desdobladas

Modelar cada mandatario adicional como un sub-tenant/όcompañía ficticia para no tocar el índice único.

**Pros:** no deroga [ADR-0023].
**Cons:** modela una mentira de dominio (compañías inexistentes); rompe reportes y grants por compañía; el firmante deja de ser trazable a una persona real.
**Esfuerzo:** M
**Riesgos:** deuda semántica alta; corrompe la analítica por compañía.

### Opción 3: Multiplicidad + cotejo por **documento** capturado en el usuario

Añadir `document_type/document_number` a `identity.users`, pedirlo en la invitación/alta y emitirlo como claim; filtrar el mandatario por documento.

**Pros:** el "documento del mandatario = documento del usuario" queda literal.
**Cons:** toca `identity.users`, invitaciones, registro y **claims/login** (superficie de seguridad); el cotejo por documento es frágil (tipo/formato/normalización); no todo usuario tiene por qué exponer su cédula.
**Esfuerzo:** L
**Riesgos:** cambios en autenticación e identidad para todos los usuarios, no solo mandatarios.

## Tradeoff aceptado

Se prefiere la Opción 1 porque **el firmante ES el usuario que se autentica**: enlazarlo por `user_id` es la representación fiel y da un match determinista sin ampliar la superficie de identidad/seguridad de toda la plataforma (Opción 3) ni corromper el dominio de compañías (Opción 2). El costo —derogar la exclusividad de [ADR-0023] y revisar la vista RF34— es acotado y sin migración de datos. El documento del mandatario se conserva como dato del PDF, no como llave de cotejo.

## Consecuencias

### Lo que se gana
- Varios mandatarios por compañía y un mandatario para varias compañías, con firmante auto-resuelto por identidad de cuenta.
- Mandato exigible por OT × PN/PJ sin `if` de OT esparcidos: una regla condicional (`ConditionalEffect.Add`, tipo `mandato`) alimentada por un puerto.
- Reuso de baúl ([ADR-0025]) e identidad admin ([ADR-0034]); mandato y solicitud virtual se generan/adjuntan/consolidan con el mismo pipeline que FUR/RUES/RNMC ([ADR-0031], [ADR-0032]).

### Lo que se pierde
- La garantía "un firmante por compañía" de [ADR-0023] deja de existir; la responsabilidad de elegir pasa a la auto-resolución + selección al aprobar.
- Un mandatario sin cuenta de usuario vinculada no participa de la auto-firma.

### Cambios operacionales
- Migración: `ALTER TABLE admin.mandate_signers` (+`document_type`, `email`, `signature_vault_id`, `identity_validation_ref`, `user_id`); reemplazo del índice único; nueva `admin.transit_office_mandate_config` (seed Sabaneta/Bello); `tramites.procedure_instances.mandate_signer_id`; alta de `mandato`/`tramite_virtual` en las matrices documentales.
- `POST /instances/{id}/transition` acepta `mandateSignerId?`; al aprobar sin match y con >1 mandatario responde `mandatario_requerido` (409).
- Al fijar el firmante se regenera el mandato y se invalida el consolidado (cascada ya existente, HU #10860).

## ADRs relacionados

- [ADR-0023-firmante-mandato-exclusividad-modelo] — **superseded** por este ADR (exclusividad → multiplicidad).
- [ADR-0025-baul-firmas-custodia-y-consumo] — fuente de la imagen de firma del mandatario (precedencia baúl > identidad).
- [ADR-0034-validacion-identidad-admin-desacoplada] — bloque agnóstico reutilizado con `subject_type='mandate_signer'`.
- [ADR-0031-compraventa-autogenerada-firmada-identidad-fur] / [ADR-0032-regeneracion-consolidado-tras-rechazo] — patrón de documento autogenerado + regeneración/consolidado.
- [ADR-0028-firma-compraventa-automatica-traspaso] — precedente de firma automática visible solo en estado ≠ borrador.

## Notas para agentes

- **Backend Agent**: derogar `uq_mandate_signer_companies_active` por el índice de 3 columnas; `mandate_signers.user_id` con `ON DELETE SET NULL`; `IMandateRequirementPolicy` (Domain) + adaptador (Infrastructure) + `NullMandateRequirementPolicy` para tests; `TramiteDocumentContext.ExigeMandato`; enganche condicional en `GenerarFurHandler` con la misma forma que `certificado_rues` (generar-o-limpiar, idempotente); cotejo del firmante por `changedByUserId`.
- **Frontend Agent**: `MandatarioFormPanel` gana tipo de documento, correo, **selector de cuenta de usuario de OT** y quita el bloqueo de "compañía ya tomada"; columna Identidad con badge + reenvío; sección Mandato en la ficha del OT; diálogo de selección de mandatario al aprobar cuando la API responde `mandatario_requerido`. Aplicar `flit-design-guardian`.
- **QA Agent**: casos de 0/1/N mandatarios; match y no-match por `user_id`; PN en Sabaneta exige mandato y en otros OT no; `tramite_virtual` siempre presente; regeneración al aprobar e invalidación del consolidado; texto legal literal vs. plantilla legacy.
- **Security Agent**: `document_number`/`email` del mandatario son PII (Ley 1581) — no loguear ni exponer en errores; RLS y auditoría en las tablas nuevas; el `user_id` no debe permitir enumeración cross-tenant.
- **Infra Agent**: DDL idempotente (`IF NOT EXISTS` + guardas), migración EF con Infrastructure como startup; seed Sabaneta/Bello y de matrices documentales.

## Referencias externas

- Resolución 12379 de 2012 (Min. Transporte, Art. 5) y Resolución 20233040017145 de 2023 — marco legal del mandato citado en las plantillas.
- Ley 1581 de 2012 — protección de datos personales (PII del mandatario).
