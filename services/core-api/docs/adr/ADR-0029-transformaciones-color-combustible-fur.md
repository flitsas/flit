# ADR-0029 — Transformaciones de color y combustible en el trámite (FUR)

- **Estado**: Propuesto · 2026-07-09
- **Módulo**: Trámites — Matrícula inicial (MI) y Traspaso (TR)
- **Requerimientos**: Pendiente A4/B4 (`PendientesFLIT2.0MI-TR.xlsx`, fila 4) — alcance **restringido** a color y combustible
- **Relacionado**: RF33 / `cambio_carroceria` (fuera de alcance de este ADR), HU #10463 (FUR), `PreflightCommand`, `FurFieldMapper`
- **Decide**: Líder Técnico

## Contexto

El operador consulta el vehículo en RUNT y recibe `vehicle_color` y `vehicle_fuel` hidratados en
`field_values` (solo lectura en UI). En la práctica el color o el combustible del vehículo pueden
diferir del RUNT al momento del trámite. Negocio pide poder **declarar** esos cambios durante MI y
TR, reflejarlos en el **FUR** (campos del vehículo + observaciones) y **no exigir documentos**
adicionales para color ni combustible.

**Fuera de alcance explícito (2026-07-09):** cambio de **carrocería** (selector de valor nuevo,
snapshot, observaciones). El checkbox existente `cambio_carroceria` + `factura_carroceria` se
conserva tal cual y **no** se modifica en este ADR.

**Restricciones de producto:**

- Color y combustible pueden declararse **a la vez**.
- Catálogos: listas **cerradas placeholder** en código; se sustituyen cuando negocio entregue la lista real.
- UX: tarjeta dedicada en el paso de consulta, impecable (4 estados, WCAG 2.1 AA), sin mezclar con “Condiciones del trámite”.

**Problema técnico:** `PreflightCommand.UpsertHydratedFields` sobrescribe `field_values` en cada
re-consulta RUNT; sin snapshot + protección, cualquier transformación se pierde al pulsar “Actualizar”.

## Decisión

1. **Modelo en `field_values` (sin tabla nueva):**
   - Snapshots inmutables tras primera hidratación (o refresco solo del snapshot):
     `vehicle_color_runt`, `vehicle_fuel_runt`.
   - Valores efectivos (van al FUR): `vehicle_color`, `vehicle_fuel`.
   - Flags opcionales: `cambio_color`, `cambio_combustible` (`"true"` / `"false"`), o derivados del
     diff snapshot vs efectivo al generar FUR.
2. **Preflight:** al hidratar desde RUNT, **siempre** actualizar `*_runt`. Los efectivos
   `vehicle_color` / `vehicle_fuel` **solo** se sobrescriben si no hay transformación activa
   (`cambio_*` ≠ true y/o efectivo aún igual al snapshot previo). Nunca pisar un cambio declarado.
3. **UI:** componente `VehicleTransformationsCard` en el paso consulta (entre `VehicleDataCard` y
   “Condiciones del trámite”). Solo color y combustible; progressive disclosure; resumen
   “se registrará en el FUR”; catálogos placeholder.
4. **FUR:** `FurCommand` / `FurFieldMapper` siguen leyendo efectivos. Al armar observaciones,
   **componer** (append) texto automático si hay cambio de color y/o combustible, p. ej.
   `Cambio de color: PLATA → NEGRO. Cambio de combustible: GASOLINA → ELECTRICO.`, sin borrar
   `fur_observations` manuales si existieran.
5. **Documentos / SubmitGate / checklist:** sin nuevos tipos ni reglas para color/combustible.
6. **Carrocería:** no tocar.

## Alternativas consideradas

### Alternativa A — Snapshot + field_values + tarjeta UX (RECOMENDADA)

- (+) Encaja con el modelo actual; sin migración DDL.
- (+) Fix de preflight acotado; FUR ya mapea color/combustible.
- (+) UX clara: RUNT vs transformación separados.
- (−) Auditoría limitada al historial de `field_values` / triggers existentes.
- Esfuerzo: **medio**. Riesgo: bajo.

### Alternativa B — Entidad `procedure_instance_transformations`

- (+) Historial y auditoría fuertes.
- (−) DDL, endpoints y UI más pesados; overkill sin documentos ni ciclo de vida complejo.
- Esfuerzo: alto. Riesgo: medio (retrabajo).

### Alternativa C — Editar in-place en `VehicleDataCard` sin snapshot

- (+) UI mínima.
- (−) Confunde dato RUNT con dato de trámite; re-consulta RUNT pisa cambios.
- Esfuerzo: bajo. Riesgo: alto (pérdida de datos / UX confusa).

## Tradeoff aceptado

Se elige **A**: velocidad y claridad UX sin schema nuevo. La protección de preflight es el
requisito no negociable. Catálogos placeholder se documentan como deuda explícita.

## Consecuencias por agente

- **Backend:** snapshots + regla de upsert en preflight; composición de observaciones en generación
  FUR; tests unitarios (preflight no pisa; observaciones con 0/1/2 cambios).
- **Frontend:** `VehicleTransformationsCard` + catálogos placeholder + tests Vitest; 4 estados UI;
  no modificar bloque carrocería/leasing.
- **QA:** escenarios MI/TR con color, combustible y ambos; re-consulta RUNT conserva cambio;
  FUR muestra campos + observaciones; carrocería regresión (checkbox intacto).
- **Security:** sin endpoints nuevos; reutiliza `PATCH field-values`.
- **Infra / DB:** sin migración; sin Fase 2b database-agent.

## Requisito vs decisión (trazabilidad)

| Decisión PO / operación | Decisión ADR |
|--------------------------|--------------|
| Color y combustible a la vez | Ambos flags/selectores independientes |
| Sin documentos para color/combustible | Sin reglas en `ConditionalDocumentRules` |
| Marcar en FUR campos + observaciones | Efectivos en mapper + texto en `observations` |
| Catálogos definidos luego | Placeholder en `vehicle-transformations` (front) / constante backend si valida |
| No tocar carrocería (selector nuevo) | Fuera de alcance; checkbox documental intacto |
| UX impecable | Tarjeta dedicada, no mezclar con condiciones documentales |

## ADRs relacionados

- [ADR-0020] — consultas multiproveedor (hidratación RUNT)
- [ADR-0022] — ciclo de vida del trámite (borrador / field_values editables)
