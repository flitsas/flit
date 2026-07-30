# HU11 — [BACKEND] Resumen del listado de trámites con actualización, gestor, fuente y firma por parte

| Campo | Valor |
|-------|-------|
| Tipo | `[BACKEND]` |
| Story Points | 5 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11056** |
| Commit | `baf8b100` |
| Ajuste origen | `modificaciones.txt:51-70` |
| Bloquea a | HU12, HU10 |

## Descripción

**Como** gestor que opera la bandeja de trámites
**Quiero** que el listado traiga la fecha de actualización, el gestor, la fuente y el estado de firma de cada parte
**Para** priorizar y hacer seguimiento sin abrir cada trámite

## Criterios de aceptación

```gherkin
Escenario: fecha de actualización
  Dado un trámite modificado después de su creación
  Cuando se consulta el listado
  Entonces el resumen incluye la fecha de la última actualización

Escenario: gestor responsable
  Dado un trámite radicado por un usuario de una compañía
  Cuando se consulta el listado
  Entonces el resumen incluye la compañía y el nombre de la persona que radica

Escenario: fuente del trámite
  Dado un trámite creado desde el tablero, desde la integración o desde Quipux
  Cuando se consulta el listado
  Entonces el resumen indica la fuente que corresponde

Escenario: estado de firma por parte
  Dado un trámite de traspaso con firma pendiente de una de las partes
  Cuando se consulta el listado
  Entonces el resumen indica el estado de firma del vendedor y del comprador por separado

Escenario: aislamiento por compañía
  Dado un usuario de una compañía
  Cuando se consulta el listado
  Entonces solo obtiene trámites de su propia compañía
```

## Brecha de datos (verificada)

Contrato actual: `InstanceSummary` — `frontend/lib/api/types/procedure-runtime.ts:106`.

| Dato pedido | Hoy | Origen disponible |
|-------------|-----|-------------------|
| Fecha de actualización | ✖ | `ProcedureInstance.UpdatedAt` (`:143`) — solo falta proyectarlo |
| Gestor (empresa + persona) | ◐ `companiaNombre` solo en el listado multi-tenant del SuperAdmin | `ProcedureInstance.CreatedByUserId` (`:138`) → nombre del usuario |
| Fuente | ✖ | `ProcedureInstance.Origin` (`:100`) |
| Firmado por parte | ✖ solo `signaturePending` agregado | Firmas por parte (`listFirmas`) / estado biométrico por rol |
| Consolidado generado (para HU10) | ✖ | Marca `consolidado_maestro_vigente` / adjunto de tipo consolidado |

## Mapeo de la columna Fuente (CONFIRMADO por el PO)

El DDL fija el dominio real: `40-ICT-procedure-external-ref.sql:4` documenta
`origin varchar(20) -- 'ict' | null = plataforma`, y el único productor de `'ict'` es
`IctOrchestrationService.cs:98`. **No existe un origen `QX`**: Quipux es canal de **salida**
(radicación), no de creación. El historial de estados usa además `migration_v1` para lo migrado de
FLIT 1.0 (`GetStatusHistoryQuery.cs:58`).

| Valor | Fuente a mostrar |
|-------|------------------|
| `origin` null | Dashboard |
| `origin = 'ict'` | Integración |
| `is_migrated` | Migrado (gana sobre `origin`: es una foto de V1) |

**QX queda fuera** (decisión del PO): no existe un origen Quipux, y acoplar el listado de trámites al
módulo Quipux para marcar "tiene envío" costaría una consulta más y crearía trámites que son
Integración **y** QX a la vez. Si algún día aparece un originador Quipux se añade un código a
`TramiteFuente` y un caso en `Desde`; el resto del listado no cambia. Coherente con la nota del
negocio, *"solo la que esté activa actualmente"*.

## Archivos previstos

- `services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/ListProcedureInstancesQuery.cs`
- `frontend/lib/api/types/procedure-runtime.ts` (contrato)
- `frontend/lib/api/tramites-client.ts` (mapeo del DTO)
- Tests: `services/core-api/tests/Flit.Tramites.Application.Tests/UseCases/ProcedureInstances/`

## Nota de rendimiento

El estado de firma por parte y la marca de consolidado no deben resolverse con una consulta por fila
(N+1). Proyectar en la misma query del listado.
