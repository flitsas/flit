# Reporte de hallazgos — criterios de enrutamiento de la ruta de preasignación de placa

**Fecha:** 2026-07-17
**Feature:** #10587 (estado interno de placa / preasignación) · **Rama:** `develop` (post-merge PR #174)
**Solicitud del usuario:** validar que el enrutamiento de la radicación cumpla estas tres reglas:

1. **Sin placa** → si el radicador **no** selecciona placa del rango y envía al OT ⇒ **ruta de preasignación**.
2. **Dígito de preferencia** → si el radicador selecciona **dígito de preferencia** y envía al OT ⇒ **ruta de preasignación**.
3. **Con placa** → si el radicador **selecciona una placa** del rango asignado ⇒ **ruta normal (sin preasignación)**.

Alcance: revisión de código sobre `develop`. No se modificó código; este documento lista hallazgos y las correcciones propuestas.

---

## 1. Cómo se decide hoy la ruta (fuente de verdad)

Toda la decisión vive en `PlatePreassignPolicy.DecideAsync` (`services/core-api/src/Flit.Infrastructure/OtRules/PlatePreassignPolicy.cs:29`), invocada por el submit (`SubmitProcedureInstanceCommand.cs:50`). El resultado se traduce a `plate_flow_status` y el `status` global **siempre** queda en `entregado`:

| Orden | Condición evaluada | Resultado | `plate_flow_status` |
|---|---|---|---|
| 1 | `modalidad != matricula_inicial` | `Standard` | `null` |
| 2 | `transit_office_id` ausente / GUID inválido | `Standard` | `null` |
| 3 | `IsAssignmentAllowedAsync == false` (ver §1.1) | `Standard` | `null` |
| 4 | `plate` no vacío **y** `TryReservePlateAsync` reserva OK | `Asignado` | `asignado` |
| 5 | Cualquier otro caso (sin placa, o reserva falló) | `Preasignado` | `preasignado` |

### 1.1 `IsAssignmentAllowedAsync` (`PlateRangeRepository.cs:168`) exige el **AND** de tres flags

- `TenantOperationalPolicies.PlatePreassignEnabled` de **la compañía que radica**.
- Grant compañía↔OT vigente (`TenantTransitOfficeGrants.IsEnabled`).
- `OtRequirements.AllowPlatePreassign` del **OT** destino.

Si **cualquiera** de los tres es falso → `Standard` → `null`.

---

## 2. Contraste regla por regla

| Regla | Comportamiento del código | ¿Cumple? |
|---|---|---|
| **R1 — sin placa → preasignación** | Con la ruta activa (§1.1) y sin `plate`, cae en el paso 5 → `preasignado`. | ✅ Sí, **condicionado** a que los 3 flags + modalidad estén OK (ver H3). |
| **R2 — dígito → preasignación** | El dígito se guarda en `plate_preferred_last_digit` (`FirmaFurStep.tsx:508`) y **no** escribe `plate`. Al no haber placa, cae en el paso 5 → `preasignado`. El dígito llega al OT como guía (`ClientProceduresSection.tsx:379-607`). | ✅ Sí, misma condición que R1. |
| **R3 — con placa → ruta normal** | Con `plate` reservable, cae en el paso 4 → **`asignado`**, no `null`. Es sub-estado del flujo de placa, no la ruta estándar. | ⚠️ **Parcial** — ver H1. Funcionalmente el OT no asigna (placa ya presente), pero **no** es "sin preasignación" en el modelo de datos. |

**Conclusión:** la lógica de enrutamiento es correcta en su intención, pero hay **una discrepancia semántica (H1)** en R3 y **tres huecos** que hacen que en la práctica el flujo "no funcione como debería" (H2, H3, H4).

---

## 3. Hallazgos

### H1 — 🟡 Regla 3: seleccionar placa produce `asignado`, no la ruta normal (`null`)

**Qué pasa.** El paso 4 de `DecideAsync` devuelve `PlateRouteDecision.Asignado` → `plate_flow_status = 'asignado'`. Esto **sí** es parte del sub-flujo de placa (Flujo A del diseño #10587), no la ruta estándar (`null`) que describe la regla 3 ("sin preasignación").

**Impacto.** Funcionalmente el OT **no** tiene que asignar placa (ya está puesta) y solo aprueba/rechaza tras SOAT vigente (gate HU #10804). Es decir, se comporta como "el OT no preasigna". Pero **en datos y en la bandeja** el trámite queda marcado como flujo de placa (`asignado`, con su badge y su gate de SOAT específico), no como un trámite normal. Si la expectativa de negocio es que elegir placa deje el trámite **idéntico a un trámite estándar** (`plate_flow_status = null`, sin gate de placa), esto **no se cumple**.

**Decisión requerida (negocio).** Hay dos lecturas válidas de la regla 3:
- **(a) "Normal" = el OT no asigna** → el comportamiento actual (`asignado`) ya lo cumple. No se toca código; solo se documenta.
- **(b) "Normal" = trámite estándar sin flujo de placa** → hay que cambiar el paso 4 para devolver `Standard` (`null`) cuando el radicador elige placa. **Ojo:** en ese caso hay que decidir **quién reserva la placa** (hoy la reserva `DecideAsync`); si se va por `Standard` sin reservar, la placa elegida quedaría disponible para otro trámite.

> **Recomendación:** confirmar con el PO cuál de las dos lecturas aplica **antes** de tocar código. La opción (a) es la de menor riesgo y es coherente con el diseño ya mergeado.

---

### H2 — 🔴 No se puede "deshacer" la selección de placa → bloquea R1 y R2 tras elegir

**Qué pasa.** En `PlacaPreasignadaSection` (`FirmaFurStep.tsx:432`):
- La única escritura del field `plate` es `pick()` (`:488`), que **siempre** guarda una placa no vacía.
- Una vez elegida (`placaElegida`), la sección muestra "Placa seleccionada / **Cambiar**" (`:538`). "Cambiar" (`:547`) solo pone `changing=true`, que reabre el selector para elegir **otra** placa.
- **No existe** ningún camino que escriba `plate = ''` (limpiar). El `select` de dígito de preferencia además **se oculta** cuando ya hay placa elegida.

**Impacto.** Si el radicador eligió una placa y luego quiere **radicar sin placa** (R1) o **usar dígito de preferencia** (R2), **no puede**: el field `plate` queda con la placa anterior, `DecideAsync` la reserva y el trámite cae en `asignado`. El único "escape" es elegir otra placa, nunca ninguna. Esto es un **hueco funcional real** que puede explicar el "no funciona": basta un click de prueba en una placa para quedar atrapado en Flujo A.

**Corrección propuesta (frontend).**
- Añadir en la vista "Placa seleccionada" (junto a "Cambiar") un botón **"Radicar sin placa"** / **"Quitar placa"** que haga `patchFieldValues(instanceId, [{ fieldKey: 'plate', valueText: '' }])` y refresque.
- Al limpiar, volver a mostrar el selector **y** el `select` de dígito de preferencia (para que R2 sea alcanzable).
- Test: `placa-preasignada-section.test.tsx` — elegir placa → "Quitar placa" → el field `plate` se limpia y reaparecen selector + dígito.

---

### H3 — 🔴 Degradación silenciosa a ruta estándar cuando falta un flag o la modalidad

**Qué pasa.** Los pasos 1–3 de `DecideAsync` devuelven `Standard` (`null`) **sin ningún error ni aviso** al radicador. Las causas más probables en un entorno recién configurado (DEV):
- La **compañía que radica** no tiene `PlatePreassignEnabled` (o el rango se creó para **otra** compañía distinta a la que radica).
- El grant compañía↔OT no está `IsEnabled`.
- El OT no tiene `AllowPlatePreassign`.
- El trámite no es `matricula_inicial` (o `ModalidadEntrada` no quedó con ese código).

**Impacto.** El radicador **no selecciona placa** esperando la ruta de preasignación (R1), pero como algún flag está apagado, el trámite se entrega como **estándar** (`plate_flow_status = null`): en la bandeja del OT **no aparece** el chip "En cola del OT" ni el botón "Asignar placa". Es la causa más citada del síntoma "el OT no ve asignar placa" (ver `reporte-novedad-ot-no-ve-asignar-placa.md`, Causa A). **Es un fallo silencioso**, no un error visible.

**Corrección propuesta.**
1. **Diagnóstico inmediato (datos, 1 min).** Sobre el trámite radicado sin placa:
   ```sql
   SELECT id, status, plate_flow_status, modalidad_entrada, transit_office_id
   FROM tramites.procedure_instances
   WHERE reference_number = '<RADICADO>';
   ```
   - `status='entregado'` y `plate_flow_status IS NULL` → confirmada la degradación (algún flag/modalidad). Verificar los **3 flags para la compañía que radica** (no para la dueña del rango) y la modalidad.
2. **Mejora de producto (recomendada, no bloqueante).** Que el wizard **avise** cuando la ruta de placa **no** está activa para la compañía/OT elegidos (p. ej. deshabilitar/ocultar la sección "Placa preasignada" con un hint "La preasignación no está habilitada para este organismo"), en vez de mostrar el selector y luego enrutar en silencio a estándar. Fuente única: reutilizar `IsAssignmentAllowedAsync` vía un endpoint de estado, ya consumido por el selector.

---

### H4 — 🟡 `DecideAsync` ignora el origen de `plate`; una placa fuera de rango cae en preasignación

**Qué pasa.** `DecideAsync` (paso 4) solo lee el **texto** del field `plate`; no mira su `source`. `TryReservePlateAsync` (`PlateRangeRepository.cs:389`) devuelve `false` si la placa **no está en el rango** de esa compañía/OT o si ya **no está `Disponible`**. En ambos casos la policy cae en el paso 5 → `preasignado`.

**Impacto (dos escenarios).**
- **Placa del RUNT (source `consultation`):** el frontend trata este caso como "no aplica preasignación" (`vinTienePlacaRunt`, `FirmaFurStep.tsx:462`), pero el backend, al ver `plate` no vacío que **no** pertenece al rango, lo enruta a `preasignado`. Inconsistencia front/back (escenario raro en matrícula inicial, pero latente).
- **Placa elegida que otro trámite tomó primero:** el radicador **sí** eligió placa (esperando R3 / ruta normal) pero, al fallar la reserva, el trámite cae **silenciosamente** en `preasignado`. Viola R3 sin avisar.

**Corrección propuesta.**
- Que `DecideAsync` (o el submit) **devuelva un error subsanable** cuando había una placa elegida por el usuario (`source = 'user'`) y la reserva falla, en vez de degradar a `preasignado` en silencio — así el radicador reintenta con otra placa.
- Opcional: excluir del intento de reserva las placas con `source = 'consultation'` (RUNT) para alinear con el frontend (tratarlas como estándar).

---

### H5 — 🟢 Verificado OK: lado del OT del dígito y de la preasignación

Cableado correcto de punta a punta (no requiere cambios):
- El dígito de preferencia se expone al OT y se muestra como **guía** en el modal "Asignar placa", resaltando (★) y ordenando primero las placas que terminan en ese dígito, **sin** filtrar ni forzar (`ClientProceduresSection.tsx:379-607`).
- Un trámite `preasignado` muestra "Asignar placa" en la bandeja del OT admin (`ClientProceduresTable.tsx`); el modal asigna del rango o fuera de rango.
- Las trazas de depuración `[PLATE-DEBUG]` que mencionaban reportes previos **ya no están** en `PlatePreassignPolicy.cs` (deuda cerrada).

> Nota de rol (de `reporte-novedad-ot-no-ve-asignar-placa.md`, Causa B): el botón "Asignar placa" **no** se pinta para SuperAdmin ni en modo QX read-only. Validar la bandeja como **OT admin** del organismo destino.

---

## 4. Resumen y priorización

| # | Hallazgo | Severidad | Regla afectada | Acción |
|---|---|---|---|---|
| **H2** | No se puede quitar la placa una vez elegida | 🔴 Alta | R1, R2 | Botón "Quitar placa" en frontend + reaparecer dígito |
| **H3** | Degradación silenciosa a estándar si falta flag/modalidad | 🔴 Alta | R1, R2 | Diagnóstico SQL de flags + aviso en wizard |
| **H1** | Placa elegida → `asignado`, no `null` (ruta normal) | 🟡 Media | R3 | Confirmar con PO lectura (a)/(b); (b) requiere cambio de policy |
| **H4** | `DecideAsync` ignora origen de placa; reserva fallida → preasignado silencioso | 🟡 Media | R3 | Error subsanable si `source='user'` y reserva falla |
| **H5** | Guía del dígito y asignación del OT | 🟢 OK | R2 | Ninguna (verificado) |

**Camino más corto para que "funcione como debería" en la validación actual:**
1. **H3 primero** (probable causa raíz del síntoma en DEV): confirmar por SQL que el trámite radicado sin placa quedó `plate_flow_status='preasignado'`; si quedó `null`, activar los 3 flags **para la compañía que radica** y verificar la modalidad.
2. **H2** (hueco funcional seguro): implementar "Quitar placa" para poder alcanzar R1/R2 tras un click de prueba.
3. **H1** requiere decisión de negocio antes de tocar código; **H4** es endurecimiento para no degradar en silencio.

> Este informe es de análisis. Las correcciones H2/H3/H4 son acotadas; H1 depende de una definición del PO. Ninguna se aplicó todavía.
