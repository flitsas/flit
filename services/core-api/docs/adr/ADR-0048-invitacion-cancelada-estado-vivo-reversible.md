# ADR-0048: La invitación cancelada es un estado vivo y reversible, no un borrado

**Fecha**: 2026-08-14
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, Product Owner
**Tags**: arquitectura, backend, frontend, seguridad, modulo-usuarios

## Contexto

`security.user_invitations` tiene hoy tres estados de facto — `pending`, `accepted`, `cancelled` —
pero la cancelación (HU #10627) se implementó como **borrado lógico**: además de `Status =
"cancelled"`, `InvitationRepository.CancelAsync` marca `DeletedAt`/`DeletedBy`
(`Flit.Infrastructure/Persistence/Repositories/InvitationRepository.cs:143-150`), y así lo documenta
el contrato del puerto (`IInvitationRepository.cs:38-42`).

Consecuencia: los tres listados de usuarios solo proyectan invitaciones con `Status == "pending"`
(`SecurityEndpoints.cs:801`, `:859`, `:945`; `AdminOtEndpoints.cs:1825`), así que al cancelar, la
fila desaparece del módulo Usuarios. Y no hay vuelta atrás: `ResendInvitationHandler.cs:31-32` exige
`pending` y responde 409. El único camino es crear otra invitación, que funciona porque
`ExistsPendingAsync` solo cuenta pendientes.

El negocio (decisión D2 del PO, 2026-08-14) pide lo contrario: la invitación cancelada **se ve** en
el listado con su estado y **se puede reactivar**. Eso convierte a `cancelled` en un estado de
negocio consultable y reversible, incompatible con la semántica de "fila borrada" que hoy comparte
con el resto del sistema (`users`, `user_role_assignments`, `roles`, donde `DeletedAt != null`
significa "no existe para el producto" y solo el SuperAdmin lo revierte con `restore`).

## Decisión

`cancelled` pasa a ser un **estado terminal-reversible del ciclo de vida de la invitación**, no un
soft-delete: `CancelAsync` deja de escribir `DeletedAt`/`DeletedBy` (conserva `UpdatedAt`/`UpdatedBy`
y el trigger `tr_user_invitations_audit`), y una invitación cancelada puede volver a `pending`
mediante un endpoint de reactivación explícito que **siempre regenera el token**.

## Alternativas consideradas

### Opción 1: `cancelled` como estado vivo; `CancelAsync` deja de marcar `DeletedAt` (recomendada)

**Pros:**
- Una sola señal de verdad para el ciclo de vida: `Status`. Nadie tiene que saber que "cancelada"
  también es "borrada".
- Elimina el estado imposible `DeletedAt != null` + fila visible y reactivable, que sería una trampa
  para el próximo que toque el módulo.
- Blinda contra el riesgo real de que alguien añada un `HasQueryFilter(x => x.DeletedAt == null)`
  sobre `UserInvitation` (ya existe ese patrón en `VehicleColorConfiguration.cs:58` y
  `VehicleServiceTypeConfiguration.cs:62`) y las canceladas vuelvan a desaparecer en silencio.
- La auditoría no depende de `DeletedAt`: el trigger `trg_audit_log` registra el `UPDATE` y
  `UpdatedAt`/`UpdatedBy` conservan quién y cuándo.

**Cons:**
- Deja filas históricas con `DeletedAt` poblado (las canceladas antes del cambio), salvo que se
  normalicen con un `UPDATE` de datos.
- Rompe dos aserciones de test existentes.

**Esfuerzo:** S
**Riesgos:** bajo — ningún consumidor de producción lee `UserInvitation.DeletedAt` (verificado por
barrido: el único escritor/lector es `InvitationRepository.cs:147`).

### Opción 2: conservar `DeletedAt` y filtrar por `Status` en todas partes

**Pros:**
- Cero cambio en el repositorio; el diff se reduce a los listados y al endpoint nuevo.
- Mantiene el patrón "toda baja marca `DeletedAt`" uniforme a ojos de un lector superficial.

**Cons:**
- Consagra un dato mentiroso: una fila "borrada" que el producto muestra y permite revivir.
- Reactivar exige *limpiar* `DeletedAt`, es decir, un "des-borrado" fuera del único mecanismo de
  restauración del sistema (`POST /superadmin/users/{id}/restore`), sin sus controles.
- Cualquier query futura que use el criterio estándar `DeletedAt == null` excluirá canceladas por
  accidente.

**Esfuerzo:** S
**Riesgos:** medio — el defecto reaparece por la puerta de atrás en la próxima HU.

### Opción 3: estado `cancelled` + tabla/columna de historial de invitaciones

**Pros:**
- Trazabilidad completa de cada ciclo cancelar→reactivar (quién, cuándo, cuántas veces).
- Permite políticas antiabuso finas sobre el número de reactivaciones.

**Cons:**
- Migración nueva y modelo nuevo para un requisito que nadie pidió.
- El `audit_log` por trigger ya cubre el 90 % de esa trazabilidad.

**Esfuerzo:** M
**Riesgos:** sobrediseño (BDUF) para 5 SP de alcance.

## Tradeoff aceptado

Se acepta dejar filas históricas con `DeletedAt` poblado (o pagar un `UPDATE` de normalización de
una línea) a cambio de que el modelo no tenga dos fuentes de verdad para "esta invitación ya no
vale". La Opción 2 es más barata hoy y más cara en cada HU futura: el criterio `DeletedAt == null`
es el reflejo automático de todo el equipo, y con la Opción 2 ese reflejo produce un bug silencioso.

## Consecuencias

### Lo que se gana
- `Status` es la única máquina de estados de la invitación: `pending → accepted | cancelled`, y
  `cancelled → pending` por reactivación.
- La reactivación es una transición de negocio auditada, no un "des-borrado".
- El listado puede exponer `cancelled` sin inventar un campo paralelo.

### Lo que se pierde
- Se rompe la lectura "todas las tablas se dan de baja igual": `user_invitations` pasa a tener bajas
  por estado y no por `DeletedAt`. Se documenta en el XML-doc del puerto.
- Dos tests deben actualizarse (`AdminOtUsersEndpointsTests.cs:575`,
  `SecurityInvitationsRoleResolutionTests.cs:211`).

### Cambios operacionales
- Ninguna migración de esquema: `status` es `varchar(20)` sin `CHECK`
  (`Sql/Ddl/03-HU10147-invitations.sql:10`) y `cancelled` ya se escribe hoy.
- El índice parcial `uq_user_invitations_tenant_email_pending ... WHERE status = 'pending'`
  (`ibid.:23-24`) sigue siendo el guardarraíl duro de la reactivación: pasar a `pending` con otra
  pendiente del mismo correo en el tenant revienta en BD. El handler debe pre-validar y el endpoint
  mapear la violación de unicidad a 409.
- Normalización opcional de datos: `UPDATE security.user_invitations SET deleted_at = NULL,
  deleted_by = NULL WHERE status = 'cancelled';` — decide el `database-agent`.

## ADRs relacionados

- ADR-0023 — catálogo global de roles (contexto de `RoleExistsInTenantAsync`).
- ADR-0024 — endurecimiento de auditoría: el trigger de audit cubre la traza de la transición.

## Notas para agentes

- **Backend Agent**: `CancelAsync` no toca `DeletedAt`. `ReactivateInvitationHandler` valida, en
  este orden: alcance/tenant (404), `Status == "cancelled"` (409), correo libre (409) y roles aún
  activos (409). Siempre token nuevo; nunca reutilizar `TokenHash`.
- **Frontend Agent**: `"cancelled"` entra en la unión de `status`. Los guardas `status !== "pending"`
  de `Usuarios.tsx`, `OtUsersSection.tsx` y `CompanyUsersPanel.tsx` dejan de ser equivalentes a "es
  un usuario real" y deben pasar a una condición explícita de fila-usuario.
- **QA Agent**: probar el ciclo cancelar→reactivar→activar, la colisión de correo y que el enlace
  anterior queda muerto tras reactivar.
- **Security Agent**: la reactivación reabre un vector de envío de correo; debe compartir el
  cooldown antiabuso del reenvío y exigir la misma policy que `DELETE /invitations/{id}`.
- **Infra Agent**: sin impacto (sin migración de esquema, sin variables nuevas).

## Referencias externas

- `docs/plan-tecnico-ajustes-modulo-usuarios.md` — novedad N3, HU-3, decisión D2 del PO.
