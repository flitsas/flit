# HU10 — [FRONTEND] Acceso al PDF consolidado desde la tabla de trámites

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 3 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11055** |
| Commit | `2af73838` |
| Ajuste origen | `modificaciones.txt:17` |
| Bloquea a | HU12 |

## Descripción

**Como** gestor que revisa su bandeja de trámites
**Quiero** abrir el expediente consolidado desde la fila del listado
**Para** revisar el documento completo de un trámite en un clic

## Criterios de aceptación

```gherkin
Escenario: consolidado generado
  Dado un trámite cuyo expediente consolidado ya está generado
  Cuando el gestor abre el listado de trámites
  Entonces la fila ofrece la acción de ver el consolidado
  Y al usarla se abre el PDF en el modal de previsualización

Escenario: consolidado no generado
  Dado un trámite sin expediente consolidado generado
  Cuando el gestor abre el listado de trámites
  Entonces la fila no ofrece la acción de ver el consolidado

Escenario: la acción no navega
  Dado un listado de trámites
  Cuando el gestor usa la acción de ver el consolidado en una fila
  Entonces no se abre el wizard del trámite
```

## Notas técnicas

- Referencia del módulo OT: `ClientProceduresTable.tsx:215-225` ("Ver consolidado"). **Diferencia
  importante:** en el OT el botón *genera si falta* (`onConsolidado` decide regenerar-o-reutilizar por
  `consolidado_maestro_vigente`). Aquí el negocio pide explícitamente **"sólo visible si ya se
  encuentra generado"** ⇒ el botón **no** debe disparar generación.
- La fila del listado de trámites es un `div role="button"` que abre el wizard
  (`TramitesTable.tsx:760-772`). Toda acción embebida necesita `stopPropagation`, como ya hace la
  estrella de prioridad (`:783`).
- Señal de "consolidado generado": hoy `InstanceSummary` no la expone. Resolver junto con HU11 (añadir
  la marca al resumen) o consultando los adjuntos del trámite al abrir la acción. **Preferible en
  HU11**, para no disparar una petición por fila.

## Dependencia

Requiere que el resumen del listado indique si el consolidado está generado ⇒ coordinar con
[HU11](HU11-resumen-listado-ampliado.md).

## Archivos previstos

- `frontend/components/operacion/TramitesTable.tsx`
- Tests: `frontend/__tests__/tramites-table.test.tsx`
