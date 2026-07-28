# ADR-0033: Subsanación como séptimo estado de negocio del ciclo de vida del trámite

**Fecha**: 2026-07-24
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, stakeholders
**Tags**: arquitectura, backend, modulo-tramites
**Supersedes**: (ninguno — enmienda a ADR-0022, ver sección "ADRs relacionados")

## Contexto

ADR-0022 (Aceptado) fijó **un único vocabulario** de estados de negocio para
`tramites.procedure_instances.status`, taxativo y cerrado en 6 valores en español (`borrador ·
anulado · preparado · entregado · aprobado · rechazado`), con la premisa explícita "no se crea
una tercera capa" (colapsando `ProcedureInstaceStatus` y la máquina de 14 estados de TRAM-12a en
una sola).

El Feature #10863 (HU #10870, #10871, #10872, #10874) necesita reabrir la edición de un trámite
`entregado` o `rechazado` cuando el Organismo de Tránsito (o una integración como Quipux) lo
observa, para que el usuario corrija y **re-radique sin perder el historial ni retroceder a
`borrador`**. Retroceder a `borrador` reutilizaría semántica ya usada para "trámite nunca
radicado" (libera la llave de duplicidad VIN/placa, permite editar actores/documentos desde
cero, no distingue "en corrección tras observación" de "recién creado" en reportes/funnel) y
además ya usa una transición documentada (`rechazado → borrador`, ADR-0022) con un significado
distinto (reinicio completo, no corrección puntual).

El code-review del Feature #10863 marcó **BLOQUEANTE** que la implementación ya en código
(`TramiteStateMachine.cs`, `TramiteEstado.cs`) agrega un 7º valor (`subsanacion`) sin ADR que
enmiende o supere ADR-0022. Este documento cierra ese hallazgo: describe la decisión ya tomada
en el código de esta rama y dos alternativas descartadas, para validación humana.

## Decisión

Se introduce un **séptimo estado de negocio, `subsanacion`**, en `TramiteEstado` y
`TramiteStateMachine` (`Flit.Tramites.Domain/Tramites/Estados/`), persistido en
`procedure_instances.status` igual que los 6 de ADR-0022. `subsanacion` reabre la edición de un
trámite `entregado`/`rechazado` **sin pasar por `borrador`**, preserva el historial completo
(cada transición agrega una fila nueva en `procedure_instance_status_history`, nunca sobrescribe)
y permite una **re-radicación selectiva** que solo re-evalúa los gates de negocio afectados por
los campos corregidos.

## Alternativas consideradas

### Opción 1: Séptimo estado propio `subsanacion` (ELEGIDA)

**Pros:**
- Semántica explícita y auditable: el historial y los reportes distinguen "trámite en corrección
  tras observación" de "trámite recién creado en borrador".
- No reabre por completo la edición de actores/documentos como si fuera un trámite nuevo; el
  candado de inmutabilidad (`trg_field_value_immutable`) y `PatchFieldValuesHandler` habilitan
  edición de forma quirúrgica, igual que en `borrador`, sin heredar el resto de reglas de
  `borrador` (p. ej. no cuenta como "sin radicar" para RF03).
- Habilita re-radicación selectiva (HU #10872): solo se re-evalúan los gates de negocio (`vin` →
  `vehicle_state`, resto → `preparation_gate`) que dependen de los campos efectivamente
  corregidos, evitando pedir de nuevo identidad/consultas ya vigentes (AC2).
- `EstadosEnProceso` incluye `subsanacion`: el bloqueo de duplicidad VIN/placa
  (`DuplicateActiveProcedurePolicy`) sigue activo mientras se corrige — un trámite en
  subsanación no libera la llave para que otro usuario radique el mismo vehículo en paralelo.
- Cambio de código acotado: `TramiteStateMachine` gana 3 transiciones nuevas
  (`entregado → subsanacion`, `rechazado → subsanacion`, `subsanacion → entregado`); no toca
  ninguna transición existente de ADR-0022.

**Contras:**
- Contradice literalmente la premisa "6 estados, vocabulario cerrado" de ADR-0022; requiere esta
  enmienda documentada explícitamente.
- Superficie nueva a mantener: el trigger `trg_field_value_immutable` crece una rama
  (`borrador OR subsanacion`), el funnel de `TramitesTable`/`EstadoFunnel` necesita una decisión
  de UX explícita (hoy: sin tarjeta propia, solo cuenta para completitud del tipo), y todo
  consumidor externo del vocabulario de estados (webhooks OT, analítica) debe aprender el 7º
  valor.
- Los tests de máquina de estados (`StateMachineTests.cs`) y los nuevos
  (`SubsanacionGateMapTests.cs`, `SubsanacionObservationTests.cs`, `FieldValueSnapshotTests.cs`)
  deben cubrir combinatoria adicional (entregado/rechazado → subsanacion → entregado, con y sin
  snapshot base).

**Esfuerzo:** M
**Riesgos:** Analítica/reportes que iteren sobre los "6 estados de ADR-0022" de forma hardcodeada
(fuera de `TramiteEstado.Todos`) quedan desactualizados hasta que se les enseñe el 7º valor;
mitigado porque el enum central (`TramiteEstado.Todos`) ya lo incluye y es la fuente de verdad.

### Opción 2: Reutilizar `borrador` para la corrección post-envío

**Pros:**
- Cero estados nuevos: se mantiene el vocabulario cerrado de ADR-0022 sin modificarlo.
- Reutiliza toda la lógica de edición y el trigger de inmutabilidad ya existentes tal cual,
  sin tocar `trg_field_value_immutable`.

**Contras:**
- Pierde trazabilidad: no hay forma de distinguir en el historial/funnel un trámite que nunca se
  radicó de uno que se radicó, fue observado, y está en corrección — información relevante para
  SLA y reportes del OT.
- Rompe semántica ya usada: `rechazado → borrador` (ADR-0022) significa "reinicio completo",
  mientras que la corrección post-observación es puntual (solo los campos señalados). Igualarlas
  induce a reabrir edición de actores/documentos completos cuando el diseño de HU #10872 exige
  re-evaluar solo los gates de lo corregido.
- `EstadosEnProceso` ya incluye `borrador`; reusarlo no distingue "trámite activo en corrección"
  de "trámite activo nunca enviado" para el bloqueo de duplicidad, perdiendo granularidad de
  negocio sin ganar nada a cambio.
- No hay forma de implementar la re-radicación selectiva (HU #10872) sin una marca de estado
  distinta que permita anclar el snapshot base (`FieldSnapshot`) al momento exacto de la
  observación.

**Esfuerzo:** S
**Riesgos:** Alto riesgo de negocio (pérdida de trazabilidad, confusión operativa OT/CEA) por
ahorro de esfuerzo bajo; descartada.

### Opción 3: Máquina paralela `SttWorkflow` desacoplada del estado de negocio

**Pros:**
- Ya existe parcialmente en el código (`Flit.Tramites.Domain/Tramites/Services/SttWorkflow.cs`,
  `TramiteEstadoStt`) con un estado `Subsanacion` propio y transiciones análogas
  (`Radicado/EnValidacion/EnTramite/Rechazado → Subsanacion`), pensado para paridad con un
  workflow STT legado.
- No tocaría `TramiteEstado`/`TramiteStateMachine` ni el vocabulario de ADR-0022 en absoluto:
  aislaría el concepto de "subsanación" en una capa secundaria, similar a `plate_flow_status`
  (HU #10785).

**Contras:**
- Repite exactamente el problema que ADR-0022 eliminó: **dos capas de estado desacopladas**
  (`TramiteEstado` vs `TramiteEstadoStt`), con el riesgo de que una quede sin cablear a runtime
  como pasó con la máquina de 14 estados de TRAM-12a. Hoy `SttWorkflow`/`TramiteEstadoStt` solo
  se referencian desde sus propios archivos y tests — el mismo patrón de "máquina huérfana" que
  motivó la reescritura de ADR-0022.
  a un status STT paralelo, y el trigger de inmutabilidad, el bloqueo de duplicidad
  (`EstadosEnProceso`) y el funnel tendrían que leer de dos fuentes o traducir entre ambas,
  duplicando lógica de mapeo.
- El editable/gestión-cerrada de `SttWorkflow` (`ExpedienteEditable`, `GestionCerrada`) ya
  duplica reglas que `PatchFieldValuesHandler` y el trigger BD implementan sobre
  `TramiteEstado`, generando dos lugares que pueden divergir silenciosamente.

**Esfuerzo:** L
**Riesgos:** Regresión directa de ADR-0022 (reintroduce la "tercera capa" que se eliminó);
descartada.

## Tradeoff aceptado

Se acepta ampliar el vocabulario cerrado de ADR-0022 de 6 a 7 estados porque la alternativa de
no ampliarlo (Opción 2) sacrifica trazabilidad y granularidad de negocio que HU #10871/#10872
requieren explícitamente (checklist de observación persistido, re-radicación selectiva por
diff de campos), y la alternativa de aislarlo en una capa paralela (Opción 3) reintroduce el
defecto arquitectónico exacto que ADR-0022 corrigió. Mantener **una sola fuente de verdad**
(`TramiteEstado`/`TramiteStateMachine`, con 7 valores en vez de 6) es más simple de auditar y
operar que dos máquinas coordinadas.

## Consecuencias

### Lo que se gana
- Trazabilidad completa: historial y reportes distinguen "en subsanación" de "borrador"/"nunca
  radicado".
- Re-radicación selectiva (HU #10872): no se re-solicitan identidad/consultas vigentes;
  solo se re-evalúan los gates de negocio (`vehicle_state`, `preparation_gate`) que dependen de
  los campos corregidos.
- El bloqueo de duplicidad VIN/placa se mantiene correcto mientras el trámite está en corrección
  (`subsanacion` cuenta como "en proceso").
- Los dos disparadores de observación (OT manual con checklist, callback de integración como
  Quipux con solo motivo) quedan unificados en un único value object
  (`SubsanacionObservation`, persistido en `status_history.metadata`), sin bifurcar el modelo de
  datos.

### Lo que se pierde
- El vocabulario de ADR-0022 deja de ser literalmente "6 estados, cerrado"; cualquier código o
  documentación que asuma esa cardinalidad exacta (fuera de `TramiteEstado.Todos`) debe
  actualizarse.
- El funnel de `TramitesTable`/`EstadoFunnel` no tiene hoy una tarjeta propia para
  `subsanacion` (decisión de UX explícita en HU #10874, documentada en el código con comentario
  `FUNNEL_ORDER no la incluye`): un trámite en subsanación es "invisible" en el conteo visual del
  embudo aunque sí se cuenta en `estadoCounts` por completitud de tipo. Queda como decisión de UX
  pendiente de validar si necesita visibilidad propia en una iteración posterior.

### Cambios operacionales
- El trigger BD `tramites.trg_field_value_immutable` crece una rama de condición
  (`v_status = 'borrador' OR v_status = 'subsanacion'`), preservando íntegras las ramas de
  `plate_flow_status` de HU #10785 (migración
  `20260724120000_HU10870_SubsanacionEditableTrigger`).
- Cualquier analítica, seed o SQL con literales de estado de ADR-0022 debe aprender el 7º valor
  o excluirlo explícitamente si no aplica (p. ej. funnel de conversión borrador→aprobado).
- Webhooks/integraciones externas que consuman `status` (contrato documentado en ADR-0022) deben
  tolerar `subsanacion` como valor válido no mapeado a los 6 originales.

## ADRs relacionados

- [ADR-0022] — Estados de negocio del ciclo de vida del trámite (N 03). Este ADR-0033 es una
  **enmienda** que amplía su vocabulario cerrado de 6 a 7 estados; no se marca `Supersedes`
  formal porque no invalida ninguna decisión de ADR-0022 (las 6+transiciones originales quedan
  intactas), solo la extiende. Si el Líder Técnico prefiere tratarlo como reemplazo total,
  ADR-0022 debería re-emitirse con `Status: Superado por ADR-0033`.
- [ADR-0018] — Modelo de datos fase 1 (contexto de `procedure_instances`, `status_history`).

## Notas para agentes

- **Backend Agent**: `TramiteEstado.Subsanacion` y las 3 transiciones nuevas
  (`entregado/rechazado → subsanacion`, `subsanacion → entregado`) ya están implementadas en
  `Flit.Tramites.Domain/Tramites/Estados/`. Cualquier nuevo endpoint/handler que enumere estados
  de negocio debe usar `TramiteEstado.Todos`/`TramiteStateMachine`, nunca una lista hardcodeada
  de 6 valores.
- **Frontend Agent**: `lib/tramites/estados.ts` ya expone `subsanacion` con label ("En
  subsanación") y chip propio. Si se decide dar visibilidad propia en el funnel
  (`EstadoFunnel.tsx`, hoy sin tarjeta para `subsanacion`), es un cambio de UX a validar con
  producto, no una corrección de bug.
- **QA Agent**: cubrir explícitamente la combinatoria `entregado → subsanacion → entregado` y
  `rechazado → subsanacion → entregado`, con y sin `FieldSnapshot` base (fail-safe de
  `SubsanacionGateMap.NoBaselineFallback`), y el bloqueo de duplicidad VIN/placa mientras el
  trámite está en `subsanacion`.
- **Security Agent**: `SubsanacionObservation` persiste checklist y snapshot de campos en
  `status_history.metadata` (jsonb); confirmar que no se filtran datos sensibles de identidad en
  ese JSON (hoy solo guarda `field_key`/valor canónico, no documentos ni biometría).
- **Infra Agent**: sin cambios de infraestructura; la migración
  `20260724120000_HU10870_SubsanacionEditableTrigger` corre como las demás migraciones
  autodescubribles (`[DbContext]`/`[Migration]` inline).

## Referencias externas

- Feature #10863 (Gestión del Trámite), HU #10870, #10871, #10872, #10874.
- Código de referencia: `services/core-api/src/Flit.Tramites.Domain/Tramites/Estados/TramiteEstado.cs`,
  `TramiteStateMachine.cs`; `services/core-api/src/Flit.Tramites.Domain/Tramites/ValueObjects/SubsanacionObservation.cs`;
  `services/core-api/src/Flit.Tramites.Domain/Tramites/Services/SubsanacionGateMap.cs`;
  `services/core-api/src/Flit.Infrastructure/Migrations/20260724120000_HU10870_SubsanacionEditableTrigger.cs`.
