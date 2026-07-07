# ADR-0023: Catálogo global de roles por tipo de entidad (Roles y Permisos)

**Fecha**: 2026-07-06
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (Feature #10504, HU #10505)
**Tags**: arquitectura, backend, datos, seguridad, rbac, roles, fase-1-diseño

## Contexto

`security.roles` nació (HU #10148, Feature #10134) como una tabla **tenant-scoped**: la
entidad `Role` hereda `TenantAuditableEntity` (`tenant_id NOT NULL`), con
`UNIQUE(tenant_id, code)` y RLS `tenant_isolation`. Cada tenant nuevo recibe su **propia
fila** de rol de sistema (`AdminCompany` para compañías, `ot_admin` para organismos de
tránsito, `SuperAdmin` para el tenant interno de FLIT), creada automáticamente al dar de
alta el tenant (`CompanyWriteRepository.CreateAsync`, `TransitOfficeTenantWriteRepository.CreateAsync`)
o por el seed de desarrollo (`DevelopmentAuthSeeder`).

Una auditoría de la BD `flit_dev` (2026-07-06) confirmó el problema que este diseño produce:

- **`AdminCompany`**: 2 filas (una por tenant: `EMPRESA_DEMO` con 7 permisos —le falta
  `auth.me.read`— y `231233`/Ejemplito con 8 permisos, completo). Los mismos permisos de
  negocio ("todos excepto `rbac.manage`") divergieron entre dos filas que deberían ser
  idénticas, solo porque se crearon en momentos distintos del ciclo de vida del catálogo de
  permisos.
- **`SuperAdmin`** y **`ot_admin`**: 1 fila cada uno (sin divergencia todavía, por
  casualidad — solo hay un tenant de cada tipo sembrado hasta ahora).
- **Roles custom** (`is_system = false`): 0 filas — la funcionalidad de roles
  personalizados por tenant, prevista en el modelo original, no se usa en la práctica.

El checklist `db-schema-validator` §A4/§A20 exige `tenant_id NOT NULL` y RLS en toda tabla
de negocio. Sin embargo, los roles de sistema (`AdminCompany`, `ot_admin`, `SuperAdmin`) son,
por diseño, **el mismo catálogo conceptual para todos los tenants de un mismo tipo de
entidad** (COMPANY | TRANSIT_OFFICE): duplicarlos por tenant no aporta aislamiento real —
solo introduce deriva de permisos (como la ya detectada) y una migración N-por-tenant cada
vez que cambia el catálogo de permisos.

Precedente directo: ADR-0019 ya documentó la misma excepción para los catálogos globales de
parametrización de trámites (`procedure_entities`, `external_data_sources`,
`consultation_templates`, `procedure_types`): sin `tenant_id`, protegidos por RBAC SuperAdmin
en vez de RLS.

## Decisión

`security.roles` y `security.role_permissions` **dejan de llevar `tenant_id`** y pasan a ser
un **catálogo GLOBAL de roles por tipo de entidad de negocio** (`target_entity_type`:
`COMPANY` | `TRANSIT_OFFICE`), administrado exclusivamente por el rol SuperAdmin de FLIT
(gobernanza fina de activar/desactivar y curar permisos — HU #10508, fuera de alcance de
esta HU).

Excepción formal al checklist A4 y A20 documentada en este ADR, mismo patrón que ADR-0019.

Cambios de esquema (HU #10505):

- `security.roles`: se elimina `tenant_id`, la política RLS `tenant_isolation` y
  `UNIQUE(tenant_id, code)`. Se agrega `target_entity_type varchar(20) NOT NULL DEFAULT
  'COMPANY'` con `CHECK (target_entity_type IN ('COMPANY','TRANSIT_OFFICE'))`, e
  `is_active boolean NOT NULL DEFAULT true`. Nuevo `UNIQUE(code, target_entity_type)`.
- `security.role_permissions`: se elimina `tenant_id` y su RLS (los permisos de un rol
  global aplican a todos los tenants que lo usan).
- `security.user_role_assignments` **no cambia** en esta HU: sigue siendo tenant-scoped
  (`tenant_id NOT NULL`, RLS `tenant_isolation`) — la ASIGNACIÓN de un rol a un usuario sigue
  siendo un hecho por tenant, solo el CATÁLOGO de roles disponibles se vuelve global. El
  índice único de multi-rol (`uq_ura_active_user_tenant`) es alcance de HU #10506 y no se
  toca aquí.
- Migración de datos: las filas duplicadas de un mismo rol de sistema (hoy, 2 filas de
  `AdminCompany`) se fusionan en una canónica por **UNIÓN** de permisos (nunca se reduce
  acceso) y se reasignan las `user_role_assignments` de las filas descartadas a la
  canónica (soft-delete de la sobrante).
- **`SuperAdmin`**: se le asigna `target_entity_type = 'COMPANY'` como default porque el
  enum no tiene un tercer valor "global/transversal" — SuperAdmin es transversal a todos los
  tenants por su propia semántica de rol (`role == 'SuperAdmin'` en el JWT lo hace
  multi-tenant en toda la capa de autorización), no por su fila en este catálogo. Esta
  decisión es intencional y menor: SuperAdmin **no se expone** en las pantallas de gestión
  de roles por tipo de entidad (esas pantallas listan/filtran por `target_entity_type` para
  administrar roles de COMPANY u OT, no el rol interno de la plataforma).

## Alternativas consideradas

### Opción A: `tenant_id` nullable con NULL = rol global

**Pros:** compatible con el checklist A4 sin excepción formal; permite, en teoría, roles
globales y roles 100% custom-por-tenant en la misma tabla.
**Cons:** consultas complejas (`WHERE tenant_id IS NULL OR tenant_id = :tid`); no resuelve
el problema real detectado (los roles de sistema YA deberían ser una única fila, no
"globales opcionalmente" — la ambigüedad NULL/no-NULL solo pospone la decisión).
**Esfuerzo:** M
**Riesgos:** drift entre tenants si un desarrollador futuro inserta por error una fila
tenant-scoped con el mismo `code` que la global (sin `UNIQUE(code)` que lo impida).

### Opción B: Mantener el duplicado por tenant (status quo)

**Pros:** cero migración; aislamiento máximo si algún día se necesitan roles de sistema
distintos por tenant.
**Cons:** es exactamente el problema ya detectado en producción-dev (deriva de permisos
entre `AdminCompany` de `EMPRESA_DEMO` y `231233`); cada cambio al catálogo de permisos de
un rol de sistema requiere una migración de datos N-por-tenant; sin ADR, cada agente futuro
reinterpreta el patrón de forma distinta.
**Esfuerzo:** — (sin cambio)
**Riesgos:** deriva silenciosa entre tenants garantizada a mediano plazo (ya ocurrió una
vez); imposible ofrecer gobernanza centralizada de roles (HU #10508) sobre un catálogo
fragmentado.

### Opción C: Catálogo global sin `tenant_id`, por tipo de entidad + RBAC SuperAdmin (elegida)

**Pros:** modelo simple y coherente con el precedente ADR-0019; una sola fuente de verdad
por rol de sistema; el catálogo de permisos de `AdminCompany`/`ot_admin` se cura una vez y
aplica a todos los tenants de ese tipo; habilita la gobernanza centralizada de HU #10508;
elimina la clase de bug ya detectada (deriva de permisos entre tenants).
**Cons:** excepción al checklist A4; se pierde la posibilidad (nunca usada — 0 roles custom
en producción-dev) de un rol de sistema con permisos distintos por tenant individual; la
asignación (`user_role_assignments`) sigue siendo tenant-scoped, así que el modelo mixto
(catálogo global + asignación por tenant) exige que los repositorios no mezclen ambos
conceptos (documentado en las convenciones de acceso a datos).
**Esfuerzo:** S
**Riesgos:** SuperAdmin debe ser el único con escritura sobre el catálogo (HU #10508); los
roles custom por tenant (si se retoman en el futuro) requerirán una decisión explícita de
si siguen siendo por `target_entity_type` global o necesitan un tercer modelo (fuera de
alcance de esta HU — hoy 0 filas en producción-dev, sin impacto).

## Tradeoff aceptado

Se acepta la excepción a checklist A4 para `security.roles` y `security.role_permissions`.
La protección se realiza mediante:

1. **RBAC**: solo el rol `SuperAdmin` puede crear/desactivar roles y curar sus permisos
   (endpoints de gobernanza — HU #10508, fuera de alcance de esta HU; en esta HU los
   handlers de aplicación quedan listos: `CreateRoleHandler`, `SetRolePermissionsHandler`,
   `SetRoleActiveHandler`, `DeleteRoleHandler`).
2. **Sin RLS** en estas dos tablas (ya no tienen `tenant_id`); lectura global filtrada por
   `target_entity_type` (no por tenant).
3. **La asignación sigue siendo tenant-scoped**: `security.user_role_assignments` no
   cambia — RLS `tenant_isolation` intacta, `tenant_id NOT NULL`. Un usuario de un tenant
   solo puede tener asignado un rol cuyo `target_entity_type` corresponda al tipo de su
   tenant (COMPANY vs TRANSIT_OFFICE); esa validación de coherencia queda para HU #10506/#10508
   (no se agrega aquí para no ampliar el alcance de esta HU).

## Consecuencias

### Lo que se gana

- Una sola fuente de verdad por rol de sistema: sin deriva de permisos entre tenants del
  mismo tipo (el bug ya detectado en `AdminCompany` no puede volver a ocurrir).
- Altas de compañías/OT más simples: `CompanyWriteRepository.CreateAsync` y
  `TransitOfficeTenantWriteRepository.CreateAsync` ya no crean una fila de rol por tenant
  (el catálogo global ya la tiene); solo persisten el tenant y, cuando aplica, el perfil OT.
- Habilita gobernanza centralizada de roles (activar/desactivar, curar permisos una sola
  vez) — HU #10508.
- Menos artefactos de BD: sin RLS ni índice por `tenant_id` en dos tablas más.

### Lo que se pierde

- No hay personalización de un rol de sistema por tenant individual en fase-1 (no se usaba:
  0 roles custom en producción-dev). Si se requiere en el futuro, exige una decisión
  explícita nueva (posible ADR de seguimiento).
- La migración de consolidación de datos (fusión de filas duplicadas de `AdminCompany`) es
  **irreversible** en su `Down` — mismo tradeoff aceptado que
  `20260630180000_RestrictTenantTypeCatalog`.

### Seguimiento

- HU #10506 (multi-rol): índice único de `user_role_assignments` — no se toca en HU #10505.
- HU #10507: JWT / login — no se toca en HU #10505.
- HU #10508 (gobernanza SuperAdmin): endpoints HTTP de activar/desactivar rol
  (`SetRoleActiveHandler` ya registrado en DI, sin endpoint todavía) y redefinición de la
  autorización fina de `SecurityEndpoints.cs` / `SecurityRolesEndpoints.cs` sobre el
  catálogo global.
- Checklist `db-schema-validator` A4/A20: marcar PASS con referencia a este ADR en PRs que
  toquen `security.roles` / `security.role_permissions`.
- DDL: `services/core-api/docs/schema/ddl/12-HU10505-catalogo-global-roles.sql`.

## Referencias

- ADR-0018: Modelo de datos fase-1 FLIT Evolution
- ADR-0019: Motor de parametrización — Catálogos globales sin tenant_id (SuperAdmin) —
  precedente directo de esta decisión.
- Checklist: `.cursor/skills/db-schema-validator/checklist-validacion-schema.md` (§A4, §A20)
- DDL: `services/core-api/docs/schema/ddl/12-HU10505-catalogo-global-roles.sql`
- Migración EF: `20260706223157_HU10505_GlobalRoleCatalog`
