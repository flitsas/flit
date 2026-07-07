# ADR-0023 — Firmantes de mandato: exclusividad compañía↔mandatario y modelo de datos

- **Estado**: Propuesto · 2026-07-07
- **Módulo**: Admin OT (Firmantes de Mandato / "Mandatario")
- **Requerimientos**: RF22, RF23, RF24, RF25, RF26, RF27, RF33, RF34, RF28 (hoja `AdminOT`)
- **Decide**: Líder Técnico + PO (por desviación de RF25)

## Contexto

En los trámites de tránsito, el propietario del vehículo autoriza a una compañía gestora
mediante un **mandato/poder** que debe ir firmado por una persona autorizada: el **mandatario**
(firmante de mandato). FLIT debe permitir configurar, por Organismo de Tránsito (OT), qué
mandatario firma los mandatos de cada compañía gestora.

Hoy **no existe** módulo de firmantes de mandato en el repo (0 referencias reales). Sí existe:

- El modelo de **grants compañía↔OT** (`admin.tenant_transit_office_grants`, `transit-grants`).
- La **auditoría** de configuración de OT (`admin.tenant_config_audit_logs`), reutilizada en la
  Oleada 1 (ciclo de vida OT, ADR previo / HU #10516–#10518).
- La infraestructura de **firma de trámites** (`procedure_instance_signatures`,
  `ISignatureProvider`) y el mandato como documento del trámite.
- El **gate de operabilidad** `IOtOperabilityGate` / `OtOperabilityGate` (Oleada 1).

El requisito **RF25** pide *"asociar múltiples firmantes de mandato para una misma combinación
de compañía gestora y OT"*. La visión de producto acordada con el usuario, en cambio, pide
**exclusividad**: una compañía pertenece a un único mandatario dentro del OT (evita ambigüedad
al decidir quién firma un mandato dado). Esta contradicción es el eje de este ADR.

## Decisiones de producto ya acordadas (entradas)

1. Registro de mandatario = **nombre + número de documento + hash** (autogenerado).
2. El **hash es una huella de integridad** del mandatario: `SHA-256(nombre + número de documento
   + fecha de registro)`. Se estampa/referencia en cada mandato generado; se regenera al editar
   los datos, y los mandatos ya emitidos conservan la huella con la que se generaron.
3. Un mandatario puede asignarse a **varias compañías**.
4. **Inactivar** = baja lógica (soft-delete); **libera** sus compañías para reasignación. No es
   un campo del formulario, es una acción.
5. Compañía **sin mandatario** (RF26): al generar el mandato solo **advertir**, no bloquear.
6. Formulario **mínimo**: nombre + número + hash (readonly). Sin tipo de documento.

## Decisión

### 1. Exclusividad estricta (alternativa recomendada)

Una combinación **(OT, compañía gestora)** tiene **como máximo un mandatario activo**. Se aplica
con un **índice único parcial** sobre la relación mandatario↔compañía filtrado por registros
activos. La UI muestra las compañías ya tomadas por otro mandatario **deshabilitadas** con la
leyenda "ya tiene mandatario: {nombre}" (mismo patrón de `OTMatrix`, Oleada 1).

> **Desviación de RF25**: RF25 pedía múltiples firmantes por (OT, compañía). Esta decisión lo
> restringe a uno. Requiere **visto bueno del PO**. Justificación: elimina la ambigüedad de
> "¿cuál de N firmantes firma este mandato?" y simplifica la regla de uso (RF33). Si el PO
> exige RF25 literal, se adopta la **Alternativa B** (ver abajo) sin cambiar el modelo de datos,
> solo relajando el índice único a "un vigente por compañía".

### 2. Modelo de datos (schema `admin`)

**`admin.mandate_signers`** — el mandatario:

| Columna | Tipo | Nota |
|---|---|---|
| `id` | uuid PK | |
| `transit_office_id` | uuid | OT al que pertenece (ámbito Admin OT) |
| `full_name` | text | nombre del mandatario |
| `document_number` | text | **PII** (Ley 1581) — no loguear, no exponer en errores |
| `integrity_hash` | text | `SHA-256(full_name + document_number + registered_at)` |
| `registered_at` | timestamptz | insumo del hash; fija en el registro |
| `is_active` | boolean | baja lógica (o `deleted_at timestamptz null`) |
| `created_at/by`, `updated_at/by` | | auditoría base |

**`admin.mandate_signer_companies`** — asignación mandatario↔compañía:

| Columna | Tipo | Nota |
|---|---|---|
| `id` | uuid PK | |
| `mandate_signer_id` | uuid FK → mandate_signers | |
| `transit_office_id` | uuid | denormalizado para el índice de exclusividad |
| `company_tenant_id` | uuid | compañía gestora (con grant en el OT) |
| `created_at` | timestamptz | |

**Exclusividad:** índice único parcial
`UNIQUE (transit_office_id, company_tenant_id) WHERE <asignación activa>`
(activo = el mandatario padre está activo). Al inactivar el mandatario, sus filas dejan de
contar para el índice → sus compañías quedan libres para reasignar.

**Aislamiento (concreción):** estas tablas **no usan RLS**; el aislamiento es a nivel de
aplicación por `transit_office_id` (patrón `catalogs`), con lectura cross-tenant vía
`SET LOCAL row_security = off` y escritura/auditoría atribuida al tenant del OT (resuelto desde
`transit_office_profiles`). Coherente con el reader de estado operativo de la Oleada 1.
**Condición de seguridad:** toda query filtra por `transit_office_id` y el acceso está gated por
`OtModulePolicy` (SuperAdmin/ot_admin). El `document_number` (PII) no se audita ni se loguea.

### 3. Comportamientos

- **RF22–RF27 (CRUD + consulta):** crear, editar (regenera hash), inactivar (soft-delete que
  libera compañías), listar/consultar por OT y por compañía.
- **Inactivación visible + reactivación (concreción de implementación):** al inactivar, el
  mandatario **no desaparece**; queda visible con estado "Inactivo" y sus compañías liberadas
  ("—"). Se puede **reactivar** (`POST .../{id}/reactivate`, auditado `is_active: false→true`)
  **sin restaurar** las compañías (pudieron ser tomadas por otro mandatario); se reasignan con
  "Editar". Los activos se listan antes que los inactivos.
- **RF28 (auditoría):** todo cambio (alta, edición, inactivación, reasignación de compañías) se
  registra en `admin.tenant_config_audit_logs` (patrón Oleada 1) en la misma transacción.
- **RF33 (regla de uso):** antes de usar un mandatario para una compañía, validar que la
  compañía esté **activa y no bloqueada** en el OT (engancha con `IOtOperabilityGate` / grants).
- **RF34 (vista):** exponer el mandatario configurado en la vista consolidada de compañías por
  OT (`client-procedures` / matriz resuelta).
- **RF26 (sin mandatario):** al generar el mandato de una compañía sin mandatario asignado →
  **advertencia** no bloqueante.

## Alternativas consideradas

### Alternativa A — Exclusividad estricta (RECOMENDADA)
Un mandatario activo por (OT, compañía). Índice único parcial.
- (+) Sin ambigüedad de firmante; regla de uso (RF33) trivial; UX clara (patrón OTMatrix ya
  probado).
- (+) Menor superficie de error operativo y legal.
- (−) **Desvía RF25** → requiere aprobación del PO.
- Esfuerzo: **medio**. Riesgo: bajo (técnico); medio (aprobación de requisito).

### Alternativa B — Varios firmantes, uno "vigente" por compañía
Se permiten N mandatarios por (OT, compañía) pero solo **uno vigente**; los demás quedan como
histórico/inactivos.
- (+) Respeta RF25 literal (varios pueden existir) sin perder claridad de "quién firma hoy".
- (−) Requiere lógica de "vigencia" y su cambio (más estados, más auditoría).
- (−) UI más compleja (elegir vigente).
- Esfuerzo: **medio-alto**. Riesgo: medio.

### Alternativa C — Varios firmantes activos simultáneos (RF25 puro)
Cualquier mandatario activo de la compañía puede firmar.
- (+) Cumple RF25 sin restricciones; redundancia (cubre ausencias).
- (−) Ambigüedad: hay que elegir el firmante en cada mandato → decisión trasladada al runtime.
- (−) RF33 y la generación del mandato se complican (selección de firmante).
- Esfuerzo: **alto**. Riesgo: alto (ambigüedad funcional).

## Consecuencias por agente

- **Backend:** nuevas tablas + migración EF Core; CRUD en Clean Architecture; índice único
  parcial de exclusividad; auditoría en `tenant_config_audit_logs`; regla RF33 vía
  `IOtOperabilityGate`; hash de integridad determinista.
- **Frontend:** tab "Mandatario" en el hub de Admin OT (`[id]/mandatarios`); formulario mínimo;
  multiselect con compañías tomadas deshabilitadas (patrón OTMatrix); 4 estados UI + WCAG 2.1 AA.
- **QA:** casos de exclusividad (rechazar compañía ya tomada), soft-delete libera compañía,
  regeneración de hash al editar, advertencia RF26, RF33 bloquea compañía bloqueada/inactiva.
- **Security:** `document_number` es PII (Ley 1581) → no loguear, no exponer en errores; el hash
  **no** es mecanismo de anonimización (es integridad). Revisar exposición en respuestas.
- **Infra:** una migración nueva; sin cambios de despliegue.

## Requisito vs decisión (trazabilidad)

| RF | Estado con esta decisión |
|----|--------------------------|
| RF22–RF24, RF27 | Cubiertos (CRUD + consulta) |
| RF25 | **Desviado** (exclusividad) — pendiente OK del PO; fallback = Alternativa B |
| RF26 | Cubierto (advertir, no bloquear) |
| RF33 | Cubierto (validación de compañía activa/no bloqueada) |
| RF34 | Cubierto (mandatario en vista consolidada) |
| RF28 | Cubierto (auditoría) |

## Estado y aceptación

Este ADR queda en **Propuesto**. Pasa a **Aceptado** solo mediante PR de aceptación del Líder
Técnico humano (regla FLIT 15). La desviación de RF25 requiere confirmación explícita del PO.
