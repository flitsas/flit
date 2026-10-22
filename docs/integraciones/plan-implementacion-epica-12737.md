# Plan de implementación — Lado FLIT · Épica #12737

> Mitad FLIT del documento conjunto FLIT #12737 + FLITO #12736. Estado **Propuesto** (actualizado a
> contrato **v3** el 2026-09-29), rama `develop`. Nada implementado. Ejemplos con datos ficticios.
>
> **Delta v3 (2026-09-29):** se elimina el canal de señal entero —outbox, suscripción, envío firmado,
> AsyncAPI— (§3.2); solo pull, con Flito consultando cada 5 min y a demanda. `cursor`+`since` → 400.
> Alerta «cliente sin sync» a 30 min. El resto del plan no cambia.
>
> **Delta v2** (el punto (a) y el (c) quedan anulados por v3): (a) se añadía el canal de señal `tramite_sync_changed` (webhook saliente firmado)
> alimentado por una outbox de sincronización **escrita por el mismo trigger** de `sync_version`
> — ver §3.2; (b) la regla de alcance pasa a ser **«radicado al menos una vez, y ya no sale nunca»**,
> implementada con un `EXISTS` sobre el historial de estados y NO con `draft_finalized_at` (§5.2);
> (c) la suscripción del canal **no** reutiliza `admin.ot_webhook_subscriptions` (es por tenant y
> esta integración es cross-compañía): va en el schema `integrations`. El resto del plan no cambia.
>
> **Parámetros cerrados por el PO (2026-09-22):** `pageSize` máx 1000 · 120 req/min por cliente ·
> ventana de estabilidad 5 s · lockout 15 min · retención de bitácora 12 meses · TTL de URL firmada
> 10 min.
> Contrato de cable: `docs/integraciones/external-api-tramites-sync.md`. Épica y anexos:
> `docs/ado-drafts/epic-flito-sync-tramites/EPIC.md`.

## 1. Resumen del lado FLIT

FLIT (core-api, .NET 10) expone una **API externa de solo lectura** bajo `/api/v1/external/*` para que
Flito sincronice de forma incremental, a demanda y cross-compañía, todos los trámites creados o
modificados desde su último cursor, con datos de vehículo, comprador, organismo, compañía gestora y
adjunto de factura. Para hacerlo confiable se añade una **marca de agua de cambios** en base de datos
(`sync_version` global + `sync_changed_at`, mantenidas por triggers que también reaccionan a cambios en
tablas hijas), un **modelo de clientes de integración** propio (secreto hasheado, scopes, JWT dedicado,
lockout temporal), **bitácora de acceso** y **rate limit** por cliente, y el contrato publicado en OpenAPI.

## 2. Arquitectura

**Dónde vive:** `services/core-api/` (Clean Architecture). Proyectos tocados:

| Capa | Proyecto | Qué entra |
|---|---|---|
| API (host) | `Flit.Api` | Endpoints `External/*`, esquema de autenticación `ExternalClient`, policies por scope, rate limit `external-client`, middleware de bitácora, exención en `TenantEnforcementMiddleware` |
| Gateway | `Flit.Gateway` | Ruta YARP `/api/v1/external/{**catch-all}` sin `JwtRequired` |
| Aplicación | `Flit.Tramites.Application` (sync, adjunto) · `Flit.Admin.Application` (clientes externos, token) | Handlers, DTOs, validación de cursor |
| Dominio | `Flit.Tramites.Domain` (puerto de lectura de sync) · `Flit.Admin.Domain` (entidad `ExternalClient`, `ExternalAccessLog`, puertos) | Contratos y reglas (lockout, rotación, scopes) |
| Infraestructura | `Flit.Infrastructure` | Repositorios (SQL crudo parametrizado + EF), `ExternalIntegrationScope` (cross-tenant), emisor JWT externo, DDL embebido + migraciones EF |
| Contratos | `contracts/openapi/core-api.v1.yaml` | Paths, schemas, `securitySchemes.externalClientAuth` |

Nuevo schema PostgreSQL **`integrations`** (registrar en `SchemaNames.cs`) para `external_clients` y
`external_access_log`; las columnas de marca de agua van en `tramites.procedure_instances`.

```mermaid
sequenceDiagram
    autonumber
    participant F as Flito
    participant G as Flit.Gateway (YARP)
    participant A as core-api /api/v1/external
    participant DB as PostgreSQL
    participant S3 as Object storage

    rect rgb(240,240,255)
    note over F,DB: Auth (client_credentials)
    F->>G: POST /api/v1/external/auth/token {clientId, clientSecret}
    G->>A: proxy (ruta sin JwtRequired; rate limit IP)
    A->>DB: SELECT external_clients WHERE client_id (is_active, locked_until)
    A->>A: Argon2id verify; si falla → failed_attempts++ (5 → locked_until = now()+15min)
    A->>DB: UPDATE last_token_at, failed_attempts=0
    A-->>F: 200 {accessToken RS256 (aud flit-external, scope[]), expiresIn 1800}
    end

    rect rgb(240,255,240)
    note over F,DB: Sync incremental
    F->>G: GET /tramites/sync?cursor=&pageSize=500 (Bearer)
    G->>A: proxy
    A->>A: esquema ExternalClient valida JWT; policy scope external.tramites.read; rate limit client_id
    A->>A: decodifica cursor {v:1, sv:N} (400 invalid_cursor)
    A->>DB: BEGIN; SET LOCAL row_security = off (ExternalIntegrationScope)
    A->>DB: SELECT ... WHERE sync_version > N AND sync_changed_at <= now()-5s ORDER BY sync_version LIMIT 501
    DB-->>A: filas (LATERAL: actor, aprobación, factura, pivot vehículo)
    A->>A: mapea ítems; enmascara PII si falta scope pii.read; hasMore = filas > 500
    A->>DB: INSERT integrations.external_access_log (client, sv_from/to, count, tenants, ms, status)
    A-->>F: 200 {items[500], nextCursor {sv:último}, hasMore, serverTime}
    end

    rect rgb(255,245,230)
    note over F,S3: Descarga adjunto factura
    F->>G: GET /tramites/{id}/adjuntos/{adjuntoId}/url (Bearer)
    G->>A: proxy
    A->>DB: SELECT attachment WHERE id AND procedure_instance_id (404 si no)
    A->>S3: presigned GET (Content-Disposition attachment, TTL corto)
    A->>DB: INSERT external_access_log (endpoint=adjunto-url)
    A-->>F: 200 {url, expiraEn, nombreArchivo, contentType}
    F->>S3: GET url firmada
    end
```

## 3. Modelo de datos y migraciones

Convención del repo: `tramites.procedure_instances` está `ExcludeFromMigrations()`; **todo DDL va en SQL
crudo embebido** `src/Flit.Infrastructure/Persistence/Sql/Ddl/NNN-*.sql` cargado por una migración EF
con `EmbeddedDdl.LoadUp(...)` (último número usado: 117). Validar con `db-schema-validator`.

### 3.1 Marca de agua en `tramites.procedure_instances` (DDL 122 y 123, implementados)

```sql
CREATE SEQUENCE IF NOT EXISTS tramites.procedure_sync_seq AS bigint;

ALTER TABLE tramites.procedure_instances
  ADD COLUMN sync_version    bigint      NOT NULL DEFAULT 0,
  ADD COLUMN sync_changed_at timestamptz NOT NULL DEFAULT now();

-- BEFORE UPDATE en el padre: cualquier UPDATE (estado, gestor, OT, deleted_at, denormalizados) bumpea.
CREATE OR REPLACE FUNCTION tramites.trg_procedure_sync_stamp() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  NEW.sync_version    := nextval('tramites.procedure_sync_seq');
  NEW.sync_changed_at := now();
  RETURN NEW;
END $$;
CREATE TRIGGER tr_procedure_instances_sync_stamp BEFORE INSERT OR UPDATE ON tramites.procedure_instances
  FOR EACH ROW EXECUTE FUNCTION tramites.trg_procedure_sync_stamp();

-- Hijas: AFTER ... FOR EACH STATEMENT con transition tables → un solo UPDATE del padre por sentencia.
CREATE OR REPLACE FUNCTION tramites.trg_procedure_child_touch() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF pg_trigger_depth() > 1 THEN RETURN NULL; END IF;   -- no reentrar desde triggers de denormalización (DDL 47)
  UPDATE tramites.procedure_instances p SET sync_changed_at = now()   -- el BEFORE UPDATE asigna sync_version
   WHERE p.id IN (SELECT procedure_instance_id FROM new_rows
                  UNION SELECT procedure_instance_id FROM old_rows);
  RETURN NULL;
END $$;
-- Repetir para: procedure_instance_actors, procedure_instance_field_values,
-- procedure_instance_status_history, procedure_instance_attachments, procedure_instance_commercial
CREATE TRIGGER tr_pi_actors_sync_touch_ins AFTER INSERT ON tramites.procedure_instance_actors
  REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION tramites.trg_procedure_child_touch();
-- (variantes UPDATE con OLD+NEW y DELETE con OLD; PostgreSQL exige un trigger por evento para transition tables)

-- Backfill único, en orden de creación, sin ruido en audit_log (patrón DDL 47):
ALTER TABLE tramites.procedure_instances DISABLE TRIGGER USER;
UPDATE tramites.procedure_instances p SET sync_version = s.sv, sync_changed_at = now()
  FROM (SELECT id, row_number() OVER (ORDER BY created_at, id) AS sv FROM tramites.procedure_instances) s
 WHERE s.id = p.id;
SELECT setval('tramites.procedure_sync_seq', (SELECT max(sync_version) FROM tramites.procedure_instances));
ALTER TABLE tramites.procedure_instances ENABLE TRIGGER USER;

CREATE UNIQUE INDEX uq_procedure_instances_sync_version ON tramites.procedure_instances (sync_version);
```

### 3.2 ~~Outbox de señal~~ — retirada en v3

El trigger de §3.1 solo mantiene `sync_version` / `sync_changed_at`; **no escribe en ninguna outbox ni
emite nada hacia Flito**. No se crean `tramites.procedure_sync_outbox` ni
`integrations.external_subscriptions`, ni el `BackgroundService` de entrega.

### 3.2 Índices de apoyo (DDL 124 — hecho, HU #13075; ver ADR-0066)

```sql
CREATE INDEX ix_pi_status_history_aprobado ON tramites.procedure_instance_status_history (procedure_instance_id, changed_at DESC)
  WHERE to_status = 'aprobado';
CREATE INDEX ix_pi_attachments_factura ON tramites.procedure_instance_attachments (procedure_instance_id, uploaded_at DESC)
  WHERE tipo = 'factura';   -- procedure_instance_attachments no tiene deleted_at
-- actors ya tiene índice por (procedure_instance_id); field_values tiene UNIQUE (procedure_instance_id, field_key).
```

### 3.3 Clientes externos y bitácora — schema `integrations` (siguiente DDL libre)

```sql
CREATE SCHEMA IF NOT EXISTS integrations;

CREATE TABLE integrations.external_clients (
  id                    uuid PRIMARY KEY,
  client_id             varchar(64)  NOT NULL,
  display_name          varchar(120) NOT NULL,
  purpose               varchar(300) NOT NULL,            -- finalidad (Ley 1581)
  secret_hash           text         NOT NULL,            -- Argon2id (@pii:high)
  previous_secret_hash  text         NULL,                -- ventana de rotación
  secret_rotated_at     timestamptz  NULL,
  must_rotate           boolean      NOT NULL DEFAULT false,
  scopes                jsonb        NOT NULL DEFAULT '[]'::jsonb,
  is_active             boolean      NOT NULL DEFAULT true,
  failed_attempts       int          NOT NULL DEFAULT 0,
  locked_until          timestamptz  NULL,
  last_token_at         timestamptz  NULL,
  created_at timestamptz NOT NULL DEFAULT now(), created_by uuid NULL,
  updated_at timestamptz NOT NULL DEFAULT now(), updated_by uuid NULL,
  row_version int NOT NULL DEFAULT 1,
  CONSTRAINT uq_external_clients_client_id UNIQUE (client_id),
  CONSTRAINT ck_external_clients_scopes_array CHECK (jsonb_typeof(scopes) = 'array')
);
-- Sin RLS: entidad cross-tenant por naturaleza (misma justificación que catálogos globales, ADR-0019).
CREATE TRIGGER tr_external_clients_row_version BEFORE UPDATE ON integrations.external_clients
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
CREATE TRIGGER tr_external_clients_audit AFTER INSERT OR UPDATE OR DELETE ON integrations.external_clients
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

CREATE TABLE integrations.external_access_log (
  id                 uuid PRIMARY KEY,
  client_id          varchar(64)  NOT NULL,
  endpoint           varchar(80)  NOT NULL,            -- 'token' | 'tramites.sync' | 'tramites.adjunto-url'
  request_id         varchar(64)  NULL,
  ip                 inet         NULL,
  sync_version_from  bigint       NULL,
  sync_version_to    bigint       NULL,
  items_count        int          NULL,
  tenant_ids         uuid[]       NULL,                -- compañías tocadas en la página
  pii_unmasked       boolean      NOT NULL DEFAULT false,
  http_status        int          NOT NULL,
  duration_ms        int          NOT NULL,
  occurred_at        timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX ix_external_access_log_client_occurred ON integrations.external_access_log (client_id, occurred_at DESC);
CREATE INDEX ix_external_access_log_occurred ON integrations.external_access_log (occurred_at);   -- depuración por retención
```

### 3.4 Orden y riesgo

| # | Migración / DDL | Riesgo | Mitigación |
|---|---|---|---|
| 1 | 118 marca de agua + backfill | **Medio-alto**: `UPDATE` masivo sobre `procedure_instances` (decenas de miles) bloquea escrituras durante el backfill; `NOT NULL DEFAULT` con reescritura | Ventana corta fuera de horario; PG 17 añade columnas con default sin reescritura; backfill en una transacción con triggers de usuario deshabilitados (como DDL 47); probar en QA con copia de PDN |
| 2 | 119 índices parciales | Bajo | `CREATE INDEX CONCURRENTLY` no se puede en transacción de migración EF → índices pequeños (parciales), aceptable; alternativa: DDL manual previo |
| 3 | 120 schema `integrations` | Bajo | Tablas nuevas, sin datos |
| 4 | Retención de bitácora (job) | Bajo | Estándar de jobs periódicos ADR-0059 (`BackgroundService` + config en BD) |

## 4. Autenticación de clientes externos

| Aspecto | Diseño |
|---|---|
| Tabla | `integrations.external_clients` (§3.3). Entidad `ExternalClient` en `Flit.Admin.Domain/Integrations/`. |
| Alta / quién | Endpoints SuperAdmin `POST/GET /api/v1/admin/external-clients`, `POST .../{id}/rotate`, `POST .../{id}/deactivate`, `GET .../{id}/access-log`. Policy `SuperAdminPolicy` (existente). El alta devuelve `clientSecret` **una sola vez**. |
| Secreto | 32 bytes aleatorios base64url; hash **Argon2id** con `Argon2PasswordHasher` (ya existe en `Flit.Infrastructure/Security/`, registrado como `IPasswordHasher`). Nunca en logs (detector de seguridad inline). |
| Rotación | `rotate` genera secreto nuevo, mueve el hash actual a `previous_secret_hash` (válido 24 h), `must_rotate=false`. Con `must_rotate=true` el token responde `403 secret_rotation_required` salvo en `/auth/rotate` (autenticado con el secreto vigente). |
| Token | `POST /api/v1/external/auth/token` → JWT **RS256** con **par de claves dedicado** (`ExternalJwt:PrivateKeyPem/Path`, `ExternalJwt:PublicKeyPem/Path`), `iss=flit-core-external`, `aud=flit-external`, `sub=client_id`, `scope` (array), `jti`, `exp=+30 min`. Emisor `ExternalClientJwtTokenIssuer` (clon acotado de `RsaJwtTokenIssuer`). |
| Validación | Segundo `AddJwtBearer("ExternalClient")` en `ApiSecurityExtensions` (mismo patrón que `IctServiceScheme`, fail-closed si no hay llave). Policies: `ExternalTramitesRead` = esquema `ExternalClient` + `RequireClaim("scope","external.tramites.read")`; `ExternalTramitesPiiRead` idem con `external.tramites.pii.read` (evaluada en handler para enmascarar, no para rechazar). |
| Scopes | `external.tramites.read`, `external.tramites.pii.read`. Flito recibe ambos. |
| Lockout | 5 fallos consecutivos → `locked_until = now() + 15 min` (configurable `ExternalClients:LockoutMinutes`), respuesta `423 client_locked`; éxito resetea `failed_attempts`; el admin puede desbloquear (`PATCH .../{id}/unlock`). |
| Rate limit | Token: fixed window por IP 10/min (`external-token`). Datos: sliding window por `client_id` 120/min (`external-client`), `429` + `Retry-After`. Registradas junto a `PublicBrandingRateLimit`. |
| Auditoría | Cada request → `integrations.external_access_log` (middleware `ExternalAccessLogMiddleware` acotado al prefijo). Alta/rotación/desactivación → `audit.audit_log` por trigger. |
| Un cliente por ambiente | `flito-dev`, `flito-qa`, `flito-pdn`, creados por SuperAdmin en cada ambiente (no se comparten secretos entre ambientes). |

## 5. Endpoints

Todos bajo `MapGroup("/api/v1/external")` en `Flit.Api/Endpoints/External/`. Errores `application/problem+json` (RFC 7807) con `type`, `title`, `status`, `detail`, `instance`, `traceId`. `Cache-Control: no-store`.

### 5.1 `POST /api/v1/external/auth/token`

Body `{ "clientId": "flito-qa", "clientSecret": "<secreto>" }` →
`200 { "accessToken": "<jwt>", "tokenType": "Bearer", "expiresIn": 1800, "scope": ["external.tramites.read","external.tramites.pii.read"] }`.
Errores: `400 validation_error`, `401 invalid_client` (uniforme: inválido o inactivo), `403 secret_rotation_required`, `423 client_locked` (con `Retry-After`), `429`.

### 5.2 `GET /api/v1/external/tramites/sync`

Query: `cursor` (opaco), `since` (ISO-8601, solo sin cursor), `pageSize` (1..1000, default 200).
Respuesta `200 { items[], nextCursor, hasMore, pageSize, serverTime }`; ítem según §6. Errores:
`400 invalid_cursor | invalid_page_size | invalid_since | cursor_and_since_exclusive`, `401`, `403 insufficient_scope`, `429`.

**Cursor:** base64url de `{"v":1,"sv":<sync_version>}`; `v` permite cambiar el formato sin romper clientes;
cursor vacío ⇒ `sv=0`. `since` se resuelve a `sv = COALESCE((SELECT min(sync_version) FROM procedure_instances WHERE sync_changed_at >= :since), max+1) - 1`.
`nextCursor` = `sv` del último ítem devuelto (o el de entrada si la página está vacía).

**Consulta de referencia** (SQL crudo parametrizado con `DbCommand`, patrón `AnalyticsReadRepository`, ADR-0021;
ejecutada dentro de `ExternalIntegrationScope` = `BEGIN; SET LOCAL row_security = off`):

```sql
WITH page AS (
  SELECT pi.*
    FROM tramites.procedure_instances pi
   WHERE pi.sync_version > @cursor
     AND pi.sync_changed_at <= now() - make_interval(secs => @stabilityLagSeconds)
     -- Alcance: radicado al menos una vez. EstadosDeLlegadaAlOrganismo (ADR-0059) = preasignacion|entregado.
     -- Una vez que la fila de historial existe, existe para siempre ⇒ el trámite que retrocede a
     -- borrador SIGUE entregándose. NO usar draft_finalized_at: significa otra cosa (marca del flujo
     -- de identidad asíncrona) y deja fuera a los trámites que se radicaron sin pasar por ella.
     AND EXISTS (SELECT 1 FROM tramites.procedure_instance_status_history h
                  WHERE h.procedure_instance_id = pi.id
                    AND h.to_status IN ('preasignacion','entregado'))
     -- Migrados de FLIT 1 fuera del feed (decisión del PO, 2026-09-29): FLITO ya los recibe por FLIT 1.
     AND pi.is_migrated = false
   ORDER BY pi.sync_version
   LIMIT @pageSizePlusOne
)
SELECT p.id, p.reference_number, p.consecutivo, p.sync_version, p.sync_changed_at,
       (p.deleted_at IS NOT NULL) AS eliminado,
       p.status, pt.code AS tipo_codigo, pt.name AS tipo_nombre, pt.family AS tipo_familia,
       p.created_at, p.submitted_at, ap.fecha_aprobacion,
       p.vin, p.plate, v.*,
       tof.code AS ot_codigo, tof.name AS ot_nombre, tof.city_code AS ot_codigo_secretaria,
       tof.city_name AS ot_ciudad, tof.department_name AS ot_departamento,
       COALESCE(a.compradores, '[]'::jsonb) AS compradores,
       f.id AS factura_adjunto_id, f.filename AS factura_nombre, f.uploaded_at AS factura_cargada_en,
       t.id AS tenant_id, t.tax_id AS nit, t.legal_name AS compania_nombre
  FROM page p
  JOIN tramites.procedure_types pt ON pt.id = p.procedure_type_id
  JOIN identity.tenants t          ON t.id = p.tenant_id
  LEFT JOIN catalogs.transit_offices tof ON tof.id = p.transit_office_id
  LEFT JOIN LATERAL (
        -- Copropiedad (ADR-0053): TODOS los actores del rol elegido (comprador > propietario), por ordinal.
        SELECT jsonb_agg(jsonb_build_object(
                 'ordinal', x.ordinal, 'porcentaje', x.ownership_percentage,
                 'actor_type', x.actor_type, 'person_type', x.person_type,
                 'document_type', x.document_type, 'document_number', x.document_number,
                 'full_name', x.full_name, 'direccion', x.metadata->>'direccion',
                 'ciudad', x.metadata->>'ciudad', 'phone', x.phone, 'email', x.email)
               ORDER BY x.ordinal) AS compradores
          FROM tramites.procedure_instance_actors x
         WHERE x.procedure_instance_id = p.id
           AND x.actor_type = CASE WHEN EXISTS (SELECT 1 FROM tramites.procedure_instance_actors y
                                                 WHERE y.procedure_instance_id = p.id AND y.actor_type = 'comprador')
                                   THEN 'comprador' ELSE 'propietario' END) a ON true
  LEFT JOIN LATERAL (
        SELECT max(h.changed_at) AS fecha_aprobacion FROM tramites.procedure_instance_status_history h
         WHERE h.procedure_instance_id = p.id AND h.to_status = 'aprobado') ap ON true
  LEFT JOIN LATERAL (
        SELECT x.id, x.filename, x.uploaded_at FROM tramites.procedure_instance_attachments x
         WHERE x.procedure_instance_id = p.id AND x.tipo = 'factura'
         ORDER BY x.uploaded_at DESC LIMIT 1) f ON true
  LEFT JOIN LATERAL (
        SELECT max(value_text) FILTER (WHERE field_key='vehicle_class')               AS clase,
               max(value_text) FILTER (WHERE field_key='vehicle_brand')               AS marca,
               max(value_text) FILTER (WHERE field_key='vehicle_line')                AS linea,
               COALESCE(max(value_text) FILTER (WHERE field_key='vehicle_year'),
                        max(value_text) FILTER (WHERE field_key='vehicle_model'))     AS modelo_ano,
               max(value_text) FILTER (WHERE field_key='vehicle_body_type')           AS carroceria,
               max(value_text) FILTER (WHERE field_key='vehicle_engine_displacement') AS cilindraje,
               max(value_text) FILTER (WHERE field_key='vehicle_passengers')          AS capacidad,
               max(value_text) FILTER (WHERE field_key='vehicle_engine_number')       AS numero_motor,
               max(value_text) FILTER (WHERE field_key='vehicle_series')              AS numero_serie,
               max(value_text) FILTER (WHERE field_key='vehicle_service')             AS tipo_servicio
          FROM tramites.procedure_instance_field_values fv
         WHERE fv.procedure_instance_id = p.id AND fv.field_key = ANY(@vehicleKeys)) v ON true
 ORDER BY p.sync_version;
```

Costo: `page` usa el índice único `(sync_version)` (range scan + LIMIT); cada LATERAL es O(1) por fila
por los índices de §3.2 y el UNIQUE `(procedure_instance_id, field_key)`. `hasMore = filas > pageSize`.
`NULLIF(btrim(x),'')` en el mapeo C# para garantizar `null` y nunca `" "`. Enteros (`modeloAno`,
`cilindraje`, `capacidad`) con `int.TryParse`; si falla, `null` + `cilindrajeTexto`. `tipoServicio` con
`VehicleServiceTypeCode.Resolve`. Fechas a `America/Bogota` (`ColombiaTime`, arch test existente).

### 5.3 `GET /api/v1/external/tramites/{id}/adjuntos/{adjuntoId}/url`

`200 { "url": "https://…", "expiraEn": "2026-09-21T10:30:00-05:00", "nombreArchivo": "factura.pdf", "contentType": "application/pdf" }`.
Errores: `404 not_found` (trámite o adjunto inexistente, eliminado o de otro trámite), `401`, `403`, `429`.
Reutiliza `IAttachmentStorage` (presigned GET, mismo mecanismo que `GetAttachmentPreviewUrlHandler`, ADR-0029),
con `Content-Disposition: attachment` y TTL corto (`ExternalClients:AttachmentUrlTtlMinutes`, propuesta 10).

## 6. Mapeo de campos

| Clave del ítem | Origen FLIT | Derivación |
|---|---|---|
| `id` | `procedure_instances.id` | uuid v7 |
| `radicado` / `consecutivo` | `reference_number` / `consecutivo` | `FTn-NNNNNNN` (CHECK + trigger de inmutabilidad, DDL 108) |
| `syncVersion` / `fechaUltimoCambio` | `sync_version` / `sync_changed_at` | nuevos (§3.1) |
| `eliminado` | `deleted_at IS NOT NULL` | tombstone: `true` ⇒ `vehiculo`, `organismo`, `factura` en `null` y `compradores` = `[]`; `companiaGestora`, `estado`, `radicado` se mantienen |
| `estado` | `status` | vocabulario `TramiteEstado.Todos`, tal cual (minúsculas). El alcance NO se filtra por `status` sino por el `EXISTS` sobre el historial (§5.2) |
| `tramite.{codigo,nombre,familia}` | `procedure_types.{code,name,family}` | |
| `fechaCreacion` / `fechaRadicacion` | `created_at` / `submitted_at` | |
| `fechaAprobacion` | `procedure_instance_status_history` | `max(changed_at) WHERE to_status='aprobado'` (`completed_at` no se escribe en la app) |
| `vehiculo.vin` / `.placa` | `procedure_instances.vin` / `.plate` | denormalizados por trigger (DDL 47) |
| `vehiculo.clase / marca / linea / carroceria / numeroMotor / numeroSerie` | `procedure_instance_field_values` | `field_key` = `vehicle_class`, `vehicle_brand`, `vehicle_line`, `vehicle_body_type`, `vehicle_engine_number`, `vehicle_series` (`VehicleFieldKeys.cs`) |
| `vehiculo.modeloAno` | `field_values` | `COALESCE(vehicle_year, vehicle_model)` → `int?` |
| `vehiculo.cilindraje` / `.cilindrajeTexto` | `field_values` `vehicle_engine_displacement` | `int.TryParse`; si falla → `null` + texto crudo |
| `vehiculo.capacidad` | `field_values` `vehicle_passengers` | `int?` |
| `vehiculo.tipoServicio.{codigo,nombre}` | `field_values` `vehicle_service` | `VehicleServiceTypeCode.Resolve` + `catalogs.vehicle_service_types` |
| `organismo.codigoTransito / nombre / codigoSecretaria / ciudad / departamento` | `catalogs.transit_offices` vía `transit_office_id` | `code` (RUNT 8 dígitos), `name`, `city_code` (DIVIPOLA), `city_name`, `department_name` |
| `compradores[]` | `procedure_instance_actors` | LATERAL `jsonb_agg`: todos los actores del rol `comprador` (o `propietario` si no hay comprador), por `ordinal`; `[]` si ninguno (ADR-0053) |
| `compradores[].ordinal / porcentajeParticipacion` | `ordinal` / `ownership_percentage` | `numeric(5,2)`; `null` con un solo actor |
| `compradores[].rolActor / tipoPersona` | `actor_type` / `person_type` | `natural` \| `juridical` |
| `compradores[].tipoDocumento` | `document_type` | código canónico FLIT (CC, NIT, CE, PAS, TI…), sin transformación |
| `compradores[].numeroDocumento / nombreCompleto / celular / correo` | `document_number` / `full_name` / `phone` / `email` | PII: enmascarar sin scope `pii.read` (`9****0000`, `c***@dominio`) |
| `compradores[].direccion / ciudad` | `metadata->>'direccion'` / `metadata->>'ciudad'` | jsonb camelCase (`ActorMetadataReader`) |
| `factura.{adjuntoId,nombreArchivo,cargadaEn}` | `procedure_instance_attachments` | `tipo='factura'`, más reciente por `uploaded_at` (la tabla no tiene `deleted_at` ni `created_at`); `cargadaEn` = `uploaded_at` |
| `companiaGestora.{tenantId,nit,nombre}` | `identity.tenants.{id,tax_id,legal_name}` | vía `procedure_instances.tenant_id` |

## 7. Archivos a crear / modificar

Rutas relativas a `services/core-api/` salvo indicación.

**Base de datos**
- `src/Flit.Infrastructure/Persistence/Sql/Ddl/118-E12737-procedure-sync-watermark.sql` — crear: secuencia, columnas, triggers padre/hijas, backfill, índice único.
- `src/Flit.Infrastructure/Persistence/Sql/Ddl/119-E12737-sync-support-indexes.sql` — crear: índices parciales aprobación/factura.
- `src/Flit.Infrastructure/Persistence/Sql/Ddl/120-E12737-integrations-external-clients.sql` — crear: schema `integrations`, `external_clients`, `external_access_log`.
- `src/Flit.Infrastructure/Migrations/2026MMDDhhmmss_E12737_*.cs` (×3) — crear: `EmbeddedDdl.LoadUp/LoadDown`.
- `src/Flit.Infrastructure/Persistence/Schemas/SchemaNames.cs` — modificar: `Integrations = "integrations"`.
- `src/Flit.Infrastructure/Persistence/Configurations/Tramites/ProcedureInstanceConfiguration.cs` — modificar: mapear `SyncVersion`/`SyncChangedAt` (solo lectura, `ValueGeneratedOnAddOrUpdate`).
- `src/Flit.Tramites.Domain/Entities/ProcedureInstance.cs` — modificar: propiedades `SyncVersion`, `SyncChangedAt`.
- `src/Flit.Infrastructure/Persistence/Entities/Integrations/{ExternalClientRow,ExternalAccessLogRow}.cs` + `Configurations/Integrations/*.cs` — crear: EF para tablas nuevas.

**Dominio / aplicación**
- `src/Flit.Admin.Domain/Integrations/{ExternalClient.cs,ExternalClientScopes.cs,IExternalClientRepository.cs,IExternalAccessLogRepository.cs,ExternalAccessLogEntry.cs}` — crear.
- `src/Flit.Admin.Application/Integrations/ExternalClients/{CreateExternalClient,RotateExternalClientSecret,DeactivateExternalClient,UnlockExternalClient,ListExternalClients,GetExternalClientAccessLog}/*Handler.cs` — crear.
- `src/Flit.Admin.Application/Integrations/Auth/IssueExternalClientTokenHandler.cs` — crear: verificación Argon2, lockout, emisión.
- `src/Flit.Admin.Application/Abstractions/IExternalClientTokenIssuer.cs` — crear.
- `src/Flit.Tramites.Domain/Repositories/IProcedureSyncReadRepository.cs` + `Sync/{ProcedureSyncItem,ProcedureSyncPage,SyncCursor}.cs` — crear: puerto y modelos de lectura.
- `src/Flit.Tramites.Application/UseCases/ExternalSync/{SyncProceduresQuery,SyncProceduresHandler,ProcedureSyncItemDto,ProcedureSyncItemMapper,PiiMasker}.cs` — crear.
- `src/Flit.Tramites.Application/UseCases/ExternalSync/GetExternalAttachmentUrlHandler.cs` — crear: valida pertenencia + `IAttachmentStorage` presigned GET.

**Infraestructura**
- `src/Flit.Infrastructure/Persistence/Repositories/ProcedureSyncReadRepository.cs` — crear: SQL §5.2 con `DbCommand`.
- `src/Flit.Infrastructure/Persistence/Repositories/ExternalIntegrationScope.cs` — crear: `SET LOCAL row_security = off` (gemelo de `SuperAdminTenantScope`, `internal`, solo inyectable en el repositorio de sync).
- `src/Flit.Infrastructure/Persistence/Repositories/{ExternalClientRepository,ExternalAccessLogRepository}.cs` — crear.
- `src/Flit.Infrastructure/Security/ExternalClientJwtTokenIssuer.cs` + `ExternalJwtOptions.cs` — crear.
- `src/Flit.Infrastructure/AdminInfrastructureExtensions.cs` — modificar: DI de lo anterior.

**API / Gateway**
- `src/Flit.Api/Endpoints/External/{ExternalAuthEndpoints,ExternalTramitesSyncEndpoints,ExternalAttachmentEndpoints}.cs` — crear.
- `src/Flit.Api/Endpoints/AdminExternalClientsEndpoints.cs` — crear: administración SuperAdmin.
- `src/Flit.Api/Authorization/ApiSecurityExtensions.cs` — modificar: esquema `ExternalClient` + policies `ExternalTramitesRead`/`ExternalTramitesPiiRead`.
- `src/Flit.Api/Authorization/ExternalClientAuthorization.cs` — crear: constantes de esquema/policies/scopes.
- `src/Flit.Api/RateLimiting/ExternalClientRateLimit.cs` + `ExternalClientOptions.cs` — crear: policies `external-token`, `external-client`.
- `src/Flit.Api/Middleware/ExternalAccessLogMiddleware.cs` — crear.
- `src/Flit.Api/Middleware/TenantEnforcementMiddleware.cs` — modificar: `/api/v1/external` explícitamente fuera del enforcement (documentado como `internal`/`public`).
- `src/Flit.Api/Program.cs` — modificar: `MapExternal*Endpoints`, middleware, rate limit, options.
- `src/Flit.Api/OpenApi/SwaggerExtensions.cs` — modificar: `securityScheme` `externalClientAuth`.
- `src/Flit.Api/appsettings*.json` — modificar: `ExternalJwt`, `ExternalClients` (lag, lockout, TTL adjunto, límites).
- `src/Flit.Gateway/appsettings.json` — modificar: ruta `external-route` `/api/v1/external/{**catch-all}` sin `AuthorizationPolicy`, antes del catch-all `/api/**`; timeout de cluster ≥ 30 s.
- `src/Flit.Infrastructure/Analytics/Scheduling/` o job nuevo `ExternalAccessLogRetentionJob.cs` — crear: depuración por retención (ADR-0059).

**Contratos y docs (raíz del repo)**
- `contracts/openapi/core-api.v1.yaml` — modificar: 3 paths externos + 6 admin, schemas `ExternalSyncPage`, `ProcedureSyncItem`, `ExternalTokenResponse`, `securitySchemes.externalClientAuth`, `x-pii`.
- `docs/integraciones/external-api-tramites-sync.md` — mantener como guía de consumo (ya existe).
- `services/core-api/docs/adr/ADR-00xx-{sync-watermark,external-clients-auth,keyset-cursor,cross-tenant-external-read}.md` — crear (4, estado Propuesto).

**Pruebas** — ver §9 (rutas allí).

## 8. Features e HUs previstas bajo #12737

Features creadas el 2026-09-29 en Sprint 9 (decisión del PO). Tags `DOR; adopcion-ia; fase-1-diseño`. SP en escala Fibonacci. Prioridad: **F1 → F3 con F2 en paralelo → F4 → F5**.

| Feature | HU | Capa | SP | Depende de |
|---|---|---|---|---|
| **F1 #13062 `[TRAMITES] - Marca de agua de sincronización de trámites`** | HU1.1 Columnas `sync_*`, secuencia y trigger del padre (DDL 122 — hecho, HU #13073) | BACKEND/DB | 5 | — |
| | HU1.2 Triggers statement-level en 5 tablas hijas + backfill (DDL 123 — hecho, HU #13074) | BACKEND/DB | 8 | HU1.1 |
| | HU1.3 Índices de apoyo (siguiente DDL libre), validación `db-schema-validator`, ADR marca de agua, prueba de integración de concurrencia EF | BACKEND/DB | 5 | HU1.2 |
| **F2 #13065 `[INTEGRACIONES] - Cliente de integración para sistemas externos`** | HU2.1 Schema `integrations`, entidad `ExternalClient` y repositorio calcados de `IntegrationClient` (core-ict), siguiente DDL libre | BACKEND | 3 | — |
| | HU2.2 `POST /external/auth/token`: Argon2id, emisor JWT dedicado, esquema `ExternalClient` + policies por permiso, bloqueo, rotación con ventana, `must_rotate`, límite por IP | BACKEND | 5 | HU2.1 |
| | HU2.3 Endpoints admin SuperAdmin (listar/crear/editar/regenerar secreto/desbloquear) + alta `flito-*` + ADR auth externa | BACKEND | 3 | HU2.1 |
| **F3 #13066 `[INTEGRACIONES] - Endpoint de sincronización de trámites, URL de adjunto de factura y contrato OpenAPI`** | HU3.1 `ExternalIntegrationScope` + `ProcedureSyncReadRepository` (SQL keyset, ventana de estabilidad) + arch test de alcance | BACKEND | 8 | F1 |
| | HU3.2 Mapeo de ítem: pivot vehículo, normalizaciones, comprador/aprobación/factura/organismo/compañía, nulls estrictos, tombstones | BACKEND | 8 | HU3.1 |
| | HU3.3 `GET /external/tramites/sync`: cursor opaco, `since`, validaciones 400, exención de tenant middleware, enmascarado PII por scope | BACKEND | 5 | HU3.2, F2 |
| | HU3.4 `GET /external/tramites/{id}/adjuntos/{adjuntoId}/url` (presigned) | BACKEND | 3 | F2 |
| | HU3.5 OpenAPI (paths, schemas, securityScheme, `x-pii`, ejemplos ficticios) + lint CI + guía de consumo | BACKEND | 3 | HU3.3 |
| | HU3.6 Pruebas de integración (Testcontainers) sync/cursor/concurrencia + prueba de carga p95 | QA/BACKEND | 5 | HU3.3 |
| | HU3.7 ADR cursor keyset + ADR lectura cross-tenant externa | BACKEND | 2 | HU3.1 |
| | HU3.8 Ruta Gateway `/api/v1/external/**` sin `JwtRequired` + timeout (necesaria para alcanzar el endpoint desde fuera en DEV) | INFRA | 2 | F2 |
| **F4 #13067 `[INTEGRACIONES] - Protección, auditoría y cumplimiento del acceso externo`** | HU4.1 Rate limit `external-client` por `client_id` (429 + `Retry-After`) | BACKEND | 3 | F2 |
| | HU4.2 `external_access_log` + middleware de bitácora (sin PII en logs), retención 12 meses | BACKEND | 5 | F2 |
| | *Segunda fase:* métricas/alertas (429, sin lectura > 30 min) y job de retención (ADR-0059) | — | — | — |
| **F5 (opcional) #13068 `[INTEGRACIONES] - Consulta de detalle de trámite para clientes externos`** | HU5.1 `GET /external/tramites/{idOrRadicado}` | BACKEND | 3 | F3 |
| | HU5.2 Pruebas + OpenAPI | BACKEND | 2 | HU5.1 |

Total: 16 HU · ~73 SP sin F5 (18 HU · ~78 SP con F5).

## 9. Pruebas

| Tipo | Alcance | Dónde |
|---|---|---|
| Unitarias | Codificación/decodificación de cursor (v, sv, base64url inválido); `PiiMasker`; mapeo de ítem (nulls estrictos, `int.TryParse`, `cilindrajeTexto`, `tipoServicio`); reglas de `ExternalClient` (lockout, rotación, scopes) | `tests/Flit.Tramites.Application.Tests/ExternalSync/`, `tests/Flit.Admin.Tests/Integrations/` |
| Integración (BD, Testcontainers PostgreSQL 17) | Triggers: cambio en cada tabla hija bumpea `sync_version` exactamente una vez por sentencia; backfill ordenado; `row_version` + EF concurrency en guardado padre+hijos; consulta keyset sin omisiones con transacciones concurrentes y lag de 5 s; filtro borrador; tombstone; precedencia comprador/propietario; copropiedad (2–4 compradores, orden y porcentaje); `compradores: []`; `fechaAprobacion` | `tests/Flit.Integration.Tests/ExternalSync/` |
| Arquitectura | `ExternalIntegrationScope` solo referenciado por `ProcedureSyncReadRepository`; `/api/v1/external` declarado en `TenantEnforcementMiddleware`; sin interpolación SQL (`SqlInterpolationArchitectureTests`); fechas Colombia (`ColombiaTimeArchitectureTests`) | `tests/Flit.Admin.Tests/Architecture/` |
| Contrato | `redocly lint` en CI (`.github/workflows/contracts.yml`); prueba que serializa un `ProcedureSyncItemDto` y lo valida contra el schema OpenAPI (todas las claves presentes) | `tests/Flit.Admin.Tests/OpenApi/` |
| Endpoint (WebApplicationFactory) | 401 sin token / token de plataforma; 403 sin scope; 400 por cursor/pageSize/since; 200 vacío; enmascarado sin `pii.read`; 404 adjunto ajeno; 423 lockout; 429 | `tests/Flit.Integration.Tests/External/` |
| Carga | Dataset sintético 50k trámites (sin PII real) en QA: página 1000 → p95 < 1,5 s; corrida completa 50k/500 por página < 3 min; 120 req/min sin degradar p95 del resto de la API | script `k6`/`bombardier` en `tests/load/external-sync.js` (nuevo) |
| Seguridad | `flit-inline-security-detector` en PRs; gitleaks; prueba de que el secreto nunca aparece en logs ni respuestas posteriores al alta; `security-agent` audita PII/Habeas Data del módulo | pipeline existente |

## 10. Despliegue y operación

**Configuración por ambiente (`appsettings.{Env}.json` / variables):**

| Clave | DEV | QA | PDN |
|---|---|---|---|
| `ExternalJwt:PrivateKeyPath` / `PublicKeyPath` | par dedicado dev | par dedicado qa | par dedicado pdn (secret manager) |
| `ExternalJwt:Issuer` / `Audience` | `flit-core-external` / `flit-external` | ídem | ídem |
| `ExternalJwt:TokenMinutes` | 30 | 30 | 30 |
| `ExternalClients:LockoutMinutes` | 15 | 15 | 15 |
| `ExternalClients:StabilityLagSeconds` | 5 | 5 | 5 |
| `ExternalClients:MaxPageSize` / `DefaultPageSize` | 1000 / 200 | ídem | ídem |
| `ExternalClients:AttachmentUrlTtlMinutes` | 10 | 10 | 10 |
| `ExternalClients:AccessLogRetentionDays` | 90 | 180 | 365 |
| `RateLimit:ExternalClientPerMinute` / `ExternalTokenPerIpPerMinute` | 120 / 10 | ídem | ídem |
| Gateway `Clusters.core-api-cluster.HttpRequest.ActivityTimeout` (HU3.8) | 60 s | 60 s | 60 s |

**Alta del cliente de Flito** (por SuperAdmin, en cada ambiente, tras desplegar F2):
`POST /api/v1/admin/external-clients { clientId: "flito-<env>", displayName: "Flito", purpose: "Sincronización de trámites para procesos Flito", scopes: ["external.tramites.read","external.tramites.pii.read"] }`
→ el secreto se entrega **una sola vez** por canal seguro (no correo, no ADO). Rotación anual o ante incidente.

**Observabilidad:** logs estructurados con `client_id`, `request_id`, `sync_from/to`, `items`, `duration_ms`
— **nunca** cuerpo de respuesta ni campos PII; métricas `external_sync_requests_total{client,status}`,
`external_sync_items_total`, `external_sync_duration_ms` (p50/p95), `external_token_failures_total`,
`external_rate_limited_total`; alerta si 429 > 5 %/15 min o si `flito-pdn` lleva 30 min sin una lectura exitosa (el cron es cada 5 min).
Health: el endpoint `/health` existente no cambia.

**Rollback:** el prefijo externo es aditivo; rollback de app = redeploy de imagen anterior (procedimiento
`flit-rollback-procedure`). El DDL del schema `integrations` (tablas nuevas) tiene `Down` limpio. DDL 122/123 e índices: `Down` elimina
triggers, índices y columnas; se recomienda **no** revertir los DDL 122/123 en PDN tras el backfill salvo
incidente (solo desactivar el prefijo en Gateway), porque rehacer el backfill vuelve a costar la ventana.

## 11. Riesgos y decisiones abiertas del lado FLIT

**Riesgos**

| Riesgo | Impacto | Mitigación |
|---|---|---|
| Orden de commit ≠ orden de `nextval` (transacción larga commitea con `sv` menor al ya entregado) | Omisión de un cambio | Ventana de estabilidad 5 s + al-menos-una-vez + prueba de integración concurrente; si aparecen casos, subir lag o usar `pg_current_snapshot()` (xmin) |
| Ruido en `audit.audit_log` y bump de `row_version` por cada cambio hijo | Auditoría inflada; posible `DbUpdateConcurrencyException` en flujos que guardan padre+hijos | Triggers statement-level; cortocircuito en `trg_audit_log` para columnas `sync_*`; prueba de integración con los flujos de wizard/actores |
| Backfill en PDN (decenas de miles de filas) | Bloqueo de escrituras minutos | Ventana fuera de horario; ensayo en QA con copia de PDN; comunicar a operación |
| Carga sobre la base transaccional (lectura en vivo) | Latencia en la UI durante una corrida grande | Páginas acotadas, rate limit, LATERAL O(1); plan B tabla proyección desnormalizada |
| Fuga cross-tenant | Exposición de datos de otras compañías por otro endpoint | `ExternalIntegrationScope` `internal`, inyectable solo en el repositorio de sync, arch test; nunca reutilizar `SuperAdminTenantScope` |
| Gateway abre un prefijo sin JWT | Superficie de ataque | Ruta específica; rate limit IP en token; core-api rechaza todo lo que no traiga esquema `ExternalClient` |
| Datos de vehículo heterogéneos (texto en campos numéricos, `vehicle_service` mixto) | Nulls inesperados en Flito | `cilindrajeTexto`, `VehicleServiceTypeCode.Resolve`, contrato documentado |
| Secreto expuesto en logs/ADO | Compromiso del cliente | Detector inline, gitleaks, entrega única por canal seguro, rotación |

**Decisiones abiertas**
1. Cortocircuito del trigger de auditoría para cambios solo-`sync_*` (sí/no) — decidir en HU1.2.
2. `pageSize` máx (1000) y cuota (120/min) definitivos — confirmar con operación.
3. Retención de `external_access_log` por ambiente (propuesta 90/180/365 días).
4. TTL de la URL firmada del adjunto (propuesta 10 min).
5. ~~HU2.5 (pantalla admin) en esta fase o solo API~~ → **cerrada (2026-09-29): solo API**, como los clientes ICT.
6. ~~Si `since` + `cursor` juntos → 400 o ignorar `since`~~ → **cerrada (2026-09-29): 400 `cursor_and_since_exclusive`**.

## 12. Entregables hacia FLITO

| Entregable | Detalle | Cuándo |
|---|---|---|
| Hosts por ambiente | `https://<gateway-dev>/api/v1/external`, `https://<gateway-qa>/api/v1/external`, `https://<gateway-pdn>/api/v1/external` (mismos hosts del Gateway actual de cada ambiente; se confirman al desplegar HU3.8) | Con F3 en DEV |
| Credenciales | `clientId` `flito-dev` / `flito-qa` / `flito-pdn` + secreto entregado una sola vez por canal seguro; scopes `external.tramites.read`, `external.tramites.pii.read` | Al alta en cada ambiente |
| Contrato | `docs/integraciones/external-api-tramites-sync.md` (v3) + `contracts/openapi/core-api.v1.yaml` (paths `/api/v1/external/*`) | Ya disponible (v3 acordada); OpenAPI con F3 |
| Ejemplo de ítem y colección | JSON ficticio en el contrato; colección Postman en `contracts/postman/` con F3.5 | Con F3 |
| Disponibilidad estimada* | **DEV**: F1+F2 en Sprint 8 (≈ 2026-09-23 → 10-06) y F3 en Sprint 9 (≈ 10-07 → 10-20) ⇒ endpoint usable en DEV ~**2026-10-20**; **QA** con F4 completa ~**2026-11-03**; **PDN** tras QA + ventana de backfill, no antes de **2026-11-10** | Sujeto a planificación de sprints |
| Cambios de contrato | Cualquier cambio de contrato se comunica por la sesión par antes de mergear; el YAML se versiona con `x-contract-version` | Continuo |

\* Estimación de ingeniería sin compromiso de capacidad: sprints de FLIT duran ~1–2 semanas y aún no
existe Sprint 8 en ADO. Se confirma en la planificación de la Épica.
