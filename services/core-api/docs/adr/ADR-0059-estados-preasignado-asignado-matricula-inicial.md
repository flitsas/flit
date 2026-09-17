# ADR-0059: `preasignacion` y `asignado` como estados de negocio de la matrícula inicial; eliminación del sub-estado `plate_flow_status`

**Fecha**: 2026-09-16
**Status**: Propuesto
**Deciders**: Willyn Londoño Calle (Líder Técnico), Andrés Jimenez Guerra (PO), Samuel Cárdenas (Dev), Architecture Agent
**Tags**: arquitectura, backend, frontend, modelo-de-datos, modulo-tramites, modulo-admin-ot, maquina-de-estados
**Enmienda a**: ADR-0022 (vocabulario de estados) · **Actualiza**: ADR-0033 (re-radicación), ADR-0046 (disparador correo de placa)

## Contexto

ADR-0022 fijó un vocabulario único de estados de negocio en `procedure_instances.status`
(`borrador · anulado · preparado · entregado · aprobado · rechazado`, luego `revocado` por HU #12166).
El Feature #10587 (HU #10785) añadió la ruta de placa de la matrícula inicial **sin tocar ese
vocabulario**: el progreso de placa vive en un sub-estado ortogonal `plate_flow_status ∈ {null,
preasignado, asignado, terminado}` mientras `status` permanece en `entregado`, con un trigger DDL
(`trg_autoset_plate_flow_status`) que lo autofija por `gate_profile.requiresPlateRequest`.

Ese diseño produjo el dolor que motiva la Epic #12549: cuatro situaciones distintas (esperando placa
del OT, placa asignada y pendiente de SOAT/impuestos, listo para decisión, y ruta estándar) se ven
todas como «Entregado» en gestor, OT, reportes e ICT, y el lenguaje no coincide con FLIT 1
(`Sent` / `Assigned`). Además, hoy la ruta de placa solo se activa si la compañía **y** el OT tienen
la preasignación configurada (AND de tres flags): la Epic #12550 pide que la elección de ruta sea
del usuario en cada trámite.

Decisiones de producto ya cerradas por el PO (comentarios 29455669 en #12549, 2026-09-15):
(A) el OT **ve** `asignado` en solo lectura y conserva corrección de placa (ventana 1 h) y revocación
(#12156); (B) la configuración de preasignación deja de decidir la ruta y queda solo para el
inventario de rangos; (C) el rechazo del OT desde `preasignacion` es un `rechazado` normal que se subsana por la ruta
existente (flag `subsanacion_activa` sobre `rechazado`, el gestor corrige el expediente y **re-radica**;
al re-radicar el sistema decide de nuevo por placa), sin atajo nuevo hacia `preasignacion` ni `entregado`, y queda marcado como «rechazado desde
preasignación» para que el gestor lo priorice (reunión PO–Dev 2026-09-16, sustituye la respuesta
escrita C). Restricciones: Quipux debe seguir con el salto único `preparado → entregado` (ADR-0051);
traspasos y otros tipos no cambian; `subsanacion` sigue siendo flag, no estado (ADR-0033).

## Decisión

Promover el sub-estado a **dos estados de negocio reales** en `status` — `preasignacion` (UI
«Preasignación») y `asignado` — exclusivos de los tipos con `requiresPlateRequest = true`; **eliminar**
`plate_flow_status` (columna, trigger, función y máquina); y gobernar las aristas de placa con una
**política por capacidad del tipo** (`TransitionContext`: `requiresPlateRequest`, `hasPlate`, actor,
`subsanacionActiva`) encima de la máquina base de ADR-0022. `terminado` desaparece: es `entregado`.

Catálogo resultante: `borrador · anulado · preparado · preasignacion · asignado · entregado · aprobado ·
rechazado · revocado`.

Transiciones nuevas o modificadas:

| Transición | Actor | Condición |
|---|---|---|
| `preparado → preasignacion` | Sistema (al radicar) | `requiresPlateRequest` ∧ sin placa |
| `preparado → entregado` | Sistema / Quipux | ¬`requiresPlateRequest` ∨ con placa; Quipux siempre |
| `preasignacion → asignado` | OT | Asigna placa; regenera FUR y dispara correo (ADR-0046) |
| `preasignacion → rechazado` | OT | Rechazado normal; persiste marca «rechazado desde preasignación» (distintivo en UI del gestor) |
| `asignado → entregado` | Gestor («Enviar al OT») | El gestor gestiona SOAT/impuestos sin salir de `asignado`; al enviar se validan checks + gate SOAT vs RUNT de `CompletePlateFlow` |
| `asignado → preasignacion` | OT | «Liberar placa» |
| `rechazado → preasignacion` \| `entregado` | Gestor (re-radicar) | **Solo** con `subsanacion_activa` (ADR-0033): sin el flag se rechaza con `transicion_no_permitida`. Sin placa y tipo que pide placa → `preasignacion`; en otro caso → `entregado`. Rechazar no pasa por `borrador` (esa arista sigue existiendo para el gestor, pero no es la subsanación) |
| `rechazado → anulado` | Gestor | Sin cambio |

Visibilidad OT (`TramiteEstado.RecibidosPorOrganismo`): + `preasignacion` (asignar placa / rechazar) y
`asignado` (RO: corregir 1 h / liberar placa). Filtros del OT con el nombre real del estado, sin sufijo «OT». Aprobar/Rechazar decisión: solo desde `entregado`.

Migración de datos (idempotente, SQL cruda, `row_security = off`): `entregado+preasignado → preasignacion` (mapeo explícito, el literal cambia),
`entregado+asignado → asignado`, `entregado+terminado → entregado`, resto igual; fila sintética en
`procedure_instance_status_history` con `{"motivo":"migracion_plate_flow_a_status"}`. El DROP de la
columna es el **último commit** del PR, tras cero lecturas en runtime.

## Alternativas consideradas

### Opción 1: Estados reales + policy por `requiresPlateRequest` *(elegida)*

**Pros:**
- Un solo vocabulario: gestor, OT, reportes, ICT y correos leen `status` y nada más.
- Paridad con FLIT 1 y con el lenguaje del PO (Epic #12549 literal).
- La regla «¿este tipo pide placa?» ya existe (`PlateRequestGate.AppliesTo`); se reutiliza, no se inventa.
- Tipos MATRICULAS sin placa (p. ej. cancelación de matrícula) quedan fuera por construcción.
- Elimina el trigger DDL que hoy decide estado «a espaldas» de `TramiteLifecycleService`.

**Cons:**
- Migración destructiva (columna + trigger + función) y ~35 archivos de test que hoy conocen el sub-estado.
- La máquina deja de ser un diccionario plano: 8 aristas necesitan contexto.

**Esfuerzo:** L
**Riesgos:** regresión Quipux si una arista intermedia se cuela (mitigado con test explícito de actor Quipux); datos DEV/QA huérfanos (mitigado con conteos antes/después).

### Opción 2: Bifurcar solo por `family = MATRICULAS`

**Pros:**
- Más simple de leer: «si es matrícula, pasa por preasignacion».
- Sin dependencia del snapshot de `gate_profile`.

**Cons:**
- Rompe los tipos de la familia MATRICULAS que **no** piden placa (cancelación, duplicado): irían a `preasignacion` sin razón.
- Contradice ADR-0050 (el tipo de trámite es la fuente única de conformación, no la familia).
- Ya se descartó en HU #10785 por el mismo motivo (el trigger usa `requiresPlateRequest`, no la familia).

**Esfuerzo:** M
**Riesgos:** alto — bug funcional inmediato en cualquier tipo MATRICULAS sin placa.

### Opción 3: Mantener `plate_flow_status` y proyectar etiquetas («Preasignación», «Asignado») en UI/API

**Pros:**
- Cero migración, cero riesgo de datos.
- El PO ve los nombres que pidió.

**Cons:**
- No cumple la Epic: siguen existiendo dos fuentes de verdad, y reportes/ICT/consultas/export siguen leyendo «entregado».
- Cada consumidor nuevo tiene que recordar componer `status + plate_flow_status` (hoy ya se olvidó en `OtQueryRepository` y en ICT).
- Perpetúa el trigger DDL que muta estado fuera del lifecycle.

**Esfuerzo:** S
**Riesgos:** deuda permanente; el dolor de producto se mantiene en todo lo que no sea la bandeja.

## Tradeoff aceptado

Se acepta una migración destructiva y una máquina con contexto a cambio de una sola fuente de verdad
de estado. El coste es un PR grande con commits por HU y un data-fix probado contra
`flitdev_vps_migrado`; el beneficio es que ningún consumidor (bandeja, reportes, ICT, correos,
consultas) vuelve a tener que componer dos columnas para saber en qué está un trámite. La Opción 3
era más barata pero no resuelve lo que el PO pidió; la Opción 2 es más simple pero incorrecta para
tipos MATRICULAS sin placa.

## Consecuencias

### Lo que se gana
- El gestor distingue en listado, resumen y dashboard un rechazo sin placa («Rechazado preasignación») de un rechazo tras revisión, y lo subsana primero.
- `status` describe la situación real; «Entregado» vuelve a significar «listo para decisión del OT».
- La ruta larga es una elección del usuario (Epic #12550), no de la configuración.
- El correo de asignación (ADR-0046) y la regeneración de FUR se disparan en una transición de estado auditada en historial, no en un cambio de sub-estado.

### Lo que se pierde
- `plate_flow_skip_to_terminado` (política de compañía) deja de tener efecto: con placa se va directo a `entregado`. Se elimina la columna/flag en la misma migración o se marca obsoleta (decidir en HU-BE-04).
- `PlateRouteDecision.Asignado/Terminado` desaparecen del contrato de `IPlatePreassignPolicy`.

### Cambios operacionales
- Migración EF con SQL cruda (`ExcludeFromMigrations`): data-fix → historial → reescritura del trigger de inmutabilidad de `field_values` (DDL 106) por `status` → DROP trigger/función/columna/CHECK. `Down()` best-effort con el reverse map.
- DDL canónico: `79-tipo-tramite-barrera-y-familia.sql`, `106-HU12167-plate-editable-asignado.sql`, seeds con `plate_flow_status`.
- `RegistrationStateMap` (V1): `Sent → preasignacion`, `Assigned → asignado` solo documental; no se re-migra.
- Endpoint `CompletePlateFlow` se renombra a la transición `asignado → entregado`; ruta vieja como alias un sprint.

## Contrato de la política (HU #12596)

`TramiteTransitionPolicy.Evaluate(from, to, TransitionContext)` se apoya en `TramiteStateMachine`
(aristas estructurales) y añade el contexto. Códigos nuevos en `TramiteEstadoErrores`, todos 422:

| Código | Cuándo |
|---|---|
| `transicion_requiere_placa` | destino `preasignacion`/`asignado` y el tipo no pide placa |
| `transicion_requiere_preasignacion` | `preparado`/`rechazado → entregado` sin placa en un tipo que la pide (Quipux exento) |
| `transicion_placa_incoherente` | `→ preasignacion` al radicar con placa, o `→ asignado` sin placa |
| `transicion_solo_ot` | asignar, liberar o rechazar en preasignación sin ser el OT |
| `transicion_solo_gestor` | radicar sin placa o enviar al OT sin ser el gestor (o proceso de sistema) |

Las aristas previas a este ADR (aprobar, rechazar-decisión, revocar, preparar) **no** miran el actor: sus
autorizaciones viven en los endpoints y no se duplican en la política.

## ADRs relacionados

- [ADR-0022] — vocabulario de estados: **enmendado** por este ADR (+2 estados).
- [ADR-0033] — subsanación: sin cambio de ruta; se añade que el rechazo puede venir de `preasignacion` y lleva marca de origen.
- [ADR-0046] — correo de asignación de placa: el disparador pasa a ser la arista `preasignacion → asignado`.
- [ADR-0050] — tipo de trámite fuente única: la policy usa `requiresPlateRequest` del tipo, no la familia.
- [ADR-0051] — bandeja Quipux: sin cambio; `preparado → entregado` salto único.

## Notas para agentes

- **Backend Agent**: `TramiteLifecycleService` evalúa `TramiteTransitionPolicy` con `TransitionContext.ForInstance(instance, command.Actor)`; el submit decide el destino con `TramiteTransitionPolicy.DestinoDeRadicacion` — `IPlatePreassignPolicy`/`PlatePreassignPolicy` **se eliminan** (decidían por los flags de compañía/OT que la decisión B retiró, y reservaban una placa "elegida de rango" que la Ruta Corta ya no contempla); «Enviar al OT» = `EnviarAlOtHandler` (`POST /instances/{id}/enviar-al-ot`, alias `/plate-flow/complete` hasta que el frontend migre); `rejected_from` lo escribe el lifecycle al entrar a `rechazado` (valor = origen) y lo limpia al subsanar o al salir de `rechazado`; se borran `PlateFlowStatus`, `PlateFlowStateMachine`, propiedad `ProcedureInstance.PlateFlowStatus` (HU #12603); `AssignPlateAsync/RevokePlateAsync/UpdatePlateAsync` y la decisión del OT evalúan la misma política con actor `Ot` (el repositorio del OT escribe `status` bajo scope del tenant cliente, como hoy); `PermiteDecisionOt` desaparece (Approve/Reject ⇔ `status == entregado`); `AttachmentRules`, `ImprontaManualStampReadiness`, `ValidateSoatViaRuntHandler`, `PlateAssignmentEmailModelProjector`, `OtEstadoResolver`, `OtMetricsReadRepository`, `OtQueryRepository` leen `status`.
- **Frontend Agent**: `lib/tramites/estados.ts` + tipos: `preasignacion`/`asignado` con chip e icono; borrar helpers `plateFlow*`; bandeja OT con contadores Preasignar/Asignados/Entregados/Aprobados/Rechazados y acciones por estado (asignar+rechazar en `preasignacion`, corregir+revocar en `asignado`, aprobar/rechazar en `entregado`); operación gestor con «Entregar» en `asignado`.
- **QA Agent**: caminos matrícula sin placa (preasignacion→asignado→entregado→decisión), con placa (preparado→entregado), rechazo en preasignacion + re-radicar, traspaso intentando `preasignacion` → 422, Quipux `preparado→entregado`, migración con filas en cada sub-estado.
- **Security Agent**: las aristas con actor OT solo desde endpoints OT con tenant resuelto del JWT; el admin de FLIT no puede mover `preasignacion → asignado`.
- **Infra Agent**: backup lógico de `procedure_instances (id, status, plate_flow_status)` antes de aplicar en DEV/QA; verificar `has-pending-model-changes` tras regenerar el snapshot.

## Referencias externas

- Epic #12549 «Estados PREASIGNAR y ASIGNADO — Matrícula Inicial Ruta Larga» · Epic #12550 «Ruta Larga y Ruta Corta».
- Plan operativo: `context/estados-preasignacion-asignado/plan.md`.
