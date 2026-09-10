
# ADR-0058: `admin.banners` como tabla de negocio global, sin `tenant_id` ni RLS por tenant

**Fecha**: 2026-09-09
**Status**: Aceptado (2026-09-09, por Willyn Londoño Calle)
**Deciders**: Willyn Londoño Calle (Líder Técnico), Architecture Agent
**Tags**: arquitectura, backend, modulo-admin, modulo-banners, modelo-de-datos

## Contexto

El Feature #12236 (Epic #12231) introduce un módulo administrable de banners promocionales (nombre,
imagen, enlace URL opcional, período de vigencia, activar/desactivar), confirmados funcionalmente por
el PO como **globales**: un único set de banners visible por igual en ambos carruseles (`Dashboard.tsx`
del gestor y `OtDashboard.tsx` del OT), para **todos** los tenants — no existe el concepto de "banner
de un tenant específico".

El checklist `db-schema-validator` (`.claude/skills/db-schema-validator/checklist-validacion-schema.md`)
exige por defecto en toda tabla de negocio nueva: `tenant_id NOT NULL` + FK a `identity.tenants` (A4),
RLS habilitado + política `tenant_isolation` (A10), `tenant_id` como primera columna de índice (A11).
El propio checklist prevé la excepción: **A19** exige que toda entidad de negocio nueva tenga un ADR
`Propuesto` que la referencie, y esa referencia cubre explícitamente el caso de excepción a A4/A10/A11.
El tech-lead-agent marcó este punto como bloqueante en la HU1 (schema/migración) por no encontrar aún
ese ADR — este documento lo resuelve.

Precedente relevante ya aceptado en el repo: `catalogs.transit_offices`
(`services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/09-HU10152-ot-admin.sql`) es una tabla
sin `tenant_id`, con `is_active`, sin RLS — bajo la convención de catálogos (A20), documentada como
tal. Los banners no son un catálogo de referencia externo (RUNT/DIVIPOLA) sino contenido editorial
mutable administrado por SuperAdmin, por lo que no encajan en A20 sin más — de ahí que se documenten
aquí como una excepción propia y explícita a A4/A10/A11, no como una reclasificación a catálogo.

Nota de contexto de seguridad ya documentada en ADR-0056: la RLS de este repo es **decorativa** (no
hay `FORCE ROW LEVEL SECURITY`; la aplicación conecta a PostgreSQL como owner). El aislamiento real
hoy lo dan el filtro de repositorio (`WHERE tenant_id = @current`) y el ownership check del endpoint,
no la RLS en sí — lo que reduce el argumento de "riesgo de fuga de datos" de omitir `tenant_id` en
esta tabla específica, porque ningún tenant tiene datos propios en `banners` que aislar de otro.

## Decisión

Modelar `admin.banners` como tabla de negocio **global**, sin columna `tenant_id` y sin política RLS,
manteniendo el resto de convenciones estándar de tabla de negocio (PK `uuidv7()`, columnas de
auditoría `created_at/by`, `updated_at/by`, `deleted_at/by`, `row_version`, triggers `row_version` +
`audit_log`). El control de acceso de **escritura** (crear/editar/borrar/activar-desactivar un banner)
lo da el permiso admin correspondiente en el endpoint (HU4), no la tabla; el de **lectura** lo resuelve
ADR-0057 (endpoint de consumo sin distinción de tenant, mismo set para todos).

## Alternativas consideradas

### Opción 1: Tabla global sin `tenant_id` ni RLS *(elegida)*

`admin.banners` sin `tenant_id`, documentando la excepción vía este ADR (A19) y un
`COMMENT ON TABLE admin.banners IS 'Global, sin tenant_id: excepción documentada en ADR-0058...'`.

**Pros:**
- Coincide exactamente con la semántica de negocio ya cerrada: un solo set de banners para todos los
  tenants, sin necesidad de replicar ni particionar nada.
- Cero riesgo de desincronización entre tenants (no hay copias que puedan divergir).
- Una sola fila por banner: historial, edición y consulta son triviales (`SELECT * FROM admin.banners
  WHERE is_active AND now() BETWEEN valid_from AND valid_until`).
- Precedente estructural ya aceptado en el repo (`catalogs.transit_offices`: sin `tenant_id`, sin
  RLS) para el caso "un dato compartido por todo el sistema".
- El argumento de seguridad de RLS es débil en este repo específico (es decorativa, ADR-0056): no se
  pierde ningún control real de aislamiento que hoy exista para otras tablas.

**Cons:**
- Excepción explícita a A4/A10/A11 del checklist — exige mantenimiento consciente: cualquier cambio
  futuro a "banners segmentados por tenant" requiere migración de schema (agregar `tenant_id`), no
  solo un cambio de query.
- Un desarrollador nuevo que vea `admin.banners` sin `tenant_id` puede asumir un bug si no lee el
  `COMMENT ON TABLE` o este ADR — mitigado documentando ambos.
- No hay política de RLS que actúe como red de seguridad adicional si algún día se introduce
  segmentación por error de código (el filtro de repositorio sería el único control) — mismo perfil de
  riesgo que ya asume el repo para RLS decorativa en general.

**Esfuerzo:** S
**Riesgos:** bajo — el negocio cerró explícitamente "un solo set global"; el riesgo de que cambie es
un riesgo de producto, no de implementación, y se paga con una migración cuando (si) ocurra.

### Opción 2: Tabla con `tenant_id NOT NULL` + fan-out a todos los tenants

Mantener `tenant_id NOT NULL` + RLS estándar; cada banner creado se replica (job o trigger) como una
fila por cada tenant existente, y cada alta de tenant nuevo dispara un fan-out retroactivo de los
banners vigentes.

**Pros:**
- Cero excepción al checklist; `admin.banners` cumple A4/A10/A11 sin ADR de excepción.
- Reutiliza el mismo patrón de filtro de repositorio que el resto de tablas de negocio.

**Cons:**
- Introduce el problema que el negocio explícitamente no tiene: N copias de la misma fila lógica, con
  riesgo real de desincronización (editar un banner exige propagar el cambio a N filas; un fallo
  parcial del job dejaría tenants viendo versiones distintas del mismo banner — justo el escenario
  "banner global" que el PO quiso evitar).
- Requiere lógica adicional no trivial: trigger/job de fan-out en creación, edición, borrado y alta de
  tenant nuevo (4 puntos de sincronización en vez de 1 tabla).
- Sobre-ingeniería para resolver una convención de schema, no un problema funcional: el negocio no
  pidió nunca personalización por tenant.
- Mayor superficie de prueba (QA debe validar consistencia entre N filas, no una).

**Esfuerzo:** M-L
**Riesgos:** alto — desincronización entre tenants es un bug funcional visible al usuario final
(banners distintos en el mismo carrusel según el tenant), exactamente lo que el PO cerró que no debía
pasar.

### Opción 3: `tenant_id` nullable (NULL = global, valor = tenant específico)

Mantener la columna `tenant_id`, pero nullable; `NULL` se interpreta como "banner global", un valor
concreto como "banner de ese tenant". RLS con política condicional
(`tenant_id IS NULL OR tenant_id = current_setting(...)`).

**Pros:**
- Dejaría espacio en el schema para una futura segmentación por tenant sin nueva migración de tabla
  (solo dejar de insertar `NULL`).
- La columna sigue presente, por si el checklist automatizado busca `tenant_id` por nombre.

**Cons:**
- Contradice A4 al pie de la letra (`tenant_id NOT NULL`) sin resolver la excepción — sigue
  necesitando el mismo ADR que la Opción 1, con el beneficio adicional de una semántica ambigua
  (`NULL` como valor de negocio implícito) que ninguna otra tabla del repo usa así.
- La política RLS condicional es más difícl de razonar y de probar que "sin RLS, sin tenant" —
  aumenta la superficie de bugs de seguridad para un beneficio hipotético no pedido.
- Resuelve un requisito que **no existe hoy** (banners por tenant) a costa de complejidad real hoy;
  clásico caso de sobre-diseño (BDUF) que el rol de este agente debe evitar.
- Si el negocio decide banners por tenant en el futuro, la migración necesaria (agregar `tenant_id`
  a una tabla que no lo tenía) no es más costosa que "dejar de insertar NULL" en la práctica, porque de
  todos modos habría que revisar el endpoint de consumo, el CRUD y el frontend — la Opción 3 no ahorra
  tanto trabajo futuro como aparenta.

**Esfuerzo:** M
**Riesgos:** ambigüedad semántica permanente en el schema; no elimina la necesidad de un ADR de
excepción, solo la vuelve parcial y menos legible.

## Tradeoff aceptado

Se acepta la excepción explícita a A4/A10/A11 — documentada aquí y en `COMMENT ON TABLE` — a cambio
de un modelo trivial de una fila por banner, sin riesgo de desincronización entre tenants. La Opción 2
se descarta porque introduce exactamente el problema (múltiples copias divergentes) que el negocio
cerró que no quería, a cambio de cumplir una convención de forma. La Opción 3 se descarta porque no
resuelve nada que el checklist no exija igual (sigue necesitando este ADR) y añade ambigüedad semántica
permanente por un requisito de segmentación por tenant que nadie pidió. El argumento de seguridad que
normalmente respalda A4/A10/A11 (aislamiento de datos entre tenants) no aplica aquí: no hay dato de un
tenant que proteger de otro, porque el dato es, por diseño de negocio, el mismo para todos.

## Consecuencias

### Lo que se gana

- Modelo de datos alineado 1:1 con el requisito funcional cerrado (un solo set global).
- Cero riesgo de desincronización entre tenants.
- CRUD, consulta y activar/desactivar son operaciones de una sola fila, sin fan-out ni jobs.

### Lo que se pierde

- `admin.banners` queda como excepción documentada que cualquier auditoría de schema debe saber leer
  (mitigado con `COMMENT ON TABLE` + cita a este ADR en la migración).
- Si el negocio pide segmentación por tenant en el futuro, se requiere una migración de schema
  (agregar `tenant_id`, backfill, y ajustar CRUD/endpoint/frontend) — no es gratis, pero es una
  decisión de producto no tomada hoy, no un costo que deba pagarse por adelantado.

### Cambios operacionales

- **Migración**: `.sql` numerado (próximo libre en el momento de escribir esta ADR: **107**,
  `Flit.Infrastructure/Persistence/Sql/Ddl/`) + migración EF Core (`EmbeddedDdl.LoadUp`), a cargo del
  `database-agent` (Modo B), siguiendo el checklist §A salvo A4/A10/A11 (excepción citada a este ADR)
  y A19 (satisfecho por este ADR).
- **DDL de referencia** (borrador, el `database-agent` lo finaliza):

```sql
-- Referencia — NO es la migración formal (database-agent la valida y ajusta)
CREATE TABLE admin.banners (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_banners PRIMARY KEY (id),
    name varchar(200) NOT NULL,
    image_storage_path varchar(200) NOT NULL, -- opaco: id del file-manager (ADR-0057)
    image_sha256 char(64) NOT NULL,            -- ETag del endpoint de imagen (ADR-0057)
    link_url varchar(2048),                    -- enlace opcional
    valid_from timestamptz NOT NULL,
    valid_until timestamptz NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT ck_banners_vigencia CHECK (valid_until > valid_from)
);

CREATE INDEX ix_banners_activo_vigencia
  ON admin.banners (is_active, valid_from, valid_until)
  WHERE deleted_at IS NULL;

COMMENT ON TABLE admin.banners IS
  'Tabla GLOBAL sin tenant_id: excepción documentada en ADR-0058-banners-tabla-global-sin-tenant-excepcion. '
  'Un único set de banners visible para todos los tenants (Feature #12236).';

-- Sin RLS: la tabla es global por diseño (ADR-0058). Triggers estándar de negocio sí aplican:
-- row_version (BEFORE UPDATE) y audit_log (AFTER INSERT/UPDATE/DELETE), igual que el resto de
-- tablas de admin.*.
```

- **Repositorio** (`database-agent` / `backend-agent`, checklist §B): el repositorio de `Banner` NO
  aplica filtro de `tenant_id` (no existe la columna); sí aplica filtro de soft-delete estándar
  (`deleted_at IS NULL`) y `AsNoTracking()` en lecturas. El interceptor de tenant
  (`set_config('app.current_tenant_id', …)`) sigue registrado globalmente para el resto de tablas, sin
  efecto sobre `admin.banners`.
- **Permisos**: nuevo módulo/permiso admin (`banners.manage` o equivalente, a definir en HU4 con el
  catálogo de roles de ADR-0023) para las operaciones de escritura; la lectura del set vigente (HU3)
  no requiere permiso — está disponible para cualquier usuario autenticado de cualquier tenant, porque
  el dato es global por diseño.

## ADRs relacionados

- `ADR-0057-banners-imagen-endpoint-propio-sin-presigned` — decisión complementaria del mismo
  Feature #12236 (storage y servido del binario de imagen).
- `ADR-0056-generacion-documental-standalone` — origen de la nota "la RLS de este repo es decorativa";
  reduce el argumento de seguridad de mantener `tenant_id` solo por RLS.
- `ADR-0023-catalogo-global-roles` — catálogo de roles sobre el que se conceden los permisos de
  escritura del módulo de banners.

## Notas para agentes

- **Database Agent**: migración numerada (próxima libre en el momento de implementar, verificar contra
  el directorio real) + migración EF envolvente. Triggers `row_version`/`audit_log` estándar de tabla
  de negocio SÍ aplican (la excepción es solo A4/A10/A11, no A5/A16). `COMMENT ON TABLE` obligatorio
  citando este ADR. Validar que no exista ya un índice/constraint duplicado antes de nombrar
  `ix_banners_activo_vigencia`.
- **Backend Agent**: repositorio de `Banner` sin filtro de tenant (no existe la columna); sin
  `IgnoreQueryFilters()` (no aplica, no hay filtro global de tenant sobre esta entidad). Endpoint de
  consumo (HU3) no filtra por tenant del caller.
- **Frontend Agent**: el set de banners es el mismo en `Dashboard.tsx` y `OtDashboard.tsx`; no debe
  filtrarse ni parametrizarse por tenant/compañía en ningún punto del cliente.
- **QA Agent**: verificar con al menos dos tenants distintos autenticados que ven exactamente el mismo
  set de banners activos y vigentes; no hay caso de prueba de "aislamiento entre tenants" para esta
  tabla porque no aplica.
- **Security Agent**: confirmar que ningún dato de `admin.banners` es PII (nombre del banner, imagen de
  marketing, URL de enlace) — no requiere `COMMENT ... @pii`. Confirmar que el permiso de escritura
  (`banners.manage`) está correctamente acotado a roles admin.
- **Infra Agent**: sin cambios de infraestructura; migración corre igual que cualquier otra en el
  pipeline existente de `core-api`.

## Referencias externas

- Ninguna (decisión interna de modelo de datos).
