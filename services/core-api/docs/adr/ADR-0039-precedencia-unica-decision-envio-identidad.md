# ADR-0039: Precedencia única de decisión de envío de validación de identidad

**Fecha**: 2026-08-05
**Status**: Aceptado
**Fecha de aceptación**: 2026-08-20
**Status previo**: Propuesto (2026-08-05)
**Deciders**: Juan Felipe Montoya Garcia (Líder Técnico) — aceptado 2026-08-20; equipo core-api, equipo tramites
**Tags**: arquitectura, backend, frontend, seguridad, identidad, tramites, feature-11260
**Supersedes**: —
**Relacionado**: `ADR-0025-baul-firmas-custodia-y-consumo.md` (Aceptado), `ADR-0034-validacion-identidad-admin-desacoplada.md` (Aceptado), `ADR-0030-persona-entidad-tenant-prevalidacion.md` (Propuesto)
**HU origen**: Feature #11260 — HUs #11262, #11263, #11264, #11265, #11266, #11267
**Plan técnico**: `docs/plan-tecnico-ajustes-validacion-identidad.md` (v2, CF-01 a CF-04, D2/D3/D4/D8–D13)

---

## Contexto

El correo de validación de identidad **se dispara hoy desde cinco puntos distintos**, cada uno con su
propio guard, y ninguno de los cinco responde la pregunta que importa: *"¿esta persona ya tiene identidad
aprobada y vigente?"*.

| # | Disparador | Guard actual | ¿Tiene UI? |
|---|---|---|---|
| 1 | `POST /biometric-validations` (prevalidación) | `FindActiveStandaloneValidationAsync` — solo validaciones **en vuelo**, solo **standalone**, por `PersonId`, y **no lee `expires_at`** | sí |
| 2 | `POST /instances/{id}/biometric` — rama **mock** | guard de idempotencia por parte dentro de la instancia | sí |
| 3 | `POST /instances/{id}/biometric` — rama **Kyverum** | guard por parte **distinto del de la rama mock**: terminaliza el enlace vencido para permitir el reenvío | sí |
| 4 | Wizard al guardar un actor (`TramiteWizard.tsx:915-921`) → `ensureIdentity` = `requiere_validacion` → `iniciarBiometric` | hereda el de (2)/(3) | **no** — automático |
| 5 | Backend, persona jurídica sin RL utilizable (`ActorsCommand.cs:369`) | hereda el de (2)/(3) y **descarta el error `biometria_activa` a propósito** | **no** — automático |

Solo `EnsureIdentity` resuelve identidad **por documento en todo el tenant**, y su veredicto es
**reutilizar**, no *impedir el envío*: nada evita que el disparador siguiente mande el correo igual.

Encima, ya conviven **cuatro normalizaciones distintas** del par (tipo, número) de documento:
igualdad exacta en SQL (`ProcedureInstanceRepository:343`), `Trim` + `ToUpperInvariant`
(`BiometricRules.IdentidadKey:308`), `Trim` + `OrdinalIgnoreCase` (`EnsureIdentityCommand.DocCoincide:159`)
y `ToUpper` **solo del número** (`ListLinkedProceduresByIdentityDocumentsAsync:409`). Para un documento que
difiera en mayúsculas o espacios, la ruta por instancia dice `requiere_validacion` mientras el chip del
listado dice *aprobado*.

Restricciones duras verificadas contra el código: la tabla
`procedure_instance_biometric_validations` **no tiene `row_version`** (no hay control optimista); el
contrato publicado en `contracts/openapi/core-api.v1.yaml` declara **201** para estos endpoints; y el
disparador (5) **depende** de que el error de "ya hay una activa" sea ignorable.

---

## Decisión

Se introduce un **componente único de precedencia de envío** —un servicio de dominio que responde
*"¿corresponde enviar correo de validación a esta persona?"*— que se consulta **antes** de los guards
existentes y **no los sustituye**. Orden de precedencia:

1. **Cobertura por baúl de firmas** (`FirmaBaulCobertura.Aplica` + firma activa y vigente) → **no enviar**.
2. **Identidad aprobada y vigente** por documento normalizado en el tenant → **no enviar, reutilizar**.
3. **Validación en vuelo con enlace no vencido** → **no enviar**.
4. **Validación en vuelo con enlace vencido** → **no crear fila nueva: encauzar al reenvío**
   (tope de 3 y cooldown de 5 min ya existentes).
5. Cualquier otro caso → **enviar**.

Decisiones anexas que este ADR cierra:

- **Normalización canónica del documento**: `Trim` + mayúsculas invariantes, **y nada más**, aplicada
  **en lectura** con índices por expresión (no migración de datos sobre cinco tablas).
- **Medición previa bloqueante**: antes de activar el componente se ejecuta una consulta de solo lectura
  que cuente los pares (actor, validación) que empatan con la regla nueva y no con la actual.
- **Concurrencia**: se resuelve con un **índice único parcial** en base de datos sobre
  `(tenant_id, documento_normalizado)` filtrado por los estados en vuelo — no con un `if` en el handler.
- **Código de estado**: los disparadores con UI siguen respondiendo **`409`**, ahora **con cuerpo
  informativo** (`motivo`, `status`, `validatedAt`, `validUntil`, `validationId`, `origen`).
- **Fuera de alcance explícito**: la ruta de **identidad administrativa** (representantes legales y
  mandatarios) y la **tabla de equivalencias de alias de tipo documental** (`NIT`/`N`, `CC`/`C`).

---

## Alternativas consideradas

### Opción 1: Componente de precedencia **previo** a los guards existentes (elegida)

Un único servicio consultado por los cinco disparadores antes de su guard actual. Los guards de
idempotencia por parte dentro de la instancia y la terminalización del enlace vencido **permanecen
intactos**: la paridad que se exige es sobre *la decisión de envío*, no sobre todos los guards.

**Pros:**
- Cierra el hueco real (persona ya aprobada y vigente) en **un solo lugar**, con una regla auditable
- Alcanza también a los disparadores automáticos (4) y (5), donde no hay UI y el efecto correcto no es
  "confirmar" sino simplemente **no enviar**
- No toca la idempotencia por parte ni la terminalización del enlace vencido: cero riesgo sobre el
  camino `borrador → preparado` que hoy funciona
- Permite un test de **paridad por disparador**: mismo documento ⇒ misma decisión en los cinco
- Reutiliza `FirmaBaulCobertura.Aplica`, el predicado unificado que ya existe

**Contras:**
- Añade una capa más: durante una transición conviven precedencia + guards heredados
- Obliga a unificar la normalización del documento, que **cambia veredictos del gate de radicación**
- El índice único parcial es cambio de schema (migración), no solo código

**Esfuerzo:** M
**Riesgos:** Alto, concentrado en la normalización (ver §Tradeoff y §Riesgos), acotado por la medición previa bloqueante.

---

### Opción 2: Resolver único que **sustituye** los guards existentes

Un solo componente que reemplaza los cinco guards y se convierte en la única autoridad sobre creación de
validaciones.

**Pros:**
- Un único camino de decisión, sin capas superpuestas ni código heredado que mantener
- Elimina de raíz la divergencia entre la rama mock y la rama Kyverum del disparador (3)

**Contras:**
- Obliga a **romper la idempotencia por parte dentro de la instancia**, que no es la misma regla que la
  precedencia por persona: una parte puede necesitar su propia fila aunque la persona tenga historial
- Obliga a reimplementar la **terminalización del enlace vencido** de la rama Kyverum, que hoy es lo que
  permite el reenvío legítimo
- Blast radius directo sobre el endpoint que alimenta la transición `borrador → preparado`
- Un solo PR grande, incompatible con el techo de 800 líneas y con el merge incremental por HU

**Esfuerzo:** L
**Riesgos:** Alto — toca el gate de radicación y la idempotencia en el mismo cambio, sin poder aislar la regresión.

---

### Opción 3: No unificar — parchear solo el guard de la prevalidación

Corregir `FindActiveStandaloneValidationAsync` para que lea `expires_at` y contemple identidad aprobada
vigente, y dejar los demás disparadores como están.

**Pros:**
- Esfuerzo mínimo, un solo archivo, sin migración ni normalización
- Riesgo nulo sobre el gate de radicación

**Contras:**
- **No cumple el pedido**: los otros cuatro disparadores siguen enviando correos duplicados a personas
  ya validadas y vigentes, incluidos los dos automáticos y sin UI
- Deja el guard de prevalidación divergiendo aún más de los de trámite: cinco reglas en vez de una
- La incoherencia de normalización sigue viva, con el listado diciendo *aprobado* y el gate diciendo
  *requiere validación* para el mismo documento

**Esfuerzo:** S
**Riesgos:** Funcional — incumple el alcance; se descarta.

---

## Tradeoff aceptado

Se elige la **Opción 1** porque separa dos preguntas que hoy están mezcladas: *"¿corresponde enviar
correo a esta persona?"* (nueva, por persona, transversal a los cinco disparadores) y *"¿esta parte de
esta instancia ya tiene su fila?"* (existente, por parte, idempotencia). La Opción 2 las colapsa en una y
paga con la idempotencia por parte y con la terminalización del enlace vencido —dos comportamientos que
hoy funcionan y que sostienen el camino de radicación—; la Opción 3 no resuelve el problema que motivó el
Feature.

Se acepta a cambio el coste de una capa adicional y de una migración de índice, y **se acepta
explícitamente el riesgo de la normalización**, mitigado por la medición previa bloqueante.

### Por qué el baúl va primero

`ADR-0025-baul-firmas-custodia-y-consumo.md` (**Aceptado**) fijó en su decisión D8 que la cobertura por
baúl **precede** a la validación biométrica: un actor jurídico con firma activa y vigente en el baúl
cuenta como identidad aprobada sin biométrica. Poner el baúl en cualquier otra posición reintroduce el
mismo defecto que ya se pagó: el **Bug #11141** existe porque `IdentityApprovalResolver` conserva una
**copia local** del predicado (`EsActorJuridico`) que ignora el mecanismo de firma elegido por el gestor,
justo en la ruta que decide radicación y FUR. Por eso el componente **debe consumir el predicado
unificado `FirmaBaulCobertura.Aplica`** y tiene prohibido crear una cuarta copia.

### Por qué la identidad administrativa queda fuera de alcance

`ADR-0034-validacion-identidad-admin-desacoplada.md` (**Aceptado**) establece que en la ruta admin
—representantes legales y mandatarios— el reenvío **sí** se permite aunque haya una validación en curso:
literalmente, "sin la guarda `biometria_activa` del flujo de trámite". Aplicarle esta precedencia
contradiría un ADR Aceptado sin un ADR que lo supersede. La ruta admin queda **deliberadamente sin
guard**, y este ADR no la toca.

### Por qué el código de estado sigue siendo 409

Pasar a **200** rompería tres cosas a la vez:

1. El **contrato publicado** en `contracts/openapi/core-api.v1.yaml`, que declara `201` para la creación;
   los consumidores (`BiometricStep.tsx:826`, `TramiteWizard.tsx:918`) tratan **todo 2xx** como éxito con
   payload completo y accederían a campos inexistentes.
2. **Cuatro pruebas de backend** clavadas a los errores `biometria_activa` / `prevalidacion_activa`.
3. Sobre todo, el **disparador automático (5)** de persona jurídica, que hoy **ignora el error a
   propósito**: convertirlo en éxito lo obligaría a distinguir "creada" de "no hacía falta" sobre un
   payload que ya no lo dice, y cualquier fallo ahí queda mudo.

`409` **con cuerpo informativo** cumple el pedido igual —la UI puede pintar "Ya validada · vigente hasta
el DD/MM/AAAA" sin una segunda llamada— y no rompe a nadie.

### Por qué el reenvío por cambio de correo conserva su comportamiento

`ActorsCommand.ResendIdentityOnEmailChangeAsync` **expira la validación previa y crea una nueva**, y así
se queda. Si la precedencia lo alcanzara, la regla 3 ("en vuelo con enlace no vencido → no enviar")
convertiría *corregir un correo mal escrito* en un **no-op silencioso**: el operador ve el correo
corregido en pantalla y el ciudadano nunca recibe nada. Es una excepción explícita, no un olvido.

### Por qué la concurrencia se cierra en la base de datos

Dos `POST` simultáneos para el mismo documento pasan ambos por el `if` del handler antes de que
cualquiera escriba. Un chequeo en aplicación **no cierra la carrera**, y la tabla
`procedure_instance_biometric_validations` **no tiene `row_version`**, así que tampoco hay control
optimista con el que respaldarlo. La única barrera real es un **índice único parcial** sobre
`(tenant_id, documento_normalizado)` filtrado por los estados en vuelo: la segunda inserción falla en el
motor y el handler la traduce al mismo `409` informativo.

### El riesgo central, con todas las letras

**Unificar la normalización del documento cambia el veredicto del gate de radicación, y el error cae
siempre a favor de radicar.** Hoy, para un documento con mayúsculas o espacios distintos, la ruta por
instancia exige biométrica mientras el listado ya lo da por aprobado; al unificar, esos trámites pasan a
ser **radicables sin biométrica nueva**. Es un cambio de veredicto, no un refactor. Por eso:

- Se adopta **`Trim` + mayúsculas y nada más**. Todo lo más agresivo (quitar puntos, guiones, ceros a la
  izquierda) **fusiona documentos de personas distintas**, y ese error también cae a favor de radicar.
- Se normaliza **en lectura**, con **índices por expresión**, en vez de migrar datos en cinco tablas.
- **Se exige una medición previa** —consulta de solo lectura que cuente cuántos trámites cambian de
  veredicto— **publicada antes de activar el componente**. Sin ese número, la HU de precedencia (#11263)
  no se activa.
- Queda **fuera de alcance, explícitamente**, la tabla de equivalencias de alias (`NIT`/`N`, `CC`/`C`):
  eso no es normalizar, **cambia la clave de identidad** y toca `EsActorJuridico` y `FirmaBaulCobertura`.
  Requiere **su propio ADR**.

---

## Consecuencias

### Lo que se gana
- Una única regla auditable de "¿se envía correo?", con la misma respuesta en los cinco disparadores
- Se acaban los correos duplicados a personas ya aprobadas y vigentes, incluidos los caminos automáticos
- El baúl recupera su precedencia declarada en `ADR-0025-baul-firmas-custodia-y-consumo.md` en una ruta más
- Un enlace vencido deja de crear filas nuevas: se encauza al reenvío, con su tope y su cooldown
- La carrera de dos `POST` simultáneos queda cerrada en el motor, no en la aplicación

### Lo que se pierde
- Una capa más de indirección: precedencia + guards heredados conviven
- El veredicto del gate cambia para un subconjunto de trámites (a favor de radicar) — coste aceptado y
  medido, no descubierto en producción
- Los alias de tipo documental siguen partiendo la identidad de una persona hasta que exista su ADR
- La ruta admin mantiene un comportamiento distinto del resto, por decisión de `ADR-0034-...`

### Cambios operacionales
- **Migración**: índice único parcial sobre `(tenant_id, documento_normalizado)` filtrado por estados en
  vuelo, más índices por expresión para la normalización en lectura
- **Gate de activación**: la medición previa debe estar publicada en la Discussion de la HU #11262 antes
  de activar #11263
- El cuerpo del `409` es información nueva expuesta al cliente: incluye `validatedAt`/`validUntil`, no
  datos biométricos ni PII adicional

---

## Riesgos y mitigación

| Riesgo | Impacto | Mitigación |
|---|---|---|
| La normalización cambia veredictos del gate de radicación | **Crítico** | Medición previa **bloqueante**; `Trim`+mayúsculas y nada más; HU de normalización aislada y mergeable sola |
| Regresión del baúl (el Bug #11141 ya costó una tercera copia del predicado) | Alto | El componente **consume** `FirmaBaulCobertura.Aplica`; test explícito de PJ + baúl vigente + mecanismo "sello de validación de identidad" |
| Romper la transición `borrador → preparado` al tocar el endpoint de trámite | Alto | Conservar la idempotencia por parte; correr `wizard-avance-bloqueado` y `biometric-step` **localmente** (el CI de PR no ejecuta `vitest`) y adjuntar evidencia |
| El índice único parcial rechaza inserciones legítimas | Medio | Filtrar solo por estados en vuelo; traducir la violación al `409` informativo, nunca a un 500 |
| Los alias de tipo documental se cuelan en la normalización | Medio | Prohibición explícita en este ADR; requieren ADR propio |

---

## ADRs relacionados

- `ADR-0025-baul-firmas-custodia-y-consumo.md` (**Aceptado**) — fija la precedencia del baúl (D8). Este ADR
  la **respeta y la extiende** al componente de precedencia; no la modifica.
- `ADR-0034-validacion-identidad-admin-desacoplada.md` (**Aceptado**) — deja la ruta admin sin guard. Este
  ADR **no la toca**: es la razón de que quede fuera de alcance.
- `ADR-0030-persona-entidad-tenant-prevalidacion.md` (**Propuesto**) — modelo de `tramites.persons` y del
  ancla por documento. Sin cambios: la normalización es en lectura.
- `ADR-0036-prevalidacion-natural-tracking-desacoplado-instancia.md` (**Propuesto**) — guard
  `prevalidacion_solo_natural`, que se evalúa **antes** de la precedencia (rechaza la persona jurídica
  standalone sin llegar a preguntar si corresponde enviar).

---

## Notas para agentes

- **Database Agent**: migración con (a) **índice único parcial** sobre `(tenant_id, documento_normalizado)`
  filtrado por los estados en vuelo y (b) **índices por expresión** `upper(btrim(...))` sobre tipo y número
  de documento en las tablas que participen de la resolución por documento. Pasar por
  `checklist-validacion-schema.md`. No se migran datos: la normalización es en lectura. Considerar que la
  tabla **no tiene `row_version`** y este índice es el único control de concurrencia.

- **Backend Agent**: el componente de precedencia es un **servicio de dominio nuevo**, consultado antes
  de los guards actuales, que **no los reemplaza**. Debe **consumir `FirmaBaulCobertura.Aplica`** — está
  prohibido crear una cuarta copia del predicado del baúl. Excepción explícita:
  `ActorsCommand.ResendIdentityOnEmailChangeAsync` **no** pasa por la precedencia. Mantener `409` y añadir
  cuerpo (`motivo`, `status`, `validatedAt`, `validUntil`, `validationId`, `origen`); actualizar
  `contracts/openapi/core-api.v1.yaml` con la respuesta 409 tipada, **sin tocar el 201**. El disparador
  automático de PJ (`ActorsCommand.cs:369`) sigue ignorando el error. La medición de la HU #11262 se
  entrega como **consulta de solo lectura**, no como script que escriba.

- **Frontend Agent**: en los tres disparadores con UI, mostrar destinatario y **confirmar antes de
  enviar**; pintar el `409` informativo sin una segunda llamada ("Ya validada · vigente hasta el
  DD/MM/AAAA"). En los disparadores automáticos (wizard al guardar actor, PJ sin RL) **no hay diálogo**:
  solo el estado resultante visible en la tarjeta de la parte. No recalcular la precedencia en el cliente.

- **QA Agent**: TC de **paridad** — mismo documento, los cinco disparadores toman la misma decisión de
  envío. TC por cada nivel de precedencia (baúl / aprobada vigente / en vuelo vigente / en vuelo vencida →
  reenvío / enviar). TC de regresión crítico: **cambiar el correo sigue reenviando** (no puede volverse
  no-op). TC de baúl: PJ con firma vigente y mecanismo "sello de validación de identidad" (escenario del
  Bug #11141). TC de concurrencia: dos `POST` simultáneos ⇒ una fila y un `409`. Verificar que las cuatro
  pruebas existentes de `biometria_activa`/`prevalidacion_activa` siguen verdes. Suites `vitest`
  ejecutadas **localmente** y adjuntas como evidencia.

- **Security Agent**: revisar que el cuerpo del `409` no filtre PII más allá de lo que el usuario del
  tenant ya ve (sin documento crudo de terceros, sin datos biométricos, sin correo completo si no
  corresponde). Confirmar que la resolución por documento **sigue acotada al tenant** (D4: no cruza
  tenants) y que la normalización no habilita colisiones entre personas distintas. Registrar que el
  cambio de veredicto del gate es **a favor de radicar** y exige la medición previa antes de activarse.

- **Infra Agent**: sin cambios de despliegue. Vigilar el tiempo de la migración de índices sobre la tabla
  más escrita del módulo y planificar su aplicación fuera de pico.

---

## Referencias externas

- Plan técnico v2: `docs/plan-tecnico-ajustes-validacion-identidad.md` (CF-01 a CF-04; decisiones D2, D3, D4, D8–D13)
- Mapa del módulo: `context/modulos/09-identidad-y-prevalidaciones.md`
- Feature #11260 y HUs #11262–#11267 en Azure DevOps
