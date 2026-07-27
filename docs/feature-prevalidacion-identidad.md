# FEATURE — Prevalidación de Identidad

> **Versión:** BORRADOR v2 — amplía el alcance con **CF-03 (edición + reenvío)**
> **Fecha:** 2026-07-22 · **Actualizado:** 2026-07-27
> **Estado:** CREADO en Azure DevOps — Feature **#10864** (Sprint 2), con 7 HUs hijas: **#10865–#10869** (alcance base) y **#10943/#10944** (ampliación CF-03). Descomposición en [descomposicion-hus-tramites.md](descomposicion-hus-tramites.md).
> **CF-03:** criterio añadido al Feature en ADO el 2026-07-27 (rev 5); HUs **#10943** (BACKEND, 5 SP) y **#10944** (FRONTEND, 3 SP) creadas en estado `New`.
> **Origen:** Sección "4 - Prevalidación Identidad" (2 criterios aportados por el usuario) + ampliación del usuario del 2026-07-27 (edición y reenvío), contrastados con el código real de `Flit.Tramites`.
> **Decisiones de producto:** cerradas con el usuario el 2026-07-22 (P1–P6b) y el 2026-07-27 (D7–D11) — ver §6.
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
- **Edición acotada** de la prevalidación (solo registros standalone) con **reenvío automático al cambiar el correo**, y **acción explícita de reenvío** de la validación de identidad.

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

### CF-03 · Edición acotada de la prevalidación y reenvío de la validación

**Enunciado:** Desde el módulo de prevalidación, el operador puede **editar** los datos de contacto de un registro de prevalidación **standalone** (los únicos editables) y **reenviar** la validación de identidad. Si el cambio es del **correo**, el reenvío es **automático** al nuevo correo.

- **Alcance de edición (D7):** editables `nombre` y `correo` (y `nombre` / `correo` del representante legal si la persona es jurídica). **NO editables:** `tipo` y `número de documento` — del titular ni del RL — porque definen la identidad (`BiometricRules.IdentidadKey`); corregirlos equivale a otra persona → se anula y se crea una prevalidación nueva.
- **Solo registros standalone:** editable ⟺ `ProcedureInstanceId IS NULL AND PersonId IS NOT NULL`. Una validación nacida dentro de un trámite es **de solo lectura** en este módulo (se gestiona desde el trámite).
- **Reenvío automático por cambio de correo (D8):** al persistir un correo distinto (comparación case-insensitive, `trim`), se dispara el reenvío **en la misma transacción**; el operador no hace nada más. Cambiar solo el nombre **no** reenvía (actualiza persona + fila; si el nombre debe llegar al proveedor, el operador reenvía manualmente).
- **Reenvío manual (D8):** acción explícita sobre el registro, sin editar nada — caso "no me llegó el correo" o enlace vencido.
- **Semántica del reenvío (D8):** se opera sobre el **mismo registro** de validación (no se crea otro): nuevo token/enlace, `ExpiresAt = now + TokenTtlHoras (24 h)`, `Status → enviado` (mock) / `en_proceso` (Kyverum) o `pendiente_envio` si el proveedor falla transitoriamente, `Attempts = 0` y `ReconcilePollCount = 0`. La **vigencia de 30 días no se toca**: `ValidUntil` se estampa al aprobar (`Approve`), como siempre.
- **Correo destino:** el del **sujeto** de la validación, tal como lo resuelve `ResolveSubject` — persona natural → correo de la persona; jurídica → correo del representante legal (con fallback al de la persona).
- **Estados permitidos (D9):** `enviado`, `en_proceso`, `pendiente_envio`, `error_envio`, `expirado`, `rechazado`. **Bloqueado** si la validación está `aprobado` — vigente (hay identidad válida, no hay nada que reenviar) **o vencida** (es un registro histórico con certificado; revalidar = crear una prevalidación **nueva** vía `POST /biometric-validations`, preservando la traza anterior).
- **Tope de reenvíos (D10):** máximo **3 reenvíos** por validación y **cooldown de 5 minutos** entre reenvíos. Un cambio de correo consume cupo igual que un reenvío manual. Requiere dos columnas nuevas: `resend_count` y `last_resent_at`.
- **Guard de trámite (D11):** si la prevalidación ya está **referenciada por un trámite**, se bloquea la edición y el reenvío. En la práctica es defensa en profundidad: el reuso del CF-02 solo toma validaciones **aprobadas y vigentes**, que ya quedan bloqueadas por D9.
- **Dónde vive:** hoy **no existe** — no hay `PUT/PATCH` ni endpoint de reenvío de validaciones ([BiometricaEndpoints.cs](../services/core-api/src/Flit.Api/Endpoints/Tramites/BiometricaEndpoints.cs) solo expone crear, listar, simular, certificado, audit y reconcile). El único `Resend*` del repo es el de invitaciones de usuario (`ResendInvitationHandler`, módulo Security), que no aplica. Requiere: 2 endpoints nuevos, un handler de edición/reenvío, migración con `resend_count` / `last_resent_at`, y acciones en la pantalla dedicada.
- **Contrato propuesto:**
  - `PATCH /api/v1/tramites/biometric-validations/{id}` — body `{ name?, email?, legalRepName?, legalRepEmail? }` → `200 { validation, captureUrl?, resent: bool }`.
  - `POST /api/v1/tramites/biometric-validations/{id}/resend` → `200 { validation, captureUrl }` · `202` si quedó encolada.
  - Errores: `403 no_editable` (validación ligada a trámite), `409 identidad_aprobada` (D9), `409 referenciada_por_tramite` (D11), `422 documento_no_editable` (D7), `429 reenvio_en_cooldown` / `429 tope_reenvios` (D10), `502/503` proveedor.
- **Auditoría / Habeas Data:** el cambio de correo es PII (`@pii:high`) → se registra en `IdentityValidationAuditEvent` con valores **enmascarados** (antes → después), autor y timestamp. Nunca en claro en logs.
- **Idempotencia con Kyverum:** al reenviar se obtiene un `KyverumVerificationId` nuevo sobre la misma fila; los webhooks del intento anterior deben **descartarse** por no coincidir con el id vigente.
- **Decisiones cerradas:** D7–D11 (§6).

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
| CF-03 | `BiometricaEndpoints` (crear / listar / simular / certificado / audit / reconcile) | **No hay** `PATCH` ni `resend` de validaciones — endpoints nuevos |
| CF-03 | `ProcedureInstanceBiometricValidation` (`Attempts`, `ReconcilePollCount`, `ExpiresAt`, `TokenHash`) | La entidad ya contempla el (re)envío (`ReconcilePollCount` se reinicia en él); faltan `resend_count` / `last_resent_at` |
| CF-03 | `IniciarPrevalidacionHandler.ResolveSubject` (natural → persona; jurídica → RL) | Reutilizable tal cual para resolver el correo destino del reenvío |
| CF-03 | `PersonRepository.FindOrCreateAsync` (pisa nombre/correo, nunca documento) | Ya alinea con D7 — el update de persona no toca el documento |
| CF-03 | `IdentityValidationAuditEvent` | Existe — se le añaden los eventos de edición y reenvío |
| CF-03 | `ResendInvitationHandler` (módulo Security) | Referencia de patrón de reenvío; **no** reutilizable (otro dominio) |

---

## 6. Decisiones de producto cerradas

### 6.1 · Alcance original (2026-07-22)

| Tema | Decisión |
|------|----------|
| P1 · Modelo de datos | **Entidad persona/sujeto a nivel tenant** (validación cuelga de persona; `ProcedureInstanceId` nullable) |
| P2 · Ubicación del módulo | **Pantalla dedicada** de creación/gestión + enlace desde `Validaciones.tsx` (que sigue como listado de monitoreo) |
| P3 · Vinculación al trámite | **Automática por referencia** (sin confirmación) |
| P4 · Vigencia | **Misma regla de 30 días** (consistente con el reuso actual) |
| P5 · Tipo de persona | **Natural y jurídica** (jurídica valida al representante legal) |
| P6a · Datos mínimos | `tipo + número documento`, `nombre`, `correo` |
| P6b · Quién crea | **Operador FLIT** |

### 6.2 · Ampliación CF-03 — edición y reenvío (2026-07-27)

| Tema | Decisión |
|------|----------|
| D7 · Campos editables | `nombre` y `correo` (+ los del RL en jurídica). **Documento NO editable** (define la identidad) → corregirlo = anular y crear nueva |
| D8 · Semántica del reenvío | **Mismo registro**: nuevo enlace/token, TTL 24 h, `Attempts`/`ReconcilePollCount` a 0. Cambio de correo ⇒ reenvío **automático**; además, acción manual de reenvío |
| D9 · Estados permitidos | `enviado`, `en_proceso`, `pendiente_envio`, `error_envio`, `expirado`, `rechazado`. **Bloqueado en `aprobado`** (vigente o vencida): revalidar = prevalidación nueva |
| D10 · Tope | **3 reenvíos** por validación + **cooldown de 5 min**. El cambio de correo consume cupo |
| D11 · Trámite en curso | Bloqueo si la prevalidación ya está referenciada por un trámite (defensa en profundidad: el reuso solo toma aprobadas vigentes, ya bloqueadas por D9) |
| D12 · Solo standalone | Editable ⟺ `ProcedureInstanceId IS NULL`. Las validaciones nacidas en un trámite son de solo lectura en este módulo |

---

## 7. Encuadre de esfuerzo y descomposición sugerida (indicativo)

| Criterio | Componente | SP aprox. |
|----------|-----------|-----------|
| CF-00 | Entidad persona/sujeto a nivel tenant + migración + repos | 8 |
| CF-01 | Endpoint/handler de inicio standalone (sujeto desde persona/body) + validación anclada a persona | 5 |
| CF-01 | Pantalla dedicada de creación/gestión + enlace desde `Validaciones.tsx` | 5 |
| CF-02 | Relajar reuso (`ProcedureInstance != null`) + vincular por persona al crear trámite | 3 |
| CF-02 | Vista transversal tolera validaciones sin instancia (columnas Trámite/Modalidad nullable) | 2 |
| CF-03 | Edición acotada + reenvío (2 endpoints, handler, migración `resend_count`/`last_resent_at`, auditoría) | 5 |
| CF-03 | Acciones "Editar" y "Reenviar" en la pantalla dedicada (confirmación, cooldown, estados deshabilitados) | 3 |
| **Total indicativo** | | **~31 SP** (23 base + 8 de la ampliación CF-03) |

> La cifra final y la separación backend/frontend por HU las define el `tech-lead-agent`. CF-00 (entidad persona) es la pieza de mayor riesgo y prerequisito de las demás.

---

## 8. Riesgos y consideraciones

- **CF-00 · Entidad persona nueva:** es infraestructura que no existe hoy; prerequisito de todo el feature. Definir con `database-agent` schema, RLS/tenant, índice `(tenant_id, document_type, document_number)` único.
- **Backfill (consideración, no alcance):** decidir si las validaciones históricas (hijas de trámite) se asocian retroactivamente a personas, o si el catálogo de personas se construye solo hacia adelante. Recomendado: hacia adelante, evaluar backfill como fase posterior.
- **Habeas Data:** crear y conservar validaciones de identidad de personas sin trámite refuerza la necesidad de base legal/consentimiento (regla FLIT #5) — marcar como requisito de seguridad.
- **Integridad del reuso:** al colgar la validación de la persona, garantizar que el reuso por referencia (P3) no rompa la trazabilidad que hoy la vista transversal deriva del trámite (`ReferenceNumber`, `Modalidad`).
- **FK nullable:** revisar el `OnDelete(Cascade)` actual de la validación hacia el trámite al hacer la FK nullable, para no borrar validaciones standalone.
- **CF-03 · Costo con el proveedor:** cada reenvío abre una verificación nueva en Kyverum y probablemente **se factura**. El tope de 3 + cooldown de 5 min (D10) acota el gasto y el abuso; validar la cifra con el contrato del proveedor.
- **CF-03 · Webhooks huérfanos:** tras un reenvío, el `KyverumVerificationId` anterior queda obsoleto. Si el webhook del intento viejo llega después, debe descartarse comparando contra el id vigente de la fila — si no, un rechazo antiguo puede pisar una validación en curso.
- **CF-03 · Carrera con el sujeto:** el usuario puede estar completando la captura justo cuando el operador reenvía. El reenvío invalida el token/enlace anterior; comunicarlo en la UI ("el enlace anterior dejará de funcionar").
- **CF-03 · Habeas Data:** el correo es PII alta. La edición debe auditarse con valores enmascarados y el consentimiento (`PersonDataConsent`) sigue anclado a la persona, no a la validación.

---

## 9. Criterios de aceptación (Gherkin) — CF-03

> Los AC de CF-00/CF-01/CF-02 viven en las HUs #10865–#10869 ya creadas. Aquí solo los de la ampliación.

### AC-01 (positivo) · Editar el correo reenvía automáticamente

```gherkin
Dado que existe una prevalidación standalone de "Ana Ríos" (CC 1020304050) en estado "enviado"
  Y su correo actual es "ana.rios@old.com"
Cuando el operador FLIT edita el registro y guarda el correo "ana.rios@new.com"
Entonces el sistema actualiza el correo en la persona y en la validación
  Y reenvía la validación de identidad al correo "ana.rios@new.com" sin acción adicional
  Y genera un enlace de captura nuevo con vigencia de 24 horas
  Y el enlace anterior deja de ser válido
  Y la respuesta indica "reenviado = true"
  Y el contador de reenvíos del registro pasa de 0 a 1
```

### AC-02 (positivo) · Reenvío manual sin editar

```gherkin
Dado que existe una prevalidación standalone en estado "enviado" con 0 reenvíos
  Y el último envío fue hace 30 minutos
Cuando el operador FLIT ejecuta la acción "Reenviar validación"
Entonces el sistema envía un enlace nuevo al mismo correo del sujeto
  Y la validación queda en estado "enviado" (mock) o "en_proceso" (Kyverum)
  Y los intentos y el contador de sondeos de reconciliación se reinician en 0
  Y el contador de reenvíos pasa de 0 a 1
```

### AC-03 (positivo) · Persona jurídica reenvía al representante legal

```gherkin
Dado que existe una prevalidación standalone de una persona jurídica (NIT 900123456)
  Y su representante legal es "Luis Pardo" con correo "luis@empresa.com"
Cuando el operador FLIT edita el correo del representante legal a "lpardo@empresa.com"
Entonces el reenvío se dirige a "lpardo@empresa.com"
  Y el sujeto de la validación sigue siendo el representante legal, no el NIT
```

### AC-04 (positivo) · Editar solo el nombre no reenvía

```gherkin
Dado que existe una prevalidación standalone en estado "enviado"
Cuando el operador FLIT corrige únicamente el nombre y guarda
Entonces el sistema actualiza el nombre en la persona y en la validación
  Y NO reenvía la validación
  Y la respuesta indica "reenviado = false"
  Y el contador de reenvíos no cambia
```

### AC-05 (negativo) · Validación de un trámite no es editable aquí

```gherkin
Dado que existe una validación biométrica creada dentro del trámite "TR-2026-000123"
Cuando el operador FLIT intenta editarla o reenviarla desde el módulo de prevalidación
Entonces el sistema responde 403 "no_editable"
  Y la interfaz muestra ese registro en modo solo lectura, sin acciones de editar ni reenviar
```

### AC-06 (negativo) · Documento no editable

```gherkin
Dado que existe una prevalidación standalone con documento CC 1020304050
Cuando el operador FLIT intenta cambiar el tipo o el número de documento
Entonces el sistema responde 422 "documento_no_editable"
  Y el mensaje indica que debe anular el registro y crear una prevalidación nueva
  Y ni la persona ni la validación cambian de documento
```

### AC-07 (negativo) · Identidad aprobada y vigente

```gherkin
Dado que existe una prevalidación standalone en estado "aprobado" con vigencia hasta dentro de 12 días
Cuando el operador FLIT intenta editar el correo o reenviar la validación
Entonces el sistema responde 409 "identidad_aprobada"
  Y la validación conserva su estado, su fecha de aprobación y su vigencia
```

### AC-08 (borde) · Aprobada y vencida → revalidar creando una nueva

```gherkin
Dado que existe una prevalidación standalone en estado "aprobado" cuya vigencia venció hace 3 días
Cuando el operador FLIT intenta reenviar esa validación
Entonces el sistema responde 409 "identidad_aprobada"
  Y la interfaz ofrece la acción "Nueva prevalidación" para la misma persona
  Y al ejecutarla se crea un registro nuevo, conservando intacto el histórico aprobado
```

### AC-09 (borde) · Cooldown entre reenvíos

```gherkin
Dado que existe una prevalidación standalone reenviada hace 2 minutos
Cuando el operador FLIT intenta reenviarla de nuevo
Entonces el sistema responde 429 "reenvio_en_cooldown"
  Y el mensaje indica cuántos minutos faltan para poder reenviar
  Y el botón de reenviar aparece deshabilitado con ese tiempo restante
```

### AC-10 (borde) · Tope de reenvíos agotado

```gherkin
Dado que existe una prevalidación standalone con 3 reenvíos ya realizados
Cuando el operador FLIT intenta reenviarla o cambiarle el correo
Entonces el sistema responde 429 "tope_reenvios"
  Y el mensaje indica que debe anular el registro y crear una prevalidación nueva
  Y no se consume una verificación adicional del proveedor
```

### AC-11 (borde) · Falla transitoria del proveedor al reenviar

```gherkin
Dado que existe una prevalidación standalone en estado "expirado"
  Y el proveedor Kyverum presenta una falla transitoria
Cuando el operador FLIT reenvía la validación
Entonces el sistema responde 202 y deja la validación en estado "pendiente_envio"
  Y el worker de envío reintenta hasta agotar sus reintentos
  Y el reenvío ya consumió cupo del tope
```

### AC-12 (borde) · Webhook del intento anterior

```gherkin
Dado que una prevalidación standalone fue reenviada y tiene una verificación nueva del proveedor
Cuando llega un webhook correspondiente a la verificación anterior
Entonces el sistema lo descarta por no coincidir con la verificación vigente
  Y el estado y los intentos de la validación no se alteran
```

### AC-13 (auditoría / Habeas Data) · Traza del cambio de correo

```gherkin
Dado que el operador FLIT cambia el correo de una prevalidación standalone
Cuando se guarda el cambio
Entonces queda un evento de auditoría con el autor, la fecha, el correo anterior y el nuevo, ambos enmascarados
  Y ni el correo anterior ni el nuevo aparecen en claro en los logs de la aplicación
```

---

## 10. Próximos pasos

1. ✅ Validación humana del alcance base (CF-00/CF-01/CF-02) — HUs #10865–#10869 creadas y en curso.
2. ✅ Validación humana de la ampliación **CF-03** (§4, decisiones D7–D12 en §6.2, AC en §9) — 2026-07-27.
3. ✅ Feature **#10864** actualizado en ADO con el criterio CF-03 y HUs **#10943** / **#10944** creadas en Sprint 2, estado `New`, tag `DOR`, `Refinement = true`.
4. Validar DoR de #10943 / #10944 (`flit-dor-dod-validator`) y activarlas cuando se vayan a implementar (**gate humano**: confirmación explícita antes de pasar a `Active` y tocar código).
5. Implementar #10943 (backend) → #10944 (frontend), en ese orden por dependencia.
