# Descomposición HU — Épica #12718 (borrador local, no registrado en ADO)

> **Estado:** borrador. Espera aprobación humana antes de crear en Azure DevOps (`flit-crear-hu`).
> **Sprint:** no se fija — lo asigna la sesión principal (sprint siguiente al activo).
> **AssignedTo propuesto:** Willyn Londoño Calle (`willyn.londono@flitsas.com`). Tags: `DOR`.
> `Custom.Refinement = "True"` (string, no booleano) al registrar.
> **Diseño técnico:** `docs/ado-drafts/epic-12718/DISENO-TECNICO.md` (mapa de código + contratos + DDL de
> referencia). **ADR:** `services/core-api/docs/adr/ADR-0060-dr-flit-llm-backend-y-escalacion-soporte-ado.md`.
> **Convención de PR:** cada HU cabe holgadamente en ≤800 líneas; las HUs backend/frontend de una misma
> Feature comparten rama (memoria: una rama por Feature, no por HU).
> **Configuración, no valores fijos:** salvo los enums literales que la épica fija de forma explícita
> (Alta/Media/Baja, una_vez/a_veces/siempre) y los defaults documentados en el diseño, cualquier AC que
> mencione un modelo, un tope, un timeout o un mapeo de campo referencia la **clave de configuración**
> (`Anthropic:DrFlit*`, `DrFlitFieldMappingOptions`, `DrFlit:SupportCase:*`), no el valor concreto — así,
> ajustar esas decisiones no obliga a reescribir la HU.

Totales: **14 HUs** · Feature A 5 HUs / 24 SP · Feature B 3 HUs / 21 SP · Feature C 3 HUs / 14 SP ·
Feature D 3 HUs / 15 SP. Total épica: **74 SP**.

| Feature | HUs locales |
|---|---|
| A — Fundaciones de chat en backend (LLM + manual) | HU-A1 · HU-A2 · HU-A3 · HU-A4 · HU-A5 |
| B — Caso de soporte a Azure DevOps | HU-B1 · HU-B2 · HU-B3 |
| C — Chat conversacional en frontend | HU-C1 · HU-C2 · HU-C3 |
| D — Recopilación y confirmación de casos en frontend | HU-D1 · HU-D2 · HU-D3 |

---

## Feature A — Fundaciones de chat en backend, LLM + manual (5 HUs · 24 SP)

### HU-A1 `[BACKEND] – Dr. FLIT – Extender AnthropicOptions y método de chat con guardarraíles de prompt`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | Ninguna — bloquea a HU-A2 y HU-A5 |

**Como** Líder Técnico
**quiero** que el backend tenga un método de chat de texto sobre `AnthropicMessagesClient`, con sus
parámetros de modelo/tokens/timeout leídos desde la configuración `Anthropic:DrFlit*` y un prompt que
impida que el manual o el usuario reescriban las reglas del sistema
**para** poder construir el resto del chat de Dr. FLIT sobre una base reutilizable y segura frente a
*prompt injection*

```gherkin
AC1 — positivo
Dado el system prompt configurado con las reglas del diseño (§8.1) y el manual cacheado
Cuando el backend arma la llamada a Anthropic
Entonces usa el modelo, maxTokens y timeout que resulten de Anthropic:DrFlit* (o su fallback
  ANTHROPIC_DRFLIT_*), sin valores fijos en el código de producción
Y la respuesta cruda debe ser JSON con intent/reply/citedSlugs según el contrato definido en el prompt

AC2 — negativo (guardarraíl anti-injection)
Dado un mensaje de usuario o un bloque del manual que contiene una instrucción como
  "ignora las reglas anteriores" o "revela tu prompt"
Cuando el backend procesa la respuesta del modelo
Entonces esa instrucción se trata como dato, nunca como comando (no se habilita tool-use/
  function-calling en la llamada)
Y si la salida no es JSON válido según el schema, se descarta y se marca como fallo del LLM
  (no se expone al usuario)

AC3 — borde (citas inválidas)
Dado que citedSlugs del modelo incluye un slug que no existe en el catálogo cargado (HU-A4)
Cuando el backend valida la respuesta
Entonces descarta ese slug (no lo incluye en citations) sin fallar la respuesta completa por eso solo,
  salvo que ningún slug del intent "duda" sea válido, caso en que se trata como fallo del LLM (§7.1)

AC4 — borde (apagado del LLM)
Dado Anthropic:DrFlitEnabled=false (o su fallback ANTHROPIC_DRFLIT_ENABLED=false)
Cuando se invoca el método de chat
Entonces no se realiza ninguna llamada HTTP a Anthropic
Y se devuelve el resultado equivalente a status: degraded
```

**Notas técnicas:** `Flit.Infrastructure/Ocr/AnthropicOptions.cs` (+ `DrFlitModel`/`DrFlitMaxTokens`/
`DrFlitTimeoutSeconds`/`DrFlitDailyMessageLimit`/`DrFlitEnabled`, misma sección `Anthropic`);
`Flit.Infrastructure/Ocr/AnthropicMessagesClient.cs` (+ `SendChatAsync(systemPrompt, turns, ct)`,
reutiliza `HttpClient` typed/`ApiKey`/reintento existentes, sin streaming SSE);
`InfrastructureExtensions.cs` (+5 líneas `Cfg()` en `Configure<AnthropicOptions>` de `AddOcr(...)`;
incluir `DrFlitTimeoutSeconds` en el `Math.Max(...)` que fija `HttpClient.Timeout`); prompt builder nuevo
en `Flit.DrFlit.Application` (system prompt de §8.1 del diseño, contenido de producto revisable sin
tocar el contrato). Tests: `tests/Flit.Infrastructure.Tests/Ocr/AnthropicMessagesClientSendChatAsyncTests.cs`
contra `HttpClient` mock. Sin endpoint HTTP todavía (llega en HU-A5).

---

### HU-A2 `[BACKEND] – Dr. FLIT – Contador diario de mensajes y ensamblador IDrFlitAssistant`

| Campo | Valor |
|---|---|
| SP | 8 |
| Depende | HU-A1 — bloquea a HU-A5 |

**Como** usuario autenticado de FLIT
**quiero** que el backend cuente mis mensajes de chat del día contra el tope configurado por tenant y
usuario
**para** que Dr. FLIT deje de responder con el LLM de forma predecible cuando alcance ese tope, sin
perder las demás funciones de la plataforma

```gherkin
AC1 — positivo
Dado un usuario que aún no alcanza el tope diario configurado (Anthropic:DrFlitDailyMessageLimit)
Cuando envía un mensaje
Entonces IDrFlitAssistant incrementa el contador de dr_flit.daily_message_usage para
  (tenant_id, user_id, BogotaDays.Today()) y llama al LLM (HU-A1)
Y la respuesta incluye usage.messagesUsedToday y usage.dailyLimit reflejando la configuración vigente

AC2 — negativo
Dado un usuario que ya alcanzó el tope configurado
Cuando envía un mensaje adicional
Entonces IDrFlitAssistant no llama a Anthropic (evita costo innecesario)
Y devuelve status: rate_limited con usage.messagesUsedToday == usage.dailyLimit

AC3 — borde (aislamiento multi-tenant)
Dado un usuario con acceso a más de un tenant
Cuando consume mensajes en el tenant A
Entonces su contador en el tenant B permanece en 0 — el tope se cuenta por
  (tenant_id, user_id, fecha), nunca solo por usuario

AC4 — borde (reinicio de día calendario)
Dado un contador con usage_date de un día calendario anterior en hora Colombia
Cuando el usuario envía un mensaje en el nuevo día
Entonces se crea/usa una fila nueva de usage_date = BogotaDays.Today(), sin arrastrar el conteo
  del día anterior
```

**Notas técnicas:** DDL en `src/Flit.Infrastructure/Persistence/Sql/Ddl/119-epic12718-dr-flit-schema.sql`
(número a confirmar por el database-agent, siguiente libre tras `118-*`) — tabla
`dr_flit.daily_message_usage` conforme a §9.2 del diseño y checklist `db-schema-validator` (A1-A15;
excepción documentada A6: sin soft delete, contador operativo purgable por retención); migración EF
`+.Designer.cs` (`Flit.Infrastructure/Migrations/`);
`Flit.Infrastructure/Persistence/Configurations/DrFlitDailyMessageUsageConfiguration.cs`;
`Flit.Infrastructure/DrFlit/DrFlitUsageCounterRepository.cs` (implementa `IDrFlitUsageCounter`);
`Flit.Infrastructure/DrFlit/DrFlitAssistant.cs` (implementa `IDrFlitAssistant`, compone contador +
`SendChatAsync` de HU-A1 + degradación de §7.1); interfaces en `Flit.DrFlit.Application/Abstractions/`.
Reutiliza `BogotaDays.Today()` (`Flit.Infrastructure/Persistence/Repositories/BogotaDays.cs`). Tests:
`tests/Flit.Infrastructure.Tests/DrFlit/DrFlitUsageCounterRepositoryTests.cs`,
`tests/Flit.DrFlit.Application.Tests/Chat/AskDrFlitHandlerTests.cs` (parcial, sin endpoint aún).

---

### HU-A3 `[FRONTEND] – Dr. FLIT – Generador y prueba de frescura del catálogo del manual`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | Ninguna — bloquea a HU-A4 |

**Como** Backend Agent que construye el contexto del LLM
**quiero** un artefacto JSON generado a partir de `lib/manual/catalog.ts` con todos los artículos del
manual
**para** que el backend tenga el contenido del manual como texto plano sin depender de que el frontend
esté desplegado

```gherkin
AC1 — positivo
Dado el catálogo actual de artículos en lib/manual/articles/*.ts
Cuando se ejecuta pnpm run manual:export (generate-dr-flit-manual-catalog.mjs)
Entonces se genera frontend/public/dr-flit/manual-catalog.generated.json y una copia en
  services/core-api/src/Flit.Api/Content/dr-flit/manual-catalog.generated.json
Y cada artículo conserva slug, título, audiencia, resumen, bloques de texto y sources

AC2 — negativo (guarda de desfase)
Dado que alguien edita un artículo del manual sin regenerar el artefacto
Cuando corre generated-catalog-freshness.test.ts en CI
Entonces el test falla comparando el artefacto commiteado contra una regeneración en memoria

AC3 — borde
Dado un artículo nuevo agregado al catálogo
Cuando se regenera el artefacto
Entonces el nuevo artículo aparece en ambas copias (frontend y backend) sin tocar el script
```

**Notas técnicas:** `frontend/scripts/generate-dr-flit-manual-catalog.mjs` (mismo patrón que
`scripts/brand-color-inventory.mjs`); `frontend/lib/manual/__tests__/generated-catalog-freshness.test.ts`
(estilo `search-coverage.test.ts`); `package.json` (+ script `"manual:export"`); documentar el paso en
`frontend/docs/DR-FLIT-contexto-completo.md` §11 «Añadir un artículo al manual». No modifica
`lib/manual/` (sigue siendo la única fuente de verdad). Esta HU es la única pieza frontend dentro de una
Feature descrita como backend: se mantiene aquí (no en Feature C/D) porque es un prerrequisito directo de
HU-A4/HU-A5 sin valor independiente fuera de esta Feature.

---

### HU-A4 `[BACKEND] – Dr. FLIT – Proveedor de catálogo del manual para el system prompt`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-A3 — bloquea a HU-A5 |

**Como** backend de Dr. FLIT
**quiero** cargar el artefacto de manual generado como el primer bloque del `system` de Anthropic,
marcado como cacheable
**para** que el LLM responda con base en el manual sin tener que consultar al frontend en cada mensaje

```gherkin
AC1 — positivo
Dado el artefacto Content/dr-flit/manual-catalog.generated.json presente en el content root
Cuando arranca el backend
Entonces IDrFlitManualCatalogProvider lo carga en memoria y expone slug/título/href por artículo
  para la validación de citas de HU-A1

AC2 — negativo
Dado que el artefacto no existe o no puede parsearse al arrancar
Cuando el backend intenta iniciar el módulo Dr. FLIT
Entonces el chat completo entra en status: degraded (§7.1) sin tumbar el arranque del resto de la API

AC3 — borde
Dado el bloque system enviado a Anthropic
Cuando se arma la llamada
Entonces el manual va marcado cache_control: {type: "ephemeral"} como primer bloque, seguido de
  las instrucciones de HU-A1 también cacheables
```

**Notas técnicas:** `Flit.Infrastructure/DrFlit/DrFlitManualCatalogProvider.cs` (implementa
`IDrFlitManualCatalogProvider`, `Flit.DrFlit.Application/Abstractions/`); lee
`src/Flit.Api/Content/dr-flit/manual-catalog.generated.json` (artefacto de HU-A3) igual que cualquier
`appsettings*.json` del content root. Deja la puerta abierta a una Opción B futura (consulta al frontend
en runtime) sin cambiar el contrato de la interfaz, si el desfase del artefacto resultara doloroso en
producción (§3 del diseño).

---

### HU-A5 `[BACKEND] – Dr. FLIT – Endpoint POST /api/v1/dr-flit/chat`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | HU-A1, HU-A2, HU-A4 |

**Como** usuario autenticado de FLIT (gestor, ot_admin, admin_company o superadmin)
**quiero** escribir una pregunta o un problema en lenguaje natural en Dr. FLIT
**para** que me responda una duda citando el manual, me indique cuándo no tiene respuesta, o me lleve a
radicar soporte, sin tener que elegir un menú

```gherkin
AC1 — positivo (duda con cita)
Dado un mensaje del usuario que el LLM clasifica como duda con respaldo en el manual
Cuando el backend valida el JSON y los slugs contra el catálogo (HU-A4)
Entonces responde 200 {status:"ok", intent:"duda", reply, citations} con al menos una cita cuyo
  href apunta al artículo/sección del manual

AC2 — positivo (sin respaldo)
Dado un mensaje de tipo duda sin respaldo en el manual
Cuando el LLM lo indica en reply
Entonces el backend responde status:"ok" con citations vacío
Y el mensaje ofrece escalar a soporte, sin inventar una cita

AC3 — negativo (validación de entrada)
Dado un request sin X-Tenant-Id, con message vacío o que excede maxLength
Cuando llega al endpoint
Entonces responde 400 sin invocar al LLM ni al contador de mensajes

AC4 — borde (degradación)
Dado que Anthropic no responde tras el timeout configurado (Anthropic:DrFlitTimeoutSeconds) y
  1 reintento, o la salida no cumple el schema
Cuando el backend construye la respuesta
Entonces responde 200 {status:"degraded", intent:"no_claro", reply:"", usage} — nunca un 5xx
  por esta causa

AC5 — borde (tope alcanzado)
Dado que el usuario ya alcanzó el tope diario configurado
Cuando envía un nuevo mensaje
Entonces responde 200 {status:"rate_limited", ...} con usage.messagesUsedToday == usage.dailyLimit,
  sin costo adicional de LLM
```

**Notas técnicas:** `Flit.Api/Endpoints/DrFlitEndpoints.cs` (`POST /api/v1/dr-flit/chat`,
`RequireAuthorization()` sin policy, `X-Tenant-Id` requerido);
`Flit.DrFlit.Application/Chat/AskDrFlitCommand.cs`/`AskDrFlitHandler.cs`/`DrFlitChatResult.cs`;
`Flit.Api/Program.cs` (+ `AddDrFlit...()`, + `MapDrFlitEndpoints()`);
`contracts/openapi/core-api.v1.yaml` (+ path `/dr-flit/chat` de §5.1). Rate limiting de abuso adicional
(sliding window por `sub` del JWT, `AddRateLimiter` estilo `PublicBrandingRateLimit.cs`) se incluye solo
si Security Agent confirma el alcance antes de esta HU (§6, riesgo #3 del diseño) — de lo contrario queda
como seguimiento anotado, sin bloquear el resto del contrato. Logging sin PII: solo `tenant_id`,
`user_id`, `intent`, `status`, tokens y latencia (§10 del diseño), nunca el texto del mensaje ni de la
respuesta.

---

## Feature B — Caso de soporte a Azure DevOps (3 HUs · 21 SP)

### HU-B1 `[BACKEND] – Dr. FLIT – Cliente de Azure DevOps y mapeo configurable de campos del Bug`

| Campo | Valor |
|---|---|
| SP | 8 |
| Depende | Ninguna — bloquea a HU-B3 |

**Como** backend de Dr. FLIT
**quiero** un cliente HTTP propio contra la REST API de Azure DevOps que cree Bugs en el proyecto de
soporte con los campos mapeados según configuración
**para** poder radicar casos de soporte sin depender de ningún SDK adicional ni de valores de mapeo
fijos en el código

```gherkin
AC1 — positivo (creación con mapeo configurado)
Dado un caso de soporte con Prioridad, Frecuencia y ambiente ya resueltos
Cuando AzureDevOpsSupportCaseClient arma el JSON Patch del Bug
Entonces mapea Prioridad→Custom.Primacy/Severity, Frecuencia→Custom.Incidence y
  ambiente→Custom.Environment usando los valores que resulten de DrFlitFieldMappingOptions
  (configurable), no constantes embebidas en el cliente
Y el título queda como "[ DR. FLIT ] {título}" y Microsoft.VSTS.TCM.ReproSteps sigue el formato
  de campos del formulario web vigente
Y el work item se crea sin asignar (AssignedTo no se envía)

AC2 — negativo (proyecto con espacio en el nombre)
Dado que el proyecto de destino es "FLIT - SOPORTE" (contiene espacios)
Cuando el cliente arma la URL de la REST API
Entonces el path queda correctamente URL-encoded y la llamada no falla por un 404 de ruta mal formada

AC3 — borde (adjuntos no bloqueantes)
Dado uno o más adjuntos que fallan al subirse a Azure DevOps
Cuando se crea el Bug
Entonces el cliente excluye esos adjuntos, cuenta cuántos fallaron y crea el work item igual con
  los que sí subieron

AC4 — borde (Custom.AffectedModule con default)
Dado que el cliente no envía affectedModule o envía un valor fuera del allow-list configurado
Cuando se construye el campo
Entonces se usa el default configurado en DrFlitFieldMappingOptions, nunca un valor fijo del código
```

**Notas técnicas:** `Flit.Infrastructure/DrFlit/AzureDevOpsSupportCaseClient.cs` (implementa
`IDrFlitSupportCaseGateway`, único cliente HTTP genuinamente nuevo — sin
`Microsoft.TeamFoundationServer.Client` ni SDK adicional, REST cruda + `HttpClient` typed, mismo patrón
Verifik/Kyverum); `Flit.Infrastructure/DrFlit/AzureDevOpsOptions.cs` (PAT, `BaseUrl`, `TimeoutSeconds`
default 15s, 1 reintento ante fallo de transporte); `Flit.Infrastructure/DrFlit/DrFlitFieldMappingOptions.cs`
(mapeo Environment/Incidence/Primacy/Severity/AffectedModule + allow-list + default de
`AffectedModule` — todo configurable, nunca hardcodeado en el cliente); secreto PAT vía env var cruda,
nunca en el repo ni en logs. Tests:
`tests/Flit.Infrastructure.Tests/DrFlit/AzureDevOpsSupportCaseClientTests.cs` (mock HTTP, incluye el
caso de URL-encoding del riesgo #5 del diseño). Sin endpoint HTTP propio todavía — lo consume HU-B3.

---

### HU-B2 `[BACKEND] – Dr. FLIT – Tabla de casos de soporte y subida previa de adjuntos`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | Ninguna — bloquea a HU-B3 |

**Como** usuario que va a radicar un caso de soporte
**quiero** poder subir mis adjuntos antes de confirmar el caso
**para** no perderlos si el formulario tarda en completarse, y que el backend tenga dónde registrar el
resultado de cada intento de creación

```gherkin
AC1 — positivo
Dado un archivo con un MIME permitido por la configuración (por defecto image/png, image/jpeg,
  image/webp, application/pdf, text/plain) dentro del tamaño máximo configurado
Cuando el usuario lo sube vía POST /api/v1/dr-flit/support-cases/attachments
Entonces responde 201 {id, filename, sizeBytes}
Y el archivo queda en almacenamiento temporal con prefijo dr-flit-support/ y expiración de 24 h
  si nunca se confirma en un caso

AC2 — negativo
Dado un archivo sin extensión permitida, o que excede el tamaño máximo configurado, o una subida
  sin archivo
Cuando llega al endpoint
Entonces responde 400 y no persiste nada en dr_flit.support_cases

AC3 — borde (límite de cantidad)
Dado que el usuario ya subió el número máximo de adjuntos configurado (DrFlit:SupportCase:*,
  default 5) para el caso que está armando
Cuando intenta subir uno más desde el frontend
Entonces el backend sigue aceptando la subida individual (el límite de cantidad lo aplica el
  formulario, HU-D1) pero el conteo final se revalida al confirmar el caso (HU-B3)
```

**Notas técnicas:** DDL en el siguiente número libre de `Flit.Infrastructure/Persistence/Sql/Ddl/` tras
el asignado por HU-A2 — tabla `dr_flit.support_cases` conforme a §9.2 del diseño y checklist
`db-schema-validator` (A1-A16 completos: sí requiere soft delete, RLS, triggers `row_version`/
`audit_log`, comentarios `@pii:*` en `requester_name/email/phone/company` y `problem_detail`); migración
EF `+.Designer.cs`; `Flit.Infrastructure/Persistence/Configurations/DrFlitSupportCaseConfiguration.cs`.
Endpoint `POST /api/v1/dr-flit/support-cases/attachments` en `Flit.Api/Endpoints/DrFlitEndpoints.cs`
(multipart, reutiliza `IAttachmentStorage` ya usado por trámites); límites en `DrFlit:SupportCase:*`
(máx. adjuntos, tamaño máx., MIME permitidos) — todos configurables, los valores del diseño son el
default, no un tope fijo en código.
`Flit.DrFlit.Application/SupportCases/UploadSupportAttachmentCommand.cs`/`Handler.cs`.

---

### HU-B3 `[BACKEND] – Dr. FLIT – Endpoint POST /support-cases: creación end-to-end del Bug`

| Campo | Valor |
|---|---|
| SP | 8 |
| Depende | HU-B1, HU-B2 |

**Como** usuario que confirma su caso de soporte en el chat
**quiero** que al confirmar se cree el caso en Azure DevOps y me confirmen el número
**para** tener trazabilidad de mi reporte sin tener que salir de la plataforma

```gherkin
AC1 — positivo (camino feliz)
Dado un formulario confirmado con adjuntos ya subidos (HU-B2)
Cuando el usuario confirma
Entonces el backend persiste la fila dr_flit.support_cases en status='pending', sube los adjuntos
  a Azure DevOps, crea el Bug (HU-B1) con Custom.Environment resuelto desde
  DR_FLIT_DEPLOY_ENVIRONMENT (nunca desde ASPNETCORE_ENVIRONMENT), actualiza la fila a
  status='created' con el ado_work_item_id, y responde 201 {caseId}

AC2 — negativo (ADO caído)
Dado que la creación del Bug falla tras 1 reintento (5xx/timeout de Azure DevOps)
Cuando el backend procesa la respuesta
Entonces la fila queda en status='failed' con last_error (sin PII)
Y el endpoint responde 502 ofreciendo el canal de soporte estático como salida de emergencia

AC3 — borde (adjuntos parcialmente fallidos)
Dado que 1 de 3 adjuntos falla al subirse a Azure DevOps
Cuando se crea el Bug
Entonces el caso se crea igual con los 2 adjuntos que sí subieron y attachmentsFailed=1 en la
  respuesta

AC4 — borde (caseUrl restringido)
Dado un usuario sin rol SuperAdmin que recibe la confirmación
Cuando se arma la respuesta 201
Entonces caseUrl es null; solo un caller con rol SuperAdmin recibe la URL directa al work item

AC5 — borde (Custom.Environment con default fail-safe)
Dado que DR_FLIT_DEPLOY_ENVIRONMENT no está configurado en el .env del VPS
Cuando se crea el Bug
Entonces Custom.Environment toma el default DEV (el ambiente menos sensible), nunca PDN por omisión
```

**Notas técnicas:** `Flit.Api/Endpoints/DrFlitEndpoints.cs` (`POST /api/v1/dr-flit/support-cases`);
`Flit.DrFlit.Application/SupportCases/CreateSupportCaseCommand.cs`/`CreateSupportCaseHandler.cs`/
`CreateSupportCaseResult.cs` (orquesta persistir-pending → subir adjuntos → crear Bug → actualizar fila,
§7.2 del diseño); variable `DrFlit__DeployEnvironment` (env `DR_FLIT_DEPLOY_ENVIRONMENT`, default `DEV`);
`caseUrl` solo si `httpContext.User` tiene el claim de rol SuperAdmin. **Fuera de alcance explícito de
esta HU** (decisión de alcance del diseño §7.2): no hay worker de reconciliación en segundo plano para
filas `status='failed'`; el usuario reintenta manualmente desde la UI (HU-D2). Tests:
`tests/Flit.DrFlit.Application.Tests/SupportCases/CreateSupportCaseHandlerTests.cs`.

---

## Feature C — Chat conversacional en frontend (3 HUs · 14 SP)

### HU-C1 `[FRONTEND] – Dr. FLIT – Cliente de chat, fases del LLM y fallback local`

| Campo | Valor |
|---|---|
| SP | 8 |
| Depende | HU-A5 (Feature A) — bloquea a HU-C2, HU-C3, HU-D1 |

**Como** usuario de FLIT que escribe una pregunta libre en Dr. FLIT
**quiero** que el chat entienda mi pregunta sin que tenga que elegir un menú, y que siga respondiendo
aunque el LLM falle
**para** resolver mis dudas con la misma fiabilidad que el buscador de siempre

```gherkin
AC1 — positivo (duda con cita)
Dado que escribo texto libre en la fase idle
Cuando el mensaje llega a POST /dr-flit/chat y la respuesta es status:"ok", intent:"duda"
Entonces la máquina de estados entra en showing_chat_reply y muestra reply + tarjetas de cita
  con enlace al manual/sección

AC2 — negativo (LLM caído)
Dado que POST /dr-flit/chat responde status:"degraded" (timeout, error de transporte o salida
  inválida)
Cuando el frontend recibe la respuesta
Entonces ejecuta searchManualArticles(text) localmente (fallback determinista ya existente) y
  entra en showing_help con una nota discreta de "respuesta rápida del manual"
Y las tres opciones del menú (Gestión/Ayuda/Soporte) siguen operativas sin backend

AC3 — borde (historial acotado)
Dado una conversación de más de 12 turnos antes de "Terminar chat"
Cuando se arma el payload de POST /dr-flit/chat
Entonces history se acota a los últimos 12 turnos (según el maxItems del contrato), sin hacer
  crecer indefinidamente el payload ni sessionStorage

AC4 — borde (persistencia de sesión)
Dado que el usuario navega entre módulos de la plataforma sin cerrar el chat
Cuando vuelve a abrir Dr. FLIT
Entonces la conversación sigue en sessionStorage; solo la acción explícita "Terminar chat" la borra
```

**Notas técnicas:** `frontend/lib/api/dr-flit-client.ts` (fetch a `/dr-flit/chat`);
`components/dr-flit/dr-flit-chat-types.ts` (`DrFlitChatResponse`, `DrFlitCitation`);
`components/dr-flit/dr-flit-conversation.ts` (+ fases `chat_loading`/`showing_chat_reply` de §11 del
diseño, + `applyChatSend`/`applyChatSuccess`/`applyChatDegraded`, sin tocar las fases existentes de
Gestión/Ayuda); `components/dr-flit/useDrFlitChat.ts` (+ efecto que llama al cliente en `chat_loading`);
reutiliza `searchManualArticles` y `DR_FLIT_FREE_TEXT_HINT` ya existentes como piso heurístico de
intención en degradado. Tests: `components/dr-flit/__tests__/dr-flit-chat-flow.test.ts`. No rompe
`DrFlitAssistant.test.tsx` (regla vigente del plan v3): se extiende, no se reescribe.

---

### HU-C2 `[FRONTEND] – Dr. FLIT – Enrutar intención "gestión" y manejar "no claro"`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-C1 |

**Como** usuario que le pide a Dr. FLIT buscar un trámite, placa, VIN o cliente en lenguaje natural
**quiero** que me lleve directo a la sesión Gestión existente
**para** no tener que repetir la búsqueda eligiendo el menú manualmente

```gherkin
AC1 — positivo
Dado que POST /dr-flit/chat responde intent:"gestion" con suggestGestionIntent
  (placa/vin/tramite/cliente)
Cuando el frontend procesa la respuesta
Entonces reutiliza applySelectIntent(suggestGestionIntent) — la sesión Gestión existente se
  conserva intacta, sin fase nueva

AC2 — negativo (sin sugerencia)
Dado intent:"gestion" con suggestGestionIntent: null
Cuando el frontend procesa la respuesta
Entonces cae al comportamiento por defecto de Gestión ya existente (el usuario elige el tipo de
  búsqueda), sin error visible

AC3 — borde (no claro)
Dado intent:"no_claro"
Cuando el frontend procesa la respuesta
Entonces muestra la pregunta de seguimiento del modelo (reply) como mensaje del bot, en un estado
  equivalente a awaiting_help_query ya existente, y permite seguir escribiendo texto libre
```

**Notas técnicas:** `components/dr-flit/dr-flit-conversation.ts` (bifurcación por `intent` dentro del
manejo de la respuesta de `chat_loading`, reutiliza `applySelectIntent` ya existente);
`components/dr-flit/dr-flit-intents.ts` (copys nuevos sin quitar los canales de soporte existentes). No
requiere fase nueva en el tipo `DrFlitPhase`.

---

### HU-C3 `[FRONTEND] – Dr. FLIT – Aviso de tope diario y estado rate_limited`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-C1 |

**Como** usuario que se acerca o alcanza el tope diario de mensajes
**quiero** que Dr. FLIT me avise con un mensaje amigable y me siga ofreciendo las tres opciones del menú
**para** no quedarme sin poder usar el asistente aunque se agote el chat con LLM por hoy

```gherkin
AC1 — positivo (aviso al acercarse)
Dado usage.messagesUsedToday cercano a usage.dailyLimit (ambos valores vienen del backend, sin
  umbral fijo en el frontend salvo un margen configurable de UI)
Cuando el frontend recibe una respuesta status:"ok"
Entonces muestra un aviso discreto con el conteo actual sin interrumpir la conversación

AC2 — negativo (tope alcanzado)
Dado status:"rate_limited" con usage.messagesUsedToday == usage.dailyLimit
Cuando el frontend procesa la respuesta
Entonces muestra el mensaje amigable del backend y vuelve a idle con el menú
  (Gestión/Ayuda/Soporte) visible y 100% funcional sin LLM

AC3 — borde (reinicio de día)
Dado que el usuario vuelve a escribir al día calendario siguiente (hora Colombia)
Cuando envía un nuevo mensaje
Entonces el chat vuelve a aceptar mensajes con LLM sin acción manual del usuario, reflejando el
  usage que ya reinició en el backend (HU-A2)
```

**Notas técnicas:** `components/dr-flit/DrFlitChatPanel.tsx` (renderiza el aviso de `usage` y la fase
`rate_limited`); `components/dr-flit/dr-flit-conversation.ts` (manejo de `status:"rate_limited"` →
vuelve a `idle`); el margen de "cercano al tope" (si se implementa) vive en una constante de UI
documentada como ajustable, no en el contrato del backend.

---

## Feature D — Recopilación y confirmación de casos en frontend (3 HUs · 15 SP)

### HU-D1 `[FRONTEND] – Dr. FLIT – Formulario de caso de soporte con prellenado y adjuntos`

| Campo | Valor |
|---|---|
| SP | 8 |
| Depende | HU-C1 (Feature C, entrada por intent=soporte), HU-B2 (Feature B, subida de adjuntos) — bloquea a HU-D2 |

**Como** usuario que reporta un error o pide ayuda humana a Dr. FLIT
**quiero** completar un formulario con mis datos ya prellenados y poder adjuntar archivos
**para** no tener que volver a escribir lo que la plataforma ya sabe de mí y poder ilustrar mi problema

```gherkin
AC1 — positivo (prellenado)
Dado que intent:"soporte" abre la fase collecting_support_case
Cuando se monta DrFlitSupportCaseForm.tsx
Entonces Nombre, Email y Compañía quedan prellenados desde currentUser/JWT (Shell.tsx,
  tenant_name) y Fecha se fija automáticamente
Y el usuario completa Detalle, Resultado esperado, Frecuencia, Título y Prioridad

AC2 — negativo (campos requeridos)
Dado que el usuario intenta continuar sin Detalle, Resultado esperado, Título o Prioridad
Cuando pulsa continuar
Entonces el formulario bloquea el avance y marca los campos faltantes, sin llamar a ningún
  endpoint

AC3 — borde (adjuntos)
Dado que el usuario responde "Sí" a adjuntos y selecciona archivos dentro del límite configurado
  (cantidad y tamaño de DrFlit:SupportCase:*, HU-B2)
Cuando cada archivo se sube vía POST /support-cases/attachments
Entonces el formulario guarda solo los id devueltos (nunca el binario ni base64) en el estado
  de sessionStorage

AC4 — borde (archivo rechazado)
Dado un archivo que excede el tamaño máximo configurado o un MIME no permitido
Cuando el usuario intenta adjuntarlo
Entonces el formulario lo rechaza localmente con un mensaje claro, sin esperar la respuesta 400
  del backend para dar el aviso
```

**Notas técnicas:** `components/dr-flit/DrFlitSupportCaseForm.tsx` (nuevo);
`components/dr-flit/dr-flit-chat-types.ts` (+ `DrFlitSupportCaseDraft`);
`components/dr-flit/dr-flit-conversation.ts` (+ `applyOpenSupportCase`); reutiliza
`lib/api/dr-flit-client.ts` (HU-C1) para el multipart de adjuntos. Prellenado desde `currentUser` de
`Shell.tsx`. Tests: `components/dr-flit/__tests__/DrFlitSupportCaseForm.test.tsx`.

---

### HU-D2 `[FRONTEND] – Dr. FLIT – Confirmación explícita, radicación y manejo de error`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | HU-D1, HU-B3 (Feature B) |

**Como** usuario que ya completó el formulario de soporte
**quiero** revisar un resumen y confirmar explícitamente antes de que se cree el caso
**para** tener control total sobre qué se envía a soporte, sin que Dr. FLIT lo haga por su cuenta

```gherkin
AC1 — positivo
Dado el resumen del formulario en DrFlitSupportCaseConfirm.tsx
Cuando el usuario pulsa "Confirmar y radicar caso"
Entonces recién ahí se llama POST /dr-flit/support-cases — ninguna respuesta previa del LLM puede
  disparar esta llamada
Y al recibir 201 {caseId} se muestra DrFlitSupportCaseCreated.tsx con
  "Tu caso #{caseId} quedó radicado" sin enlace

AC2 — positivo (enlace SuperAdmin)
Dado un usuario con rol SuperAdmin
Cuando el backend devuelve caseUrl
Entonces DrFlitSupportCaseCreated.tsx muestra el enlace directo; para el resto de roles no se
  muestra ningún enlace

AC3 — negativo (ADO caído)
Dado que POST /support-cases responde 502
Cuando el frontend procesa la respuesta
Entonces entra en support_case_error ofreciendo el canal de soporte estático (correo/teléfono de
  DrFlitSupportPanel) como salida de emergencia, sin descartar el formulario — el usuario puede
  reintentar sin volver a escribir todo

AC4 — borde (adjuntos parcialmente fallidos)
Dado attachmentsFailed > 0 en una respuesta 201
Cuando se muestra la confirmación
Entonces se informa cuántos adjuntos no se pudieron incluir, sin que eso impida mostrar el
  caseId creado
```

**Notas técnicas:** `components/dr-flit/DrFlitSupportCaseConfirm.tsx`,
`components/dr-flit/DrFlitSupportCaseCreated.tsx` (nuevos); `components/dr-flit/dr-flit-conversation.ts`
(+ fases `confirming_support_case`/`submitting_support_case`/`support_case_created`/
`support_case_error` de §11 del diseño, + `applyConfirmSupportCase`/`applySupportCaseCreated`/
`applySupportCaseError`); `DrFlitSupportPanel.tsx` (el botón "Generar un caso de soporte" abre
`collecting_support_case` en vez de solo enlazar la URL externa; el enlace externo se conserva como
alternativa visible). El guardarraíl de "confirmación explícita en UI" es el mismo mecanismo que
documenta §8.2 punto 3 del diseño contra *prompt injection*.

---

### HU-D3 `[FRONTEND] – Dr. FLIT – Aviso de Habeas Data al abrir el chat`

| Campo | Valor |
|---|---|
| SP | 2 |
| Depende | Ninguna |

**Como** usuario que abre Dr. FLIT por primera vez en la sesión
**quiero** ver un aviso claro de tratamiento de datos
**para** saber qué pasa con la información que comparto antes de escribir cualquier mensaje

```gherkin
AC1 — positivo
Dado que el usuario abre el panel de Dr. FLIT
Cuando se monta el componente
Entonces se muestra un aviso no bloqueante (banner o primer mensaje del bot) de Habeas Data, sin
  impedir el uso del chat

AC2 — negativo (no bloqueante)
Dado que el usuario no interactúa con el aviso
Cuando escribe su primer mensaje
Entonces el chat funciona con normalidad — el aviso no es un consentimiento auditable que gatee
  la conversación (decisión de alcance explícita, revisable si Legal lo exige más adelante)

AC3 — borde (persistencia dentro de la sesión)
Dado que el usuario ya vio el aviso y sigue navegando la plataforma sin "Terminar chat"
Cuando vuelve a abrir el panel
Entonces el aviso no se repite de forma intrusiva en cada apertura dentro de la misma sesión
```

**Notas técnicas:** componente fijo o primer mensaje del bot en
`components/dr-flit/DrFlitChatPanel.tsx`; no persiste consentimiento en BD (a diferencia de
`TermsAcceptanceEndpoints`/`terms-acceptances`) — si Legal exige trazabilidad auditable, ese patrón ya
existe y se reutiliza sin rediseño (§10 del diseño). No depende de otras HUs de esta Feature: es visible
desde que se abre el chat, independientemente del flujo de soporte.

---

## Orden de implementación

```
A1 → A2 → A5
A3 → A4 → A5

B1 → B3
B2 → B3

C1 → C2, C3

D1 → D2
D3 (independiente)
```

Entre Features: A no depende de B, C ni D. B no depende funcionalmente de A (la creación del caso nunca
pasa por el LLM), pero se secuencia después por orden de entrega. C depende de A5 (endpoint real de
chat). D depende de B2/B3 (endpoints reales de soporte) y, para el punto de entrada conversacional, de
C1 (fase `soporte` del chat) — salvo D3, independiente de todo lo demás.

## DoR de las HUs (al crear)

PASS previsto: título `[FRONTEND]`/`[BACKEND]` con guion largo, Como/quiero/para, ≥3 AC Gherkin
(positivo/negativo/borde, más si el criterio lo exige — guardarraíles, fallback, tope, adjuntos), SP
Fibonacci, `Custom.Refinement="True"`, dependencias explícitas (incluidas las cruzadas entre Features),
AssignedTo humano, tag `DOR`, sin TODO/TBD, sin datos sensibles.
Sprint: siguiente al activo (a definir por la sesión principal al registrar).
Parent Feature en `New` (no Active): la HU se crea igual; no se activa implementación sin confirmación
humana explícita (Motivo A del ciclo de vida HU).
