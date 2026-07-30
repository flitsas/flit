# HU12 — [FRONTEND] Rediseño de columnas de la tabla de trámites

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 8 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11057** |
| Commit | `2af73838` |
| Ajuste origen | `modificaciones.txt:51-70` |
| Depende de | HU11 (datos), HU09 (ver documentos), HU10 (consolidado) |

## Descripción

**Como** gestor que opera la bandeja de trámites
**Quiero** ver en la tabla la información acordada con el negocio
**Para** identificar y hacer seguimiento a cada trámite sin abrirlo

## Columnas acordadas con el negocio

| # | Columna | Nota del negocio |
|---|---------|------------------|
| 1 | Radicado | |
| 2 | VIN | |
| 3 | Placa | |
| 4 | Trámite / Modalidad | |
| 5 | Propietario / vendedor | aplica para traspasos |
| 6 | Firmado | estado de la firma del vendedor |
| 7 | Comprador | |
| 8 | Firmado | estado de la firma del comprador |
| 9 | Fecha de creación | |
| 10 | Fecha de actualización | |
| 11 | Secretaría | |
| 12 | Gestor | empresa – nombre de la persona que radica |
| 13 | Fuente | Dashboard, integración, QX (solo la que esté activa actualmente) |
| 14 | Acciones | continuar, ver documentos, consolidado |

## Criterios de aceptación

```gherkin
Escenario: columnas de la tabla
  Dado el listado de trámites
  Cuando el gestor lo abre
  Entonces la tabla muestra radicado, VIN, placa, trámite o modalidad, propietario o vendedor, firmado del vendedor, comprador, firmado del comprador, fecha de creación, fecha de actualización, secretaría, gestor, fuente y acciones

Escenario: matrícula inicial sin vendedor
  Dado un trámite de matrícula inicial
  Cuando se muestra en la tabla
  Entonces las columnas de vendedor y de su firma se presentan como no aplicables

Escenario: acciones de la fila
  Dado un trámite del listado
  Cuando el gestor abre la columna de acciones
  Entonces dispone de continuar, ver documentos y consolidado según lo que el estado del trámite permita

Escenario: fidelidad de diseño
  Dado el listado de trámites en pantalla estrecha y en modo oscuro
  Cuando se muestra la tabla
  Entonces respeta el diseño UI vigente y la cabecera permanece alineada con las filas
```

## Estado actual

`TramitesTable.tsx:603-614` — Compañía · Placa (+radicado) · Vendedor · Comprador · VIN · Vehículo ·
Modalidad · Paso · Estado · Organismo · Creado · Acciones.

Diferencias a resolver:

- **Nuevas:** Firmado (vendedor), Firmado (comprador), Fecha de actualización, Gestor, Fuente.
- **Renombrar** para consistencia: *Organismo* → **Secretaría**; *Creado* → **Fecha de creación**;
  *Modalidad* → **Trámite / Modalidad**; *Vendedor* → **Propietario / vendedor**.
- **Decidido con el PO:** se **conservan** *Vehículo*, *Paso* y *Estado* (el negocio dijo "visualizar
  **mínimo** esta información"; además *Estado* y *Paso* son los chips que orientan qué hacer con cada
  trámite, quitarlos sería una regresión funcional). Resultado: **17 columnas**.
- *Compañía* queda cubierta por *Gestor* ⇒ **se eliminó la columna dedicada del SuperAdmin**. No se
  pierde el dato: era la misma razón social que ahora encabeza *Gestor* (empresa + persona), y ahí la
  ven todos los perfiles. Lo delató un test al encontrar el nombre de la empresa dos veces en la misma
  fila. El **filtro** por compañía del SuperAdmin sigue intacto.

## ⚠️ Riesgos

- La tabla no es un `<table>`: es un grid CSS con ancho mínimo fijo (`min-w-[1340px]`, `:592`) y una
  cabecera separada de las filas. Añadir cinco columnas exige revisar el `gridTemplateColumns` de
  ambas y el desplazamiento horizontal. Antecedente: `docs/reporte-desalineamiento-tablas.md`.
- Con 14+ columnas conviene evaluar prioridad de columnas o truncado con tooltip antes que ensanchar
  indefinidamente.
- Respetar `flit-design-guardian`: la fidelidad al prototipo es requisito, no preferencia.

## Archivos previstos

- `frontend/components/operacion/TramitesTable.tsx`
- `frontend/components/operacion/TramitesListToolbar.tsx` (si cambian filtros asociados)
- Tests: `frontend/__tests__/tramites-table.test.tsx`
