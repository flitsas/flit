# ADR-0060: Dr. FLIT conversacional con LLM en backend y escalación de soporte a Azure DevOps

**Fecha**: 2026-09-24
**Status**: Propuesto
**Deciders**: Producto/PO (decisiones ya cerradas por negocio, ver Contexto) · Willyn Londoño Calle (Líder Técnico, pendiente validar) · Architecture Agent
**Tags**: arquitectura, backend, frontend, llm, integracion-externa, modulo-dr-flit, azure-devops
**Revierte**: Decisión **D2** de `docs/plan-tecnico-dr-flit-v3.md` §3 («¿Ayuda con LLM? Mantener keyword-matching… LLM como Fase 4 opcional»)
**Relacionado**: no enmienda ni contradice el resto del plan v3 (catálogo de 40 artículos, audiencias, normativa como fuente principal, buscador determinista) — los **reutiliza** como fallback y como base documental del LLM.

## Contexto

DR-FLIT v3 (`frontend/components/dr-flit/`) es hoy un asistente **UI-only**: máquina de estados
determinista + búsqueda por keywords sobre 40 artículos del manual (`frontend/lib/manual/`), sin
backend propio y sin LLM. La decisión D2 del plan v3 (2026-09-21, mismo tramo de trabajo) fue
explícita: «mantener keyword-matching; LLM como Fase 4 opcional», razonada en que ~40 artículos no
justificaban la complejidad.

La Epic #12718 «DR. FLIT — Chat de soporte y consulta de manuales» (proyecto ADO **FLIT - EVOLUTION**)
pide algo que el keyword-matching no puede dar: **conversación libre** que (a) entienda si el usuario
tiene una duda o necesita soporte sin que elija un menú, (b) responda dudas citando manual/sección con
enlace, admitiendo cuando no sabe, y (c) recopile conversacionalmente los campos de un caso de soporte
y lo **radique automáticamente** como Bug en el proyecto ADO **FLIT - SOPORTE**, confirmando el ID al
usuario. Esto es un salto cualitativo de UX que el buscador por keywords no resuelve: la Fase 4
opcional que D2 pateó hacia adelante es, con esta épica, la Fase 4 real.

Decisiones de producto/negocio ya cerradas (no se reabren en este ADR, se documentan como marco):

- LLM: **Claude `claude-haiku-4-5`** desde core-api, reutilizando la integración Anthropic ya
  existente (`Flit.Infrastructure/Ocr/AnthropicMessagesClient.cs`, `AnthropicOptions`); modelo y tope
  diario de mensajes por usuario **configurables** por ambiente.
- El manual completo es el contexto del modelo (con *prompt caching*); el modelo **solo** responde con
  base en el manual y devuelve slugs citados en salida estructurada validada; si el LLM falla o excede
  el tope, **fallback al buscador determinista actual**.
- Intenciones: `duda` · `soporte` · `gestion` (enruta a la sesión Gestión existente, que se conserva
  intacta) · `no_claro` (pregunta de seguimiento).
- Casos de soporte: Bug en ADO **FLIT - SOPORTE**, título `[ DR. FLIT ] {título}`,
  `Microsoft.VSTS.TCM.ReproSteps` con el mismo formato de campos que el formulario web vigente, mapeo
  de `Custom.Environment` / `Custom.Incidence` / `Custom.Primacy` / `Severity` / `Custom.AffectedModule`
  / `Custom.TypeBug` **configurable**, **sin asignar** (igual que el formulario web hoy: soporte
  triagea).
- Identidad técnica: cuenta de servicio nueva con PAT de alcance mínimo (Work Items R/W en
  **FLIT - SOPORTE**), secreto por ambiente **solo en backend**.
- Habeas Data: aviso al abrir el chat; nombre/teléfono/correo se capturan en un formulario dentro del
  chat que va **directo** al endpoint de creación del caso, **sin pasar por el LLM**; al modelo solo
  llega la conversación/descripción.

### Aclaración de la regla 4 (`AssignedTo` nunca vacío) — no es una excepción ad-hoc

La regla 4 de `.claude/rules/00-flit-conventions.mdc` («AssignedTo: humano, NUNCA agente, NUNCA
vacío») gobierna el ciclo de vida de **HUs de desarrollo** (y de Bugs internos de desarrollo/QA, ver
skill `bug-reporter`) en los proyectos de trabajo de ingeniería (`FLIT`, `FLIT - EVOLUTION`, etc.):
existe para que ningún ítem de trabajo interno quede huérfano de responsable humano.

Los Bugs que crea Dr. FLIT viven en **`FLIT - SOPORTE`**, un proyecto ADO distinto, y son **casos de
soporte al cliente**, no HUs de desarrollo: replican exactamente el comportamiento que ya tiene el
formulario web de soporte vigente hoy (sin asignar, a la espera de que el equipo de soporte triage y
asigne). No es una excepción nueva que este ADR inventa — es el comportamiento ya vigente en
`FLIT - SOPORTE`, documentado aquí explícitamente para que una auditoría futura no lo lea como
violación de la regla 4. Si en el futuro se quiere una asignación automática de primer nivel (p. ej.
por `Custom.AffectedModule`), es una HU aparte con su propio ADR si sienta precedente de enrutamiento.

## Decisión

Revertir D2: **sí** hay LLM, orquestado desde **core-api** (no desde el navegador ni desde un servicio
nuevo), reutilizando la integración Anthropic existente como patrón (no el mismo cliente interno, ver
Consecuencias) y manteniendo el buscador determinista del frontend como **red de seguridad** cuando el
LLM no está disponible o el usuario agotó su tope diario. Se añade una integración nueva —cliente HTTP
propio contra la REST API de Azure DevOps, mismo patrón que los demás clientes externos de
`Flit.Infrastructure` (typed `HttpClient`, `IOptions`, secreto vía env var cruda)— para crear los Bugs
de soporte. El detalle de endpoints, contratos, modelo de datos y máquina de estados del frontend vive
en `docs/ado-drafts/epic-12718/DISENO-TECNICO.md`; este ADR fija la decisión arquitectónica de fondo y
sus alternativas.

### Configurabilidad del motor — decisión de segundo nivel

El modelo, sus parámetros y el interruptor del LLM son, por diseño, lo más volátil de este ADR (cambian
por costo, por incidente del proveedor o por decisión de producto, no por evolución de arquitectura).
Se evalúan dos niveles, no excluyentes entre sí:

**Nivel 1 — `appsettings` por ambiente, MISMA sección y MISMO estilo que ya usa el analizador/
clasificador OCR (elegido como base, obligatorio en v1).** Hoy `AnthropicOptions` (sección
`Anthropic`, `services/core-api/src/Flit.Infrastructure/Ocr/AnthropicOptions.cs`) ya tiene
`Model`/`MaxTokens`/`TimeoutSeconds` para el analizador (Haiku) y `ClassifierModel`/
`ClassifierMaxTokens`/`ClassifierTimeoutSeconds` para el clasificador (Sonnet), cada uno con fallback a
env `ANTHROPIC_*` en `InfrastructureExtensions.cs` (líneas ~1243-1252). Dr. FLIT **no crea una sección
ni un cliente paralelo**: agrega claves nuevas a la MISMA clase y MISMA sección, con el mismo estilo —

```csharp
public string DrFlitModel { get; set; } = "claude-haiku-4-5";
public int DrFlitMaxTokens { get; set; } = 600;
public int DrFlitTimeoutSeconds { get; set; } = 20;
public int DrFlitDailyMessageLimit { get; set; } = 30;
public bool DrFlitEnabled { get; set; } = true;
```

con fallback `ANTHROPIC_DRFLIT_MODEL` / `ANTHROPIC_DRFLIT_MAX_TOKENS` /
`ANTHROPIC_DRFLIT_TIMEOUT_SECONDS` / `ANTHROPIC_DRFLIT_DAILY_MESSAGE_LIMIT` /
`ANTHROPIC_DRFLIT_ENABLED`, mismo `Cfg()` env-first ya existente. Reutiliza la **misma** `ApiKey`,
el **mismo** `BaseUrl` y el **mismo** `HttpClient` typed (`AddHttpClient<AnthropicMessagesClient>`) que
ya sirve al analizador y al clasificador — `AnthropicMessagesClient` recibe el modelo/tokens/timeout
**por llamada** (`model ?? _options.Model`, ya es el patrón existente), así que Dr. FLIT solo necesita
un método nuevo en esa misma clase (chat de texto, sin bloques `document`/`image`) que pase
`_options.DrFlitModel` / `_options.DrFlitMaxTokens` / `_options.DrFlitTimeoutSeconds` — cero clase de
opciones nueva, cero cliente HTTP nuevo. Cambiar estos valores requiere editar el `.env` del VPS y
reiniciar el contenedor — mismo costo operativo que cualquier otro ajuste de integración externa del
repo hoy (Verifik, Fasecolda, `TramiteValidations`). **Suficiente para lanzar la Epic.**

**Nivel 2 — override en caliente desde la consola Super Admin (alternativa, opcional/futura, NO forma
parte de la base de este ADR).** Un módulo Plataforma/Integraciones persiste en BD un override de
`{model, maxTokens, timeoutSeconds, dailyMessageLimit, enabled}` que, si existe, tiene **prioridad
sobre `AnthropicOptions`**; el modelo se valida contra una **lista blanca** de ids permitidos (p. ej.
`claude-haiku-4-5`, `claude-sonnet-5`) — nunca se acepta un id arbitrario, evita que un error de tipeo o
un intento malicioso apunte a un modelo inexistente o no autorizado por costo. Cada cambio queda
auditado (quién, cuándo, valores antes/después), mismo patrón ya vigente de
`AdminIctJobSettingsEndpoints` / `SaveIctJobSettingsHandler` (configuración operativa persistida que
SuperAdmin ajusta sin redeploy) — se reutiliza esa forma, no se inventa una nueva.

| | Pros | Contras | Esfuerzo |
|---|---|---|---|
| Nivel 1 solo | Cero piezas nuevas (tabla, endpoint, UI); mismo patrón que toda integración externa del repo | Cambiar modelo o apagar el LLM ante un incidente/costo exige acceso a Infra + redeploy — minutos a horas, no segundos | — (ya incluido en Feature A) |
| Nivel 1 + Nivel 2 | Apagar el LLM o bajar a un modelo más barato **sin redeploy**, en segundos, ante un pico de costo o un incidente del proveedor — quien tiene esa urgencia (Líder Técnico/Soporte) no depende de Infra | Tabla + migración + endpoint SuperAdmin + UI nuevos; una superficie más para auditar (quién puede cambiar el modelo); dos fuentes de verdad a explicar (appsettings = default, BD = override) | M adicional sobre Feature A |

**Decisión de este ADR:** construir el Nivel 1 como parte de la Epic (no es opcional, es la base de
todo el diseño). El Nivel 2 queda **propuesto pero no comprometido** — es una HU opcional dentro del
plan de descomposición (`docs/ado-drafts/epic-12718/DISENO-TECNICO.md` §15) para que el Líder Técnico y
el PO decidan si el valor de "apagar el LLM en caliente" justifica el esfuerzo adicional antes de que
exista un incidente real que lo demande.

## Alternativas consideradas

### Opción 1: LLM orquestado en backend (core-api), manual como artefacto generado, fallback determinista en frontend *(elegida)*

**Pros:**
- El secreto de Anthropic y el PAT de ADO nunca salen del backend — cumple la decisión de negocio y
  el patrón de secretos ya vigente (`Anthropic:ApiKey` / `ANTHROPIC_API_KEY` por ambiente).
- El tope diario por usuario es **enforceable** de verdad: solo el backend puede contar mensajes de
  forma confiable (un contador en el cliente se resetea con solo recargar la página).
- Reutiliza patrones ya probados del repo: typed `HttpClient` + `IOptions` (Verifik, Kyverum,
  Anthropic OCR), `Cfg()` env-first, degradación graceful a un mensaje usable en vez de una excepción.
- El fallback determinista (`searchManualArticles`) ya vive en el frontend y no necesita al backend
  para funcionar — sigue siendo la ruta segura cuando el LLM falla.
- La creación del caso de soporte (side effect real) queda en el mismo backend que ya controla
  autorización, tenant y auditoría de todo lo demás en FLIT.

**Cons:**
- Nueva integración externa (Azure DevOps) que el backend no tenía; nuevo bounded context
  (`Flit.DrFlit.*`) aunque pequeño.
- El manual vive en `frontend/lib/manual/` (TypeScript) y el backend necesita su contenido como texto:
  hace falta un mecanismo de entrega del catálogo al backend (ver detalle y sub-alternativas en
  `DISENO-TECNICO.md` §Manual como contexto del LLM).

**Esfuerzo:** M
**Riesgos:** nuevo bounded context a mantener; acoplamiento de build entre frontend (dueño del manual)
y backend (consumidor) — mitigado con un artefacto generado y una prueba que detecta desfase (ver
diseño técnico).

### Opción 2: LLM orquestado desde el navegador (frontend llama directo a Anthropic)

**Pros:**
- Cero cambios en core-api; el frontend ya tiene el manual completo en memoria, sin artefacto que
  generar ni sincronizar.
- Latencia mínima (un salto de red menos).

**Cons:**
- **Contradice la decisión de negocio ya cerrada** de que el secreto vive solo en backend: cualquier
  forma de exponer la API key de Anthropic al navegador (incluso ofuscada) es extraíble por
  DevTools/interceptor. Un proxy delgado que solo reenvía la key tampoco resuelve esto — sigue siendo
  la key del cliente, solo que con un salto extra.
- El tope diario por usuario **no es enforceable**: el navegador no es una fuente de verdad confiable
  para un contador de negocio.
- La creación del caso de soporte necesitaría el PAT de ADO también accesible desde el navegador (o un
  segundo backend delgado solo para eso) — mismo problema de secretos, duplicado.

**Esfuerzo:** S (aparente) — en la práctica se vuelve M al tener que resolver igual el problema de
secretos con una pieza de backend adicional, sin ganar nada sobre la Opción 1.
**Riesgos:** ALTO — fuga de credenciales, abuso de costo sin control server-side, viola una decisión de
negocio ya tomada (Habeas Data / manejo de secretos).

### Opción 3: Servicio nuevo dedicado («Dr. FLIT service») separado de core-api

**Pros:**
- Aísla el bounded context conversacional del resto de core-api desde el día 1; podría escalar
  independientemente si el uso crece mucho.
- No compite por recursos/despliegue con el resto de la API si el volumen de chat es alto.

**Cons:**
- Nueva unidad desplegable: nuevo pipeline CI/CD, nuevo contenedor en `docker-compose.prod.yml`, nueva
  entrada de red/CORS, nuevo secreto a gestionar por VPS — todo esto ya existe y funciona para
  core-api.
- Duplica en vez de reutilizar el patrón `AnthropicOptions`/`Cfg()`/typed `HttpClient` que ya está
  probado en producción para Verifik, Kyverum y el propio Anthropic OCR.
- El volumen esperado (chat de soporte interno de una plataforma B2B, no un producto masivo) no
  justifica hoy una pieza de infraestructura nueva — sería sobre-diseño (BDUF) para el tamaño real del
  problema.

**Esfuerzo:** L
**Riesgos:** costo operativo y de mantenimiento no justificado por el volumen actual; retrasa la
entrega de la Epic para resolver un problema de escala que no existe todavía.

## Tradeoff aceptado

Se acepta la complejidad de un bounded context nuevo y pequeño dentro de core-api (`Flit.DrFlit.*`) y
un mecanismo de entrega del manual al backend, a cambio de mantener los secretos (Anthropic + PAT ADO)
donde ya viven todos los demás secretos del repo y de que el tope diario sea real. La Opción 2 se
descarta por violar una decisión de negocio ya cerrada, no por preferencia de arquitectura. La Opción 3
se descarta por sobre-dimensionar la solución para el volumen esperado; si el chat de Dr. FLIT crece
mucho (multiplataforma, volumen alto, necesidad de escalar independiente de core-api), extraerlo a un
servicio propio queda como evolución natural — la interfaz `IDrFlitAssistant` en Application ya lo deja
listo para ese día (se cambia la implementación de Infrastructure sin tocar el contrato).

## Consecuencias

### Lo que se gana
- Dr. FLIT conversa de verdad (texto libre, sin menú) manteniendo el mismo nivel de control de
  secretos, tenant y autorización que el resto de FLIT.
- Los casos de soporte se radican solos, con confirmación explícita del usuario — cero backlog de
  soporte perdido en un correo o formulario externo no instrumentado.
- El buscador determinista de 40 artículos no se descarta: sigue siendo el fallback y el mecanismo del
  portal público `/manual` — cero regresión funcional de lo entregado en v3.

### Lo que se pierde
- El "cero backend" que hacía a DR-FLIT trivial de desplegar (era build-only del frontend) desaparece
  para el flujo conversacional: ahora depende de core-api, de la disponibilidad de Anthropic y de Azure
  DevOps. Mitigado por el fallback determinista, que sigue funcionando sin backend para la sesión
  Gestión y para Ayuda por menú.
- El manual deja de ser propiedad exclusiva del frontend: gana un consumidor más (el backend), lo que
  implica disciplina de build para no desincronizar el artefacto generado (ver diseño técnico).

### Cambios operacionales
- Nuevo secreto por ambiente: PAT de servicio de Azure DevOps con alcance mínimo (Work Items R/W,
  `FLIT - SOPORTE`), inyectado igual que `ANTHROPIC_API_KEY` (env var cruda del `.env` del VPS, nunca
  en el repo, nunca en logs).
- **Trampa de ambiente a evitar** (ver `windows-build-y-test-gotchas` / `validaciones-tramite-por-ambiente`
  en la memoria del repo): DEV, QA y PDN corren **los tres** con `ASPNETCORE_ENVIRONMENT=Development`
  (mismo `docker-compose.prod.yml`, se diferencian solo por el `.env` de cada VPS). `Custom.Environment`
  del Bug **no puede inferirse** con `IsDevelopment()` ni con el nombre del entorno ASP.NET — necesita
  una variable de negocio propia (`DrFlit__DeployEnvironment` o similar), mismo patrón que
  `TRAMITE_VALIDATION_*_MODE` de la HU #10970. Ver detalle y alternativa en el diseño técnico.
- Nuevo build step recomendado en el frontend (`pnpm run manual:export` o equivalente en CI) para
  generar el artefacto de manual que consume el backend — con una prueba de guarda que falla si el
  artefacto está desactualizado respecto a `lib/manual/articles/*.ts`.

## ADRs relacionados

- No hay ADR previo para la integración Anthropic OCR (`AnthropicMessagesClient`/`AnthropicOptions`):
  se tomó como patrón de código a reutilizar, no como decisión arquitectónica formal previa.
- [ADR-0057] (`jerarquia-de-clientes-alcance-tipado-fail-closed`) y el resto de ADRs de autorización no
  cambian: Dr. FLIT no crea un modelo de permisos nuevo, usa `RequireAuthorization()` sin policy
  (cualquier usuario autenticado, igual que `UserUiPreferencesEndpoints`).

## Notas para agentes

- **Backend Agent**: nuevo bounded context `Flit.DrFlit.Application` / `Flit.Infrastructure/DrFlit/`
  para los handlers de chat y de caso de soporte, PERO el modelo de chat en sí **no** crea una clase de
  opciones ni un cliente HTTP nuevos: agregar `DrFlitModel`/`DrFlitMaxTokens`/`DrFlitTimeoutSeconds`/
  `DrFlitDailyMessageLimit`/`DrFlitEnabled` a la MISMA `AnthropicOptions` (sección `Anthropic`) y un
  método nuevo (p. ej. `SendChatAsync`) en la MISMA `AnthropicMessagesClient` — mismo `HttpClient`
  typed, misma `ApiKey`, mismo patrón `Cfg()` env-first (`ANTHROPIC_DRFLIT_*`) que ya usan `Model`/
  `ClassifierModel`. El cliente HTTP de Azure DevOps sí es nuevo (no hay nada que reutilizar ahí), sin
  SDK adicional (REST cruda + `HttpClient`, mismo patrón que Verifik/Kyverum — no añadir
  `Microsoft.TeamFoundationServer.Client` ni paquetes NuGet de ADO sin ADR aparte). Detalle completo de
  endpoints, DDL y archivos en `docs/ado-drafts/epic-12718/DISENO-TECNICO.md`.
- **Frontend Agent**: el manual (`frontend/lib/manual/`) sigue siendo la única fuente de verdad; el
  artefacto que consume el backend es **generado**, nunca editado a mano. La máquina de estados
  (`dr-flit-conversation.ts`) gana fases nuevas sin romper las existentes — ver diseño técnico.
- **QA Agent**: casos de fallback (LLM caído, tope diario agotado, ADO caído) son tan importantes como
  el camino feliz — no son edge cases opcionales.
- **Security Agent**: validar que el PAT de ADO nunca aparece en logs ni en respuestas de error;
  validar que el formulario de datos personales del caso de soporte nunca pasa por el LLM (contrato
  `POST /support-cases` no debe aceptar el historial de chat como fuente de nombre/teléfono/correo);
  revisar guardarraíles de *prompt injection* en `DISENO-TECNICO.md`.
- **Infra Agent**: aprovisionar el PAT de servicio de ADO (alcance mínimo, `FLIT - SOPORTE`) y la
  variable de ambiente de negocio (`DrFlit__DeployEnvironment` o el nombre que se fije en
  implementación) en el `.env` de cada VPS — **no** confiar en `ASPNETCORE_ENVIRONMENT`.

## Referencias externas

- Epic #12718 «DR. FLIT - Chat de soporte y consulta de manuales» (proyecto ADO **FLIT - EVOLUTION**).
- `docs/plan-tecnico-dr-flit-v3.md` (decisión D2 revertida, §6 «Ideas naturales para el siguiente
  tramo» ya anticipaba esta Fase 4).
- Azure DevOps REST API — Work Items (`POST .../_apis/wit/workitems/${type}`) y Attachments
  (`POST .../_apis/wit/attachments`), `api-version=7.1`.
- Anthropic Messages API — Prompt Caching (`cache_control: {type: "ephemeral"}`).
