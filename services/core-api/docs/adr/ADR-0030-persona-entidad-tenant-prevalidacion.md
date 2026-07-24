# ADR-0030: Entidad Persona a nivel tenant para prevalidaciones de identidad

**Fecha**: 2026-07-24  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT, equipo core-api, equipo tramites  
**Tags**: arquitectura, backend, datos, tramites, identidad, feature-10864, fase-1-diseño  
**Supersedes**: —  
**HU origen**: #10865 (CF-00 del Feature #10864)

---

## Contexto

El sistema de validaciones biométricas actual (`ProcedureInstanceBiometricValidation`) exige que toda
validación esté anclada a un trámite: `procedure_instance_id NOT NULL FK → tramites.procedure_instances`.
No existe ninguna entidad que represente a una persona/sujeto a nivel tenant con independencia del trámite.

El Feature #10864 introduce la capacidad de **validar la identidad de una persona antes de que exista un
trámite** (prevalidación standalone), y reutilizar esa validación automáticamente cuando el trámite se crea
después. Para esto se necesita un ancla persistida para la persona en el tenant que:

1. Permita crear y recuperar validaciones standalone por `(tenant_id, document_type, document_number)`
2. Haga posible la trazabilidad PII (Ley 1581/Habeas Data) de personas sin trámite
3. Identifique a la persona para persona jurídica (NIT) cuya validación biométrica la realiza el RL

La clave de negocio `BiometricRules.IdentidadKey(tenant, tipoDoc, documento)` ya existe como función
estática en el dominio; este ADR formaliza esa clave como entidad persistida.

---

## Decisión

Crear la tabla `tramites.persons` en el bounded context tramites, con `(tenant_id, document_type,
document_number)` como clave de negocio única. Hacer `procedure_instance_id` nullable en
`procedure_instance_biometric_validations` y agregar FK a `tramites.persons` (`person_id`).
Cambiar `ON DELETE CASCADE → ON DELETE SET NULL` en la FK de instancia.

---

## Alternativas consideradas

### Opción 1: `tramites.persons` — BC tramites (elegida)

Nueva tabla en el schema `tramites`, mismo bounded context que la validación. La entidad `Person`
en C# vive en `Flit.Tramites.Domain.Entities`.

**Pros:**
- Sin dependencia cross-BC; tramites es autónomo
- Mismo DbContext, sin migraciones cross-schema
- Patrón consistente con `ProcedureInstanceActor` (datos RL en la misma fila, no tabla separada)
- `IPersonRepository` sigue el mismo contrato que `IProcedureInstanceRepository`
- `IdentidadKey` existente se convierte en PK lógica de la entidad sin cambio de semántica

**Contras:**
- Una persona conceptualmente podría existir en otros módulos FLIT (fuera de tramites); si eso ocurre,
  se necesitaría mover la entidad o crear un duplicado
- Datos del RL embebidos (no tabla separada) — denormalización, aunque acorde al patrón actual

**Esfuerzo:** M  
**Riesgos:** Bajo

---

### Opción 2: `identity.persons` — BC identity, cross-schema FK

Nueva tabla en el schema `identity` (junto con `tenants`, `users`, `roles`). FK cross-schema
desde `tramites.procedure_instance_biometric_validations → identity.persons`.

**Pros:**
- Separación conceptual correcta: una persona es una identidad, no un tramite
- Reutilizable en módulos futuros (autenticación, portal ciudadano)
- Cohesión con el módulo `identity`

**Contras:**
- FK cross-schema requiere que `identity.persons` exista antes de `tramites` (orden de migración complejo)
- `identity` hoy gestiona tenants/users/roles/permisos; agregar personas es una expansión de scope sin
  HU planificada en ese módulo
- `IdentitySubject` ya es un value object en `Flit.Tramites.Application` (misma semántica); duplicar
  la entidad en dos BCs genera confusión de naming
- Complejidad de RLS cross-schema
- Mayor riesgo de conflicto con HUs del módulo identity paralelas

**Esfuerzo:** L  
**Riesgos:** Medio-alto

---

### Opción 3: Sin entidad nueva, `procedure_instance_id` nullable solamente

Hacer `procedure_instance_id` nullable sin crear tabla `persons`. Las prevalidaciones standalone
se identifican por el composite `(tenant_id, document_type, document_number)` de la fila de
validación + un índice compuesto. No hay entidad de persona formal.

**Pros:**
- Cambio mínimo (sin migración de tabla nueva)
- Sin FK nueva, sin repositorio nuevo
- Menor riesgo de conflicto con otras HUs

**Contras:**
- **Viola CF-00/P1** (decisión de producto cerrada): "entidad persona/sujeto a nivel tenant"
- Sin entidad formal, no hay integridad referencial al actualizar el documento de la persona
- Difícil construir un historial de validaciones por persona en el futuro
- Sin `COMMENT @pii:` en una entidad formal, el cumplimiento de Ley 1581 es más difícil de auditar
- El UPSERT de persona en cada validación standalone corre el riesgo de race condition sin FK

**Esfuerzo:** S  
**Riesgos:** Viola P1 (regla FLIT #5 — decisión de producto); deuda de modelo

---

## Tradeoff aceptado

Se elige **Opción 1** porque:
- La persona/sujeto en la fase actual de FLIT es primariamente un participante de trámites. La expansión
  al BC `identity` sería prematura y agrega complejidad injustificada para el scope del Feature #10864.
- La Opción 3 viola una decisión de producto cerrada (P1) y cierra la puerta a reportes por persona.
- La denormalización de datos del RL en la fila `persons` (en lugar de tabla separada) es intencionada
  y consistente con el patrón `ProcedureInstanceActor.metadata` ya validado en el repo. Si en el futuro
  se necesita una tabla `legal_representatives` separada, se puede migrar con un ADR nuevo.

---

## Consecuencias

### Lo que se gana
- Entidad formal de persona en el tenant que sirve como ancla para validaciones standalone
- Trazabilidad PII completa (Ley 1581) con `COMMENT @pii:` en `tramites.persons`
- El webhook de Kyverum y la reconciliación **no requieren cambios** (correlacionan por `validationId`)
- `FindVigenteApprovedByDocumentAsync` con un cambio de una línea incluye prevalidaciones standalone
- `IdentidadKey` existente se preserva como función estática; la entidad la materializa sin duplicar lógica

### Lo que se pierde
- `ON DELETE CASCADE` en `procedure_instance_biometric_validations → procedure_instances` pasa a
  `SET NULL`. El borrado de una instancia ya no borra sus validaciones (que ahora pueden ser standalone).
  Esto requiere que el código que accede a `instance.BiometricValidations` sea robusto a colecciones
  con `ProcedureInstanceId = null` (no aplica hoy porque el in-memory filter por instancia evita ese path).
- Acoplamiento ligero de la entidad `persons` al BC tramites. Si en fases futuras se requiere una
  entidad persona cross-BC, este ADR debe ser supersedido.

### Cambios operacionales
- La migración `HU10865_PersonEntityAndNullableInstanceId` es la primera a ejecutar en DEV antes de
  cualquier HU de este feature.
- El `ck_biometric_validation_anchor` CHECK constraint requiere que todas las filas existentes tengan
  `procedure_instance_id IS NOT NULL` (verificar antes de aplicar en QA/PDN).
- El campo `legal_basis` (base legal del tratamiento de datos) no está en scope de este ADR pero
  debe ser evaluado por el equipo de producto antes del despliegue a PDN (ver §Notas §Security Agent).

---

## ADRs relacionados

- [ADR-0018] — Modelo de datos fase-1 FLIT Evolution (DbContext único, schema tramites)
- [ADR-0025] — Baúl de firmas: custodia y consumo (patrón `EnsureIdentityHandler` + precedencias)
- [ADR-0022] — Estados de negocio del ciclo de vida del trámite

---

## Notas para agentes

- **Database Agent**: Materializar migración EF Core según DDL §7 del diseño técnico
  `docs/design/FEATURE-10864-prevalidacion-identidad.md`. Checklist §A completo para `tramites.persons`.
  El índice de cobertura `ix_biometric_validations_vigente_approved` es crítico para CF-02.
  La migración `Down()` debe hacer DROP COLUMN `person_id` con guards para no fallar si hay filas.
  Verificar CHECK constraint `ck_biometric_validation_anchor` antes de aplicar en QA/PDN.

- **Backend Agent**: `IniciarPrevalidacionHandler` en `UseCases/Persons/`, no modificar
  `IniciarKyverumVerifyHandler`. La resolución del sujeto PN/PJ sigue el patrón `IdentitySubjectResolver`.
  En `EnsureIdentityCommand.cs` solo cambia una condición en `FindVigenteApprovedByDocumentAsync`.
  Ver diseño §8.1 para lista completa de archivos.

- **Frontend Agent**: `TenantBiometricValidation.instanceId/referenceNumber/modalidad` pasan a
  nullable en TypeScript. Riesgo de conflicto con `feature/AB-10863-gestion-tramite` en `Validaciones.tsx`.
  Coordinar merge con el LT antes de abrir PR de #10868.

- **QA Agent**: TCs críticos: (1) reuso de standalone vigente al crear trámite, (2) standalone expirada
  no reutilizada, (3) standalone de otro tenant no reutilizada, (4) webhook Kyverum en standalone → aprobada.
  Regresión en `EnsureIdentityHandler` con los fixtures existentes.

- **Security Agent**: Verificar base legal del tratamiento PII en `persons` antes de PDN (Ley 1581).
  Confirmar que el endpoint `POST /biometric-validations` está detrás del rol `operador_flit`.
  Auditar que `provider_payload` se sanitiza en `IniciarPrevalidacionHandler` (igual que en el webhook).
  Confirmar comentarios `@pii:` en DDL final de `database-agent`.

- **Infra Agent**: Sin cambios de infraestructura. Verificar que el pipeline de migraciones en DEV/QA
  ejecute `HU10865_PersonEntityAndNullableInstanceId` antes de las HUs #10866/#10867.

---

## Referencias externas

- Ley 1581 de 2012 (Colombia) — Habeas Data, tratamiento de datos personales
- PostgreSQL docs — `ON CONFLICT DO UPDATE`, RLS policies, UUIDv7
- Diseño técnico completo: `docs/design/FEATURE-10864-prevalidacion-identidad.md`
