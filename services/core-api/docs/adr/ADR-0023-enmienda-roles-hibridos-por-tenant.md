# ADR-0023 (Enmienda A): Roles híbridos — catálogo global más roles propios de la compañía

**Fecha**: 2026-10-08
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (Feature #13437, Épica #12750, HU #13440 a #13443) — la aceptación es exclusiva del Líder Técnico humano
**Tags**: arquitectura, backend, datos, seguridad, rbac, roles, rls, enmienda
**Enmienda a**: [ADR-0023](ADR-0023-catalogo-global-roles.md) (catálogo global de roles, HU #10505)

## Contexto

ADR-0023 convirtió `security.roles` en un catálogo **global** sin `tenant_id`. La Épica #12750 (Feature #13437) pide
que el Administrador de Compañía cree, edite y elimine **roles propios** de su compañía y defina sus permisos, sin
depender de FLIT, y que ninguna compañía vea ni use los roles de otra. Con un catálogo estrictamente global esto no
se puede expresar: un rol creado por la compañía A sería visible y asignable en la compañía B.

Decisiones de negocio ya tomadas (no se re-discuten aquí): AdminCompany gestiona solo roles COMPANY propios; los
globales COMPANY los ve en solo lectura y los puede asignar; eliminar un rol asignado devuelve 409; el producto
`plataforma` completo (módulos, permisos y `product_code`) es **no delegable** a una compañía; ot_admin y demás roles
pierden la gestión de roles pero conservan asignar roles existentes (`UserAdminPolicy`).

## Decisión

**Modelo híbrido en la misma tabla**: `security.roles.tenant_id uuid NULL` con FK `fk_roles_tenants` a
`identity.tenants`.

- `tenant_id IS NULL` = rol global del catálogo FLIT (todos los roles existentes quedan así; ADR-0023 sigue vigente
  para ellos).
- `tenant_id = <tenant>` = rol propio de esa compañía.

Detalle (DDL `134-HU13440-roles-por-tenant.sql`, migración `20261008140000_HU13440_RolesPorTenant`):

1. **Unicidad parcial** entre filas vigentes (`deleted_at IS NULL`): `uq_roles_code_target_entity_type` pasa a cubrir
   solo `tenant_id IS NULL`; nuevo `uq_roles_tenant_code` sobre `(tenant_id, lower(code))`. Dos compañías pueden usar el
   mismo code.
2. **Trigger `tr_roles_tenant_code_not_global`**: un índice único no compara dos subconjuntos, así que el trigger
   rechaza (SQLSTATE 23505, mensaje `ROLE_CODE_DUPLICATE`) que un rol de tenant repita el code de un rol global
   (cualquier `target_entity_type`, sin distinguir mayúsculas), que un rol global nuevo pise el de un rol de tenant, y
   que un tenant use `admin_<producto>` (reservado para los roles espejo de AdminCompany aunque aún no exista la fila).
   El repositorio traduce el 23505 a `RoleCodeDuplicateException` (409).
3. **RLS `tenant_isolation`** sobre `security.roles`: globales, propios y bypass de `app.is_superadmin`. Es
   **defensa en profundidad nominal** (ver Riesgos): el aislamiento efectivo es el filtro por tenant de
   `RoleRepository` y de los lookups que asignan o invitan con un rol.
4. **`security.role_permissions` no cambia** (sigue sin `tenant_id`, ADR-0023): hereda el aislamiento del rol. Solo se
   escriben permisos de roles que el caller puede ver.
5. **Tope de privilegios (aplicación)**: el Admin de Compañía solo otorga permisos que él mismo posee, de módulos de
   productos encendidos para su tenant (`platform.tenant_products`), y nunca del producto `plataforma` (403
   `PERMISSION_PLATFORM_ONLY`, `PERMISSION_MODULE_NOT_ENABLED`, `PERMISSION_NOT_HELD`). Endpoints nuevos bajo
   `/api/v1/security/roles` con `AdminCompanyPolicy`; `/api/v1/superadmin/roles*` no cambia.

## Alternativas consideradas

### Opción A: `tenant_id` nullable con NULL = global (elegida)

- Reutiliza `security.roles`, los handlers y `user_role_assignments` sin tocar la asignación.
- El catálogo global sigue siendo único (sin la deriva que motivó ADR-0023).
- Costo: dos subconjuntos con reglas de unicidad distintas (índices parciales + trigger) y RLS que depende del GUC.

### Opción B: tabla aparte `security.tenant_roles`

- Aislamiento limpio con `tenant_id NOT NULL` y RLS estándar (cumple checklist A4/A10 sin excepción).
- Pero obliga a duplicar `role_permissions`, a que `user_role_assignments.role_id` y `invitation_roles.role_id`
  apunten a dos tablas (FK polimórfica o columnas alternas), y a tocar todos los joins de login y de JWT.
  Más riesgo de regresión en autenticación para un beneficio de aislamiento que la app ya impone por filtro.

### Opción C: volver a `tenant_id NOT NULL` por tenant (status quo previo a ADR-0023)

- Es el modelo que ADR-0023 descartó: una copia por tenant de cada rol de sistema, deriva de permisos y una
  migración N-por-tenant en cada cambio del catálogo de permisos.

## Tradeoff aceptado

Se acepta una tabla con dos reglas de unicidad y un trigger a cambio de no tocar la asignación ni la autenticación. La
RLS no aísla hoy; se acepta porque ningún DDL del repositorio usa `FORCE ROW LEVEL SECURITY` y la app conecta como
owner (precedente: DDL 105 y las demás tablas). Endurecerlo es una decisión de infraestructura aparte.

## Consecuencias

### Lo que se gana

- AdminCompany autogestiona roles propios con tope de privilegios y auditoría (`AdminAuditFilter`).
- Cero migración de datos: los roles existentes quedan con `tenant_id` NULL.
- ADR-0023 sigue válido para el catálogo global; esta enmienda solo agrega el subconjunto por tenant.

### Lo que se pierde / limitaciones aceptadas

- **Frescura del claim `permissions` del JWT (limitación aceptada)**: los permisos del token se emiten al iniciar
  sesión o refrescar. Un cambio de permisos de un rol se refleja en el **siguiente token**; y el tope de privilegios se
  evalúa **solo al guardar** el rol, con el claim del token vigente del caller. Un permiso retirado al caller después de
  crear el rol no se retira del rol.
- Un rol de tenant no sobrevive al Down de la migración (se borran sus asignaciones, invitaciones y permisos).

## Riesgos

- **M4 — RLS nominal**: la policy usa `app.current_tenant_id`. Si algún ambiente conectara con un rol **no owner**
  sin fijar `app.current_tenant_id` (por ejemplo en el login), los roles de tenant **desaparecerían de los joins de
  login** y de la emisión del JWT, y esos usuarios perderían permisos en silencio. Mitigación: antes de endurecer el
  rol de conexión (o de usar `FORCE ROW LEVEL SECURITY`), fijar el GUC en el contexto de autenticación o excluir
  `security.roles` del endurecimiento. Cubierto por `RolesPorTenantPostgresTests` (ejercita la policy con un rol no
  owner).
- **Lookups por `Code`** (`AdminCompany`, `ot_admin`, `SuperAdmin`): se restringen a `tenant_id IS NULL` o al tenant
  destino para que un rol propio nunca los sustituya (el trigger ya impide repetir esos codes).

## Seguimiento

- El Líder Técnico decide la aceptación de esta enmienda (estado `Propuesto`).
- Si se decide endurecer la conexión a un rol no owner, abrir un ADR propio sobre GUC y `FORCE ROW LEVEL SECURITY`.

## Referencias

- ADR-0023 (catálogo global de roles), ADR-0019 y ADR-0058 (excepciones de tabla global), ADR-0063 (productos).
- Épica #12750; Feature #13437; HU #13440, #13441, #13442, #13443.
- `services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/134-HU13440-roles-por-tenant.sql`.
