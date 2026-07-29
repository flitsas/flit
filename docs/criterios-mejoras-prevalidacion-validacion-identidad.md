# Criterios y plan — Mejoras Prevalidación / Validación de identidad

> **Versión:** BORRADOR v1  
> **Fecha:** 2026-07-28  
> **Estado:** Criterios de entendimiento + plan técnico (aún no Feature/HUs en ADO)  
> **Origen:** Solicitud del usuario (2026-07-28) contrastada con el código actual de Feature **#10864** (prevalidación) y el submódulo **Validaciones de Identidad** (HU #10234+).  
> **Features hermanos:** [feature-prevalidacion-identidad.md](feature-prevalidacion-identidad.md) · [design/FEATURE-10864-prevalidacion-identidad.md](design/FEATURE-10864-prevalidacion-identidad.md) · ADR-0030.

---

## 1. Qué se entiende (resumen)

| # | Pedido | Interpretación |
|---|--------|----------------|
| 1 | Al crear prevalidación, siempre persona natural | Quitar persona jurídica del alta de prevalidación; el sujeto Kyverum es siempre el titular natural. |
| 2 | Módulo Prevalidaciones = solo prevalidaciones | Listar únicamente filas con `ProcedureInstanceId = null`. Hoy se mezclan con validaciones de trámite. |
| 3 | Módulo Validaciones = ambas | Mantener el listado transversal actual (trámite + prevalidación). |
| 4 | Documento completo | Dejar de enmascarar a últimos 4 en listados; mostrar `tipo + número` íntegro. |
| 5 | Columna correo | Exponer y pintar `email` en Prevalidaciones y en Validaciones. |
| 6 | Detalle en Prevalidación | Vista de detalle con proceso Kyverum, reintentos y actualización “en vivo” (mismo patrón que el trámite). |
| 7 | Tracking en ambos módulos | Historial detallado por registro: etapas, reintentos, fallos (envío, webhook, reconcile, outbox). |
| 8 | Tracking en el trámite | Mostrar **todas** las validaciones/reintentos del trámite por parte; hoy la UI solo usa la **última**. |

---

## 2. Criterios funcionales (verificables)

### CF-01 · Prevalidación solo persona natural

**Enunciado:** Al crear (y editar, si aplica) una prevalidación, el formulario solo admite datos de **persona natural**. No se ofrece ni se acepta persona jurídica ni bloque de representante legal.

- **Resultado medible:**
  - UI de `PrevalidacionForm` sin selector natural/jurídica ni campos RL.
  - Body de `POST /biometric-validations` siempre con `personType = natural` (o equivalente implícito).
  - Backend rechaza o ignora de forma explícita `juridical` en el flujo de prevalidación (422 o contrato estrecho).
- **Fuera de alcance de este CF:** validación de actores jurídicos **dentro de un trámite** (sigue usando `IdentitySubjectResolver` / RL como hoy).
- **Cambia decisión previa:** Feature #10864 (P5) cubría natural + jurídica; este criterio **restringe** el módulo de prevalidación a natural.

### CF-02 · Listado del módulo Prevalidaciones = solo standalone

**Enunciado:** En `/tramites/prevalidaciones` solo aparecen validaciones sin trámite (`ProcedureInstanceId IS NULL`).

- **Resultado medible:**
  - El cliente envía `standalone=true` a `GET /api/v1/tramites/biometric-validations`.
  - No hay fallback que muestre filas de trámite si el filtro “queda vacío”.
  - Filas de trámite dejan de aparecer en solo lectura en esta pantalla.
- **Nota de código hoy:** el BE ya soporta `Standalone` en `TenantBiometricValidationListQuery`; el FE intenta el filtro pero `tramites-client.listTenantBiometricValidations` **no serializa** `standalone`, y el módulo hace filtro client-side + fallback a “todas”.

### CF-03 · Módulo Validaciones sigue mostrando ambas

**Enunciado:** El submódulo transversal de Validaciones de Identidad continúa listando prevalidaciones y validaciones de trámite (comportamiento actual deseado).

- **Resultado medible:** sin `standalone` (o `standalone` omitido) → mismas filas que hoy; badge “Prevalidación” cuando `instanceId == null`; navegación a trámite cuando hay `instanceId`.
- **Opcional (no bloqueante):** filtro UI “Solo trámite / Solo prevalidación / Todas” reutilizando el query param ya existente.

### CF-04 · Documento completo en listados

**Enunciado:** En Prevalidaciones y Validaciones el documento se muestra completo (`tipoDocumento` + `numeroDocumento`), sin enmascarar a últimos 4 ni truncar la lectura del número.

- **Resultado medible:** se elimina (o se desactiva en estas vistas) `maskDoc(...)`; celdas sin `truncate` que oculte el número.
- **Nota de código hoy:** el DTO tenant ya envía el documento **completo**; el enmascarado es solo FE (`PrevalidacionesModule`, `Validaciones`).

### CF-05 · Columna correo en ambos listados

**Enunciado:** Ambas tablas muestran una columna **Correo** con el email de la validación.

- **Resultado medible:**
  - `TenantBiometricValidationDto` incluye `Email`.
  - Tipo FE `TenantBiometricValidation` incluye `email`.
  - Columnas visibles en `PrevalidacionesModule` y `Validaciones`.
- **Nota de código hoy:** el email existe en entidad/`BiometricValidation` por instancia, pero el DTO de listado tenant lo **omite a propósito** (comentario HU #10234). Este CF **revierte** esa omisión para monitoreo operativo.

### CF-06 · Detalle de prevalidación (proceso + reintentos Kyverum)

**Enunciado:** Desde el módulo de Prevalidaciones se puede abrir el **detalle** de un registro y ver el proceso de validación y los reintentos con Kyverum, con actualización cercana a tiempo real (mismo patrón que en el trámite).

- **Resultado medible:**
  - Acción “Ver detalle” (o fila clicable) → panel/página de detalle.
  - Muestra: estado actual, intentos `Attempts/MaxAttempts`, `ultimoIntentoMotivo` (si aplica), enlace/QR si está vigente, score/fechas, reenvíos (`ResendCount` / tope).
  - Polling mientras el estado sea no terminal (p. ej. cada 5 s, patrón `BiometricStep`), con pause-on-hidden.
  - Acciones coherentes con el estado: reconciliar (si aplica), copiar/abrir enlace, reenviar (si editable).
- **Reutiliza:** lógica de `KyverumPendingView` / poll de `BiometricStep`; bitácora vía audit (ver CF-07).

### CF-07 · Tracking detallado en Validaciones y Prevalidaciones

**Enunciado:** En ambos módulos, por cada registro, el operador puede consultar un **tracking** del proceso: etapas, reintentos, procesos fallidos (envío, webhook, reconcile, outbox/dead-letter).

- **Resultado medible:**
  - UI de historial (timeline o tabla) por `validationId`.
  - Fuente primaria: `identity_validation_audit` (ya existe; hoy la bitácora en trámite es solo SuperAdmin).
  - Debe mostrar al menos: envío, intento rechazado, webhook, reconcile, expiración, error de envío / reintentos de cola, reenvíos de prevalidación.
  - Accesible a roles de operación del tenant (no solo SuperAdmin), con datos ya saneados (sin secretos/PII extra).
- **Nota:** hoy `IdentityAuditPanel` en `BiometricStep` exige SuperAdmin e `instanceId`; para standalone hace falta endpoint de audit **sin** instancia (o genérico por `validationId` + tenant).

### CF-08 · Tracking en el trámite: todas las validaciones / reintentos

**Enunciado:** Dentro del trámite, el tracking de identidad muestra **cada** validación o reintento asociado al trámite (por parte), no solo la última.

- **Resultado medible:**
  - `GET .../instances/{id}/biometric` ya puede devolver varias filas por parte; la UI deja de quedarse solo con `matches[matches.length - 1]` para el historial.
  - La tarjeta de estado operativo puede seguir destacando la **vigente/más reciente**, pero el panel de tracking lista **todas** (orden cronológico), cada una con su estado, fechas, intentos y enlace a su audit.
  - Aplica también a `IdentityStatusPanel` si concentra el consolidado: historial expandible por actor.
- **Nota de código hoy:** comentario explícito en `BiometricStep` — “Más reciente para la parte… tras posibles reintentos (rechazado → nueva validación)”.

---

## 3. Fuera de alcance (explícito)

- Cambiar el proveedor Kyverum o sus mappers.
- SignalR/SSE: el “tiempo real” sigue siendo **polling HTTP** (como hoy).
- Rediseñar vigencia de 30 días / reuso CF-02 de prevalidación en trámites (se mantiene).
- SMS/WhatsApp como canal de envío del enlace.
- Módulo Admin de identidad de mandatarios (`AdminIdentityValidation`) — flujo distinto.

---

## 4. Estado actual en el proyecto (baseline)

| Área | Ya implementado | Gap vs criterios |
|------|-----------------|------------------|
| Alta prevalidación | `POST /biometric-validations`, Form natural+jurídica | CF-01: quitar jurídica |
| Listado prevalidaciones | Página + módulo; filtro client-side frágil | CF-02: cablear `standalone=true` y quitar mezcla |
| Listado validaciones | Paginación, filtros, poll 15 s, ambas | CF-03: OK; CF-04/05 pendientes |
| Documento | BE completo; FE `maskDoc` | CF-04: quitar máscara en estas vistas |
| Correo en listado | Omitido en DTO tenant | CF-05: agregar al contrato |
| Detalle / live prevalidación | No existe; solo listado + form | CF-06: nueva UI + poll |
| Audit / bitácora | Tabla + GET audit por instancia (SuperAdmin) | CF-07: exponer en módulos + standalone |
| Historial en trámite | Solo última validación por parte en UI | CF-08: listar todas |
| Reintentos Kyverum | `Attempts`/`MaxAttempts`, workers, outbox, webhook | Datos listos; falta UI de tracking unificada |
| `ResendCount` prevalidación | En entidad BE | No viaja en listado tenant |

### Archivos ancla

| Capa | Path |
|------|------|
| Prevalidaciones UI | `frontend/components/atom/modules/PrevalidacionesModule.tsx` |
| Form alta | `frontend/components/atom/modules/PrevalidacionForm.tsx` |
| Validaciones UI | `frontend/components/atom/modules/Validaciones.tsx` |
| Trámite identidad | `frontend/components/operacion/BiometricStep.tsx` |
| Consolidado trámite | `frontend/components/operacion/IdentityStatusPanel.tsx` |
| Cliente API | `frontend/lib/api/tramites-client.ts` |
| Listado tenant | `ListTenantBiometricValidationsQuery.cs` |
| Audit | `IdentityValidationAuditEvent` + `ListIdentityAuditByValidationAsync` |
| Endpoints | `BiometricaEndpoints.cs` |

---

## 5. Plan de implementación (por fases)

Orden pensado para reutilizar lo existente, minimizar riesgo y poder abrir PRs ≤ 800 líneas.

### Fase 0 — Contrato y filtros (backend + cliente) · base de CF-02, CF-05

1. **Cablear `standalone` en FE**  
   - Añadir `standalone?: boolean` a `TenantBiometricValidationFilters`.  
   - Serializar en `listTenantBiometricValidations`.  
   - En `PrevalidacionesModule`: llamar siempre con `standalone: true`; eliminar fallback que mezcla trámites.
2. **Extender DTO de listado tenant**  
   - Agregar `Email` (y, si sirve al detalle/listado: `Attempts`, `MaxAttempts`, `ResendCount`, `LastAttemptAt`).  
   - Actualizar tipo TS + tests de contrato.
3. **Tests:** handler de listado (standalone + email); cliente/unit del filtro.

**Dependencias:** ninguna. Desbloquea CF-02 y CF-05.

### Fase 1 — Listados UX (frontend) · CF-01, CF-04, CF-05

1. **PrevalidacionForm:** remover UI jurídica/RL; forzar `personType: 'natural'`.  
2. **BE (defensa):** validar/rechazar `juridical` en `IniciarPrevalidacion` (y documentar en OpenAPI si aplica).  
3. **PrevalidacionesModule + Validaciones:**  
   - Columna Correo.  
   - Documento completo (sin `maskDoc`).  
   - Ajuste de grid/columnas y estados vacíos.  
4. **Tests RTL** existentes de form/módulo: actualizar expectativas.

**Dependencias:** Fase 0 para email en payload.

### Fase 2 — Tracking por validación (backend + UI compartida) · CF-07

1. **Endpoint de audit por `validationId` scoped a tenant**, usable sin `instanceId` (prevalidaciones) y con validación de pertenencia al tenant.  
   - Reutilizar `ListIdentityAuditByValidationAsync`.  
   - Autorización: roles de operación del módulo (no solo SuperAdmin), alineado a quién ve Validaciones/Prevalidaciones.  
2. **Componente compartido** `IdentityValidationTrackingPanel` (extraer/generalizar `IdentityAuditPanel`):  
   - Timeline/tabla de etapas, outcomes, timestamps, errores.  
   - Consumible desde Validaciones, Prevalidaciones y trámite.  
3. **En listados:** acción “Ver tracking” / drawer por fila.  
4. **Tests:** endpoint standalone + permisos; snapshot UI básico.

**Dependencias:** ninguna dura sobre Fase 1; puede ir en paralelo tras Fase 0.

### Fase 3 — Detalle de prevalidación “en vivo” · CF-06

1. **Vista detalle** (ruta o panel lateral) para `validationId` standalone:  
   - Estado, documento, correo, intentos, motivo último intento, enlace/QR, vigencia, reenvíos.  
   - Poll 5 s si no terminal (patrón `BiometricStep`).  
   - Reutilizar acciones ya existentes: resend, edit, reconcile si el endpoint aplica a standalone.  
2. **Incluir** el panel de tracking (Fase 2) dentro del detalle.  
3. **Tests:** estados cargando/error/lleno; poll pausado con tab oculta.

**Dependencias:** Fase 0 (datos enriquecidos) + Fase 2 (tracking).

### Fase 4 — Historial completo en el trámite · CF-08

1. **BiometricStep:**  
   - Mantener la tarjeta operativa sobre la validación **más reciente**.  
   - Añadir sección “Historial de validaciones” con **todas** las filas de esa parte (orden cronológico).  
   - Cada ítem: estado, fechas, intentos, enlace a tracking (audit de ese `validationId`).  
2. **IdentityStatusPanel:** enlace o expandible al mismo historial (evitar dos fuentes de verdad).  
3. **Tests:** fixture con 2+ validaciones por parte (rechazada + nueva en proceso/aprobada); assert de que ambas se renderizan en el historial.

**Dependencias:** Fase 2 recomendada (mismo panel de tracking).

### Fase 5 — Cierre de calidad

1. Actualizar tests E2E/unitarios afectados.  
2. Evidencias unitarias por AC (`dev-tester`) cuando existan HUs.  
3. Revisar DoR/DoD y convenciones FLIT antes de PRs.  
4. Documentar en Discussion de HUs el cambio de política PII (documento + email visibles en listados autenticados del tenant).

---

## 6. Descomposición sugerida en HUs (indicativa)

| HU tentativa | Tipo | Criterios | SP (ind.) |
|--------------|------|-----------|-----------|
| HU-A | BACKEND | DTO email (+ intentos/resend), filtros `standalone` ya OK, endpoint audit por validationId tenant | 3–5 |
| HU-B | FRONTEND | Solo natural; listados solo standalone; doc completo; columna correo | 3 |
| HU-C | FRONTEND | Tracking compartido en Validaciones + Prevalidaciones | 5 |
| HU-D | FRONTEND | Detalle prevalidación con poll + tracking | 5 |
| HU-E | FRONTEND | Historial completo de validaciones en trámite (`BiometricStep` / panel) | 3–5 |

> Estimación gruesa; la descomposición formal la confirma `tech-lead-agent` al crear el Feature en ADO.

---

## 7. Decisiones a confirmar con producto (antes de implementar)

| ID | Pregunta | Default propuesto |
|----|----------|-------------------|
| D1 | ¿El BE debe **rechazar** `juridical` en prevalidación o solo la UI deja de enviarlo? | Rechazar (422) para no dejar puerta trasera. |
| D2 | ¿Quién ve el tracking? ¿Mismos roles que el módulo Validaciones, o sigue restringido a SuperAdmin? | Mismos roles del módulo (operación); sin secretos. |
| D3 | ¿Documento y correo completos también en exports/KPIs del Dashboard? | Solo tablas de Validaciones y Prevalidaciones en este alcance. |
| D4 | ¿Detalle de prevalidación = ruta dedicada o drawer en la misma página? | Drawer/panel en la misma página (menos navegación). |
| D5 | En el trámite, ¿el historial muestra también prevalidaciones **reutilizadas** (referenced) además de las creadas en la instancia? | Sí, si el GET biometric ya las incluye; etiquetar “reutilizada / otro origen”. |

---

## 8. Criterios de aceptación Gherkin (borrador)

### CF-01

```gherkin
Scenario: Alta de prevalidación solo persona natural
  Given un operador en el módulo de Prevalidaciones
  When abre el formulario de nueva prevalidación
  Then no ve la opción de persona jurídica ni campos de representante legal
  And al enviar, el sistema crea la validación para el titular como persona natural
```

### CF-02

```gherkin
Scenario: El módulo Prevalidaciones no mezcla trámites
  Given existen validaciones de trámite y prevalidaciones standalone en el tenant
  When el operador abre Prevalidaciones
  Then solo ve filas sin trámite asociado
```

### CF-04 / CF-05

```gherkin
Scenario: Documento y correo visibles completos
  Given una prevalidación con documento "CC 1234567890" y correo "ana@ejemplo.com"
  When el operador ve el listado de Prevalidaciones o Validaciones
  Then el documento se muestra completo
  And la columna correo muestra "ana@ejemplo.com"
```

### CF-06 / CF-07

```gherkin
Scenario: Detalle y tracking de prevalidación
  Given una prevalidación en estado "en_proceso"
  When el operador abre el detalle
  Then ve el estado, intentos y el historial de etapas/reintentos
  And la información se actualiza periódicamente mientras no sea terminal
```

### CF-08

```gherkin
Scenario: Historial completo en el trámite
  Given un trámite con dos validaciones de identidad para el comprador (una rechazada y una nueva en proceso)
  When el operador consulta el tracking de identidad del trámite
  Then ve ambas validaciones en el historial
  And la vista operativa destaca la más reciente
```

---

## 9. Próximos pasos recomendados

1. Confirmar decisiones D1–D5.  
2. Crear Feature + HUs en ADO (`tech-lead-agent` / `feature-creator`) a partir de estos criterios.  
3. Implementar por fases 0→4 con PRs hacia `develop`.  
4. No iniciar código hasta activación de HU (Motivo A / gate humano).

---

## 10. Trazabilidad de cambios de producto

| Antes (Feature #10864) | Ahora (este documento) |
|------------------------|-------------------------|
| Prevalidación natural **y** jurídica | Solo **natural** |
| Listado prevalidaciones con filtro frágil / mezcla | Solo standalone, filtro server-side |
| Email omitido en listado tenant | Email visible en ambos módulos |
| Documento enmascarado en FE | Documento completo |
| Sin detalle live de prevalidación | Detalle + poll + tracking |
| Bitácora solo SuperAdmin en trámite (última validación) | Tracking en módulos + **todas** las validaciones del trámite |
