---
name: Feature 10128 Runtime
overview: "Feature #10128: flujo acelerado (≤4 HUs), capa multi-proveedor future-proof (Verifik MVP + gateway Johan post-#10128), UX inspirada en Johan.Jimenez sobre motor genérico parametrizable."
status: approved
approvedAt: 2026-06-18
approvedBy: Samuel Cardenas
adoFeatureId: 10128
branch: feature/scardenas-tramites-10128
relatedFeatures: [10116, 10120]
todos:
  - id: branch-bootstrap
    content: Rama feature/scardenas-tramites-10128 + plan local
    status: completed
  - id: ado-hus
    content: Crear HUs 2-4 en ADO bajo #10128
    status: in_progress
  - id: hu-10150
    content: "Implementar HU #10150 (DDL runtime + EF)"
    status: completed
  - id: hu-api-runtime
    content: HU-2 API instancias (#10199)
    status: completed
  - id: hu-frontend-operacion
    content: HU-3 Tab Operación (#10200)
    status: completed
  - id: hu-consultas-verifik
    content: HU-4 multi-proveedor + Verifik
    status: completed
  - id: pr-ci
    content: PR a develop
    status: pending
isProject: false
---

# Plan orquestado — Feature #10128 Runtime

Ver plan canónico en historial del orquestador. Resumen ejecutivo:

- **Rama:** `feature/scardenas-tramites-10128` (desde `feature/scardenas-tramites`)
- **4 HUs:** #10150 (DDL) + 3 nuevas (API runtime, Frontend Operación, Consultas multi-proveedor)
- **Consultas:** Verifik real MVP; `IConsultationProvider` + stub gateway Johan
- **UX:** Motor genérico; demo Matrícula + RUNT vehículo; patrones Johan (semáforo, wizard shell)
- **Docs:** `context/FLIT-Documentacion-Endpoints.md`, ADR-0020 (Propuesto)

HUs ADO (tras creación):

| # | ID | Título | SP |
|---|-----|--------|-----|
| 1 | [#10150](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/10150) | Migración tablas instancias runtime | 5 |
| 2 | [#10199](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/10199) | API runtime instancias | 8 |
| 3 | [#10200](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/10200) | Tab Operación wizard dinámico | 8 |
| 4 | [#10201](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/10201) | Capa multi-proveedor consultas | 5 |

## Deuda técnica — HU #10199 (registrada por REX, aceptada para MVP)

Endpoints: `POST/GET/PATCH field-values/POST submit` bajo `/api/v1/tramites/instances`. Build + 24 tests verdes. Deuda a abordar cuando exista auth real / antes de prod:

- **D-199-1 (M1):** `POST /instances` toma `tenant_id` del body (decisión MVP acordada); GET/PATCH/Submit usan header `X-Tenant-Id`. Uniformar al header (o derivar de claim) cuando la auth deje de ser stub. Vector de spoofing nulo hoy (auth stub).
- **D-199-2 (m1):** `reference_number` (`TRM-{año}-{D6}`) se genera con `count+1` no atómico → dos POST concurrentes del mismo tenant chocan contra `uq_procedure_instances_tenant_reference` y devuelven 500 en vez de 409/retry. Mover a secuencia DB o capturar la violación y reintentar.
- **D-199-3 (m2):** `RowVersion` está marcado `IsConcurrencyToken` pero nunca se incrementa en update → concurrencia optimista inactiva (consistente con `ProcedureTypeRepository`). Incrementar en `SaveChanges` override + mapear `DbUpdateConcurrencyException` → 409.
- **D-199-4 (m3):** `ChangedBy`/`UpdatedBy` no se propagan en PATCH/Submit (no hay user actor sin auth real). Propagar desde claims al implementar auth.
- **D-199-5 (OBS-3):** Test de integración del trigger AC2 (UPDATE field_values con instancia completed → `check_violation`) diferido: no existe proyecto de integración (sin Testcontainers/Respawn) y se priorizó "sin tooling nuevo". AC2 cubierto a nivel unitario (handler 409 en no-draft) + verificación funcional en #10150. Crear `Flit.Tramites.Integration.Tests` cuando se introduzca harness de DB real.
- **D-199-6 (n4):** Falta test que confirme aislamiento por tenant en el repo (tenant B no recupera instancia de tenant A) — requiere integración con DB real (ligado a D-199-5).

## Deuda técnica — HU #10200 (registrada por REX, veredicto APROBADO CON RESERVAS)

Frontend Tab Operación (selector published + wizard dinámico + persistencia borrador + semáforo stub) + delta backend (endpoint público published-list + seed dev). Gates verdes: backend build 0/0 + 24 tests; frontend typecheck/lint limpios + 4 tests vitest/RTL por AC. Deuda:

- **D-200-1 (CRITICAL — RESUELTA):** El seed dev (`12-HU10200-dev-seed.sql` vía migración EF `20260619013416_HU10200_DevSeed`) corría incondicionalmente en cualquier entorno (incl. prod) → tenant/user ficticios + force-publish de MATRICULA_NUEVA. **Gateado** en `Up()`: solo ejecuta el SQL si `ASPNETCORE_ENVIRONMENT==Development` o `FLIT_DEV_SEED in {1,true}`; en prod es no-op. La migración igual se registra en `__EFMigrationsHistory`. Nota: el repo NO aplica migraciones en runtime (`Database.Migrate()` ausente) — solo vía `dotnet ef database update`.
- **D-200-2 (MAJOR):** El wizard no valida campos `isRequired` (placa/VIN) client-side antes de `saveDraft`/`submit`; se puede avanzar/Finalizar con requeridos vacíos. Añadir bloqueo + `aria-invalid` por step.
- **D-200-3 (MAJOR):** El borrador no se rehidrata en UI: `getInstance` está implementado pero sin usar; al recargar la página se pierde el formulario (los valores solo viven en estado local). Cablear hidratación vía `GET /instances/{id}` al reabrir.
- **D-200-4 (MINOR):** GUIDs dev hardcodeados acoplados frontend (`dev-constants.ts`) ↔ backend (seed). Reemplazar por contexto de sesión cuando exista auth real (ligado a D-199-1/D-199-4).
- **D-200-5 (MINOR):** Mensajes de error 409 `not_draft`/`not_published`/404 se muestran crudos (`"409 Conflict: ..."`) sin traducir a copy de usuario. Añadir mapeo código→mensaje amigable.
- **D-200-6 (cobertura):** Tests vitest cubren happy-path de AC1/AC2/AC3 pero no rutas de error (409/404), estados vacío/error/loading de UI, ni el bloqueo por riesgo rojo (`blockedByRisk`). Ampliar cuando se priorice cobertura.
- **Gap parametrización:** Ningún `procedure_type` seedeado por #10151 tenía steps/sections/fields; el seed dev solo configuró MATRICULA_NUEVA (1 step/1 section/3 fields). Los otros 4 types siguen en draft sin config — parametrizar vía UI cuando se necesiten en el dropdown.

## Deuda técnica — HU #10201 (registrada por REX, veredicto APROBADO CON RESERVAS)

Capa multi-proveedor de consultas (registry `IConsultationProvider` + Verifik RUNT real + stub `flit_integrations` + ADR-0020 Propuesto). Gates verdes: backend build 0/0 + 52 tests (24 previos + 28 nuevos); frontend lint/typecheck limpios + 4 tests vitest; migración data-only idempotente (snapshot sin cambios). Deuda:

- **D-201-1 (MINOR):** la consulta `vehicle-by-plate` requiere `documentType`+`documentNumber` del propietario (keys `owner_document_type`/`owner_document_number` con fallback) en `fieldValues`; si faltan, el provider degrada a check `unknown` + overall `yellow`. El MVP prioriza VIN (17 chars alfanum). Cablear la captura del doc del propietario cuando el trámite lo requiera.
- **D-201-2 (MAJOR diferido):** mismatch de configuración entre entornos. El código (`InfrastructureExtensions.AddConsultationProviders`) lee SOLO env vars `VERIFIK_BASE_URL/VERIFIK_API_TOKEN/VERIFIK_AUTH_SCHEME/VERIFIK_TIMEOUT_SECONDS` vía `Environment.GetEnvironmentVariable` (fuente de verdad `.env.verifik.example`), pero `docker-compose.prod.yml` inyecta `Verifik__BearerToken/BaseUrl/TimeoutSeconds` (config binding) que el código NUNCA bindea → en prod el token no llegaría al provider. La sección `Verifik` del `appsettings.*.example` es puramente documental. Alinear: o cambiar el compose a `VERIFIK_*`, o que el código bindee también `IConfiguration.GetSection("Verifik")`. Además `.env.verifik` no se autocarga en dev (`docker-compose.yml` sin `env_file`) → hay que `source .env.verifik` antes de `dotnet run`.
- **D-201-3 (MINOR):** `RunConsultationHandler.IsNotDraftViolation` detecta el `check_violation` del trigger AC2 por substring (`"draft"`) en el mensaje de excepción, porque Application no referencia Npgsql. Frágil (falso positivo si otra excepción contiene "draft"). Introducir excepción de dominio tipada o traducción en el repo cuando haya harness de DB real (liga D-199-5).
- **D-201-4 (cobertura, mayor riesgo):** `VerifikConsultationProvider.SendAsync` (manejo 404/5xx/timeout/JsonException + selección VIN/placa + header auth) no tiene test unitario (requeriría `HttpMessageHandler` mock; se evitó añadir paquete por preferencia de tooling mínimo). El mapeo de respuestas SÍ está cubierto (`VerifikResultMapperTests`, 14+ casos). Añadir test de transporte (handler fake hand-rolled o nuevo `Flit.Infrastructure.Tests`) cuando se priorice.
- **D-201-5 (MINOR, preexistente):** `src/Flit.Api/appsettings.Development.json.example` ya era JSON inválido en HEAD (clave `Identity` duplicada + bloque SMTP huérfano sin `"Smtp": {`); esta HU solo insertó el bloque `Verifik` válido. No es regresión de #10201. Arreglar el `.example` en una limpieza aparte.
- **D-201-6 (MINOR):** `VerifikVehicleResponse.CilindrajeNormalizado` (tolerancia al typo `cilidraje`/`cilindraje`) está implementado y testeado pero `VerifikResultMapper` NO hidrata el cilindraje (solo `plate/vin/vehicle_year`, los field_keys del seed MATRICULA_NUEVA). Decidir si se hidrata `engine_displacement` o se documenta como tolerancia preparada para futuro.
