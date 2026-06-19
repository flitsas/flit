---
name: Trámites Rework — proceso real (matrícula inicial + traspaso)
overview: "Convertir el motor genérico de trámites (#10128) en un proceso vehicular real end-to-end para 2 modalidades (matrícula inicial VIN-first / traspaso estándar placa-first), portando conceptualmente el dominio de Johan.Jimenez con modelo explícito (sin JSONB `_`). Alcance completo: actores, documentos, flujo por tipo, consultas mockeadas, preflight, biométrica, firma, portal, FUR/PDF. Terceros mockeados detrás de la capa de proveedores."
status: draft
proposedAt: 2026-06-18
proposedBy: Samuel Cardenas
work_id: FEAT-2026-06-18-002
branch: feature/scardenas-tramites-10128
referenceProject: /Users/samuelcardenasg23/Desktop/work/Flit/capacitacion-ia-flit/Johan.Jimenez
relatedFeatures: [10128, 10116, 10138]
decisions:
  tipologias_mvp: [matricula_inicial, traspaso_standard]
  profundidad: full (incl. biométrica mock, firma, portal, FUR/PDF)
  storage_docs: disco_local
  consultas: contract-first (real|mock por proveedor; Verifik RUNT real en DEV ya; swap mock→real sin rework)
todos:
  - id: recon
    content: Recon Johan.Jimenez + auditoría estado real flit
    status: completed
  - id: scope-approval
    content: Aprobación humana del plan y estrategia de branch/PR
    status: completed
  - id: slice-0-domain
    content: Portar dominio puro (tipologías, gates, state machines, vin-policy) a C#
    status: completed
  - id: slice-1-schema
    content: Migración núcleo — instances (modalidad/tipología/checklist) + attachments, preflight, comercial, events. Biométrica/firmas/portal diferidos a slices 6-7.
    status: completed
  - id: slice-2-actores
    content: Captura de actores end-to-end (command + endpoint + UI) — comprador→BUYER, vendedor→OWNER
    status: completed
  - id: slice-3-documentos
    content: Documentos adjuntos (storage disco path-traversal-safe + endpoints + UI grid + auto-checklist vía dominio)
    status: completed
  - id: slice-4-flujo
    content: Flujo diferenciado matrícula/traspaso cableado a gates (4a backend keystone + 4a-fix gating docs estricto + 4b wizard shell + consulta persist→preflight)
    status: completed
  - id: slice-5-consultas
    content: Consultas contract-first (Verifik vehículo/SIMIT/RNMC + INTEMPO) + toggle real|mock por proveedor
    status: completed
  - id: slice-6-biometrica
    content: Biométrica mock (3 fotos, scoring mock, magic link)
    status: pending
  - id: slice-7-firma-portal
    content: Firma e-doc + portal público participantes + FUR/PDF
    status: pending
isProject: true
---

# Plan — Trámites Rework (proceso real)

## Contexto y veredicto de recon

Dos reconocimientos (2026-06-18):

**Norte — Johan.Jimenez** (Node/TS, Drizzle/PG, React/Vite). Joya portable: dominio puro y testeado en `packages/shared-types/src/` — catálogo de tipologías + checklist dinámico (`tramite-tipologias.ts`), matriz paso×actor (`tramite-tipologia-matriz.ts`), gates del wizard como fuente única UI/backend (`traspaso-gates.ts`), máquinas de estado (`tramites.state.ts` interna + `tramite-workflow.ts` STT), invariante VIN (`tramites.vin-policy.ts`). Antipatrón a NO copiar: estado escondido en JSONB con claves `_` (`vehiculo._vendedor`, `_runtComprador`...). Consultas externas detrás de capa de servicio con modo `direct|cea-proxy|mock`.

**Estado real flit:** motor genérico de captura de campos, **no** un proceso de trámites. Tres pilares ausentes:
1. **Actores** — `procedure_instance_actors` existe pero está muerta (sin command/endpoint/UI).
2. **Documentos adjuntos** — ausentes end-to-end (las tablas de #10138 modelan requisitos, no archivos).
3. **Lógica por tipo** — cero; `submit` es flip de estado genérico; matrícula y traspaso son indistinguibles.
Solo `MATRICULA_NUEVA` configurado, vía seed dev (no parametrización real).

**Activo reutilizable:** la capa `IConsultationProvider`/registry de #10201 (aún sin commitear) es la abstracción correcta para mockear terceros. Se conserva.

## Decisiones del usuario (2026-06-18)

- **Tipologías MVP:** `matricula_inicial` (VIN-first, 1 actor comprador) + `traspaso_standard` (placa-first, 2 actores vendedor+comprador). Las otras 4 (sucesión, remate, importación, flota) quedan parametrizables sin configurar.
- **Profundidad:** completa — incluye biométrica (mock Anthropic), firma electrónica, portal público magic-link, generación FUR/PDF.
- **Storage documentos:** disco local (`uploads/tramites/{instanceId}/`). Deuda explícita para prod multi-instancia.

## Principio rector

Portar **conceptualmente** el dominio de Johan a la arquitectura de flit (.NET Clean Architecture, sin MediatR, EmbeddedDdl, header X-Tenant-Id), **modelando con tablas/columnas explícitas** en vez del JSONB `_`. Diff por vertical slice, cada uno con GIA (tests) + REX (review) antes de avanzar. ADR cuando se cruce boundary o se introduzca patrón nuevo (storage de archivos, biométrica, portal público).

## Vertical slices (orden de ejecución)

| # | Slice | Capa principal | Persona | Entregable verificable |
|---|-------|----------------|---------|------------------------|
| 0 | **Dominio puro** | Domain C# | SAM | Tipologías+checklist, matriz paso×actor, gates, state machines, vin-policy + tests de paridad. Sin IO. |
| 1 | **Schema** | BD | DRE | Migración: actores usables, `procedure_instance_attachments`, `..._biometric_validations`, `..._signatures`, `..._events`, `..._participants`, `..._preflight_snapshots`, datos comerciales. Columnas explícitas. RLS + audit + triggers como el resto del schema. |
| 2 | **Actores end-to-end** | API + UI | SAM + MAIA | Command + `POST/GET .../instances/{id}/actors` + UI captura (tipo+nº doc) según trámite. |
| 3 | **Documentos** | API + UI | SAM + MAIA | Storage disco + `POST .../attachments` (multipart) + UI grid de subida por tipo + auto-marcado de checklist. |
| 4 | **Flujo diferenciado** | API + UI | SAM + MAIA | Wizard matrícula (5 pasos VIN-first) vs traspaso (6 pasos placa-first) cableado a gates del slice 0. Inmutabilidad de pasos cerrados. |
| 5 | **Consultas + preflight (contract-first)** | API + UI | SAM + MAIA | Proveedores construidos contra contratos reales documentados (`context/FLIT-Documentacion-Endpoints.md`), alcance MVP: **Verifik vehículo VIN/placa + Verifik SIMIT + Verifik RNMC + INTEMPO vehículo** (alterno). Cada proveedor con impl `real` (HttpClient) y `mock` (JSON con forma real), seleccionable por config/env **por proveedor**. Verifik vehículo real en DEV ya (de #10201). `FlitIntegrationsGatewayProvider` = transporte futuro a gateway Johan. Preflight semáforo server-driven sobre campos reales (`soat[].estado`, `tecnoMecanica[].vigente`, `tieneGravamenes`, SIMIT `multas[]`, RNMC `correctiveMeasures[]`). Diferidos: SMARTCLAUD/RUES/CONDUCTOR. |
| 6 | **Biométrica** | API + UI | SAM + MAIA | Flujo 3 fotos + scoring mock (sin Anthropic real) + magic link + reintentos. ADR storage/biométrica. |
| 7 | **Firma + portal + FUR** | API + UI | SAM + MAIA | Firma e-doc (mock), portal público participantes (magic-link, Ley 1581), generación FUR/contrato PDF. |

**Cada slice:** GIA (tests nuevos + regresión) → REX (review) → commit lógico en la rama. Deuda nueva la registra BECCA aquí.

## Consultas — estrategia contract-first (aclaración del usuario 2026-06-18)

El "mock" es temporal: las APIs (gateway Johan / Verifik) estarán disponibles pronto. Por eso NO se construyen hacks desechables — se construye contra el **contrato real** documentado en `context/FLIT-Documentacion-Endpoints.md`, de modo que el swap mock→real sea solo cambiar el transporte.

- **Forma única de respuesta:** mappers y preflight se construyen contra el JSON real documentado (ejemplos del doc). El `mock` devuelve esa misma forma; el `real` la trae por HTTP. Mappers/UI no distinguen.
- **Toggle por proveedor:** config/env decide `real|mock` por proveedor (no global). Verifik RUNT VIN/placa = `real` en DEV ya (de #10201, `.env.verifik`). SIMIT/RNMC/RUES/INTEMPO/SMARTCLAUD = `mock` hasta token/gateway.
- **Multi-proveedor real:** el registry resuelve el proveedor desde `consultation_templates.external_refs.provider` (verifik | intempo | flit_gateway). INTEMPO documenta el mismo dato de vehículo por proveedor alterno → valida el diseño del registry con UN proveedor alterno (suficiente).
- **Alcance de proveedores MVP (recortado):** Verifik vehículo (VIN/placa, real DEV), Verifik SIMIT (comparendos), Verifik RNMC, e INTEMPO vehículo (mock, prueba del registry). **Diferidos/descartados:** SMARTCLAUD (SIMIT SOAP — redundante con Verifik SIMIT, stack SOAP desproporcionado, deuda técnica documentada), RUES (solo flota_corporativa, diferida), CONDUCTOR (licencia, no aplica a trámite de vehículo). El registry los soporta como proveedores futuros sin rework.
- **Gateway Johan:** `FlitIntegrationsGatewayProvider` (stub #10201, ADR-0020) es el transporte real futuro a las APIs de Johan, no un mock rival. Queda seleccionable.
- **Demo funcional temprana:** matrícula (VIN) y traspaso (placa) muestran datos RUNT reales en DEV + semáforo real.

## Orden interno por slice

`Domain → migración (DDL/EF) → Application (handlers) → Api (endpoints) → Frontend (cliente/hooks/UI) → tests`.

## No tocar / fuera de alcance (esta iteración)

- Las 4 tipologías diferidas (sucesión, remate, importación, flota) — solo dejar el motor preparado.
- Integraciones reales de SIMIT/RNMC/RUES/INTEMPO/SMARTCLAUD — mock-con-forma-real hasta tener token/gateway (Verifik RUNT VIN/placa SÍ real en DEV).
- Auth real (sigue stub; tenant/usuario dev). Liga deuda D-199-*.
- Storage S3/MinIO (se queda disco; deuda).
- Reescrituras "de paso" fuera del módulo trámites.

## Estrategia de branch/PR (a confirmar con el usuario)

Recomendación: seguir en `feature/scardenas-tramites-10128`, commitear por slice (historia revisable), el trabajo #10201 sin commitear entra como base del slice 5. Un PR final cuando el proceso esté usable end-to-end (decisión del usuario: "arreglar todo antes del PR"). Alternativa: PRs apilados por slice.

## Rollback

Cada slice es un commit lógico revertible. Migraciones con `Down()`. Feature gateado por tipo de trámite (las modalidades nuevas no afectan los endpoints existentes hasta cablearse).

## Riesgos

- **Alcance grande** → mitigado por slices con gate de aprobación/verificación entre cada uno.
- **Doble máquina de estados** (interna vs STT) en Johan arrastra deuda — al portar, decidir si se unifica o se replican ambos grafos. HARRY/ADR.
- **Biométrica/firma/portal** dependen de proveedores externos — todo mock en esta fase, cablear real es fase posterior.
- **Storage disco** no apto prod — deuda explícita.

## Deuda y items de integración — Slices 0 + 5 (registrado por BECCA)

**Slice 0 (dominio):**
- **DR-0-1:** `VinPolicyEvaluator` decide el invariante sobre trámites ya cargados; falta la capa IO (cargar/normalizar/ordenar por VIN) — va en el slice de infraestructura.
- **DR-0-2:** `TramiteModalidadEntrada` tiene códigos canónicos pero aún sin columna/mapeo EF (Slice 1).
- **DR-0-3:** `ChecklistOverride` modelado; (de)serialización JSON desde BD queda para Application/Infra.
- **DR-0-4:** Gates aún no integrados con `ProcedureInstance`/wizard runtime (Slice 4); son fuente de verdad pura lista para consumir.
- **DR-0-5:** `CA1861` suprimido solo en el csproj de tests (arrays-literal inline). Opcional: convertir a `static readonly`.

**Slice 5 (consultas):**
- Hereda D-201-* (mismatch config Verifik prod/dev, cobertura de transporte HTTP).
- Documentar los 3 env vars de modo (`VERIFIK_SIMIT_MODE`, `VERIFIK_RNMC_MODE`, `INTEMPO_MODE`) en `appsettings.Development.json.example` (junto con arreglar el JSON inválido preexistente — D-201-5).

**Slice 1 (schema):**
- **DR-1-1 (cosmético):** EF lista índices solo-FK en el snapshot que el DDL hand-written no crea (mismo patrón pre-existente de `procedure_instance_actors`, ya tolerado). Manda el DDL real; no afecta BD.
- **DR-1-2:** `procedure_instance_actors.actor_type` se deja intacto (sirve para roles MVP).
- **DR-1-3:** `storage_path` apunta a disco local — deuda prod (S3/MinIO) ya en plan.
- **DR-1-4:** `tipo`/`causal`/`overall`/`source` son varchar libres (sin CHECK/enum BD); validación en Application. Blindaje BD opcional en slice posterior.
- **DR-1-5 (observación SCOUT):** `procedure_instance_events` solapa conceptualmente con `procedure_instance_status_history` (#10150). Events = timeline general/QR; history = transiciones de estado. Coexisten (como Johan); revisar si se unifican más adelante.

**Slice 2 (actores):**
- **DA-2-1:** `actor_type` en español (comprador/vendedor) vs entity codes en inglés (BUYER/OWNER); el mapeo vive en `PutActorsHandler`. Punto de acoplamiento implícito; mover a tabla de mapeo si se quiere blindar.
- **DA-2-3:** `vendedor→OWNER` asume "propietario saliente"; revisar si aparece tipología con vendedor≠owner (remate/sucesión, diferidas).
- **DA-2-4 (verificar en integración):** PUT = reemplazo total in-memory; confirmar que EF emite DELETE-antes-de-INSERT bajo `UNIQUE(instance, entity)`. Sin optimistic-lock en el set (hereda D-199 RowVersion).
- **DR-S2-FE-2:** `modalidadFor(family)` es mapeo grueso TRASPASO→2 / resto→1; cableado fino por tipología llega en Slice 4.
- **DR-S2-FE-3/5:** rehidratación sin autosave/dirty-tracking; `numeroDocumento` sin validación por tipoDoc (NIT/DV). Diferibles.

**Slice 3 (documentos):**
- **DA-3-1:** storage local no multi-instancia (S3/Blob pendiente, ya en plan).
- **DA-3-2:** MIME validado por header del cliente, no magic bytes; considerar sniffing de firma.
- **DA-3-3:** sin endpoint de descarga/serve binario (no estaba en contrato; añadir cuando se necesite ver/descargar adjuntos).
- **DA-3-4:** borrado no transaccional FS↔DB (acepta huérfanos en draft).
- **DA-3-5 (REX):** `FileMode.CreateNew` + timestamp ms lanza en colisión exacta (mismo ms, mismo nombre) → posible 500; degradar (sufijo/retry) si se endurece.
- **DR-S3-FE:** validación mime FE por `File.type` (no magic bytes; backend es la verdad); `uploadAttachment` usa `fetch` directo (no el helper `request` que fuerza JSON) — si hay más multipart, extraer `requestForm`.

**Slice 4a (keystone) + 4a-fix:**
- Preflight: providers-por-modalidad hardcoded en el handler (templates son 1:1; el fan-out es lógica del wizard). Compón overall + persiste snapshot + degrada a unknown.
- **DA-4a-1:** `RuntSnapshot`/`SimitSnapshot` del wizard se derivan heurísticamente (actor con doc ⇒ RUNT consultado; SIMIT inferido del overall). Cablear snapshot real cuando se persistan consultas por actor.
- **DA-4a-2:** check de impuesto vehicular no lo emite ningún provider MVP → gate de impuesto traspaso solo bloquea con override `paz_salvo_impuesto`. Confirmar fuente del check.
- **DA-4a-3:** `TramiteRadicado=true` hardcodeado; derivar del estado STT cuando exista.
- **4a-fix:** gating ESTRICTO de docs obligatorios cableado (matrícula paso 2 + traspaso paso 6 + blocker global `documentos_incompletos`; `ForzarContinuar` NO lo omite). Re-key traspaso paso 2 `documentos`→`validacion`.
- **DE-4b (en curso):** instancias deben persistir `modalidad_entrada`/`tipologia_codigo` al crearse (si no, gating inerte) — lo cierra el backend companion de 4b + default defensivo del resolver + seed dev de procedure_type traspaso.

**Slice 4b (frontend wizard):**
- **DS-4B-4:** quedó código muerto del flujo viejo config-driven (`DynamicFieldRenderer`, `useProcedureInstance.saveDraft/runConsulta/goToStep`) — candidato a limpieza si ninguna vista lo consume.
- **DS-4B (template):** `ConsultaStep`/`runConsulta` usan `RUNT_VEHICLE` hardcodeado; parametrizar el template de consulta cuando haya más de uno.
- **DS-4B-5:** `CommercialForm` valida solo valorVenta>0 + causal; tasa/derechos/método sin validación fina.

**Integración (al cablear slices superiores):**
- **DI-1:** estandarizar el literal del semáforo en **`yellow`** (FE + Track B + Johan) — el dominio documentó `amber` en `PreflightSnapshot`. No afecta la lógica del gate (solo bloquea en `red`).

## Verificación en vivo (2026-06-19) — bugs encontrados y arreglados

Se corrió la app real (Postgres local + core-api con `.env.verifik` + RUNT real). **Resultado: matrícula y traspaso consultan RUNT real end-to-end, semáforo verde con datos reales, wizard server-driven responde al estado persistido.** Los mocks (NSubstitute) no exponían nada de esto. Bugs corregidos:

**Código (rework):**
- BV-1 `reference_number` con `COUNT+1` colisionaba (D-199-2) → `MAX+1` + retry ante UNIQUE (Infra) + guardia FE anti doble-create (React StrictMode).
- BV-2 `formFieldId` `Guid` no-nullable rompía PATCH field-values (400) → nullable + resuelto por `fieldKey` server-side.
- BV-3 (CRÍTICO) `db.Update(instance)` sobre grafo trackeado marcaba hijos NUEVOS como Modified → UPDATE de 0 filas → no persistían. Agravado: PK store-generated (`uuidv7()`) con `Id=Guid.NewGuid()` → EF infería Modified. Fix: quitar `Update()` en 5 handlers + `repo.Add(entity)` explícito (estado Added→INSERT) en field-values/actores/attachments/comercial/status-history/RunConsultation.
- BV-4 (CRÍTICO) `repo.SaveChangesAsync` hacía `...ContinueWith(_=>{})` → **tragaba todas las excepciones** → 200 con datos no persistidos. Fix: propagar.
- BV-5 DTO Verifik no parseaba la respuesta real (`garantiasMobiliarias` es array no objeto → JsonException; `tecnoMecanica` array bajo ese nombre; `tieneGravamenes`/`prendas` en `informacionGeneral`) → DTO+mapper aliñados a la forma real.
- BV-6 preflight traspaso leía doc del propietario del actor vendedor (inexistente en paso consulta) → ahora de `field_values` (`owner_document_*`).
- BV-7 `HasTrigger` no declarado en ninguna entidad → declarado en las 8 con triggers (necesario para una DB con todos los triggers).

**Entorno / heredado (feature original):**
- BV-8 Seed `12-HU10200-dev-seed.sql` insertaba en `identity.tenants`/`users` con columnas inventadas (nunca corrió contra el schema real) → aliñado al schema real (`name/nit/slug/status`; `users.tenant_id` NOT NULL).
- BV-9 DB local drifteada: funciones `trg_audit_log`/`trg_row_version` ausentes (recreadas) y tabla `audit.audit_logs` ausente (creada). **⚠️ Riesgo prod:** averiguar cómo se crea la infraestructura de auditoría (`audit_logs` + funciones) en un deploy real — `SchemaBootstrap.cs` la define pero no parece autoejecutarse y ningún migration la crea; sin ella, TODA escritura auditada falla.

**Limitaciones de la corrida:** RUNT vehículo (VIN/placa) = real; SIMIT/RNMC quedaron `unknown` (requieren capturar actores, paso posterior) — no se ejercitaron con datos. Frontend no se manejó por navegador (extensión Chrome de Claude no conectada); se verificó el backend vía API smoke (las mismas llamadas que hace la UI).

**Artefactos de la corrida (limpiar):** instancias draft de prueba en `flit_dev`, fila `MANUALTEST123` en field_values (instancia 5d75cbf0), `context/verifik-vin-real-sample.json` (PII real, untracked, NO commitear), `audit.audit_logs` creada a mano. La DB local conviene reconstruirla limpia (drop+migrate+bootstrap+seeds) para quitar el drift.

## Slices 6 y 7 completados (2026-06-19)

**Todos los slices 0–7 hechos y verdes. 405 tests (344 backend = 76 Domain + 268 Application; 61 frontend), build 0/0.** Commiteado granular en `feature/scardenas-tramites-10128` con prefijo `tramites-rework(<capa>)`, **sin push**.

- **Slice 6 — Biométrica (mock):** tabla `procedure_instance_biometric_validations` (RLS+audit), scorer mock inyectable contract-first (3 fotos→score 85 aprobado, swap a Anthropic real); magic-link (raw→SHA256, TTL 24h, máx 5 intentos); fotos vía storage de disco; endpoints authed + públicos `/api/v1/public/biometric/{token}`; página pública `/biometric/[token]`; wizard paso `identidad` cableado a estado real (matrícula=comprador, traspaso=ambas partes).
- **Slice 7A — Firma + FUR (mock):** tabla `procedure_instance_signatures`; proveedor firma mock inyectable (swap a ZapSign), solo traspaso, idempotente por rol; **FUR/contrato mock SIN librería PDF** (decisión del usuario — placeholder con datos reales, swap a generador real = deuda), gated por biométrica, persistido como attachment (`tipo` fur/compraventa) + evento; preflight firma compraventa; paso `fur` del wizard cableado.
- **Slice 7B — Portal público + Ley 1581:** tabla `procedure_instance_participants` (RLS+audit, unique token_hash); magic-link hasheado, TTL 24h, single-use, sin enumeración; consent Ley 1581 versionado (`2026-06-19-v1`) con IP/UA truncada como prueba; endpoints públicos `/api/v1/public/portal/{token}` (view/consent/documentos/firma/finalizar) que agregan biométrica+firma+docs por rol; admin invitar/list/reinvite (cooldown 24h); página pública `/portal/[token]`.

### Deuda nueva slices 6–7
- **DF-1 (descarga de archivos):** NO hay endpoint de descarga/serving — attachments (pre-existente), FUR/compraventa generados y fotos biométricas solo se LISTAN, no se descargan. Falta `GET /instances/{id}/attachments/{attId}/download` (stream desde storage) + wiring FE.
- **DF-2 (rate-limiting):** endpoints públicos (biométrica/portal) sin rate-limit (TODO en `PublicPortalEndpoints`). No existe limiter reusable en el repo.
- **DF-3 (mocks swappables):** scorer biométrico (→ Anthropic real), generador FUR (→ PDF real), proveedor firma (→ ZapSign) son mocks contract-first; swap = cambiar transporte.
- **DF-4 (.gitignore):** regla `storage/` (línea 79) ignora por error las fuentes C# `Storage/` (FS case-insensitive en macOS) → se hizo `git add -f` para no romper CI; afinar la regla (anclar/excluir).
- **DF-5 (canSubmit):** biométrica/firma se reflejan en el estado del paso pero NO vetan `canSubmit` en matrícula (se preservó test pre-existente). Revisar si el submit debe bloquearse hasta identidad aprobada.

## Deuda heredada validada (de planes previos)

D-199-* (auth stub, reference_number no atómico, RowVersion inactivo), D-200-* (validación required client-side, rehidratación borrador, GUIDs dev hardcodeados), D-201-* (mismatch config Verifik prod, cobertura transporte HTTP). Se arrastran; se abordarán donde cada slice las toque.
