# Propuesta — flujo de preasignación de placa determinista y completo (radicación → placa → SOAT → aprobación)

**Fecha:** 2026-07-17
**Feature:** #10587 (estado interno de placa) · **Rama:** `develop`
**Motivación del usuario:** al radicar **sin placa** o con **dígito de preferencia**, el trámite no cae en la ruta de preasignación; poner los flags por **SQL varias veces no lo resuelve**. Se pide (1) poder **quitar la placa** ya elegida para escoger otra o un dígito, y (2) una propuesta para **garantizar todo el flujo** de preasignación de punta a punta.

> Documento de propuesta (análisis + diseño). No modifica código. Complementa `reporte-hallazgos-criterios-ruta-preasignacion-placa.md`.

---

## 1. Por qué "no funciona" aunque pongas los flags por SQL

La decisión de ruta (`PlatePreassignPolicy.DecideAsync`) es un **AND silencioso**: si **cualquiera** de sus condiciones falla, devuelve `Standard` → `plate_flow_status = null` → el trámite se entrega como uno normal, **sin error ni señal** para el radicador ni para el OT. Ese "fallo silencioso" es la raíz de la frustración: puedes corregir un flag y seguir chocando con **otra** condición sin enterarte de cuál.

### 1.1 Lo que **descarté** con evidencia (no es la causa)

- **No es RLS cegando la lectura de flags.** No existe `FORCE ROW LEVEL SECURITY` en el DDL; el rol de la app es dueño de las tablas, así que **RLS no filtra** las lecturas del runtime (los `set_config('app.current_tenant_id', …)` de los repos alimentan los **triggers de auditoría**, no el filtrado). Por tanto `IsAssignmentAllowedAsync` **sí ve** los flags; leerlos de más o de menos no explica el fallo. *(Se propone igual un endurecimiento menor en §3.4.)*
- **No es la máquina de estados ni el `transit_office_id`.** Como la **entrega tuvo éxito** (`status='entregado'`), los gates de `EvaluarEntregaAsync` ya probaron que el `transit_office_id` está en `field_values`, el grant existe y el OT es operable. La modalidad canónica es exactamente `"matricula_inicial"`.

### 1.2 Lo que **sí** corta (ordenado por probabilidad)

Como la entrega funcionó, la ruta cae en `Standard` por una de estas causas, todas **de identidad/consistencia de datos**, no de "el flag está apagado en abstracto":

| # | Condición de `DecideAsync` / `IsAssignmentAllowedAsync` | Por qué sigue fallando aunque "pongas el flag" |
|---|---|---|
| **C1** | `PlatePreassignEnabled` de **la compañía que radica** | El flag se puso en **otra** compañía (o el **rango se creó** para la compañía X, pero el radicador pertenece a la compañía Y). `IsAssignmentAllowedAsync` usa el **tenant del trámite** (`procedure_instances.tenant_id`), no el de la consola donde creaste el rango. |
| **C2** | Grant compañía↔OT `IsEnabled` para **ese** `transit_office_id` | El grant existe para un `transit_office_id` distinto al que quedó en el `field_value` del trámite (hay más de un perfil/OT, o se usó el id de catálogo vs. el del perfil). |
| **C3** | `OtRequirements.AllowPlatePreassign` del `transit_office_id` del trámite | No existe fila de `ot_requirements` para **ese** office (→ `FirstOrDefault` = `null` → `?? false`), o está en otro office. |
| **C4** | `modalidad == matricula_inicial` | El trámite no es matrícula inicial (poco probable si esperabas la sección de placa). |
| **C5** | `plate` vacío → cae en `Preasignado` | Si en una prueba anterior clicaste una placa, el `field_value` `plate` **quedó poblado** y ya no se puede limpiar (§2) → cae en `Asignado`, no en preasignación. |

> **Insight clave:** "crear el rango" prueba los 3 flags **solo para `request.CompanyTenantId` y en ese instante** (`AdminPlateRangesEndpoints.cs:160`). El submit los revalida para **la compañía dueña del trámite** y para el **`transit_office_id` que quedó en el field_value**. Si esos dos no son idénticos a los del rango, la ruta cae en `Standard`. Por eso el SQL "a ciegas" no converge.

### 1.3 Diagnóstico decisivo en **una** consulta (reemplaza el `<RADICADO>`)

En vez de tantear flags, esta consulta muestra en **una fila** exactamente cuál de C1–C4 corta, ya resuelto **para la compañía y el office reales del trámite**:

```sql
WITH t AS (
  SELECT pi.id, pi.tenant_id AS company_tenant, pi.status, pi.plate_flow_status,
         pi.modalidad_entrada,
         (SELECT fv.value_text FROM tramites.procedure_instance_field_values fv
           WHERE fv.procedure_instance_id = pi.id AND fv.field_key = 'transit_office_id'
           LIMIT 1)::uuid AS office_id,
         (SELECT fv.value_text FROM tramites.procedure_instance_field_values fv
           WHERE fv.procedure_instance_id = pi.id AND fv.field_key = 'plate' LIMIT 1) AS plate
  FROM tramites.procedure_instances pi
  WHERE pi.reference_number = '<RADICADO>'
)
SELECT t.*,
       (t.modalidad_entrada = 'matricula_inicial')                              AS c4_es_matricula,
       COALESCE(top.plate_preassign_enabled, false)                             AS c1_flag_compania,
       EXISTS (SELECT 1 FROM admin.tenant_transit_office_grants g
                WHERE g.tenant_id = t.company_tenant
                  AND g.transit_office_id = t.office_id AND g.is_enabled)        AS c2_grant_vigente,
       COALESCE(oreq.allow_plate_preassign, false)                              AS c3_ot_permite
FROM t
LEFT JOIN admin.tenant_operational_policies top ON top.tenant_id = t.company_tenant
LEFT JOIN admin.ot_requirements oreq            ON oreq.transit_office_id = t.office_id;
```

La primera columna en `false` de `c1…c4` es la causa. Corrige **esa** para **`company_tenant`/`office_id`** de la fila (no para la compañía de la consola).

---

## 2. Ajuste 1 — Poder **quitar la placa** elegida (reabrir R1 y R2)

**Problema (confirmado en código).** En `PlacaPreasignadaSection` (`FirmaFurStep.tsx:432`) la única escritura del field `plate` es `pick()` (`:488`), que **siempre** guarda una placa no vacía. "Cambiar" (`:547`) solo reabre el selector para elegir **otra** placa; **no existe** ningún camino que escriba `plate=''`. Además el `select` de dígito **se oculta** cuando ya hay placa. ⇒ Un solo click de prueba en una placa deja el trámite atrapado en Flujo A (`asignado`) y **bloquea** radicar sin placa (R1) o por dígito (R2).

**Solución (frontend, acotada).** Añadir "Quitar placa" en la vista de placa elegida y reabrir selector + dígito al limpiar.

```tsx
// En el bloque `if (placaElegida && !changing)` (FirmaFurStep.tsx:538), junto a "Cambiar":
<button
  type="button"
  disabled={saving}
  onClick={() => void clearPlate()}
  className="rounded-lg border px-3 py-1 text-[11px] font-semibold"
>
  Quitar placa
</button>

// Nueva función, análoga a pick():
const clearPlate = async () => {
  setSaving(true); setError(null);
  try {
    await tramitesClient.patchFieldValues(instanceId, [
      { formFieldId: null, fieldKey: 'plate', valueText: '' },   // '' → DecideAsync cae en Preasignado
    ]);
    setChanging(true);   // reabre selector + dígito
    onRefresh?.();
  } catch {
    setError('No se pudo quitar la placa. Inténtalo de nuevo.');
  } finally { setSaving(false); }
};
```

- `DecideAsync` ya trata `plate=''` como "sin placa" (`!string.IsNullOrWhiteSpace`, `PlatePreassignPolicy.cs:69`) → **no requiere cambio de backend**.
- Con `changing=true` reaparecen el grid de placas **y** el `select` de dígito → R1 y R2 quedan alcanzables tras un click de prueba.
- **Test** (`placa-preasignada-section.test.tsx`): elegir placa → "Quitar placa" → se llama `patchFieldValues(plate,'')`, reaparecen selector y dígito.

---

## 3. Ajuste 2 — Enrutamiento **determinista y observable** (no más fallo silencioso)

El objetivo: cuando la compañía **usa** preasignación, radicar **sin placa** o **con dígito** debe **garantizar** la ruta B, y si algo está mal configurado, el sistema lo **dice** en vez de degradar en silencio.

### 3.1 `DecideAsync` devuelve un **motivo** (backend)

Cambiar el retorno de `PlateRouteDecision` (enum plano) por un `PlateRouteResult { Decision, Reason }` donde `Reason ∈ { NotMatriculaInicial, NoOffice, PreassignNotEnabled, PlateReserved, NoPlate }`. No cambia el enrutamiento; **añade trazabilidad**:
- `SubmitProcedureInstanceHandler` **loguea** el motivo con `ILogger` (structured: `instanceId`, `companyTenant`, `officeId`, `reason`). Reemplaza cualquier `Console.*` de depuración.
- El motivo viaja al historial (`reason` de la transición ya existe, `SubmitProcedureInstanceCommand.cs:51-62`): hoy solo distingue asignado/preasignado/estándar; añadir el **por qué** del estándar ("preasignación no habilitada para compañía/OT").

### 3.2 Señal en el wizard **antes** de radicar (frontend + endpoint de estado)

Hoy la sección "Placa preasignada" se muestra **siempre** en matrícula inicial con OT elegido, aunque la ruta esté inactiva para esa compañía/OT → el radicador cree que preasigna y termina en `Standard`. Propuesta:

- **Endpoint de estado** `GET /api/v1/tramites/plate-preassign/status?transitOfficeId=…` que devuelva `{ enabled: bool }` reutilizando `IsAssignmentAllowedAsync(companyTenant, office)` (el `companyTenant` sale del token del radicador — misma resolución que `plate-preassign/available`, ya registrada en el middleware tras el fix del Hallazgo 1). 
- **En `PlacaPreasignadaSection`:** si `enabled == false`, **no** mostrar el selector como si preasignara; mostrar un aviso: *"La preasignación de placa no está habilitada para este organismo/compañía. El trámite se entregará de forma estándar."* Así el radicador ve el estado real **antes** de radicar (elimina el síntoma "no funciona").
- Mantiene el default seguro: compañías sin preasignación siguen en ruta estándar, ahora **de forma explícita**.

### 3.3 Garantía "sin placa/dígito ⇒ preasignado" cuando la compañía lo usa

**Decisión fijada (2026-07-17): bloquear con mensaje.** Si `PlatePreassignEnabled` de la compañía es `true` y es matrícula inicial con OT elegido, pero el **grant** o `AllowPlatePreassign` del OT no están, el submit **no** degrada a `Standard` en silencio → **rechaza la radicación** con un error subsanable (`plate_route_misconfigured`, HTTP 422) y un mensaje claro: *"La preasignación de placa está activa para tu compañía pero el organismo de tránsito no está habilitado (grant o allow_plate_preassign). Corrige la configuración antes de radicar."* El radicador/admin lo corrige, en vez de entregar un trámite que "desaparece" de la cola de placa.

- Compañías con el flag en `false` → `Standard` normal, sin fricción (no se bloquea nada).
- El bloqueo aplica **solo** a la combinación "compañía activa + OT mal configurado" — la que hoy produce el fallo silencioso.
- El mensaje se mapea desde `PlateRouteResult.Reason == PreassignMisconfigured` en el endpoint de submit.

### 3.4 Endurecimiento de lectura (consistencia, bajo riesgo)

`IsAssignmentAllowedAsync` (`PlateRangeRepository.cs:168`) lee `tenant_operational_policies`/`tenant_transit_office_grants`/`ot_requirements` **sin** el guard `ExecuteCrossTenantReadAsync` que **sí** usa su gemela `ListEligibleCompaniesAsync` (`:211`) para las **mismas** tablas. Hoy no rompe (owner bypassa RLS), pero es una asimetría frágil ante un futuro `FORCE ROW LEVEL SECURITY` o cambio de rol. Alinear ambas por consistencia y a prueba de futuro.

---

## 4. El flujo completo garantizado (end-to-end)

Con los ajustes anteriores, la cadena queda cerrada y verificable. Todo el sub-flujo de placa vive en `plate_flow_status`; el `status` global sigue en `entregado` (máquina == develop):

| Paso | Actor | Qué ocurre | Símbolo / gate | Estado |
|---|---|---|---|---|
| 1. Radicación sin placa / con dígito | Radicador | `DecideAsync` → `Preasignado` | `PlatePreassignPolicy.cs:81` | `entregado` + `preasignado` |
| 1'. Radicación con placa del rango | Radicador | `DecideAsync` reserva y → `Asignado` | `TryReservePlateAsync` `:389` | `entregado` + `asignado` |
| 2. OT ve el trámite en cola | OT admin | Chip "en cola OT" + botón "Asignar placa" | `ClientProceduresTable.tsx` (`plateFlowStatus==='preasignado'`) | — |
| 3. OT asigna placa (guía = dígito) | OT admin | del rango o fuera de rango; sub-estado → `asignado` | `AssignPlateAsync` `:388` | `preasignado → asignado` |
| 4. Gestor registra SOAT | Gestor/Radicador | `soat_estado` editable con `plate_flow_status='asignado'` (trigger) | `PatchFieldValues` + trigger HU10611 | `asignado` |
| 5. Gate visual SOAT | OT admin | aprobar/rechazar ocultos hasta `soatEstado==='vigente'` | `SoatGate` (HU #10804) | `asignado` |
| 6. OT aprueba | OT admin | gate duro SOAT + placa → `Utilizada` + limpia `plate_flow_status` | `TransitionAsync` `:272-322`, `SoatGate.BlocksApproval` `:289` | `entregado → aprobado` |

Los pasos 2–6 **ya están implementados y verificados** en código (§ referencias). Los ajustes de esta propuesta blindan **el paso 1** (que la ruta se active y sea observable) y su **alcanzabilidad** (§2). Es decir: el trabajo restante para "que funcione completo" es **entrada + diagnóstico**, no la cadena del OT.

---

## 5. Plan de implementación (por fases, acotado a PR ≤ 800 líneas)

| Fase | Alcance | Archivos | Tests |
|---|---|---|---|
| **F1 — Quitar placa** (§2) | Botón + `clearPlate()` + reabrir dígito | `frontend/components/operacion/FirmaFurStep.tsx` | `placa-preasignada-section.test.tsx` |
| **F2 — Señal en wizard** (§3.2) | Endpoint `plate-preassign/status` + aviso "no habilitada" | `ProcedureInstanceEndpoints.cs`, `PlateRangeRepository`/policy, `FirmaFurStep.tsx`, `tramites-client.ts` | test endpoint + render aviso |
| **F3 — Enrutamiento observable** (§3.1, §3.3) | `PlateRouteResult` con motivo + log `ILogger` + error subsanable si compañía activa y OT mal config | `IPlatePreassignPolicy.cs`, `PlatePreassignPolicy.cs`, `SubmitProcedureInstanceCommand.cs` | `PlatePreassignPolicyTests`, `SubmitProcedureInstanceTests` |
| **F4 — Endurecimiento lectura** (§3.4) | `IsAssignmentAllowedAsync` bajo `ExecuteCrossTenantReadAsync` | `PlateRangeRepository.cs` | test de `IsAssignmentAllowed` |

**Orden sugerido:** F1 (desbloquea la prueba manual de inmediato) → F2 (hace visible el estado real) → F3 (elimina el silencio) → F4 (consistencia). F1 y F2 resuelven el 80 % de la fricción reportada.

### Gates FLIT
- HU en ADO bajo Feature #10587 con **confirmación humana explícita** antes de `Active` (§ regla innegociable). Evaluar 1 HU FULLSTACK (F1+F2) + 1 HU BACKEND (F3+F4), o una sola si cabe en ≤ 800 líneas.
- ADR corto para §3.3 (decisión "bloquear vs. degradar en silencio"), estado `Propuesto`.
- Reviewer humano real + checks verdes antes de merge a `develop`.
- `dev-tester` obligatorio al cerrar cada HU (evidencias PASO 6 por AC).

---

## 6. Decisiones del PO — **RESUELTAS (2026-07-17)**

1. **§3.3** — compañía con preasignación activa pero OT/grant mal configurado → **BLOQUEAR la radicación con mensaje subsanable** (`plate_route_misconfigured`, HTTP 422). No se degrada en silencio. *(Implementa en F3.)*
2. **Regla 3 / H1** — elegir placa del rango → **se deja en `asignado`; el OT aprueba tras validación de SOAT** (comportamiento actual). `DecideAsync` **no cambia** el paso 1'; F3 solo añade el motivo/log y el bloqueo de (1). *(H1 del reporte previo queda cerrado como "comportamiento esperado".)*

> Plan cerrado. Nada implementado aún: pendiente **activar la HU en ADO** (gate humano) antes de codificar.
