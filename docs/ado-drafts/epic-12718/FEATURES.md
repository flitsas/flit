# Features bajo la épica #12718 — borradores (no registrados en ADO)

> **Estado:** borrador local. Espera aprobación explícita antes de registrar en Azure DevOps
> (`feature-creator` / tech-lead Modo A). La creación real la ejecuta la sesión principal.
> **Padre:** Epic #12718 — proyecto **FLIT - EVOLUTION**, organización `FlitDevOps`.
> **Título épica:** "DR. FLIT - Chat de soporte y consulta de manuales".
> **Proyecto de destino de los casos de soporte:** **FLIT - SOPORTE** (distinto del proyecto de la épica).
> **AssignedTo propuesto:** Willyn Londoño Calle (`willyn.londono@flitsas.com`).
> **Sprint:** no se fija aquí — lo decide la sesión principal (regla: sprint **siguiente** al activo, nunca el activo). Tag `DOR` obligatorio antes de Active.
> **Área:** FLIT - EVOLUTION.
> **Diseño técnico:** `docs/ado-drafts/epic-12718/DISENO-TECNICO.md` (fuente obligatoria, referenciada por
> sección en cada Feature/HU). **ADR:** `services/core-api/docs/adr/ADR-0060-dr-flit-llm-backend-y-escalacion-soporte-ado.md`
> (`Propuesto`).
> **Todo lo marcado "revisable" en el diseño técnico está sujeto a cambio sin reescribir Features ni HUs**
> — las HUs de este borrador referencian claves de configuración (`Anthropic:DrFlit*`,
> `DrFlitFieldMappingOptions`, `DrFlit:SupportCase:*`), no valores fijos, salvo los defaults del diseño
> y los enums literales que sí fija la épica (Alta/Media/Baja, una_vez/a_veces/siempre).

## Decisiones humanas ya cerradas (no se re-preguntan)

- **LLM:** `claude-haiku-4-5` desde core-api, vía `AnthropicMessagesClient`/`AnthropicOptions`
  existentes (claves `DrFlit*`, fallback env `ANTHROPIC_DRFLIT_*`). Modelo y tope diario de mensajes
  **configurables** por ambiente — no se hardcodean en las HUs.
- **Fallback:** si el LLM falla, excede el tope o su salida no cumple el contrato, se degrada al
  buscador determinista actual (`searchManualArticles`) — nunca se pierde ninguna de las tres funciones
  básicas de Dr. FLIT (Gestión, Ayuda por menú, Soporte).
- **Intenciones:** `duda` · `soporte` · `gestion` (enruta a la sesión Gestión existente, que se
  conserva intacta) · `no_claro` (pregunta de seguimiento).
- **Casos de soporte:** Bug en proyecto ADO **FLIT - SOPORTE**, título `[ DR. FLIT ] {título}`,
  `Microsoft.VSTS.TCM.ReproSteps` con el formato del formulario web vigente, `Custom.Environment` desde
  `DR_FLIT_DEPLOY_ENVIRONMENT` (nunca `ASPNETCORE_ENVIRONMENT`, que es `Development` en los tres
  ambientes), Prioridad Alta/Media/Baja → `Custom.Primacy` 1/2/3 y `Severity` 2/3/4, Frecuencia
  una_vez/a_veces/siempre → `Custom.Incidence` 1/2/3+, `Custom.AffectedModule` inferido con default
  configurable, `Custom.TypeBug` = "Sin Definir", **sin asignar** (mismo comportamiento ya vigente del
  formulario web de soporte — ver aclaración de la regla 4 en ADR-0060).
- **Identidad técnica:** cuenta de servicio nueva con PAT de alcance mínimo (Work Items R/W en
  `FLIT - SOPORTE`), secreto por ambiente, solo en backend.
- **Confirmación al usuario:** `"Tu caso #N quedó radicado"` sin enlace; el enlace directo al work item
  solo se muestra a rol SuperAdmin.
- **Habeas Data:** aviso no bloqueante al abrir el chat; los datos de contacto (nombre/teléfono/correo)
  se capturan en el formulario del chat y van directo a ADO, **nunca** pasan por el LLM (mitigación de
  *prompt injection* y de fuga de PII a la vez, §8.2 del diseño).
- **Feature E — override del modelo desde consola Super Admin: DESCARTADA para este plan.** El Nivel 1
  (`appsettings`/env, §6.1 del diseño) es la base obligatoria y suficiente para lanzar la Epic; el
  Nivel 2 queda documentado en ADR-0060 como alternativa futura, sin HUs en este borrador.

## Supuestos tomados (a validar por el humano)

- **HU-A3 del diseño técnico (§15) se partió en dos HUs** (`HU-A3` frontend + `HU-A4` backend) para
  respetar la regla FLIT de HUs separadas por capa (`[FRONTEND]`/`[BACKEND]`) — el diseño la agrupaba en
  una sola por ser un mismo hilo de trabajo (generador del manual + proveedor que lo consume). No cambia
  el alcance, solo la unidad de PR.
- **No se creó una HU de schema independiente.** `dr_flit.daily_message_usage` va dentro de HU-A2 y
  `dr_flit.support_cases` dentro de HU-B2 porque cada tabla tiene un único consumidor dentro de la misma
  Feature (no hay un tercer módulo que dependa de ambas) y el DDL+EF+configuración de cada una cabe
  holgadamente en el presupuesto de PR de su HU — separar el schema habría creado una dependencia
  artificial de una sola tabla sin aportar paralelismo real.
- El margen de UI para "avisar cuando el usuario se acerca al tope" (HU-C3) se trata como una constante
  de frontend ajustable, no como parte del contrato `usage` del backend (que solo expone
  `messagesUsedToday`/`dailyLimit`).
- Feature B (backend de soporte a ADO) se describe como **no dependiente** de Feature A: la creación del
  caso nunca pasa por el LLM (decisión de diseño explícita), así que ambas Features backend pueden
  avanzar en paralelo si el equipo lo decide, aunque el orden sugerido más abajo las secuencia.
- El número de DDL (`119-epic12718-dr-flit-schema.sql` en el diseño) es un valor de referencia — el
  `database-agent` confirma el siguiente número libre al implementar (§16 del diseño).

## Prerrequisitos externos

Provisión de Infra Agent / equipo humano, **antes** de activar las HUs que los consumen (no bloquean la
creación de las HUs en ADO, sí bloquean su paso a `Active`/implementación real):

- **Cuenta de servicio de Azure DevOps** con PAT de alcance mínimo (Work Items R/W, proyecto
  `FLIT - SOPORTE`), un secreto distinto por ambiente (DEV/QA/PDN), inyectado como env var cruda en el
  `.env` de cada VPS — nunca en el repo, nunca en logs (bloquea HU-B1/HU-B3).
- **`ANTHROPIC_DRFLIT_MODEL` / `ANTHROPIC_DRFLIT_MAX_TOKENS` / `ANTHROPIC_DRFLIT_TIMEOUT_SECONDS` /
  `ANTHROPIC_DRFLIT_DAILY_MESSAGE_LIMIT` / `ANTHROPIC_DRFLIT_ENABLED`** en el `.env` de cada VPS
  (opcionales: si se omiten, `AnthropicOptions` cae a los defaults de §6.1 del diseño).
- **`DR_FLIT_DEPLOY_ENVIRONMENT=DEV|QA|PDN`** en el `.env` de cada VPS (default `DEV`, fail-safe) —
  obligatorio porque los tres ambientes comparten `ASPNETCORE_ENVIRONMENT=Development` (§9.3 del diseño,
  bloquea que `Custom.Environment` del caso de soporte sea correcto en QA/PDN).
- Confirmar el costo real vigente en anthropic.com/pricing antes de fijar el tope diario por defecto en
  producción (los números de §4 del diseño son estimados, no comprometidos).
- Confirmación de **Security Agent** sobre el alcance del rate limiting de abuso adicional (§6, riesgo
  #3 del diseño) antes de cerrar el contrato final de HU-A5 — si no se confirma a tiempo, HU-A5 se
  entrega sin ese control adicional y queda como seguimiento, no bloquea el resto del plan.

## Decisiones revisables (sujeto a cambio sin reescribir HUs)

- Modelo Anthropic (`claude-haiku-4-5`) y sus topes (`DrFlitMaxTokens`, `DrFlitTimeoutSeconds`,
  `DrFlitDailyMessageLimit`) — viven en `Anthropic:DrFlit*`/env; las HUs los referencian como "el tope
  configurado", nunca como un número fijo en el AC.
- El mapeo de campos del Bug (`Custom.AffectedModule` y su default, `Custom.Incidence`,
  `Custom.Primacy`/`Severity`, allow-list de módulos) vive en `DrFlitFieldMappingOptions`, configurable.
- Límites de adjuntos (cantidad, tamaño, MIME permitidos) viven en `DrFlit:SupportCase:*`, configurable.
- Rate limiting de abuso (*sliding window*) — alcance a confirmar por Security Agent (ver Prerrequisitos).
- El contenido del prompt del sistema (§8.1 del diseño) es contenido de producto, revisable sin tocar el
  contrato técnico del endpoint `/chat`.
- Nivel 2 (override de modelo en caliente desde consola Super Admin) — descartado explícitamente de este
  plan (Feature E); puede reabrirse como Feature nueva sin tocar A-D si el Líder Técnico/PO lo priorizan.
- Consentimiento auditable de Habeas Data más allá del aviso no bloqueante — fuera de alcance de HU-D3;
  si Legal lo exige, el patrón (`TermsAcceptanceEndpoints`) ya existe y se reutiliza sin rediseño.

---

## Feature A

Título: `[DR.FLIT] - Fundaciones de chat con LLM y manual en backend`

# OBJETIVO

Dotar a core-api de la capacidad de clasificar la intención de un mensaje libre y responder dudas
citando el manual, reutilizando la integración Anthropic ya existente, con tope diario configurable por
usuario/tenant y degradación segura cuando el LLM no está disponible o alucina.

# DESCRIPTION

Nuevo bounded context `Flit.DrFlit.*` (ADR-0060, Opción 1). Reutiliza `AnthropicOptions`
(`Flit.Infrastructure/Ocr/AnthropicOptions.cs`), que gana los campos `DrFlitModel`/`DrFlitMaxTokens`/
`DrFlitTimeoutSeconds`/`DrFlitDailyMessageLimit`/`DrFlitEnabled` (misma sección `Anthropic`, fallback
`ANTHROPIC_DRFLIT_*`), y `AnthropicMessagesClient`, que gana un método de chat de texto sin streaming
(`SendChatAsync`). El manual (`frontend/lib/manual/`) llega al backend como artefacto JSON generado
(`manual-catalog.generated.json`, Opción A del diseño §3), con una prueba de frescura que evita el
desfase entre `lib/manual/articles/*.ts` y lo que consume el LLM. El tope diario de mensajes se cuenta en
Postgres (`dr_flit.daily_message_usage`, por `tenant_id`+`user_id`+día calendario en hora Colombia vía
`BogotaDays.Today()`) para que sea real incluso con el backend detrás de un balanceador — un contador en
el cliente no sirve (se resetea al recargar). El endpoint `POST /api/v1/dr-flit/chat` (§5.1 del diseño)
clasifica cada mensaje en `duda`/`soporte`/`gestion`/`no_claro`, cita el manual con enlace cuando responde
una duda con respaldo, y se degrada de forma segura (`status: degraded`/`rate_limited`, nunca 5xx por
causa de negocio). Los guardarraíles contra *prompt injection* (§8.2 del diseño) —sin tool-use, validación
server-side de cada slug citado contra el catálogo cargado, la creación de casos de soporte nunca
disparada por el LLM— se implementan desde la base, no como parche posterior.

Fuentes de código: `services/core-api/src/Flit.Infrastructure/Ocr/AnthropicOptions.cs`,
`services/core-api/src/Flit.Infrastructure/Ocr/AnthropicMessagesClient.cs`,
`services/core-api/src/Flit.Infrastructure/InfrastructureExtensions.cs`,
`services/core-api/src/Flit.DrFlit.Application/` (nuevo),
`services/core-api/src/Flit.Infrastructure/DrFlit/` (nuevo),
`services/core-api/src/Flit.Api/Endpoints/DrFlitEndpoints.cs` (nuevo),
`services/core-api/src/Flit.Api/Content/dr-flit/manual-catalog.generated.json` (nuevo),
`frontend/scripts/generate-dr-flit-manual-catalog.mjs` (nuevo),
`frontend/lib/manual/__tests__/generated-catalog-freshness.test.ts` (nuevo).

# CRITERIOS FUNCIONALES

- [ ] El backend clasifica cada mensaje libre del usuario en `duda`/`soporte`/`gestion`/`no_claro` según
      el contrato `POST /api/v1/dr-flit/chat`.
- [ ] Las respuestas de tipo `duda` citan el manual (artículo/sección) con enlace directo, usando solo
      slugs validados contra el catálogo cargado — nunca una cita inventada por el modelo.
- [ ] Cuando no hay respaldo en el manual para una duda, la respuesta lo indica explícitamente y ofrece
      escalar a soporte.
- [ ] Existe un tope diario de mensajes por usuario y tenant, configurable por ambiente
      (`Anthropic:DrFlitDailyMessageLimit`), que al alcanzarse responde `status: rate_limited` sin
      bloquear el resto de la plataforma.
- [ ] Si Anthropic no responde o su salida no cumple el contrato, el endpoint responde `status: degraded`
      (nunca 5xx por esta causa), dejando el fallback determinista al frontend.
- [ ] Ningún texto del usuario ni del modelo se registra en los logs de aplicación (§10 del diseño).

---

## Feature B

Título: `[DR.FLIT] - Radicación de casos de soporte en Azure DevOps`

# OBJETIVO

Que confirmar un caso de soporte desde Dr. FLIT lo radique automáticamente como Bug en el proyecto ADO
**FLIT - SOPORTE**, sin que la creación dependa nunca del LLM, con mapeo de campos configurable y una
salida de emergencia si Azure DevOps no responde.

# DESCRIPTION

Integración nueva y aislada con Azure DevOps: la creación del caso siempre es un formulario estructurado
que habla directo con el backend (§7.2/§8.2 del diseño) — ninguna respuesta del modelo puede dispararla.
`AzureDevOpsSupportCaseClient` es el único cliente HTTP genuinamente nuevo del backend (REST cruda +
`HttpClient` typed, mismo patrón que Verifik/Kyverum, sin SDK adicional de ADO) y usa una cuenta de
servicio con PAT de alcance mínimo (Work Items R/W en `FLIT - SOPORTE`), secreto por ambiente. El mapeo
de campos del Bug (`Custom.Environment`, `Custom.Incidence`, `Custom.Primacy`/`Severity`,
`Custom.AffectedModule`) vive en `DrFlitFieldMappingOptions`, configurable, no fijo en el código.
`dr_flit.support_cases` registra cada intento (`status = pending/created/failed`) para auditoría y
reconciliación manual. Los adjuntos se suben antes de confirmar (`POST /support-cases/attachments`,
reutiliza `IAttachmentStorage` ya usado por trámites) y no bloquean la creación del caso si alguno falla.
Si Azure DevOps está caído, el usuario recibe `502` con el canal de soporte estático (correo/teléfono ya
configurados) como salida de emergencia — sin worker de reconciliación en segundo plano, decisión de
alcance explícita de v1 (§7.2 del diseño).

Fuentes de código: `services/core-api/src/Flit.Infrastructure/DrFlit/AzureDevOpsSupportCaseClient.cs`
(nuevo), `AzureDevOpsOptions.cs` (nuevo), `DrFlitFieldMappingOptions.cs` (nuevo),
`services/core-api/src/Flit.Infrastructure/Persistence/Configurations/DrFlitSupportCaseConfiguration.cs`
(nuevo), `services/core-api/src/Flit.Api/Endpoints/DrFlitEndpoints.cs` (soporta también
`/support-cases` y `/support-cases/attachments`), `services/core-api/src/Flit.DrFlit.Application/SupportCases/`
(nuevo).

# CRITERIOS FUNCIONALES

- [ ] Confirmar el formulario de soporte crea un Bug en el proyecto ADO `FLIT - SOPORTE` con título
      `[ DR. FLIT ] {título}` y `Microsoft.VSTS.TCM.ReproSteps` con el formato del formulario web
      vigente, **sin asignar**.
- [ ] El mapeo de `Custom.Environment`/`Custom.Incidence`/`Custom.Primacy`/`Severity`/
      `Custom.AffectedModule` es configurable (`DrFlitFieldMappingOptions`), no un valor fijo en el código.
- [ ] `Custom.Environment` se resuelve desde `DR_FLIT_DEPLOY_ENVIRONMENT` (nunca desde
      `ASPNETCORE_ENVIRONMENT`, que es `Development` en los tres ambientes).
- [ ] El usuario recibe la confirmación del ID de su caso (`"Tu caso #N quedó radicado"`, sin enlace
      salvo rol SuperAdmin).
- [ ] Si Azure DevOps no está disponible, el usuario recibe un mensaje de error con el canal de soporte
      estático como salida de emergencia, sin perder los datos ya diligenciados.
- [ ] Los adjuntos (hasta el máximo configurado) no bloquean la creación del caso si alguno falla al
      subirse.

---

## Feature C

Título: `[DR.FLIT] - Chat conversacional en frontend`

# OBJETIVO

Que Dr. FLIT entienda lenguaje natural sin que el usuario elija un menú, mostrando saludo ameno y
detección de intención por conversación, manteniendo intactas Gestión y Ayuda por menú, y degradándose
al buscador determinista existente cuando el backend no puede responder con LLM.

# DESCRIPTION

El chat de Dr. FLIT en `frontend/components/dr-flit/` gana la capacidad de entender texto libre sin
romper nada de lo que ya funciona: el menú explícito (Gestión/Ayuda/Soporte) sigue operando 100% sin LLM
— principio rector del diseño (§1). La máquina de estados `dr-flit-conversation.ts` gana fases nuevas
(`chat_loading`, `showing_chat_reply`, §11 del diseño) que conviven con las existentes, sin tocarlas.
Cuando el LLM responde `duda`, se muestran las citas del manual con enlace directo al final; cuando
responde `gestion`, se reutiliza el flujo Gestión existente sin fase nueva; cuando responde `no_claro`, se
muestra la pregunta de seguimiento del modelo. Cuando el backend se degrada (LLM caído o salida inválida)
o se alcanza el tope diario, el frontend cae al buscador determinista (`searchManualArticles`) que ya
existe hoy — la ruta segura ya probada en v3 nunca se descarta. La conversación persiste en
`sessionStorage`; solo "Terminar chat" la borra.

Fuentes de código: `frontend/components/dr-flit/dr-flit-conversation.ts`,
`frontend/components/dr-flit/useDrFlitChat.ts`, `frontend/components/dr-flit/dr-flit-intents.ts`,
`frontend/components/dr-flit/DrFlitChatPanel.tsx`, `frontend/lib/api/dr-flit-client.ts` (nuevo),
`frontend/components/dr-flit/dr-flit-chat-types.ts` (nuevo).

# CRITERIOS FUNCIONALES

- [ ] Escribir texto libre en el FAB de Dr. FLIT (visible en toda la plataforma) dispara la
      clasificación de intención sin que el usuario elija un menú.
- [ ] Preguntas de seguimiento (`intent: no_claro`) se muestran como mensaje del bot cuando la intención
      no es clara, sin romper la conversación.
- [ ] Cuando el LLM está caído o se alcanzó el tope diario, el chat sigue respondiendo con el buscador
      determinista existente, sin que el usuario pierda funcionalidad.
- [ ] La conversación persiste en la sesión del navegador; solo "Terminar chat" la borra.
- [ ] Un mensaje clasificado como `gestion` lleva al usuario a la sesión Gestión existente sin que tenga
      que repetir la búsqueda.

---

## Feature D

Título: `[DR.FLIT] - Recopilación y confirmación de casos de soporte en frontend`

# OBJETIVO

Que cuando Dr. FLIT detecta intención de soporte, recopile los campos del caso en un formulario
prellenado, y que el caso solo se radique tras confirmación explícita del usuario sobre un resumen
visible — nunca por acción del LLM.

# DESCRIPTION

El punto de entrada donde vive el guardarraíl más importante del diseño: la creación del caso **nunca**
la dispara el LLM, solo un clic humano sobre un resumen visible en pantalla (§8.2 punto 3 del diseño).
`DrFlitSupportCaseForm.tsx` prellena Nombre/Email/Compañía desde el JWT/perfil (`currentUser` de
`Shell.tsx`, `tenant_name` del JWT) y Fecha automática; el resto (Detalle, Resultado esperado, Adjuntos,
Frecuencia, Ambiente, Teléfono, Título, Prioridad) lo completa el usuario. Los datos de contacto van
directo al endpoint de creación del caso, nunca al LLM — mitigación de *prompt injection* y de fuga de PII
a la vez (§8.2 punto 4). `DrFlitSupportCaseConfirm.tsx` y `DrFlitSupportCaseCreated.tsx` cierran el flujo
con el mensaje `"Tu caso #N quedó radicado"` (enlace solo si SuperAdmin) o con el estado de error que
ofrece el canal estático si Azure DevOps está caído. El aviso de Habeas Data es no bloqueante y vive en el
propio panel del chat, sin modelarse como consentimiento auditable persistido (decisión de alcance
revisable, §10 del diseño).

Fuentes de código: `frontend/components/dr-flit/DrFlitSupportCaseForm.tsx` (nuevo),
`DrFlitSupportCaseConfirm.tsx` (nuevo), `DrFlitSupportCaseCreated.tsx` (nuevo),
`DrFlitSupportPanel.tsx`, `dr-flit-conversation.ts`, `dr-flit-chat-types.ts`.

# CRITERIOS FUNCIONALES

- [ ] El formulario de soporte recopila Nombre, Email, Compañía, Fecha, Detalle, Resultado esperado,
      Adjuntos (Sí/No + archivos), Frecuencia, Ambiente, Teléfono, Título y Prioridad (Alta/Media/Baja).
- [ ] Nombre, Email y Compañía llegan prellenados desde el JWT/perfil del usuario.
- [ ] El caso solo se radica tras un clic explícito de confirmación sobre un resumen visible — ninguna
      respuesta del LLM puede dispararlo.
- [ ] Los datos de contacto (nombre/teléfono/correo) van directo al endpoint de creación del caso, nunca
      pasan por el LLM.
- [ ] El usuario ve un aviso de Habeas Data no bloqueante al abrir el chat.

---

## DoR-Feature (previo a Active)

| Criterio | A | B | C | D |
|---|---|---|---|---|
| Módulo en título (`[DR.FLIT]`) | PASS | PASS | PASS | PASS |
| Objetivo | PASS | PASS | PASS | PASS |
| Descripción extendida con fuentes de código | PASS | PASS | PASS | PASS |
| ≥3 criterios funcionales | PASS | PASS | PASS | PASS |
| Sprint (siguiente al activo) | PENDING — lo asigna la sesión principal | PENDING | PENDING | PENDING |
| Area FLIT - EVOLUTION | PASS | PASS | PASS | PASS |
| Tag `DOR` | PENDING — se agrega al registrar | PENDING | PENDING | PENDING |
| AssignedTo humano | propuesto Willyn Londoño Calle | propuesto Willyn Londoño Calle | propuesto Willyn Londoño Calle | propuesto Willyn Londoño Calle |
| Sin placeholders TODO/TBD | PASS | PASS | PASS | PASS |
| Sin datos sensibles | PASS | PASS | PASS | PASS |

No se registra nada en ADO hasta aprobación humana explícita. HUs hijas: ver `HUS.md`
(14 HUs: 5 en A, 3 en B, 3 en C, 3 en D).

## Orden de implementación entre Features

1. **Feature A** (fundaciones de chat en backend) — sin dependencia de B, C o D; el endpoint `/chat`
   debe existir antes de que Feature C tenga algo real contra qué integrar.
2. **Feature B** (caso de soporte a ADO) — **sin dependencia funcional de A** (la creación del caso nunca
   pasa por el LLM); puede avanzar en paralelo con A si el equipo lo decide. Se secuencia después de A en
   este plan solo por orden de entrega, no por bloqueo técnico.
3. **Feature C** (chat conversacional en frontend) — depende de Feature A (necesita `POST /dr-flit/chat`
   real para integrar, no solo un mock).
4. **Feature D** (recopilación y confirmación del caso) — depende de Feature B (necesita
   `/support-cases` y `/support-cases/attachments` reales) y, para el punto de entrada conversacional
   (`intent: soporte` abre el formulario), de Feature C — salvo HU-D3 (aviso de Habeas Data), que es
   independiente y puede entregarse en cualquier momento.

Recomendación: una rama por Feature (memoria del proyecto), PRs en el orden A → B → C → D. Si el equipo
tiene capacidad para dos frentes backend en paralelo, A y B pueden correr a la vez sin conflicto de
archivos (tocan carpetas `DrFlit/` distintas dentro de `Flit.Infrastructure`).
