# ADR-0040: Tracking de identidad agrupado por persona

**Fecha**: 2026-08-05
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, equipo core-api, equipo tramites
**Tags**: arquitectura, backend, frontend, identidad, tramites, feature-11261
**Supersedes**: **decisión 2 de `ADR-0036-prevalidacion-natural-tracking-desacoplado-instancia.md`**
(tracking anclado a `validationId`). La **decisión 1 de ese ADR —prevalidación restringida a persona
natural— sigue vigente y no se toca.**
**Relacionado**: `ADR-0030-persona-entidad-tenant-prevalidacion.md` (Propuesto), `ADR-0039-precedencia-unica-decision-envio-identidad.md` (Propuesto), `ADR-0021-analitica-fuente-datos-tramites.md` (Aceptado)
**HU origen**: Feature #11261 — HUs #11268, #11269, #11270, #11271, #11272, #11273
**Plan técnico**: `docs/plan-tecnico-ajustes-validacion-identidad.md` (v2, CF-05 a CF-07, D1/D5/D6/D7/D13)

---

## Contexto

La unidad de trabajo del módulo de identidad es hoy **la validación**, no la persona:

- La grilla de Validaciones muestra **una fila por validación**. Una persona con siete intentos ocupa
  **siete filas**, sin ninguna señal de que son la misma persona.
- El detalle y la bitácora son **por `validationId`** —la decisión 2 de
  `ADR-0036-prevalidacion-natural-tracking-desacoplado-instancia.md`—, así que pintar el historial
  completo de una persona costaría **N peticiones** (una por validación), con **polling de 5 s** encima.
- `GET /biometric-validations` lo comparten **tres superficies**: `Validaciones.tsx`,
  `PrevalidacionesModule.tsx` y **`Dashboard.tsx`, que hace dos llamadas solo para sus tarjetas**. El
  campo de total es **el mismo** para la grilla y para las tarjetas (`Total` = `stats.Total`, un campo
  para dos semánticas), y la rama del filtro `motivoRechazo` **pagina en memoria sobre un tope de 2000
  filas**.
- **Toda petición es mono-tenant**: los endpoints exigen la cabecera `X-Tenant-Id` y responden **400** sin
  ella, también al SuperAdmin. En un resultado **nunca** hay más de un tenant.

El pedido de negocio es ver **una fila por persona** —con su estado, su contador y su peor alerta— y poder
abrir todas sus validaciones de una vez. Eso choca de frente con el ancla por `validationId` que fijó
`ADR-0036-...`, y por eso hace falta un ADR que la supersede.

---

## Decisión

**Vista y detalle de identidad agrupados por persona**, con la clave `(tenant_id, documento
normalizado)` —la misma normalización canónica que fija
`ADR-0039-precedencia-unica-decision-envio-identidad.md`—, expuestos en un **modo o endpoint propio** que
**no altera** el comportamiento actual de los otros dos consumidores (Dashboard y Prevalidaciones).

1. **Grilla agrupada.** Cada fila es una persona, con: estado de su validación **más reciente**, contador
   de validaciones, y **peor alerta** según la severidad `atascada > rechazada > expirada > por_vencer`,
   restringida a validaciones no terminales o de los últimos 30 días.
2. **Consulta con `DISTINCT ON`** en SQL crudo dentro del repositorio, respaldada por un **índice por
   expresión** sobre el documento normalizado.
3. **Detalle multi-validación en una sola petición**: las N validaciones de la persona con sus trámites
   asociados y su tracking, de más reciente a más antigua, con **N acotado a 50 + paginación**, y polling
   detenido cuando **todas** son terminales.
4. **Filtros**: en modo agrupado solo aplican los de semántica de persona (`documento`, `estado`,
   `vigencia`, `fechas`). Los de semántica de validación (`referenceNumber`, `modalidad`, `partyRole`,
   `provider`, `score`, `motivoRechazo`) **se deshabilitan** y la UI indica por qué.
5. **Indicadores de cabecera (KPI): siguen contando validaciones**, con su etiqueta explícita.
6. **Sin nivel de empresa**, pese a estar en el pedido original.
7. **Atascadas**: acordeón **por persona**, conservando *Reintentar* por fila y *Reintentar todos*.

---

## Alternativas consideradas

### Opción 1: Modo agrupado en endpoint/modo propio (elegida)

Una superficie nueva —parámetro de modo o ruta hermana— que devuelve filas por persona, con su propia
proyección y su propio contrato. Los tres consumidores actuales de `GET /biometric-validations` siguen
recibiendo exactamente lo mismo que hoy.

**Pros:**
- **Cero regresión** en Dashboard y Prevalidaciones: el contrato que consumen no cambia
- Permite un DTO limpio por persona (contador, peor alerta) sin contaminar el DTO de validación, que ya
  sirve a tres audiencias
- Los totales agrupados y los totales de validación pueden coexistir sin sobrecargar un mismo campo
- El detalle multi-validación se diseña de entrada con tope y paginación, en vez de heredar el cap de 2000

**Contras:**
- Dos superficies de listado que mantener y documentar
- Duplica parte de la lógica de filtros que ya existe en el listado plano
- Obliga a explicar en la UI por qué unos filtros desaparecen en modo agrupado

**Esfuerzo:** M
**Riesgos:** Bajo-medio — el riesgo real es el SQL crudo, no el contrato.

---

### Opción 2: Agrupar dentro del endpoint existente

Añadir la agrupación a `GET /biometric-validations` y que todos sus consumidores la reciban.

**Pros:**
- Un solo endpoint de listado; sin duplicación de filtros ni de proyección
- Ningún consumidor tiene que aprender una ruta nueva

**Contras:**
- **Rompe el Dashboard**: sus dos llamadas cuentan validaciones para las tarjetas; agrupadas devolverían
  personas y las tarjetas mentirían
- **Rompe Prevalidaciones**, cuya unidad de gestión es la validación (editar, reenviar) y no la persona
- **Falsea los totales**: `Total` es hoy un solo campo para la grilla y para las tarjetas; agrupado
  significaría dos cosas a la vez
- La rama del filtro `motivoRechazo` **pagina en memoria sobre 2000 filas**: agrupada devolvería conteos
  simplemente falsos
- Cambio breaking de un contrato ya versionado y en uso

**Esfuerzo:** M
**Riesgos:** Alto — tres consumidores afectados y conteos incorrectos por diseño; se descarta.

---

### Opción 3: Agrupar en el cliente

Traer las filas planas y agruparlas por documento en el frontend.

**Pros:**
- Cero cambios de backend, cero SQL nuevo, cero migración
- Entrega inmediata de una versión visual del pedido

**Contras:**
- **Imposible con paginación server-side**: la misma persona reaparece en la página siguiente, y el
  contador de cada grupo es el de la página, no el real
- Los KPI y el conteo total quedarían desalineados con lo que muestra la grilla
- Empeoraría con el polling: cada refresco puede recomponer grupos distintos
- Traería más filas de las necesarias sobre la tabla más escrita del módulo

**Esfuerzo:** S
**Riesgos:** Alto — resultado incorrecto, no lento; se descarta.

---

## Tradeoff aceptado

Se elige la **Opción 1**: se paga mantener dos superficies de listado a cambio de **no romper a los tres
consumidores** del endpoint actual y de poder diseñar el contrato agrupado con sus propias garantías
(tope, paginación, semántica de totales). La Opción 2 es más barata en apariencia y termina entregando
conteos falsos en Dashboard y Prevalidaciones; la Opción 3 no es una aproximación, es un resultado
incorrecto que la paginación garantiza.

### Por qué supersede la decisión 2 de `ADR-0036-...`

Aquel ADR resolvió, correctamente para su alcance, que el tracking se consultara **por `validationId`**
sin exigir un `instanceId` —lo que desbloqueó las prevalidaciones standalone—. El requerimiento ahora es
distinto: la unidad de consulta pasa a ser **la persona**. Mantener el ancla por `validationId` como única
puerta obligaría a N peticiones más polling para pintar un historial. Se supersede **esa decisión
concreta**; el endpoint por `validationId` **sigue existiendo** y sirviendo al detalle de una validación
individual. La **decisión 1 de `ADR-0036-...` (prevalidación solo persona natural) permanece intacta**.

### Por qué `DISTINCT ON` y no `GROUP BY` + `COUNT`

`GROUP BY` + `COUNT` no da "la validación más reciente" con sus campos: obliga a una segunda pasada (o a
una subconsulta correlacionada) sobre **la tabla más escrita del módulo**. `DISTINCT ON (documento
normalizado) ... ORDER BY created_at DESC` la resuelve en **una sola pasada**, devolviendo la fila
completa de la más reciente.

El coste es explícito: **EF Core no expresa `DISTINCT ON`**, así que entra **SQL crudo en el
repositorio**. Es un precedente que **ya existe** en el repo —analítica consulta con SQL crudo
cross-schema, según `ADR-0021-analitica-fuente-datos-tramites.md`—, de modo que no se está abriendo una
puerta nueva, sino usando una existente y acotada al repositorio.

### Por qué hace falta un índice por expresión

La agrupación es sobre el documento **normalizado** (`Trim` + mayúsculas, por
`ADR-0039-precedencia-unica-decision-envio-identidad.md`), no sobre las columnas crudas. Un índice de
columnas crudas **no lo sirve**: la expresión de la consulta no coincide con la del índice y el motor cae
a un recorrido secuencial. Además, la tabla hoy ya carece de un índice `(tenant_id, created_at)` que
respalde el orden de la grilla. Se requiere un índice **por expresión** que cubra la clave de agrupación
y el orden.

### Por qué se descarta agrupar por empresa

Estaba en el pedido original y **no se puede construir hoy**: la cabecera `X-Tenant-Id` es obligatoria y
su ausencia devuelve **400 también al SuperAdmin**, así que el resultado es siempre de **una sola
compañía** y el nivel de empresa sería un **grupo único**, sin información. Habilitarlo de verdad
exigiría un endpoint **multi-tenant** nuevo, **autorización del reencolado cross-tenant** (hoy el requeue
opera dentro del tenant) y **tocar el middleware de tenant**, que es la única frontera de aislamiento real
del módulo. Es un cambio de seguridad, no "un campo más", y requeriría su propio ADR.

### Por qué unos filtros se deshabilitan en vez de aplicarse a medias

`referenceNumber`, `modalidad`, `partyRole`, `provider`, `score` y `motivoRechazo` son atributos **de una
validación**, no de una persona. Aplicarlos a la fila agrupada admite dos lecturas incompatibles —"personas
con **alguna** validación que cumple" y "personas cuya **más reciente** cumple"— y el usuario no puede
distinguir cuál está viendo: el contador diría una cosa y el filtro otra. Deshabilitarlos con una
explicación visible es honesto; aplicarlos a la más reciente en silencio produce conteos que el usuario
interpretará mal. (Nota: `motivoRechazo` además pagina en memoria sobre un cap de 2000 filas — agrupado,
sus conteos serían directamente falsos.)

### Por qué los KPI siguen contando validaciones

Los indicadores de cabecera miden **carga operativa**: cuántas validaciones hay en proceso, atascadas o
por vencer. Ese es el trabajo pendiente real, y no se reduce porque tres de ellas sean de la misma
persona. Cambiarlos a personas rompería además la continuidad histórica de las tarjetas del Dashboard.
Se conservan contando validaciones, **etiquetados explícitamente** para que no se confundan con el
contador por persona de la grilla.

---

## Consecuencias

### Lo que se gana
- Una fila por persona: siete intentos dejan de ocupar siete filas y se ven como un historial
- El historial completo de una persona en **una sola petición**, en vez de N + polling
- Peor alerta por persona con severidad explícita (`atascada > rechazada > expirada > por_vencer`)
- Atascadas navegables por persona, conservando *Reintentar* por fila y *Reintentar todos*
- Dashboard y Prevalidaciones **intactos**

### Lo que se pierde
- Dos superficies de listado que mantener, con filtros que divergen entre modos
- **SQL crudo en el repositorio**: fuera del alcance de las validaciones de EF Core, requiere revisión
  manual y test de integración contra base real
- El nivel de empresa del pedido original queda **descartado**, no pospuesto: hoy no es construible
- Los filtros de validación desaparecen en modo agrupado, lo que exige explicación en la UI

### Cambios operacionales
- **Migración**: índice por expresión sobre el documento normalizado (y el orden por `created_at`) en
  `procedure_instance_biometric_validations`
- Nuevo contrato en `contracts/openapi/core-api.v1.yaml` para el modo/endpoint agrupado y para el detalle
  multi-validación (con tope 50 y paginación declarados)
- Sin migración de datos: la normalización se aplica en lectura

---

## Riesgos y mitigación

| Riesgo | Impacto | Mitigación |
|---|---|---|
| El SQL crudo se desalinea del modelo al evolucionar el schema | Medio | Confinado al repositorio; revisión por `db-schema-validator`; test de integración contra base real |
| Regresión silenciosa en Dashboard o Prevalidaciones | Alto | El modo agrupado vive en superficie propia; test de no-regresión que compare la respuesta del Dashboard antes/después |
| Consulta agrupada lenta sobre la tabla más escrita del módulo | Medio | `DISTINCT ON` en una pasada + índice por expresión; medir con volumen realista antes de activar |
| Detalle multi-validación sin tope degenera con personas de historial largo | Medio | Tope 50 + paginación desde el diseño; polling detenido con todas terminales |
| Divergencia de normalización con `ADR-0039-...` | Alto | Ambas consumen **la misma** función canónica (`Trim`+mayúsculas); prohibido reimplementarla |

---

## ADRs relacionados

- `ADR-0036-prevalidacion-natural-tracking-desacoplado-instancia.md` (**Propuesto**) — este ADR
  **supersede su decisión 2** (tracking anclado a `validationId` como única puerta). Su **decisión 1**
  (prevalidación solo persona natural) **sigue vigente**. El endpoint por `validationId` no se elimina.
- `ADR-0039-precedencia-unica-decision-envio-identidad.md` (**Propuesto**) — fija la función canónica de
  normalización del documento que este ADR usa como clave de agrupación.
- `ADR-0030-persona-entidad-tenant-prevalidacion.md` (**Propuesto**) — modelo de `tramites.persons`; sin
  cambios: la agrupación es por documento normalizado, no por `person_id`.
- `ADR-0021-analitica-fuente-datos-tramites.md` (**Aceptado**) — precedente de SQL crudo en el repositorio.

---

## Notas para agentes

- **Database Agent**: **índice por expresión** sobre `(tenant_id, upper(btrim(documento)))` más el orden
  por `created_at DESC` en `procedure_instance_biometric_validations`. Revisar el SQL crudo del
  `DISTINCT ON` contra `checklist-validacion-schema.md`. Sin migración de datos. Aplicar el índice fuera
  de pico: es la tabla más escrita del módulo.

- **Backend Agent**: consulta agrupada con **`DISTINCT ON` en SQL crudo**, confinada al repositorio (EF
  Core no lo expresa; el precedente es analítica). **No** modificar la proyección ni los totales del
  listado plano: Dashboard y Prevalidaciones deben devolver byte a byte lo de hoy. El detalle
  multi-validación es **una sola petición** con tope 50 y paginación. La clave de agrupación usa **la
  función canónica de normalización** de `ADR-0039-...` — no reimplementarla. Los KPI siguen contando
  validaciones. Documentar en `contracts/openapi/core-api.v1.yaml` el modo agrupado y el detalle.

- **Frontend Agent**: modo agrupado en la grilla con contador y expansión; acordeón por persona en el
  panel de atascadas conservando *Reintentar* por fila y *Reintentar todos*. **Deshabilitar** los filtros
  de validación en modo agrupado **e indicar por qué** (no ocultarlos en silencio). Etiquetar los KPI como
  "validaciones", no "personas". Detener el polling cuando todas las validaciones de la persona sean
  terminales. No agrupar en el cliente en ningún caso: con paginación server-side el resultado es
  incorrecto.

- **QA Agent**: TC principal — 7 validaciones del mismo documento ⇒ **1 fila con `count=7`** y el estado
  de la más reciente. TC de severidad de la peor alerta (`atascada > rechazada > expirada > por_vencer`).
  TC de **no-regresión del Dashboard**: sus dos llamadas devuelven exactamente lo mismo que antes del
  cambio. TC de Prevalidaciones sin cambios de comportamiento. TC de normalización: documentos que
  difieren en espacios/mayúsculas caen en la **misma** fila. TC del detalle: persona con >50 validaciones
  pagina y no revienta. TC de que los filtros deshabilitados no se envían al backend.

- **Security Agent**: confirmar que el modo agrupado mantiene el `tenant_id` como frontera dura y que el
  documento normalizado **no** habilita colisiones entre personas distintas del tenant. Verificar que el
  detalle multi-validación no expone campos que el listado plano ya saneaba (sin secretos de webhook, sin
  tokens, sin rutas de fotos). Registrar que la agrupación por empresa se **descarta** y que habilitarla
  exigiría endpoint multi-tenant, autorización del reencolado cross-tenant y cambios en el middleware de
  tenant — decisión de seguridad con ADR propio.

- **Infra Agent**: sin cambios de despliegue. Vigilar el plan de ejecución de la consulta agrupada tras
  crear el índice y el tiempo de creación del índice sobre la tabla más escrita del módulo.

---

## Referencias externas

- Plan técnico v2: `docs/plan-tecnico-ajustes-validacion-identidad.md` (CF-05 a CF-07; decisiones D1, D5, D6, D7, D13)
- Mapa del módulo: `context/modulos/09-identidad-y-prevalidaciones.md`
- ADR superado parcialmente: `services/core-api/docs/adr/ADR-0036-prevalidacion-natural-tracking-desacoplado-instancia.md`
- Feature #11261 y HUs #11268–#11273 en Azure DevOps
