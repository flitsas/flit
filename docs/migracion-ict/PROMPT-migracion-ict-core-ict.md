# Migración ICT (Integración con Terceros) a FLIT 2.0 — nuevo servicio `core-ict`

## Context

FLIT 1.0 expone la **Integración con Terceros (ICT)** como **8 microservicios Node.js/TypeScript** (Express + TSOA + TypeORM) detrás de AWS API Gateway, apoyados en Cognito, DynamoDB, S3 y 5 lambdas programadas. Un cliente gestor hace login, registra trámites en lote (matrículas / traspasos / otros), el sistema los valida (reglas de negocio + fuentes externas RUNT/SOAT/RTM/RNMC) y, si pasan, los convierte en trámite.

FLIT 2.0 vive en el monorepo `services/core-api` (.NET 10, Clean Architecture modular, PostgreSQL, sin Cognito/DynamoDB, File Manager corporativo en vez de S3 directo). El objetivo es **migrar toda la ICT a un único servicio nuevo `services/core-ict`** en el monorepo, que:
- reproduce el pipeline v1 (staging + validación de negocio + validación de fuentes externas) y **solo al final crea un trámite en BORRADOR en core-api** vía **gRPC**, reutilizando el mismo camino que ya usa el frontend / la importación masiva (`CreateProcedureInstanceHandler` + `PatchFieldValuesHandler`);
- tiene **su propio login** (independiente del login de plataforma), resolviendo la compañía desde `identity.tenants` (ya no se necesita `companyManagerId`; si un cliente viejo lo envía, se ignora);
- migra las **5 lambdas programadas** a `BackgroundService` in-process;
- añade **mejoras** que v1 no tenía: edición de pre-trámites, alertas ICT, logs en Postgres (no DynamoDB), adjuntos vía File Manager, jobs event-driven, y un **submódulo nuevo en el frontend** para ver logs y alertas ICT.

**Entregable de este documento:** es el **prompt maestro** (Parte A) más el **desglose Feature + HUs** (Parte B) para que una IA ejecute la migración. El usuario pidió ambos formatos.

> **Nota de fuentes v1:** el código v1 está en `C:\Users\Abraham\Downloads\ProyectoFLIT\Version1\` (`BackApiExternalAuth`, `BackApiExternalTransact`, `BackApiExternalTransactAttach`, `BackApiExternalAttachments`, `BackApiExternalTransactValiQueryExt`, `BackApiExternalLog`, `BackApiExternalTransactWebHook`, `BackApiExternalTransactStatus`). Los 2 stored procedures a portar están en `BackApiExternalTransact/src/context/business.sql` y en `ScriptFlit/*sp_processor_validation_business*.sql` / `*_external*.sql` (el usuario ya los compartió íntegros en la conversación).

---

## Decisiones de arquitectura (fijadas por el usuario — NO reabrir)

| # | Decisión | Elegido |
|---|----------|---------|
| 1 | Comunicación core-ict ↔ core-api | **gRPC** (net-new en el monorepo; hoy no existe) |
| 2 | Pipeline de validación | **Mantener el de v1**: validar fuentes externas ANTES de pasar a borrador |
| 3 | Autenticación ICT | **Login propio, 100% independiente** del login v2 (estilo v1, mejorado, sin Cognito) |
| 4 | Vocabulario de estados hacia el cliente | **Nuevo, v2-native** (no los códigos numéricos v1) |
| 5 | Modelo de datos | **Híbrido**: convenciones v2 (uuidv7, RLS, auditoría, DDL embebido) **conservando nombres de tabla/columna v1** para portar los SP |
| 6 | Mapeo de `transaction_type` 1–16 | **Mapear TODOS** (matrículas/traspasos/otros); los aún no publicados en v2 quedan en catálogo extensible |
| 7 | Mejoras | Edición de pre-trámites · Alertas ICT · Logs Postgres · Adjuntos FileManager · Jobs event-driven · **Submódulo frontend logs+alertas** |
| 8 | Entrega | **Prompt maestro + índice de HUs** (este documento) |

---

# PARTE A — Prompt maestro para la IA

> Copia todo lo que sigue como especificación de trabajo. Respeta las convenciones del monorepo (ver §A.11) y trabaja HU por HU según el índice de la Parte B, un commit por HU (`HU{id}: descripción`), PR a `develop`.

## A.1 Objetivo

Crear `services/core-ict`, un servicio .NET 10 hermano de `services/core-api`, que migra la ICT de FLIT 1.0. Ingesta trámites en lote, los valida con el pipeline v1 (negocio + fuentes externas), y al pasar la validación **crea el trámite en BORRADOR en core-api por gRPC** (reutilizando los handlers existentes, sin reimplementar reglas de trámite). Expone login propio, estado, reproceso, webhooks, adjuntos, logs y alertas, y un submódulo frontend.

## A.2 Estructura de solución y despliegue

Solución propia `services/core-ict/Flit.Ict.slnx` (formato `.slnx`, igual que `services/core-api/Flit.slnx`). Copiar `Directory.Build.props` / `Directory.Packages.props` de core-api (net10, `TreatWarningsAsErrors`, CPM, CA1848). Proyectos bajo `services/core-ict/src/`:

| Proyecto | Rol |
|---|---|
| `Flit.Ict.Domain` | Entidades del schema `ict`, enums (`TransactionType` 1–16, `IctEstado` v2), value objects, validadores puros (SOAT/RTM/VehicleAge/RNMC), máquina de estados ICT, interfaces de repos y puertos (`IProcedureDraftClient`, `IConsultationClient`, `IIctTokenIssuer`) |
| `Flit.Ict.Application` | Casos de uso CQRS-lite artesanal (carpeta por caso), FluentValidation por comando |
| `Flit.Ict.Infrastructure` | `IctDbContext` (EF + Npgsql snake_case), repos, migraciones + **DDL embebido** (incluye los 2 SP portados), `IctRsaJwtTokenIssuer`, `Argon2PasswordHasher` (reusar patrón de core-api), cliente gRPC hacia core-api, 5 `BackgroundService`, `HttpClient` de webhooks, DI en `IctInfrastructureExtensions.cs` |
| `Flit.Ict.Api` | Host: Minimal APIs (login ICT, register, status, reprocess, logs, edición, adjuntos), **gRPC server** de callback de estados, `Program.cs` con `db.Database.Migrate()` de arranque |
| `Flit.Ict.Grpc.Contracts` | Solo los `.proto` + código generado. **Se referencia desde AMBAS soluciones** (añadir la ruta relativa `../core-ict/src/Flit.Ict.Grpc.Contracts/...csproj` a `services/core-api/Flit.slnx`); cada consumidor elige `GrpcServices` Server/Client en su `<Protobuf>` |

Grafo de referencias como core-api: `Api → Infrastructure → Application → Domain`; `Api`/`Infrastructure` → `Grpc.Contracts`. Tests en `services/core-ict/tests/` (xUnit v3, Testcontainers-Postgres para SP/gRPC/RLS).

**Puertos** (configurables): core-ict REST `4014`; core-api gRPC server (orquestación) `4013`; core-ict gRPC server (callback de estados) `4015`. Los puertos gRPC son tráfico este-oeste interno (no pasan por YARP).

**docker-compose:** añadir servicio `core-ict` en `docker-compose.yml` (dev) y `docker-compose.prod.yml` (replicar el bloque de core-api con la convención de puertos por ambiente DEV/QA/PDN), `depends_on: core-api` (por el orden de migración, ver §A.4). **NO tocar `pnpm-workspace.yaml`** (solo declara `frontend`; los servicios .NET se orquestan por compose + slnx). **Un solo Postgres** compartido (`flit_dev` en dev), schema `ict`.

**Gateway YARP** (`services/core-api/src/Flit.Gateway/appsettings.json`): añadir cluster `core-ict-cluster` → `http://core-ict:4014/` y rutas:
- `/api/ict/auth/{**catch-all}` → **sin** policy de plataforma (ICT valida su propio token).
- `/api/v1/ict/{**catch-all}` → para el submódulo frontend (logs/estado) con `JwtRequired` de plataforma (tráfico interno FLIT con el JWT de usuario).

## A.3 Contrato gRPC (net-new en core-api y core-ict)

Añadir paquetes `Grpc.AspNetCore`, `Grpc.Net.ClientFactory`, `Google.Protobuf`, `Grpc.Tools`. **Dos servicios unarios, direcciones opuestas, sin server-streaming** (el push se apoya en el outbox transaccional existente para conservar la semántica at-least-once).

**`ict_orchestration.proto`** — core-ict = **cliente**, core-api = **servidor**. core-api implementa un adaptador delgado (`Flit.Api/Grpc/IctOrchestrationService.cs`, mapeado con `app.MapGrpcService<>()` en un endpoint Kestrel HTTP/2 dedicado :4013) que resuelve del DI los handlers existentes y traduce mensajes↔records. **Debe existir una RPC transaccional `CreateDraftFromIct` que orqueste create+patchFieldValues+actors+comercial+adjuntos en una sola unidad de trabajo** (mismo espíritu que `BulkImportProcedureInstancesHandler`, pero atómica por trámite — ver `services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/BulkImport/BulkImportProcedureInstancesHandler.cs`). RPCs: `CreateDraftFromIct`, `PatchFieldValues`, `UpsertActors`, `PatchCommercial`, `RegisterAttachment`, `FinalizeDraft`, `Submit`, `GetStatus`. El `tenant_id` viaja **explícito** en cada mensaje; `procedure_type_code` cae directo en `CreateProcedureInstanceRequest.ProcedureTypeCode` (resuelve solo tipos publicados). Los `error_code` del handler (`not_found`, `not_published`, `modalidad_not_available`, `invalid_reference`…) se propagan tal cual. El `status` devuelto usa el vocabulario v2 (`TramiteEstado`).

**`ict_state_callback.proto`** — core-api = **cliente**, core-ict = **servidor** (:4015). Cuando cambia el estado de un trámite originado en ICT, core-api hace push unario `NotifyProcedureStateChanged(tenant_id, procedure_instance_id, external_ref, from_status, to_status, occurred_at, reason)`. **Reutilizar el outbox existente**: añadir un `IctProcedureStateChangeNotifier` como notifier compuesto junto al `OtWebhookProcedureStateChangeNotifier`, disparado por `ProcedureStateChangeOutboxProcessor` (`services/core-api/src/Flit.Infrastructure/Messaging/`). Solo empuja si `procedure_instances.origin == 'ict'`.

**Correlación:** añadir columnas aditivas nullables a `tramites.procedure_instances` (tabla `ExcludeFromMigrations`, alterar con `migrationBuilder.Sql`): `origin varchar(20)`, `external_ref varchar(64)` + índice parcial. `CreateDraftFromIct` las setea (`origin='ict'`, `external_ref = ict.external_integration_master.id`).

**Auth entre servicios:** service-token JWT client-credentials RS256 con audiencia/llave dedicadas (`aud="flit-internal"`, `scope="ict.orchestration"` para core-ict→core-api; `aud="core-ict-internal"` para core-api→core-ict), validado con un esquema JwtBearer aparte + policy. **NO** se reenvía el token del tercero. Hardening futuro (mTLS interno) documentado como opcional.

## A.4 Modelo de datos — schema `ict` (híbrido)

Reglas transversales (aplican a las 19 tablas del núcleo):
1. Nuevo schema `ict` (`CREATE SCHEMA IF NOT EXISTS ict;` como primera línea del primer DDL; añadir a `SchemaNames`).
2. PK `id uuid NOT NULL DEFAULT uuidv7()` (reemplaza `bigserial`). Conservar los identificadores naturales v1 (`manager_id_transaction`, `transaction_flit`, `traffic_secretary_code`, códigos de catálogo) como columnas de negocio.
3. `company_manager_id integer` → **`tenant_id uuid NOT NULL REFERENCES identity.tenants(id)`** en toda tabla que lo tenía; donde los SP filtraban por `company_manager_id`, filtran por `tenant_id`.
4. Auditoría estándar + triggers en tablas mutables (`row_version`, `created_at/by`, `updated_at/by`, `deleted_at/by`, `tr_<tabla>_row_version` → `public.trg_row_version`, `tr_<tabla>_audit` → `public.trg_audit_log`).
5. **RLS `tenant_isolation`** en toda tabla con `tenant_id` de negocio: `USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true),'')::uuid)`. Los repos setean el GUC con `set_config('app.current_tenant_id', @tenant, true)`.
6. PII con `COMMENT ... IS '@pii:...'` (documentos, nombres, correos, teléfonos, direcciones, firmas).
7. DDL embebido: `services/core-ict/src/Flit.Ict.Infrastructure/Persistence/Sql/Ddl/NN-ICT-*.sql` como `EmbeddedResource`, cargados con un `EmbeddedDdl.LoadUp(...)` propio desde migraciones EF (`migrationBuilder.Sql(...)`); entidades mapeadas con `ToTable(..., t => t.ExcludeFromMigrations())` + `HasTrigger(...)`. (Convención de numeración como en core-api `01-…`→`38-…`; ver `services/core-api/.../Sql/Ddl/`.)

**Las 19 tablas del núcleo (nombres v1 conservados):** `external_integration_master` (pre-trámite; `process_status_id` sigue siendo el estado INTERNO que leen/escriben los SP; añadir `procedure_instance_id uuid` nullable = enlace al borrador materializado), `external_integration_actors`, `external_integration_source_query`, `external_integration_source_response`, `external_integration_source_response_log`, `external_integration_sequence`, `external_integration_process_status`, `external_integration_parameter_process_status`, `external_integration_operation_type`, `external_integration_procedure_type` (con `parent_procedure_id` — jerarquía matrícula/traspaso/otros), `external_integration_document_type`, `external_integration_guarantee_operation_type`, `external_integration_transformation_type`, `external_integration_master_transformation_type`, `external_integration_allowed_documents`, `external_integration_configuration_documents`, `external_integration_attachment_association`, `external_integration_transaction_attachments`, `external_integration_webhook_master`. Los catálogos globales (sin `tenant_id`) van **sin RLS**. Recrear los enums v1 (`external_integration_actors_actor_type_enum`, `external_integration_actor_level_type_enum` VEHI/MAIN/LERE/ASSI, `external_integration_sequence_query_type_enum`) en el schema `ict`.

**Tablas nuevas (mejoras v2, nombres v2-style):** `ict.integration_clients` (credenciales, §A.5), `ict.procedure_type_mapping` (§A.4.1), `ict.integration_log` + `ict.pretramite_events` + `ict.job_runs` (§A.8), y `ict.pipeline_signal` opcional (event-driven, §A.6).

**Remapeo de FKs que en los SP apuntan a tablas v1 fuera de ICT** (ICT referencia por id, delega la escritura autoritativa en core-api):

| v1 (en los SP) | v2 |
|---|---|
| `company_parameters` (NIT, `hasIntegrationTransactionModule`) | `identity.tenants` (tax_id) + `admin.tenant_profiles` + `admin.tenant_operational_policies` / scope en `ict.integration_clients` |
| `company_process_restrictions` (restricción negativa) | `admin.tenant_transit_office_grants` (**grant positivo**: novedad si NO existe grant habilitado) |
| `traffic_secretaries` (`traffic_agency_code`, `is_active_flit`) | `catalogs.transit_offices` (`code`, `is_active`); resolver a `transit_office_id uuid` |
| `identity_validation_master` | biometría v2 sobre `procedure_instances` (ver TODO ICT-BIO, §A.7) |
| `vehicle_registration_master` / `vehicle_transfer_master` / `vehicle_otherservice_master` (3 masters) | **una sola** `tramites.procedure_instances` + `procedure_instance_field_values` (EAV vin/plate) + `source_response` (jsonb) |

### A.4.1 `ict.procedure_type_mapping` (transaction_type 1–16 → ProcedureType)

Catálogo global: `external_transaction_type smallint UNIQUE`, `procedure_type_code varchar(60)`, `is_published boolean`, `description`. Seed: `1,2 → MATRICULA_NUEVA (is_published=true)`, `3,4 → TRASPASO_STANDARD (is_published=true)`, `5–16 → stubs OTRO_TRAMITE_NN (is_published=false)`. Los códigos publicados deben coincidir con los que resuelve `CreateProcedureInstanceHandler` (`MATRICULA_NUEVA`/`TRASPASO_STANDARD`). Flujo: `transaction_type` → mapping → si `is_published`, `CreateDraftFromIct(procedure_type_code)`; si no, el master se acepta y valida pero al materializar devuelve `modalidad_not_available` y queda **CON NOVEDADES** (reprocesable cuando se publique el tipo). **TODO(ICT-TYPES):** sembrar los procedure_types 5–16 en core-api cuando negocio los habilite.

## A.5 Autenticación ICT independiente

Vive íntegra en core-ict; NO toca `identity.users`/`security.user_credentials`.

**`ict.integration_clients`**: `id uuid`, `tenant_id uuid FK identity.tenants`, `username citext UNIQUE` (global), `password_hash` (Argon2id, `@pii:high`), `previous_password_hash` + `password_changed_at` + `must_rotate` (rotación), `scopes jsonb`, `is_active`, `failed_login_attempts` + `locked_until` (rate-limit), `last_login_at`, auditoría. **SIN RLS** (el login busca por username sin conocer el tenant todavía; misma exención que `identity.users` en core-api; la seguridad se aplica en app y el `tenant_id` se deriva de la fila).

`POST /api/ict/auth/login` `{ username, password, companyManagerId? }` — `companyManagerId` se **acepta y descarta**. Handler `LoginIntegrationClientHandler` (espejo de `LoginHandler` de core-api): busca por username, verifica `is_active`/`locked_until`, `Argon2PasswordHasher.Verify` (tiempo constante + dummy hash para no filtrar existencia), incrementa intentos/aplica lock ante fallos, resuelve `tenant_id`, emite token.

**`IctRsaJwtTokenIssuer`** (espejo de `RsaJwtTokenIssuer`): **par de llaves RSA propio**, `iss="https://ict.flit.co"`, `aud="flit-ict"`, claims `sub=integration_client_id`, `tenant_id`, `tenant_name`, `scopes`; vida corta (1–2h). Validación en `Flit.Ict.Api` con JwtBearer (`ValidIssuer/ValidAudience` ICT, `MapInboundClaims=false`) + policy `IctClientPolicy` (`RequireClaim("scope","ict.transactions.write")`) + middleware que impone el `tenant_id` del token (el cliente nunca elige tenant por header). Aislado del token de plataforma (issuer/aud/llave distintos). Mejoras sobre v1: rotación (`must_rotate` + endpoint `POST /api/ict/auth/rotate`), rate-limit (fila + rate-limiter del Gateway sobre `/api/ict/auth/login`), scopes granulares, auditoría sin PII.

## A.6 Ingesta / registro y los 5 BackgroundServices

**`POST /api/v1/ict/register`** (equivalente `createList` v1). Respuesta idéntica a v1 para compat: `{ TotalRows, TotalRowsProcessed, Detail:[{ Plate, Status(1=ok|2=error), Message, TransactionFlit }] }`. Handler `RegisterIctBatchHandler`:
1. Multi-tenant desde el token (tenant_id + NIT); cada fila cuyo `company_manager_document` ≠ NIT del token → `Status=2`.
2. Límite de lote configurable en BD (`IctIngestOptions.MaxItemsPerBatch`, v1 `MAX_NUMBER_OF_ITEMS_TO_BE_CREATED`, default 20; si se excede → 422 sin procesar nada).
3. Normalizar snake_case → aplanar `seller/buyer/lessee` (+ `legal_representative_*`/`principal_mandante_*`) a `ict.external_integration_actors` con `actor_type` (mapper C# puro, testeable). FluentValidation valida estructura antes de persistir.
4. Duplicados intra-lote: tipos 3/4 por `plate`, 1/2 por `vin`, **5–16** por `plate+transaction_type` (v1 solo cubría 5–11 en el batch — corregir a 5–16 para casar con la disponibilidad del SP). Fila duplicada → `Status=2`.
5. Persistir pre-trámite (`process_status_id=1, business_validation=0, external_validation=0`) + actores en schema `ict`. **No** se crea nada en `tramites.*` todavía.

**Los 5 `BackgroundService`** (patrón `AnalyticsSchedulerProcessor` + `ScheduleDueEvaluator`: `StartupDelay`+`PollInterval`, `RunCycleAsync` try/catch por ítem, scope por unidad de trabajo, claim `FOR UPDATE SKIP LOCKED`, zona `America/Bogota`, rama InMemory para tests, `AddHostedService<>()`). Ventana **08–20 configurable** (`IctWindowEvaluator` puro). La paridad par/impar de minutos de v1 ya NO es necesaria (jobs in-process + `SKIP LOCKED`); opcional por config.

| # | Job v1 | Trabajo v2 | PollInterval |
|---|---|---|---|
| 1 | RunBusinessValidations | `CALL ict.sp_processor_validation_business()` en tx propia (advisory lock por réplica) | 30–60 s |
| 2 | RunExternalApiValidations | `CALL ict.sp_processor_validation_external()` | 30–60 s |
| 3 | ExternalOrchestratorProcessPending | claim de `source_query` pendientes, `SemaphoreSlim(10)`, reintentos ≤3, valida (SOAT/RTM/RNMC/DRIVER), escribe `source_response` | 15–30 s |
| 4 | ExternalSourceResponseProcessAndSend | claim de masters listos, arma payload, **gRPC `CreateDraftFromIct`** | 15–30 s |
| 5 | ExternalWebhookNotifications | claim de webhooks pendientes, POST al gestor | 5–15 s |

**Idempotencia:** flags monotónicos (`business_validation 0→1→2`, `external_validation 0→1→2`, `is_data_queried`, `is_notified`) dentro de la misma tx que hace el trabajo. **Orden por estado en BD, no por reloj** (gating por flags). **Mejora event-driven:** `Channel<Guid>` en memoria (o `ict.pipeline_signal`) que el paso previo señaliza al terminar una fila; cada job espera `PollInterval` **o** la señal → latencia end-to-end de ~9–19 min (v1) a segundos-decenas de segundos. Registrar cada ejecución en `ict.job_runs` (started/finished, SLA) para alertas.

## A.7 Portado de los 2 stored procedures (mantener como PL/pgSQL)

**Recomendación: mantener los SP como procedures PL/pgSQL** (DDL embebido en `Flit.Ict.Infrastructure`, invocados con `CALL` por los jobs 1 y 2). Es la razón de la decisión 3 (schema híbrido con nombres v1). Portar ~1024 líneas de reglas campo-por-campo a C# es alto riesgo. Ajustes al portar:
- Cambiar schema a `ict.*`; `company_manager_id` → `tenant_id`; `id` uuid (revisar JOINs/`RETURNING`). El equipo valida columna a columna que la firma calce.
- **Quitar** el hack hardcodeado de Bancolombia (`business.sql`, `where company_manager_document='890903938'`); si hace falta, regla de rate-limit configurable por tenant.
- `ROLLBACK` interno del SP: el job llama cada `CALL` en su propia transacción y trata la excepción como "ciclo fallido, reintentar".
- **RLS:** los jobs son cross-tenant; el rol que ejecuta el `CALL` con `BYPASSRLS` o el SP `SECURITY DEFINER` con owner adecuado. Documentar.

**Adaptación de las validaciones de DISPONIBILIDAD al modelo single-table** (único bloque que cambia de fondo; el resto del SP se porta 1:1):
- **VIN (tipos 1,2)** y **placa (tipos 3,4):** reemplazar la consulta a `vehicle_registration_master`/`vehicle_transfer_master` por `EXISTS` sobre `tramites.procedure_instances pi JOIN tramites.procedure_instance_field_values fv ON fv.procedure_instance_id=pi.id AND fv.field_key IN ('vin'|'plate')` con `pi.status NOT IN ('anulado','rechazado') AND pi.deleted_at IS NULL`.
- **`company_process_restrictions` (tipos 1,2):** invertir semántica → novedad si **NO** existe grant habilitado en `admin.tenant_transit_office_grants` para `(tenant_id, transit_office resuelto por code)`.
- **Otros trámites (tipos 5–16):** `procedure_instances` NO tiene `requested_process`; aproximar por `procedure_type_id` uniendo con `ict.procedure_type_mapping`. **TODO(ICT-DISP-5-16):** dos transaction_type v1 distintos que mapeen al mismo procedure_type v2 colisionarían; validar granularidad con negocio.
- **Identidad no bloqueante (`identity_validation_master`):** **TODO(ICT-BIO):** omitir del SP en v1.0 del porte (dejar el `UPDATE` comentado con el TODO) y delegar al auto-flujo de identidad/firma que ya dispara core-api tras crear el borrador (`IdentityValidationOutboxProcessor`).

**`sp_processor_validation_external`:** no toca tablas de trámite; se porta **1:1** cambiando el schema a `ict` (construye filas en `source_query` según `external_integration_sequence`; recrear los enums de actor_level/query_type).

## A.8 Orquestador de fuentes externas y transformación → borrador

**Job 3 — fuentes externas.** **Reusar las integraciones de core-api por gRPC, NO crear HttpClient propios en core-ict** (RUNT Verifik+Kyverum con cadena/failover, SIMIT, RNMC, Conductor, y el override por tenant `IConsultationTenantOverrideProvider` ya viven en core-api con sus credenciales y toggle mock/real). Añadir a core-api un `ConsultationGrpcService.Query(tenantId, queryType, plate|vin|documentNumber|documentType, rnmcDate)` (fachada sobre `IConsultationProviderRegistry`/`ChainResolver`). core-ict **interpreta y valida localmente** (validadores puros portados a `Flit.Ict.Domain`): intérprete por `query_type` (VEHICLE/VIN/DRIVER/RNMC); **SOAT** (bloquea si no vigente), **RTM** (bloquea según antigüedad y tipo, salvo traspaso unilateral tipo 4 que solo advierte), **VehicleAge**, **RNMC** (bloquea si hay sanciones activas), **DRIVER** (paz y salvo). Concurrencia 10, reintentos ≤3 con backoff + dead-letter. Escribe `source_response` con `is_data_queried=true`, `is_data_valid`; si bloquea → master a CON NOVEDADES + webhook.

**Job 4 — transformación → borrador.** Por master `external_validation=2` sin novedades: agrupar `source_response` por `query_type/actor_level`; resolver secretaría (`traffic_secretary_code` → `catalogs.transit_offices` → `transit_office_id`; reusar `ITransitOfficeCodeResolver` que ya usa el bulk-import); mapear `transaction_type → procedure_type` vía `ict.procedure_type_mapping`; llamar **gRPC `CreateDraftFromIct`** (crea borrador + field_values vin/plate + campos de vehículo desde RUNT + actores seller→owner/buyer/lessee/RL/mandante + comercial precio/fecha para traspaso + adjuntos por referencia). Resultado: **Ok** → `process_status_id=PROCESADO`, guardar `procedure_instance_id` en el master, estado ICT `borrador_creado`, webhook 'PROCESADO'; **Fallo** → `process_status_id=4` (CON NOVEDADES) + webhook. El `mapTypeTransaction` de v1 queda subsumido en `ict.procedure_type_mapping`.

## A.9 Estados v2, reproceso y webhooks

**Plano A — ciclo del PRE-TRÁMITE (nativo v2, expuesto por la API ICT).** Enum `IctEstado` (español snake_case, como `TramiteEstado`): `recibido → en_validacion_negocio → en_validacion_externa → procesado → borrador_creado`; ramas `con_novedades` (reprocesable) y `anulado`. Máquina `IctStateMachine` pura. Terminales: `borrador_creado`, `anulado`.

**Plano B — mapeo interno `process_status_id` v1 → `IctEstado`** (los SP siguen escribiendo los códigos numéricos; se traducen al exponer): `1→recibido`; `business_validation=1,process_status_id=2 → en_validacion_negocio`; `business_validation=2,external_validation 0/1 → en_validacion_externa`; `3→procesado`; `4→con_novedades`; master con `procedure_instance_id` → `borrador_creado`; `2→anulado`.

**Plano C — reflejo del estado v2 del trámite.** Desde `borrador_creado`, la API ICT **deja de gobernar** el estado y **proyecta** `tramites.procedure_instances.status` (`borrador/preparado/entregado/aprobado/rechazado/anulado`), recibido por el callback gRPC alimentado por el outbox de core-api. El enum STT (`TramiteEstadoStt`) se expone como sub-detalle opcional del flujo del organismo de tránsito.

Endpoints: `GET /api/v1/ict/status/{managerIdTransaction}` → `{ ictEstado, procedureInstanceId?, tramiteStatus?, comments }` (filtra por tenant/RLS). `POST /api/v1/ict/reprocess/{managerIdTransaction}` → solo si `ictEstado == con_novedades` (resetea flags al punto adecuado y limpia `business_comments_validation`; el pipeline lo re-toma).

**Webhooks (Job 5).** Outbox `ict.external_integration_webhook_master` (conserva nombre v1; añadir `tenant_id`, `payload jsonb`, `target_url`, `attempts`, `next_attempt_at`, RLS). Entrega con patrón `ProcedureStateChangeOutboxProcessor` (PollInterval 5 s, `BatchSize`, claim `FOR UPDATE SKIP LOCKED`, backoff + dead-letter). **Payload con vocabulario v2** (`{ managerIdTransaction, ictEstado, tramiteStatus?, procedureInstanceId?, transactionType, message, timestamp }` — nunca códigos numéricos v1). `target_url` por tenant/gestor (nunca desde el payload de ingesta sin validar). Señal en-proceso al encolar → entrega casi inmediata (mejora el "<9 min" de v1).

## A.10 Adjuntos, logs, alertas, edición

**Adjuntos.** Reusar `IAttachmentStorage`/`FileManagerAttachmentStorage` (presign→S3→register; sin credenciales AWS). Tablas `ict` que portan v1: `external_integration_transaction_attachments`, `external_integration_allowed_documents`, `external_integration_configuration_documents` (obligatorios por procedure_type), `external_integration_attachment_association`. `RequiredDocumentsValidator` (obligatorios por tipo; **`process_without_attached_documents`** hace bypass; **`closed_document`** bloquea nuevos adjuntos → 409). **Transferencia al borrador por REFERENCIA (no re-subir bytes):** `CreateDraftFromIct`/`RegisterAttachment` envía metadata (tipo, filename, mimetype, size, sha256, **storage_path**) y core-api ejecuta su `RegisterAttachmentHandler` (registra metadata de un objeto ya en S3) → mismo objeto S3, cero doble almacenamiento, dispara el `AutoMark` del checklist. Idempotencia por `(procedure_instance_id, sha256)`.

**Logs en Postgres (reemplazo de DynamoDB).** `ict.integration_log` (crudo por request: `log_type` auth/transaction/webhook/external, `direction` in/out, method, path, status, `request/response/headers jsonb REDACTADOS`, `correlation_id`, `duration_ms`, `usuario`, `created_at`; RLS; índices por tenant/fecha/correlation) + `ict.pretramite_events` (timeline sanitizado por pre-trámite, vocabulario cerrado). **Doble barrera de redacción:** (1) al capturar, nunca persistir crudo — redactar `authorization/cookie/tokens/PII` antes de escribir; (2) al servir, masker por nombre de clave (reusar `LogQxSensitiveDataMasker` de Quipux, deja últimos 4 chars). Instrumentación: middleware inbound (correlación + escritura en scope propio que sobrevive al rollback del caso de uso), `DelegatingHandler` outbound (fuentes externas), interceptor gRPC. Retención por partición mensual + job de purga (p.ej. 90 días crudo, 1 año timeline). `id_log_reprocess` preservado. Endpoint `GET /api/v1/ict/logs?...` paginado/filtrado con masker + gate `RequirePermission("ict.logs.read")`.

**Alertas ICT (reusar el subsistema de Reportes 2.0, cross-schema).** core-ict comparte Postgres → el `AnalyticsSchedulerProcessor` puede leer `ict.*` directo. Cambios mínimos: (1) extender el CHECK de `metric` en `analytics.alert_rules`; (2) añadir casos SQL a `AlertMetricsReadRepository` (fija el GUC RLS por tenant en una tx): `ict_stuck_in_validation`, `ict_novelty_rate_pct`, `ict_webhook_delivery_failures`, `ict_jobs_out_of_sla` (SLA de `ict.job_runs`); (3) añadir las opciones al union `AlertMetric` del frontend. Email y CRUD/historial ya resueltos. Para acknowledge: añadir `acknowledged_at/by` a `analytics.alert_events` + `POST /api/v1/analytics/alert-events/{id}/ack` (cambio aditivo, beneficia también a las alertas existentes).

**Edición de pre-trámites (nuevo en v2).** Editable mientras el pre-trámite esté **antes de `external_validation` iniciada** (corte conservador); una vez materializado (borrador creado) → `409 already_materialized`. `PATCH /api/v1/ict/pretramites/{id}` (parcial; no editables: llaves de correlación placa/VIN, `procedure_type` ya validado, tenant). Concurrencia optimista con `row_version` (`IsConcurrencyToken` + trigger; stale → `409`). **Reset selectivo** de validaciones: solo si cambian campos "validation-affecting" (resetea `business_validation=0`, `external_validation=pending` y re-encola el pipeline). Cada edición escribe un evento de timeline (stage `editado`).

## A.11 Submódulo frontend ICT (logs + alertas)

Calcar el módulo **LOG QX**. Registrar `ModuleId "ict-logs"` en `Shell.tsx` y `lib/nav/modules.ts` (`ALL_MODULE_IDS`/`UNIVERSAL_MODULE_IDS`), entrada en el dock gateada por permiso, render en `app/page.tsx` con `{module === "ict-logs" && canReadIctLogs && <IctLogs />}`. Gate: `canReadIctLogs(p)` en `lib/auth/jwt.ts` (`ict.logs.read`; sembrar el permiso en RBAC). Componente `frontend/components/atom/modules/IctLogs.tsx` con dos pestañas: **Logs** (buscador por compañía/correlation_id/fecha/tipo/estado, timeline expandible con visor JSON + copia — PII ya enmascarada por backend; 4 estados de UI + paginación) y **Alertas ICT** (lista `alert_events` de métricas `ict_*` con badges por estado + botón Acknowledge + filtros). Cliente `frontend/lib/api/ict-client.ts` (`apiFetch` con Bearer JWT): logs → `/api/v1/ict/...` (Gateway → core-ict); alertas → `/api/v1/analytics/alert-events` (core-api). **Siempre por el Gateway**, nunca directo. Respetar el design system (tokens del prototipo, dark mode, `flit-design-guardian`; cero colores nuevos).

## A.12 Convenciones obligatorias del monorepo

- **CA1848**: logging con `LoggerMessage` source-gen (`LogInformation` directo NO compila). **Sin MediatR / Polly / AutoMapper**. FluentValidation por comando. Minimal APIs con `MapGroup`/`MapPost`. Handlers POCO Scoped.
- Columnas/tablas **en inglés snake_case** (`EFCore.NamingConventions`); valores pueden ir en español. Schema propio `ict`. PK `uuidv7()`. Auditoría estándar + triggers + RLS por `app.current_tenant_id`. PII con `COMMENT @pii`.
- **DDL crudo embebido** (`EmbeddedResource` + `EmbeddedDdl.LoadUp` + `ExcludeFromMigrations`), no diff EF; validar ejecutando el SQL real contra Postgres (build verde NO prueba el DDL). Migraciones se auto-aplican al arrancar (`db.Database.Migrate()`).
- **Orden de migración:** core-api migra primero (crea `uuidv7()`, `public.trg_row_version`, `public.trg_audit_log`, `audit.audit_logs`); core-ict `depends_on: core-api` y **asume** esas funciones (no las redefine). Cada servicio migra solo su schema.
- Git: rama `feature/AB-XXXXX-...` desde `develop`, **un commit por HU** (`HU{id}: descripción`), PR a `develop` (nunca `main`). No push/PR/Azure DevOps (lo orquesta Cursor). Actualizar `contracts/openapi/core-api.v1.yaml` si cambian contratos públicos. Tests xUnit por cada AC.

## A.13 TODOs y riesgos explícitos (dejar documentados en código)

1. **TODO(ICT-TYPES):** procedure_types 5–16 no publicados en v2 → materialización de "otros trámites" devuelve `modalidad_not_available` hasta sembrarlos.
2. **TODO(ICT-DISP-5-16):** disponibilidad de otros trámites aproximada por `procedure_type_id` (v2 no tiene `requested_process`).
3. **TODO(ICT-BIO):** validación de identidad no bloqueante omitida del SP; delegada al auto-flujo de identidad de core-api.
4. **RLS de los SP:** jobs cross-tenant → `SECURITY DEFINER`/`BYPASSRLS`; aislar y documentar.
5. **`username` global** en `ict.integration_clients` (al ignorar `companyManagerId`): el aprovisionamiento debe garantizar unicidad.
6. **gRPC net-new** en core-api: requiere `Grpc.AspNetCore` + `CreateDraftFromIct` transaccional. Alternativa de menor fricción (REST `POST /tramites/instances` + `PATCH field-values`) pierde atomicidad multi-paso — no recomendada.

---

# PARTE B — Feature + desglose de HUs (5 HUs, índice de ejecución)

**Feature:** "Migración ICT a FLIT 2.0 (core-ict)". **Máximo 5 HUs** — cada una agrupa varios subsistemas por fase para no saturar el backlog. Crear con `feature-creator` / `flit-crear-hu`; AC en Gherkin; un commit por HU (los commits son grandes: trabajar por sub-tareas internas, pero cerrar una sola HU). El detalle técnico de cada subsistema está en la Parte A.

| # | HU | Alcance | Subsistemas que agrupa (Parte A) | Depende |
|---|----|---------|----------------------------------|---------|
| 1 | **Fundación `core-ict`: solución, schema `ict`, auth propia y canal gRPC** | BACKEND | Bootstrap solución modular + Gateway route/cluster + docker-compose + healthchecks (§A.2); modelo de datos schema `ict` (19 tablas híbridas + `procedure_type_mapping` + enums, RLS/auditoría/DDL embebido) (§A.4); auth ICT independiente (`ict.integration_clients`, login, `IctRsaJwtTokenIssuer`, rotación/rate-limit/scopes, gate `ict.*`) (§A.5); contrato gRPC bidireccional (`CreateDraftFromIct` + state callback) + service-token + interceptores + columnas `origin`/`external_ref` en core-api (§A.3) | — |
| 2 | **Ingesta, adjuntos y edición de pre-trámites** | BACKEND | `POST /api/v1/ict/register` (límite configurable, normalización seller/buyer/lessee, duplicados intra-lote 1-16) (§A.6); adjuntos vía FileManager (presign→register, documentos obligatorios, `process_without_attached_documents`, `closed_document`) (§A.10); edición de pre-trámites (PATCH, `row_version`, reset selectivo, bloqueo post-materialización) (§A.10) | 1 |
| 3 | **Pipeline de validación (SP portados) y jobs programados** | BACKEND | `sp_processor_validation_business` + `sp_processor_validation_external` portados con adaptación de disponibilidad a single-table `procedure_instances` (§A.7); orquestador de fuentes externas (reuse gRPC core-api, validadores SOAT/RTM/RNMC/DRIVER, concurrencia 10 / reintentos ≤3) (§A.8); 5 `BackgroundService` + event-driven + `ict.job_runs`/SLA (§A.6) | 1, 2 |
| 4 | **Transformación a borrador, estados, reproceso y webhooks** | BACKEND | Job 4 → gRPC `CreateDraftFromIct` (borrador + field_values + actores + comercial + adjuntos por referencia) con `procedure_type_mapping` (§A.8); estados v2-native + mapeo interno + proyección del estado del trámite; reproceso (§A.9); webhooks (`external_integration_webhook_master` outbox, payload v2, backoff/dead-letter) (§A.9) | 1, 3 |
| 5 | **Observabilidad ICT: logs, alertas y submódulo frontend** | BACKEND + FRONTEND | Logs Postgres (`ict.integration_log`/`pretramite_events`, doble barrera de redacción, middleware/interceptores, retención, endpoint) (§A.10); alertas ICT cross-schema en `analytics.alert_rules` (4 métricas + acknowledge) (§A.10); submódulo frontend `?m=ict-logs` (logs+alertas) con gate `ict.logs.read` (§A.11) | 2, 3, 4 |

**Ejemplos de AC (Gherkin):**

```gherkin
# HU 2 — Ingesta / edición
Escenario: Registro por lote respeta el límite configurable
  Dado un límite de lote de 20 y un token ICT válido de la compañía con NIT "901698038"
  Cuando envío POST /api/v1/ict/register con 21 registros
  Entonces responde 422 y no persiste ningún pre-trámite

Escenario: Fila con company_manager_document ajeno al tenant se marca en error
  Dado un token ICT de la compañía "901698038"
  Cuando una fila trae company_manager_document "890903938"
  Entonces esa fila responde Status=2 y las demás válidas se procesan

Escenario: Editar un pre-trámite en estado editable resetea la validación de negocio
  Dado un pre-trámite en "en_validacion_negocio" con row_version 3
  Cuando envío PATCH con un campo validation-affecting y rowVersion=3
  Entonces responde 200, row_version=4, business_validation=0 y se registra evento "editado"

Escenario: Bloqueo tras materialización
  Dado un pre-trámite en "borrador_creado"
  Cuando envío PATCH a ese pre-trámite
  Entonces responde 409 "already_materialized"

# HU 4 — Transformación / estados / webhooks
Escenario: Pre-trámite validado crea el borrador en core-api
  Dado un pre-trámite de traspaso con business_validation=2 y external_validation=2 sin novedades
  Cuando corre el Job 4
  Entonces core-ict invoca gRPC CreateDraftFromIct, recibe un procedure_instance_id,
    lo guarda en el master, el ictEstado pasa a "borrador_creado" y se encola webhook "PROCESADO"

# HU 5 — Alertas / observabilidad
Escenario: Alerta por trámites atascados en validación
  Dada una regla activa metric="ict_stuck_in_validation" operator="gt" threshold=10 con cooldown vencido
  Y 12 pre-trámites en en_validacion_externa por encima del SLA
  Cuando corre el AnalyticsSchedulerProcessor
  Entonces registra un analytics.alert_events con metric_value=12 y envía email
```

---

## Archivos de referencia clave (leer antes de implementar)

**core-api (patrones a calcar):**
- `services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/BulkImport/BulkImportProcedureInstancesHandler.cs` — reuso create+patch por fila (base de `CreateDraftFromIct`).
- `services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/CreateProcedureInstanceCommand.cs` y `.../AttachmentsCommand.cs` (`RegisterAttachmentHandler`, `AttachmentRules`).
- `services/core-api/src/Flit.Infrastructure/Analytics/Scheduling/AnalyticsSchedulerProcessor.cs`, `ScheduleDueEvaluator.cs`, `AlertMetricsReadRepository.cs`.
- `services/core-api/src/Flit.Infrastructure/Messaging/ProcedureStateChangeOutboxProcessor.cs`.
- `services/core-api/src/Flit.Infrastructure/Security/RsaJwtTokenIssuer.cs`, `Flit.Modules.Security.Application/Auth/Login/LoginHandler.cs`, `Argon2PasswordHasher.cs`.
- `services/core-api/src/Flit.Api/Authorization/ApiSecurityExtensions.cs`, `Middleware/TenantEnforcementMiddleware.cs`.
- `services/core-api/src/Flit.Infrastructure/Storage/FileManagerAttachmentStorage.cs`.
- DDL: `.../Sql/Ddl/01-HU10146-identity-security-auth.sql` (credential-store+RLS+triggers), `06-HU10150-procedure-instances.sql` (single-table destino), `07-HU10154-admin-tenants.sql` (grants OT), `22-n03-procedure-state-change-outbox.sql`, `31-HU10624-analytics-schedules-alerts.sql`, `32-HU10710-quipux-integracion.sql` (plantilla schema-integración), `09-HU10152-ot-admin.sql` + `27-HU10659-...transit-offices...` (catálogo secretarías).
- `services/core-api/src/Flit.Gateway/appsettings.json`, `Program.cs`, `InfrastructureExtensions.cs`; `docker-compose.yml` / `docker-compose.prod.yml`.

**frontend (plantilla del submódulo):**
- `frontend/components/atom/modules/LogQx.tsx`, `frontend/lib/api/admin-log-qx.ts`, `frontend/lib/api/analytics-scheduling.ts`, `frontend/components/.../Shell.tsx`, `frontend/lib/nav/modules.ts`, `frontend/lib/auth/jwt.ts`, `frontend/app/page.tsx`.

**v1 (fuente a portar):** `Version1/BackApiExternalTransact/` (`externalTransactionService.ts`, `transactionPayloadBuilderService.ts`, `context/business.sql`), `Version1/ScriptFlit/*sp_processor_validation_*.sql`, `Version1/BackApiExternalTransactValiQueryExt/` (orquestador + validadores SOAT/RTM), `Version1/BackApiExternalAuth/`, `Version1/BackApiExternalLog/`.

---

## Verificación end-to-end

1. **Build/tests:** `pnpm run build:core-api`; build de `Flit.Ict.slnx`; `dotnet test` (xUnit) de core-ict con **Testcontainers-Postgres** para: SP portados (golden tests contra casos v1 conocidos), flujo gRPC `CreateDraftFromIct` + adjunto por referencia (verificar que `procedure_instance_attachments` apunta al mismo `storage_path`), RLS cross-schema, claim `FOR UPDATE SKIP LOCKED` (dos réplicas no procesan el mismo master), redacción de logs (barrera 1 no persiste token/crudo; barrera 2 enmascara), y cada SQL de métrica de alerta.
2. **DDL real:** aplicar el DDL embebido contra un Postgres real (el build verde no valida DDL); confirmar las 19 tablas + nuevas en el schema `ict`, RLS activa, triggers, y el seed de `procedure_type_mapping`.
3. **Smoke del pipeline:** `POST /api/ict/auth/login` → token; `POST /api/v1/ict/register` con un traspaso (payload del ejemplo del usuario) → `TransactionFlit`; esperar/forzar ciclos de jobs 1→2→3→4; `GET /api/v1/ict/status/{id}` debe transicionar `recibido → en_validacion_negocio → en_validacion_externa → procesado → borrador_creado`; verificar que aparece un trámite en **BORRADOR** en core-api (con `origin='ict'`, `external_ref`) y en el listado del frontend; confirmar webhook entregado con vocabulario v2.
4. **Novedad y reproceso:** registrar un caso que falle una regla (p.ej. placa mal formada o VIN duplicado) → `con_novedades` + webhook; `POST /api/v1/ict/reprocess/{id}` → vuelve a correr el pipeline.
5. **Frontend:** con un usuario con `ict.logs.read`, abrir `?m=ict-logs`; ver logs (PII enmascarada) y alertas ICT; probar Acknowledge. Sin el permiso, el módulo no monta.
6. **Compat clientes v1:** un login que envíe `companyManagerId` debe funcionar (se ignora); la respuesta de `/register` conserva la forma `{ TotalRows, TotalRowsProcessed, Detail[] }`.
