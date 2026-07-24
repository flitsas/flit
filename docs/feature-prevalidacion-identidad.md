# FEATURE — Prevalidación de Identidad

> **Versión:** BORRADOR v1
> **Fecha:** 2026-07-22
> **Estado:** CREADO en Azure DevOps — Feature **#10864** (Sprint 2), con 5 HUs hijas **#10865–#10869**. Descomposición en [descomposicion-hus-tramites.md](descomposicion-hus-tramites.md).
> **Origen:** Sección "4 - Prevalidación Identidad" (2 criterios aportados por el usuario), contrastados con el código real de `Flit.Tramites`.
> **Decisiones de producto:** cerradas con el usuario el 2026-07-22 (ver §6).
> **Features hermanos:** [Reglas transversales del ciclo de vida](feature-reglas-ciclo-vida-tramite.md) · [Gestión del Trámite](feature-gestion-tramite.md).

---

## 1. Metadatos propuestos del Feature (tentativos)

| Campo | Valor propuesto |
|-------|-----------------|
| **Título** | `[Trámites] Prevalidación de identidad: validaciones standalone ancladas a persona y reutilizables en trámites` |
| **Proyecto ADO** | FLIT - EVOLUTION |
| **Sprint** | El **siguiente** al activo (regla FLIT #1) — a confirmar con Tech Lead |
| **Story Points (estimación gruesa)** | **~23 SP** (indicativo, sujeto a descomposición en HUs — ver §7) |
| **Assignee** | Humano (regla FLIT #4) — p. ej. Willyn Londoño |
| **Tag** | DOR (tras validar DoR de Feature) |

> Los Story Points aquí son una estimación de encuadre. La cifra final y la separación backend/frontend por HU las define el `tech-lead-agent`.

---

## 2. Objetivo

Permitir **validar la identidad de una persona antes (y con independencia) de que exista un trámite**, mediante un módulo de prevalidación que crea validaciones **standalone** ancladas a una **entidad de persona a nivel tenant**, y que se **reutilizan automáticamente** cuando después se crea un trámite con esa persona como actor. Esto adelanta el cuello de botella de identidad (el enlace VID puede tardar) y evita gastar validaciones repetidas.

---

## 3. Descripción / Alcance

### Incluye

- **Entidad de persona/sujeto a nivel tenant** (nueva) donde anclar validaciones fuera del trámite.
- **Creación de validaciones standalone** (sin `procedure_instance_id`) desde un módulo de prevalidación.
- **Reutilización automática por referencia** de una prevalidación vigente al crear un trámite cuyo actor coincide por documento.
- **Cobertura de persona natural y jurídica** (esta última valida al representante legal).
- **Pantalla dedicada** de creación/gestión, enlazada desde la vista transversal `Validaciones.tsx` (que sigue siendo el listado de monitoreo).

### No incluye (fuera de alcance)

- Rediseño del ciclo de vida de la validación (webhook/reconciliación ya operan por Id propio, se reutilizan).
- Cambios en el proveedor Kyverum o en los mappers.
- Envío del enlace por SMS/WhatsApp (el canal actual es el correo del proveedor).
- Migración masiva de validaciones históricas a personas (ver §8 como consideración, no alcance).

### Aclaración terminológica

En FLIT **"OT" = Organismo de Tránsito**.

---

## 4. Criterios funcionales (mejorados y verificables)

> Formato: **enunciado → resultado medible → condición → dónde vive en el código → decisión cerrada**.
> Los AC en Gherkin (positivos / negativos / borde) se redactan en el paso siguiente.

### CF-00 · (Infraestructura) Entidad de persona/sujeto a nivel tenant

**Enunciado:** Existe una entidad de persona a nivel tenant, identificada por `(tenant_id, tipo_documento, número_documento)`, que agrupa las validaciones de identidad de esa persona con independencia del trámite.

- **Resultado:** una validación puede anclarse a una **persona** (`SubjectId`/`PersonId`) y, opcionalmente, a un trámite (`ProcedureInstanceId` **nullable**).
- **Reutiliza:** la clave de negocio ya existente `IdentidadKey(tenant, tipoDoc, documento)` y los datos de persona ya copiados en la fila de validación.
- **Dónde vive:** hoy **no existe** ninguna entidad persona/party/subject (solo `ProcedureInstanceActor`, hijo del trámite). Requiere entidad nueva + migración; `ProcedureInstanceBiometricValidation.ProcedureInstanceId` pasa a **nullable** y se agrega FK a persona.
- **Decisión cerrada (P1):** modelo con **entidad persona/sujeto a nivel tenant**.

### CF-01 · Crear prevalidación de identidad sin trámite

**Enunciado:** En el módulo de prevalidación, un operador crea una validación de identidad **sin trámite asociado**, aportando los datos de la persona; el sistema genera una **nueva validación** con su enlace, estados y vigencia.

- **Resultado:** `POST /biometric-validations` (sin `{instanceId}`) crea/asocia una **persona** y una validación con `ProcedureInstanceId = null`, estado inicial `pendiente_envio`/`enviado` y su `CaptureUrl`.
- **Datos mínimos (P6a):** `tipo + número de documento`, `nombre`, `correo` (obligatorios — el correo recibe el enlace).
- **Tipo de persona (P5):** natural (valida al titular) y jurídica (valida al **representante legal**, como resuelve hoy `IdentitySubjectResolver`, pero construido desde el body/persona, no desde un actor de trámite).
- **Quién crea (P6b):** **operador FLIT**.
- **Ciclo de vida:** idéntico al actual — webhook (`POST /webhooks/kyverum-verify/{validationId}`) y reconciliación operan por el **Id propio de la validación**, sin necesidad de instancia.
- **Dónde vive:** hoy el inicio (`IniciarKyverumVerifyHandler`, `POST /instances/{id}/biometric`) **exige instancia en borrador** y resuelve el sujeto desde el actor. Requiere endpoint/handler nuevo sin `{instanceId}` que construya el `IdentitySubject` desde la persona.
- **Decisión cerrada:** módulo con pantalla dedicada (P2), creación por operador (P6b), persona natural y jurídica (P5).

### CF-02 · Reutilización automática de la prevalidación al crear el trámite

**Enunciado:** Cuando se crea un trámite y un actor coincide (por `tenant + tipo/número de documento`) con una prevalidación **vigente y aprobada**, esa validación se **reutiliza por referencia**, sin gastar una nueva.

- **Resultado:** al iniciar el trámite, el actor queda validado con outcome `reusada` (referencia a la validación de la persona); no se re-envía enlace.
- **Vinculación (P3):** **automática por referencia** (sin confirmación del operador), como ya hace `EnsureIdentityHandler` hoy.
- **Vigencia (P4):** misma regla actual — **30 días** (`BiometricRules.VigenciaDias`), corte por `ValidUntil`, "por vencer" ≤7 días.
- **Cambio mínimo:** relajar el predicado `ProcedureInstance != null` en `FindVigenteApprovedByDocumentAsync` para que una prevalidación **standalone** también sea fuente reutilizable; resolver la referencia vía la **persona**.
- **Dónde vive:** `EnsureIdentityHandler` / `FindVigenteApprovedByDocumentAsync` (`ProcedureInstanceRepository.cs`), reuso por referencia (rediseño HU #10350, no clona).
- **Decisión cerrada:** reutilización automática por referencia, vigencia 30 días.

---

## 5. Trazabilidad al código real

| Criterio | Artefactos existentes | Estado hoy |
|----------|----------------------|-----------|
| CF-00 | `IdentidadKey(tenant, tipoDoc, documento)`; datos de persona copiados en `ProcedureInstanceBiometricValidation` | Clave por persona ya existe; **no hay entidad persona** (solo `ProcedureInstanceActor`) |
| CF-01 | `IniciarKyverumVerifyHandler`, `POST /instances/{id}/biometric`, `IdentitySubjectResolver` | Exige instancia en borrador + sujeto desde actor — falta camino standalone |
| CF-01 | `ProcedureInstanceBiometricValidation.ProcedureInstanceId` (FK **NOT NULL**) | Atadura dura de BD — hacer nullable + FK a persona |
| CF-01 | `Validaciones.tsx` (HU #10234) | Solo lista/monitorea — falta acción de creación (irá a pantalla dedicada) |
| CF-02 | `EnsureIdentityHandler`, `FindVigenteApprovedByDocumentAsync` (30d, por referencia) | Reuso por documento ya existe; exige `ProcedureInstance != null` — relajar |
| CF-02 | Webhook `PublicKyverumWebhookEndpoints`, reconciliación (por Id propio) | Ya independiente del trámite — se reutiliza |

---

## 6. Decisiones de producto cerradas (2026-07-22)

| Tema | Decisión |
|------|----------|
| P1 · Modelo de datos | **Entidad persona/sujeto a nivel tenant** (validación cuelga de persona; `ProcedureInstanceId` nullable) |
| P2 · Ubicación del módulo | **Pantalla dedicada** de creación/gestión + enlace desde `Validaciones.tsx` (que sigue como listado de monitoreo) |
| P3 · Vinculación al trámite | **Automática por referencia** (sin confirmación) |
| P4 · Vigencia | **Misma regla de 30 días** (consistente con el reuso actual) |
| P5 · Tipo de persona | **Natural y jurídica** (jurídica valida al representante legal) |
| P6a · Datos mínimos | `tipo + número documento`, `nombre`, `correo` |
| P6b · Quién crea | **Operador FLIT** |

---

## 7. Encuadre de esfuerzo y descomposición sugerida (indicativo)

| Criterio | Componente | SP aprox. |
|----------|-----------|-----------|
| CF-00 | Entidad persona/sujeto a nivel tenant + migración + repos | 8 |
| CF-01 | Endpoint/handler de inicio standalone (sujeto desde persona/body) + validación anclada a persona | 5 |
| CF-01 | Pantalla dedicada de creación/gestión + enlace desde `Validaciones.tsx` | 5 |
| CF-02 | Relajar reuso (`ProcedureInstance != null`) + vincular por persona al crear trámite | 3 |
| CF-02 | Vista transversal tolera validaciones sin instancia (columnas Trámite/Modalidad nullable) | 2 |
| **Total indicativo** | | **~23 SP** |

> La cifra final y la separación backend/frontend por HU las define el `tech-lead-agent`. CF-00 (entidad persona) es la pieza de mayor riesgo y prerequisito de las demás.

---

## 8. Riesgos y consideraciones

- **CF-00 · Entidad persona nueva:** es infraestructura que no existe hoy; prerequisito de todo el feature. Definir con `database-agent` schema, RLS/tenant, índice `(tenant_id, document_type, document_number)` único.
- **Backfill (consideración, no alcance):** decidir si las validaciones históricas (hijas de trámite) se asocian retroactivamente a personas, o si el catálogo de personas se construye solo hacia adelante. Recomendado: hacia adelante, evaluar backfill como fase posterior.
- **Habeas Data:** crear y conservar validaciones de identidad de personas sin trámite refuerza la necesidad de base legal/consentimiento (regla FLIT #5) — marcar como requisito de seguridad.
- **Integridad del reuso:** al colgar la validación de la persona, garantizar que el reuso por referencia (P3) no rompa la trazabilidad que hoy la vista transversal deriva del trámite (`ReferenceNumber`, `Modalidad`).
- **FK nullable:** revisar el `OnDelete(Cascade)` actual de la validación hacia el trámite al hacer la FK nullable, para no borrar validaciones standalone.

---

## 9. Próximos pasos

1. Validación humana de este borrador (objetivo, alcance, criterios).
2. Redacción de **AC en Gherkin** por criterio (positivos / negativos / borde).
3. Descomposición en HUs por capa (backend / frontend) vía `tech-lead-agent` — CF-00 primero (prerequisito).
4. Creación en Azure DevOps (gate humano: confirmación explícita antes de crear/activar).
