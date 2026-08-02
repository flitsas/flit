# ADR-0034 — Validación de identidad administrativa desacoplada del trámite (bloque compartido)

- **Estado**: Aceptado · 2026-07-23 (aceptado por el Líder Técnico humano — regla FLIT 15)
- **Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15)
- **Módulo**: Admin Compañías (representantes / mandatarios) + Infraestructura de identidad (Kyverum)
- **Requerimientos**: `RL-Escrituras-Firma.txt` línea 39 ("enviar un correo para que realice la validación de identidad"); habilitador de `docs/plan-tecnico-mandato-solicitud-virtual.md` (D1)
- **Tags**: arquitectura, backend, seguridad, identidad, modulo-companias

## Contexto

Al registrar un **representante legal por compañía** (ADR-0033), si no hay firma en el baúl ni una validación de identidad vigente, el negocio pide poder **enviar un correo** para que el representante **valide su identidad** — un flujo **desacoplado de cualquier trámite** (ocurre a nivel admin, antes de existir un trámite). La misma necesidad la tiene el **mandatario por OT** (plan de mandato, D1: "validación de identidad del mandatario una vez a nivel admin, reutilizable 30 días").

Hoy toda la validación biométrica está **anclada a un trámite**: `ProcedureInstanceBiometricValidation` es hija de `ProcedureInstance`, con `PartyRole` restringido a `comprador`/`vendedor`, iniciada por `KyverumVerifyCommand`/`BiometricaCommand`, y el correo lo envía el proveedor **Kyverum**. La vigencia es de 30 días (`BiometricRules.VigenciaDias`) con `CertificateHash` = `firmaSerie`. Existe un outbox/reconciliador de identidad reutilizable. Falta una forma de disparar y custodiar una validación **sin** un trámite que la contenga. Este ADR decide **cómo** obtener esa validación admin, buscando que sea **un único bloque compartido** por el representante (ADR-0033) y el mandatario (ADR-0023 / plan de mandato).

## Decisión

Se crea una **entidad/agregado de validación de identidad administrativa** desacoplada del trámite, expuesta por un servicio `IAdminIdentityValidationService`, que **reutiliza el proveedor Kyverum y el outbox de identidad** existentes. Espeja a `ProcedureInstanceBiometricValidation` en lo esencial (`status`, `capture_url`, `validated_at`, `valid_until` 30 días, `certificate_hash`, `kyverum_verification_id`, secreto de webhook cifrado) pero cuelga del **sujeto admin** (representante o mandatario), no de un trámite.

- **Disparo:** `POST` admin → inicia la verificación → Kyverum **envía el correo** al sujeto. Reenvío permitido si nunca se realizó o venció (sin la guarda `biometria_activa` del flujo de trámite).
- **Aprobación:** al aprobar, se persiste `certificate_hash`/`valid_until` y se **vincula** al registro del sujeto (`legal_representatives.identity_validation_ref` en ADR-0033; el mandatario tendrá su vínculo análogo).
- **Consumo en el trámite:** la validación admin vigente se resuelve por documento igual que hoy (`FindVigenteApprovedByDocumentAsync` / `IdentityApprovalResolver`), de modo que el representante/mandatario ya validado **no** re-valida dentro del trámite.
- **Reutilización:** el servicio es **agnóstico del sujeto** (representante | mandatario) para que ADR-0033 y el plan de mandato lo consuman sin duplicar lógica.

## Alternativas consideradas

### Opción 1 — Agregado de validación admin desacoplado + servicio compartido (RECOMENDADA)
**Pros:** modela con precisión "validación a nivel admin"; reutiliza proveedor Kyverum + outbox + reglas de vigencia; un único bloque para representante y mandatario; sin contaminar datos de trámites.
**Cons:** nueva entidad/tabla + webhook/reconciliación propios; requiere confirmar que Kyverum permite una verificación sin trámite.
**Esfuerzo:** M-L. **Riesgos:** medio (dependencia del proveedor — ver §Riesgos).

### Opción 2 — Trámite "sintético"/oculto para reusar `ProcedureInstanceBiometricValidation`
**Pros:** cero cambios en la infraestructura de identidad; reusa la entidad tal cual.
**Cons:** ensucia el dominio de trámites (instancias fantasma en reportes/analítica); `PartyRole` no admite un rol admin; hacky y difícil de auditar; fugas de estado hacia el wizard.
**Esfuerzo:** M. **Riesgos:** alto (deuda y datos espurios).

### Opción 3 — Sin flujo biométrico admin: solo firma del baúl
**Pros:** mínimo esfuerzo; el representante sin firma solo puede "registrar en baúl".
**Cons:** **no cumple** el requerimiento (línea 39 pide explícitamente el correo de validación); deja al mandatario sin su D1.
**Esfuerzo:** S. **Riesgos:** funcional (incumple alcance).

## Tradeoff aceptado

Se acepta construir infraestructura nueva (Opción 1) para tener un bloque **limpio y compartido** entre representante y mandatario, en lugar de reutilizar por atajo el modelo de trámite (Opción 2), que introduciría datos espurios y deuda difícil de revertir. La Opción 3 se descarta por incumplir el requerimiento. El riesgo real se concentra en la capacidad del proveedor (Kyverum) para una verificación desacoplada; se mitiga con un plan de degradación (ver Riesgos).

## Consecuencias

### Lo que se gana
- Un único servicio de validación de identidad admin reutilizable por representantes (ADR-0033) y mandatarios (plan de mandato), con vigencia y no-repudio (`certificate_hash`).
- El representante/mandatario ya validado no re-valida dentro del trámite.

### Lo que se pierde
- Nueva superficie (entidad, webhook, reconciliación, endpoints de disparo/reenvío) y su mantenimiento.

### Cambios operacionales
- Configuración del webhook/secreto de Kyverum para el flujo admin; reutilizar el outbox/reconciliador existentes.
- Migración de la tabla de validación admin (RLS por tenant, secreto cifrado, PII marcada).

## Riesgos y mitigación

| Riesgo | Mitigación |
|---|---|
| Kyverum **no** permite verificación sin trámite (R2 del plan de mandato) | Al implementar (HU-9 de ADR-0033), validar con el proveedor primero. Si no lo permite: degradar a "registrar en baúl" en fase 1 y escalar el bloqueo al Líder Técnico antes de comprometer el correo. |
| Secreto de webhook en claro | Cifrado en reposo; nunca en logs. |
| PII del sujeto | `document_number`/correo no logueados; RLS por tenant. |
| Correo a un sujeto equivocado | El disparo exige documento+correo verificados del registro; auditoría del envío. |

## ADRs relacionados

- [ADR-0033] — directorio RL por compañía: consumidor primario (acción "enviar correo" al guardar sin firma/identidad).
- [ADR-0023] — firmante de mandato: consumidor futuro (D1 del plan de mandato) del mismo servicio.
- [ADR-0025] — baúl de firmas: alternativa a la validación biométrica (precedencia baúl→identidad).

## Notas para agentes

- **Database Agent**: tabla de validación admin (RLS por tenant, `valid_until`, `certificate_hash`, `kyverum_verification_id`, secreto cifrado); marcar `@pii`.
- **Backend Agent**: `IAdminIdentityValidationService` agnóstico del sujeto; disparo/reenvío; consumo del webhook/reconciliador de identidad existente; vínculo al sujeto (`identity_validation_ref`). **Confirmar capacidad del proveedor antes de codificar** (R2).
- **Frontend Agent**: en el panel de representante, acción "Enviar correo de validación" + estado (enviado/aprobado/vencido) con `StatusBadge`.
- **QA Agent**: reenvío sin guarda `biometria_activa`; vigencia 30 días; reutilización en el trámite sin re-validar; degradación si el proveedor no soporta el flujo.
- **Security Agent**: cifrado del secreto de webhook, PII, no-repudio (`certificate_hash`), autorización SuperAdmin.
- **Infra Agent**: configuración del webhook Kyverum para el flujo admin; sin cambios de despliegue mayores.

## Estado y aceptación

**Aceptado** el 2026-07-23 por el Líder Técnico humano (regla FLIT 15). Queda **abierto el riesgo R2**: antes de codificar la HU de validación admin se debe confirmar con Kyverum el soporte de verificación desacoplada del trámite; si no lo soporta, aplica el plan de degradación documentado.
