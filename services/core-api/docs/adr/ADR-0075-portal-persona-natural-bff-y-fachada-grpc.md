# ADR-0075: Portal Persona Natural con servicio BFF propio y fachada gRPC delgada en core-api

**Fecha**: 2026-10-27
**Status**: Propuesto
**Deciders**: Willyn Londoño (solicitante), Claude Code (agente de implementación). Aceptación: Líder Técnico (regla FLIT 15).
**Tags**: arquitectura, backend, frontend, integraciones, mensajería, seguridad, habeas-data
**Épica / Feature**: épica «Portal Persona Natural» (ID por asignar en ADO) · borradores en `docs/ado-drafts/epic-persona-natural/`
**Diseño y contratos**: `docs/design/PERSONA-NATURAL-arquitectura-y-contratos.md`

## Contexto

Se construye un portal público para que una **persona natural** radique **cualquier tipo de trámite que FLIT 2
declare disponible para persona natural**, sin crear cuenta. El **traspaso** (`TRASPASO_STANDARD`) es solo el **piloto**:
es el único wizard que existe diseñado en el prototipo. El front ya existe como prototipo (`transfer-step-trail`:
TanStack Start + Vite, todo simulado). El alcance termina cuando el trámite queda **creado en FLIT 2** bajo una empresa
«Persona Natural», con el organismo de tránsito que el ciudadano eligió, y el ciudadano puede seguir su estado desde
el portal.

> **Corrección del PO (2026-10-27):** el supuesto S3 «solo traspaso» queda **anulado**. Esta versión del ADR lo
> reemplaza por la decisión 10 (wizard dinámico dirigido por la definición del tipo).

Restricciones que ya están fijadas:

1. **Front y back propios**, como ICT: el front no habla con FLIT 2.
2. **Consultas en línea** (vehículo, conductor) y **validaciones de identidad** las ejecuta FLIT 2; **creación del
   trámite por cola**.
3. **Sin cuenta de ciudadano**: la identidad se prueba por trámite y por persona con el enlace de validación.
4. **Dato mínimo** para cualquiera; detalle completo solo dentro de un trámite y para actores validados.
5. **Datos personales de ciudadanos** (Ley 1581): el portal guarda la PII en un schema propio.
6. **Cualquier tipo de trámite elegible**: la definición del tipo (pasos, campos, actores, documentos) la dicta FLIT 2;
   el portal no codifica reglas de un tipo concreto.

Lo que ya existe y condiciona la decisión:

- **ICT** (`core-ict` → `core-api`) usa gRPC `IctOrchestration` con un esquema HMAC propio (`IctService`), el
  `tenant_id` **dentro del mensaje** y el contrato dentro de `services/core-ict/.../Protos`. Es previo a ADR-0070.
- **ADR-0070**: entre servicios, gRPC con **token de servicio de Identidad** (`svc-<código>`), empresa en la metadata
  `x-flit-tenant-id`, contratos en `contracts/proto/flit/<servicio>/v<n>/`, REST hacia afuera.
- **core-consultas** ya atiende `flit.consultas.v1.ConsultasService` (`ConsultarVehiculo`, `ConsultarConductor`, …)
  con `aud=consultas`, scope `platform.consultas`, caché, cadena por empresa y medición por producto.
- **core-api** ya atiende `flit.tramites.v1.CatalogosService.ListarOrganismosTransito` (scope
  `platform.tramites.catalogos`) y existe el scope `platform.tramites.ict` (`aud=tramites`).
- **SDK de plataforma** (`Flit.Platform.Sdk`): outbox + RabbitMQ (`AddFlitOutbox`), bandeja idempotente por
  `EventId` (`AddFlitConsumer`), `ServiceTokenProvider`, `PlatformServiceCallInterceptor`; la plantilla
  `services/core-plantilla` genera servicios con schema propio.
- **Prevalidación de identidad** (Feature #10864): validaciones standalone ancladas a una persona del tenant y
  reutilizables por referencia al crear el trámite.
- La **API externa** `/api/v1/external/*` (ADR-0067) es para integradores máquina a máquina con feed por cursor; solo
  escribe adjuntos.

**Hechos verificados sobre cómo FLIT 2 define un tipo de trámite** (investigación del 2026-10-27, rutas relativas a
`services/core-api/`):

- **Fuente única de tipos**: `tramites.procedure_types` (DDL en `src/Flit.Infrastructure/Persistence/Sql/Ddl/`), con
  `family` (CHECK `MATRICULAS|TRASPASO|OTROS`, DDL 79), `wizard_enabled` (barrera de operación, ADR-0050),
  `publication_status`, `version` y `gate_profile jsonb`. Según los seeds del repo solo tienen `wizard_enabled = true`
  `MATRICULA_NUEVA`, `TRASPASO_STANDARD` (DDL 85) y `TRASPASO_UNILATERAL` (DDL 98); el resto está apagado por diseño
  (comentario del DDL 82: «base técnica, no diseño funcional validado»). En runtime pueden haberse encendido con
  `PUT /api/v1/superadmin/procedure-types/{id}/wizard-enabled`; eso no se ve desde el repo.
- **Definición declarativa parcial y consultable**: `procedure_steps` → `procedure_sections` (con `section_type`) →
  `form_fields` (key, label, tipo, required, options, validationSchema); `procedure_document_requirements` (matriz
  documental con precedencia OT > Default); `conformation_rules`; y `gate_profile` (`ProcedureTypeGateProfile.cs`:
  `entryMode` VIN/PLATE/BOTH, `requiresSeller/Buyer/Lessee`, `allowsMultipleSeller/Buyer`,
  `requiresCommercialValue`, `requiresBiometrics`, `biometricActors`, `signatureActors`, `requiresPlateRequest`,
  `hasPrendaGate`, `allowsComplementaryTransformations/Prenda`, `transitOfficeSource`, entre otros).
- **Endpoints que ya exponen partes**: `GET /api/v1/procedure-types/{code}/configuration` (pasos, secciones y campos; **sin**
  `gate_profile` ni documentos), `GET /api/v1/tramites/wizard-preview`, `GET /api/v1/tramites/document-requirements/preview`
  (depende del organismo), `POST /preflight-preview` (consulta RUNT/RNMC/SIMIT sin persistir) y
  `GET /api/v1/tramites/procedure-types`. **No hay RPC gRPC de definición**: `CatalogosGrpcService` solo atiende
  `ListarOrganismosTransito`.
- **Lo que sigue en código, no en datos**: reglas condicionales de documentos (`ConditionalDocumentRules`,
  `CancelacionCausales`), prenda (`PrendaGate`), improntas (`ImprontaPolicyEvaluator`), transformaciones, exigencia de
  Cámara de Comercio y de mandato (`CamaraComercioRequirementResolver`, `ExigeMandato`) y las cantidades de actores
  (solo flags `allowsMultiple*`). `TramiteTipologiaCatalog.cs` conserva solo 2 tipologías (ADR-0050 propone retirarlo).
- **El wizard de FLIT 2 es dirigido por servidor con renderers fijos**: `TramiteWizard.tsx` lee los pasos de
  `WizardStateDto` y `sectionRendererRegistry.ts` mapea `section_type` a 7 cuerpos (consulta, documentos, actores,
  prenda, identidad, fur, genérico). Un tipo nuevo se dibuja solo si usa `section_type` conocidos.
- **ICT** solo publica 2 tipos en `ict.procedure_type_mapping` (transacciones 1 y 3); no es referencia de cobertura.
- **No existe una marca «disponible para persona natural»**: el código solo distingue `natural`/`juridical` por actor
  (`ActorPersonTypes.cs`; NIT ⇒ jurídica). La viabilidad por tipo es una **inferencia** (ver el documento de diseño §6.5).

## Decisión

1. **Servicio nuevo `core-persona-natural`** (nace de `core-plantilla`): BFF público y dueño del schema
   `persona_natural` (borradores, solicitudes, personas del portal, espejo de estado, auditoría). Su front,
   `frontend-persona-natural`, solo habla con él.
2. **Fachada gRPC delgada en `core-api`**: `flit.tramites.v1.PersonaNaturalService`
   (`contracts/proto/flit/tramites/v1/persona_natural.proto`), adaptador sobre los casos de uso existentes
   (mismo patrón que `IctOrchestrationService`, sin reimplementar reglas). Autenticada con **token de servicio**
   (`svc-persona-natural`, scope nuevo `platform.tramites.persona_natural`, `aud=tramites`) y empresa en
   `x-flit-tenant-id`, **no** con el esquema HMAC de ICT ni con `tenant_id` en el mensaje.
3. **Consultas en línea sin pasar por una fachada nueva**: el BFF llama directo a `core-consultas`
   (`ConsultasService`) con su token (`platform.consultas`) y la empresa «Persona Natural». La proyección a
   **dato mínimo** ocurre en el BFF; la cuota, la caché y la medición de consumo quedan en Consultas.
4. **Empresa «Persona Natural» real**: una fila de `admin.companies` creada por el SuperAdmin, cuyo id se entrega
   al BFF por configuración (`PersonaNatural:TenantId`). `core-api` **liga** el cliente `svc-persona-natural` a esa
   empresa y rechaza cualquier otra en la fachada (un token con el scope no puede actuar por cualquier empresa).
5. **Creación por cola**: el BFF persiste la solicitud y escribe en su outbox `persona_natural.solicitud.radicada`
   (exchange `flit.persona-natural`) y responde `202`. Un consumidor en `core-api` (bandeja del SDK, idempotente por
   `EventId` y por `external_ref = solicitud_id`) crea y radica el trámite con origen `persona_natural`, y publica el
   resultado. El estado posterior vuelve por `tramites.procedure.state_changed`, filtrado por origen/empresa.
6. **Identidad por prevalidación standalone**: la validación de cada parte se pide a FLIT 2 como prevalidación
   anclada a la persona **antes** de que exista el trámite; al radicar se usa por referencia **solo dentro de esa
   solicitud**. En el portal no hay reutilización entre trámites ni vigencia de 30 días: cada trámite pide una validación
   nueva a cada parte (decisión del PO, 2026-10-27; el comportamiento de FLIT 2 para sus propios usuarios no cambia).
7. **Sin cuenta**: sesión efímera por trámite emitida por el BFF (token corto ligado a la solicitud); reanudar o
   consultar un radicado exige **radicado + cédula**. Captcha y límites por IP/cédula en el BFF, tope global de
   consultas RUNT por empresa.
8. **Endpoints propios, no proxy**: el BFF expone un catálogo cerrado de rutas REST (OpenAPI
   `contracts/openapi/persona-natural.v1.yaml`) por el Gateway. Ninguna ruta reenvía a FLIT 2 de forma genérica.
9. **Repo del front** (el supuesto S2 quedó anulado por el PO el 2026-10-27): el front vive en este monorepo como app
   propia `frontend-personanatural` (Next.js, patrón de `frontend-comparendos`), sin login del hub; el diseño se porta
   desde el repo de referencia `transfer-step-trail`. Consume el OpenAPI publicado en `contracts/openapi`.
10. **Wizard dinámico dirigido por la definición del tipo** (reemplaza el supuesto S3). La fachada suma
    `ObtenerDefinicionTipoTramite(code, organismo_id)` y `ListarTiposDisponibles`; el BFF expone
    `GET /tipos-tramite` y `GET /tipos-tramite/{code}/definicion`. La definición compone, sin reimplementar reglas, lo que
    FLIT 2 ya sabe (configuración de pasos/campos, matriz documental por organismo, `gate_profile`). El front pinta los
    pasos por `section_type` con un registro de renderers equivalente al de FLIT 2 y el diseño del repo; **el portal no
    contiene lógica de ningún tipo concreto**. Pago y Envío son pasos propios del portal, no parte de la definición.
11. **Elegibilidad por tipo = lista blanca explícita en FLIT 2** (Feature J, HU-J2), no deducida. Un tipo es
    `disponible_pn` solo si: está publicado y con `wizard_enabled`; está en la lista blanca; todos sus actores pueden
    ser persona natural (sin NIT, sin Cámara de Comercio, sin mandato/tramitador, sin contrato de leasing, sin
    verificación física de improntas, sin documentos externos por causal); toda sección tiene un `section_type` que el
    portal sabe pintar; y tiene tarifa vigente (HU-J1). Si falla alguna condición, el BFF responde `disponible=false`
    con un motivo y el tipo no entra al wizard.
12. **Piloto y cierre de capacidades**: se arranca con `TRASPASO_STANDARD`. Los tipos que dependen de reglas que hoy
    viven solo en código —jurídica/NIT, mandato, impronta, leasing, causales de cancelación, prenda con acreedor
    jurídico, FUR/firma— **permanecen no disponibles** hasta que esa capacidad se cierre (se vuelve dato o se diseña su
    pantalla). Habilitar un tipo es una decisión de configuración, no un despliegue.
13. **Snapshot de la definición en la solicitud**: al abrir la sesión se guarda `definicion_version` y la definición
    resuelta; el borrador se valida y se pinta contra ese snapshot. Al radicar, el consumidor en `core-api` revalida
    contra la versión vigente y, si cambió de forma incompatible, devuelve `definicion_obsoleta` (el ciudadano
    reinicia con la definición nueva). El consumidor crea el trámite para el `tipo_tramite_codigo` de la solicitud con
    los mismos handlers (`CreateProcedureInstanceHandler`, `PatchFieldValuesHandler`, `PutActorsHandler`,
    `PutCommercialHandler`, `RegistrarPrendaHandler`, `RunPreflightHandler`), sin rama por tipo en el portal.

## Alternativas consideradas

### Opción A: BFF propio + fachada gRPC delgada en core-api (elegida)

**Pros:**
- Aísla la PII del portal en un schema y un servicio propios; un incidente del portal no abre `core-api`.
- Reutiliza todo lo que ya existe: SDK, outbox/inbox, `ConsultasService`, handlers de trámites, plantilla de servicio.
- Sigue ADR-0070 y ADR-0064 (gRPC entre servicios, cola para lo asíncrono) y el precedente ICT.
- Superficie pública mínima y controlada: solo las rutas del BFF.

**Cons:**
- Un servicio más que desplegar, monitorear y versionar (compose, CD, base de datos, usuario de broker).
- Dos contratos nuevos que mantener (OpenAPI externo y `.proto` interno) más los eventos.
- Latencia extra de un salto en las consultas.

**Esfuerzo:** L
**Riesgos:** deriva entre la fachada y el wizard de FLIT 2 si los handlers cambian; se mitiga reutilizando los mismos
handlers y con pruebas de contrato.

### Opción B: Módulo dentro de core-api con endpoints públicos `/api/v1/persona-natural/*`

**Pros:**
- Sin servicio nuevo, sin gRPC ni cola: llamada en proceso a los casos de uso.
- Menos infraestructura y menos latencia.

**Cons:**
- La PII de ciudadanos anónimos queda en el mismo proceso y la misma base que los trámites de todas las empresas.
- Abre al público una superficie nueva en el servicio más crítico y más cargado; un ataque de abuso del portal
  degrada a todos los OT.
- Contradice la dirección de la plataforma (servicios por dominio con schema propio, ADR-0064).
- No hay cola: se pierde el desacople y la idempotencia por bandeja.

**Esfuerzo:** M
**Riesgos:** incidente de seguridad o de carga con alcance a toda la plataforma; Habeas Data más difícil de aislar.

### Opción C: Front directo a la API externa REST (`/api/v1/external/*`) con las server functions del propio front

**Pros:**
- Cero servicios backend nuevos; se reaprovecha el login `client_credentials` y el feed.
- Rápido para un prototipo.

**Cons:**
- La API externa es solo lectura más adjuntos: habría que ampliarla para crear trámites, consultas e identidad,
  con un contrato pensado para integradores B2B, no para ciudadanos anónimos.
- Sin base de datos propia no hay borradores, reanudación, espejo de estado ni cola; el secreto del cliente viviría
  en el entorno del front.
- Sin lugar para anti-abuso, auditoría de PII ni reglas de dato mínimo.
- Mezcla el modelo de «clientes máquina» (ADR-0067) con tráfico anónimo masivo.

**Esfuerzo:** M
**Riesgos:** secreto expuesto, abuso de consultas RUNT sin tope, incumplimiento de Habeas Data.

### Decisión transversal: cómo soportar varios tipos de trámite

Se evaluó con la Opción A ya elegida (BFF + fachada); aplica igual a las otras.

#### Alternativa T1: un wizard hardcodeado por tipo en el front (y un caso de uso por tipo en el BFF)

**Pros:**
- Cada pantalla se diseña a medida; es lo que hoy existe para el traspaso.
- No hace falta una definición consultable.

**Cons:**
- Replica en el portal reglas que FLIT 2 ya tiene (actores, documentos, campos); deriva garantizada cuando FLIT 2
  cambie un tipo.
- Cada tipo nuevo es desarrollo de front, de BFF y de pruebas; el catálogo tiene 18+ tipos.
- Contradice la barrera de operación (`wizard_enabled`, ADR-0050): habilitar un tipo en FLIT 2 no lo habilitaría en el
  portal sin código.

**Esfuerzo:** XL por tipo · **Riesgos:** reglas divergentes, tipos olvidados, sobrecoste de mantenimiento.

#### Alternativa T2: wizard dinámico dirigido por la definición del tipo (elegida)

**Pros:**
- FLIT 2 sigue siendo la única fuente de pasos, campos, actores y documentos; el portal solo renderiza.
- Habilitar un tipo es configuración (lista blanca + tarifa), sin desplegar el front.
- Reutiliza `GetProcedureTypeConfigurationHandler`, `PreviewDocumentosInformativosHandler`, `ProcedureTypeGateProfile` y
  la misma estructura «server-driven con renderers fijos» que ya usa el wizard de FLIT 2.

**Cons:**
- La definición actual es parcial: las reglas condicionales y las cantidades de actores viven en código (por eso la
  decisión 11 exige lista blanca y no deducción) y hay que construir el RPC y el DTO.
- Hace falta un registro de renderers en el portal equivalente al de FLIT 2 y un renderer «genérico» de campos que el
  repo de UI no tiene.
- Los `section_type` sin pantalla (p. ej. `fur`) dejan el tipo fuera hasta diseñarlos.

**Esfuerzo:** L (una vez) y S por tipo habilitado · **Riesgos:** definición cambiante a mitad de un borrador (mitigado con
el snapshot, decisión 13); exponer un tipo cuya regla condicional no está en la definición (mitigado con la lista blanca).

#### Alternativa T3: reutilizar el wizard de FLIT 2 incrustado en el portal (iframe o micro-front)

**Pros:**
- Cero renderers nuevos; cada tipo sale con su comportamiento real.

**Cons:**
- El wizard de FLIT 2 exige sesión, rol, compañía y `AppShell`; asume un operador y no un ciudadano anónimo.
- Rompe la regla de que el front del portal no habla con FLIT 2 y mezcla la línea visual de FLIT con la del portal
  (el repo `transfer-step-trail` tiene su propia línea base).
- Expone la superficie completa de trámites a tráfico anónimo.

**Esfuerzo:** M · **Riesgos:** abuso, fuga de datos de otras compañías, UX inconsistente.

## Tradeoff aceptado

Se elige la **Opción A**: pagar un servicio más y dos contratos nuevos a cambio de aislar la PII y la superficie
pública del portal, y de reutilizar sin copiar las reglas de FLIT 2. La Opción B ahorra infraestructura pero
concentra el riesgo en `core-api`; la C no resuelve borradores, cola ni anti-abuso.

Para los tipos se elige **T2**: se paga un RPC de definición, un registro de renderers y una lista blanca explícita a
cambio de no duplicar reglas y de habilitar tipos por configuración. T1 es más simple para un solo tipo, pero no escala
y duplica reglas; T3 contradice el aislamiento que justifica la Opción A.

## Consecuencias

### Lo que se gana
- El portal escala y falla de forma independiente de `core-api`.
- La PII de ciudadanos vive en `persona_natural` con cifrado, retención y auditoría propios.
- FLIT 2 sigue siendo el único dueño del trámite, de las consultas y de la identidad.
- Todo lo transaccional es idempotente y reintentable (outbox + bandeja).

### Lo que se pierde
- Simplicidad operativa: nuevo contenedor, base, usuario de broker, ruta de gateway, secreto de servicio.
- Consistencia inmediata: el trámite aparece en FLIT 2 tras la cola (segundos), no en la misma llamada.
- Cobertura inmediata de todos los tipos: solo los de la lista blanca y sin dependencias de reglas en código se
  ofrecen; el resto espera a que su capacidad se cierre (decisión 12).

### Cambios operacionales y dependencias
- `svc-persona-natural` en `Suite:Oidc:ServiceClients` con scopes `platform.consultas`,
  `platform.tramites.catalogos` y el nuevo `platform.tramites.persona_natural`; el scope nuevo entra en
  `OidcDefaults.ServiceScopeAudiences` (`aud=tramites`) y en el contrato de plataforma §3 (rama propia, lo revisa el
  líder).
- `Tramites:Bus:Habilitado` debe estar activo en cada ambiente para que el estado vuelva por el bus.
- Puertos propuestos (por confirmar con el líder): REST interno 4029/5029/6029 y gRPC de salud 8087;
  `docs/despliegue-y-puertos.md` §2.d.
- `buf lint` / `buf breaking` cubren el `.proto` nuevo; el AsyncAPI se valida con `contracts/asyncapi/validate.cjs`.
- Las migraciones EF de `core-persona-natural` y de `core-api` necesitan `Designer.cs` o `[DbContext]`/`[Migration]`
  inline, o no corren al arrancar.
- Tamaño del impacto: ICT no se toca; su migración al esquema de token de servicio queda fuera de esta épica.

### Riesgos abiertos
- **Costo de consultas RUNT** con tráfico anónimo: el tope global y el captcha son requisitos, no mejoras.
- **Cierre del vínculo cliente↔empresa** en la fachada: sin él, el scope por sí solo no basta (ver Decisión 4).
- **Texto de consentimiento y retención** de la PII del portal: pendiente de validación legal antes de PDN.
- **Elegibilidad mal deducida**: exponer un tipo cuya regla condicional vive solo en código (jurídica/NIT, mandato,
  leasing, causales, improntas) produciría trámites que FLIT 2 rechaza al crear. Mitigación: lista blanca explícita
  (decisión 11), `EvaluarRequisitos` en servidor y una prueba de contrato por tipo habilitado.
- **Definición que cambia con el borrador abierto**: mitigado con snapshot y `definicion_obsoleta` (decisión 13); el
  costo es que el ciudadano puede tener que reiniciar un trámite largo.
- **`section_type` sin pantalla en el portal** (p. ej. `fur`, firma): el tipo no se puede ofrecer hasta diseñarlo; el
  PO debe decidir el diseño de los demás tipos (documento de UI, §12).
- **Definición incompleta en el origen**: `gate_profile` no está en el DTO público y la configuración pública no trae
  documentos; el RPC compone varios handlers y puede quedar atado a su forma interna (pruebas de contrato).
- **Mapeo de nombres del catálogo del front** (siete nombres B2C) a códigos reales de FLIT 2: hoy no existe y es una
  propuesta (documento de diseño §6.5).

## ADRs relacionados

- ADR-0064 (datos separados, eventos y reportes) — outbox/bandeja y schema por servicio.
- ADR-0065 (consultas externas como capacidad de plataforma) — origen de `ConsultasService`.
- ADR-0067 (clientes de integración externos) — la API externa que esta opción no reutiliza.
- ADR-0070 (gRPC entre servicios, REST hacia afuera) — marco de contratos y autenticación.
- ADR-0071 (avisos de sincronización a FLITO) — precedente de aviso sobre la outbox del SDK.

## Notas para agentes

- **Backend Agent**: la fachada es un adaptador delgado; cada RPC llama a los handlers existentes
  (`CreateProcedureInstanceHandler`, `PatchFieldValuesHandler`, `PutActorsHandler`, `PutCommercialHandler`,
  `RegisterIntegrationAttachmentHandler`, `RunPreflightHandler`, `TransitionProcedureInstanceHandler`). No copiar el
  código de `IctOrchestrationService`; extraer lo común si hace falta.
- **Backend Agent (tipos)**: `ObtenerDefinicionTipoTramite` compone `GetProcedureTypeConfigurationHandler`,
  `PreviewDocumentosInformativosHandler`, `ProcedureTypeGateProfile` y `ConditionalDocumentRules`; no reimplementa reglas
  ni añade ramas por tipo. La elegibilidad sale de la lista blanca de la HU-J2.
- **Frontend Agent**: el front nunca conoce URLs ni credenciales de FLIT 2; solo el OpenAPI del BFF. No codifica
  nada de un tipo concreto: pinta la definición por `section_type`.
- **Database Agent**: `persona_natural` es un schema propio con usuario propio; no leer tablas de otros servicios.

## Excepciones de convención de datos del BFF

El esquema `personanatural` de `core-personanatural` se aparta a propósito de la convención de datos de los esquemas de
FLIT 2 (checklist `db-schema-validator`, §A). Decisión del PO; el Líder Técnico las ratifica al aceptar este ADR.

- **Sin `tenant_id` de aislamiento ni RLS (A4, A10, A11):** el BFF es un servicio propio con base y usuario propios y un
  solo tenant, así que no hay aislamiento entre inquilinos que proteger dentro de su base. `procedure_request.tenant_id`
  existe solo como dato de la solicitud y para la idempotencia por empresa.
- **Sin disparadores `row_version`/`audit_log` (A16) ni borrado lógico (A5, A6):** la aplicación gestiona `row_version`
  (token de concurrencia) y `updated_at`; la auditoría propia son `pii_access_log`, `identity_audit` y `status_history`;
  la retención anonimiza o borra físicamente (Ley 1581), por lo que no hay `deleted_*` ni `created_by`/`updated_by`
  (no hay usuarios autenticados en el portal).
- **PK y tipos (A3):** los `id` son `uuid` generados por la aplicación (sin `DEFAULT uuidv7()` en la base) y las tablas de
  auditoría usan `bigint identity`.
- **Nombres en singular (A2):** las tablas están en inglés y snake_case, pero en singular (`procedure_request`,
  `requester_person`, …), igual que `outbox`/`inbox` del SDK, en lugar de plural. Los valores de dominio (estados,
  orígenes) siguen en español porque son datos y no identificadores.
- **`ON UPDATE RESTRICT` (A8):** EF Core no lo modela; está escrito a mano en la migración `ModeloDelPortal`. Si una
  migración futura recrea una FK, debe repetirlo.
- **PII (A15):** las columnas con datos personales cifrados (AES-GCM) o huellas HMAC llevan `COMMENT` con `@pii:high|medium`.

## Decisiones posteriores a la revisión formal (2026-10-27)

Tras la revisión de código, seguridad y datos de las ramas del portal, el PO decidió:

14. **El evento de radicación no lleva datos personales.** `tramites.persona_natural.radicacion_solicitada` (contrato en
    `contracts/asyncapi/tramites-events.v1.yaml`) trae solo identificadores, huella de la definición y referencias de adjuntos.
    El consumidor en core-api (HU D4) obtiene las partes por PULL con `RadicacionService.ObtenerSolicitudParaRadicar` del BFF
    (gRPC interno, cliente `svc-tramites`, audiencia `personanatural`, scope `platform.personanatural.leer`, solo solicitudes
    `en_cola`, cada lectura auditada en `pii_access_log`). Así no hay llave de cifrado compartida con core-api ni cifrado en la
    outbox o el broker, fuera de la retención.
15. **La empresa «Persona Natural» no se siembra por migración.** Se crea al activar el portal con
    `deploy/postgres/crear-empresa-persona-natural.sql` (paso del handoff), para no aparecer en listados de SuperAdmin de los
    ambientes donde el portal no está encendido.
16. **Validaciones de identidad sin reutilización ni vigencia** (solo portal): cada trámite pide una nueva a cada parte; las
    consultas de FLIT 2 por documento ignoran las validaciones ancladas al portal (`portal_solicitud_ref`).
17. **Nombres del esquema `personanatural` en inglés, snake_case, singular** (migración única `ModeloDelPortal`); las
    excepciones de convención de datos del BFF están al final de este ADR.
18. **El portal no abre CORS en el gateway:** el navegador llama al proxy de su propio host.
