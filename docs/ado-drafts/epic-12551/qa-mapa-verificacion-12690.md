# Mapa QA — Feature #12690 Textos en trámites

**Para:** saber **dónde abrir** cada criterio  
**Feature:** [#12690](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12690)  
**HUs:** [#12695](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12695) · [#12696](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12696) · [#12697](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12697) · [#12698](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12698)  
**Rama / PR:** `feature/AB-12690-textos-tramites` · [PR #408](https://github.com/flitsas/flit/pull/408) (sin mergear a `develop` al armar este mapa)

Certificar **solo** filas con ganador distinto del texto anterior. Lista maestra: [campos cambiados](https://github.com/flitsas/flit/blob/develop/docs/ado-drafts/epic-12551/lista-qa-campos-cambiados.md).

## Módulos y rutas

| Rol | Módulo | Cómo llegar | Qué homologó |
|---|---|---|---|
| Gestor | **Trámites** (listado) | Dock → Trámites · `/tramites` | Cabeceras A01–A06, A13 Exportar, chips de estado (catálogo B), E04 Organismo de tránsito |
| Gestor | **Detalle del trámite** | Fila → ver detalle | A07 actores, A08 especificaciones, A09 motor/chasis/serie, A12 Ver consolidado, E05 toast consolidado |
| Gestor | **Wizard / crear-editar** | Nuevo trámite o continuar | A05 Organismo de tránsito, A09, A10 Capacidad (no «Pasajeros») |
| Gestor | **Exportar Excel** | Listado → Exportar | Mismas cabeceras que la tabla en pantalla |
| OT | **Bandeja de trámites** | Hub OT → Trámites · `/admin/transit-offices/{id}/client-procedures` | A01 Vendedor, A03 Trámite, A06 Gestor, A13 Exportar, KPI/chips (catálogo B) |
| OT | **Detalle / modal del trámite** | Fila de la bandeja | A07, A08, A09, A12 Ver consolidado, E01 Aprobar/Rechazar, E02 Adjuntar LT, A05 OCR LT |

## Qué mirar (texto ganador)

| ID | Dónde clicar | Antes | Ahora (ganador) |
|---|---|---|---|
| A01 | Bandeja OT, columna de parte vendedora | Propietario / vendedor | **Vendedor** |
| A03 | Bandeja OT | Tipo trámite | **Trámite** |
| A04 | Listado gestor | Fecha de creación / actualización | **Fecha radicación** |
| A05 / E04 | Listado, wizard y acordeón gestor | Secretaría · Secretaría de destino | **Organismo de tránsito** |
| A05 | OCR LT en detalle OT | Organismo | **Organismo de tránsito** |
| A06 | Bandeja OT (persona) | Empresa / Gestor | **Gestor** |
| A07 | Detalle OT (y gestor si aplica) | Actores del Trámite | **Actores del trámite** |
| A08 | Detalle OT | Especificaciones del vehículo | **Especificaciones técnicas** |
| A09 | Detalle gestor, detalle OT y wizard | N. Motor / N. Chasis / N. Serie | **Nº Motor / Nº Chasis / Nº Serie** |
| A10 | Wizard gestor | Pasajeros | **Capacidad** |
| A12 | Detalle OT (y gestor) | Ver consolidado del expediente | **Ver consolidado** |
| A13 | Bandeja OT (y gestor si exporta) | Exportar a Excel | **Exportar** |
| E01 | Menú de fila y pie de detalle OT | Aprobar trámite / Rechazar trámite | **Aprobar / Rechazar** |
| E02 | Diálogo OT al adjuntar LT | Adjuntar Licencia de Tránsito (LT) | **Adjuntar LT** |
| E05 | Toast al generar consolidado | Consolidado regenerado/cargado | **Consolidado generado.** |
| B01–B14 | Chips de estado en ambos listados | — | **No cambiar** (ya unificados) |

## Qué no certificar como fallo de este Feature

| Tema | Por qué |
|---|---|
| Columnas Vehículo vs VIN+Placa (A02) | Layout RN-07: se homologa el dato, no el armado de columnas |
| Marcas, Fuente, Paso, Preasignación | Solo un rol las ve (RN-07): no clonar al otro |
| KPI «Por decidir», plurales de cola, Rechazado preasignación (A14–A16) | Ganador N/A |
| Enviar al OT, Asignar placa | Acciones de un solo rol: no deben aparecer en el otro |
| Prefijo «Administración OT —» de otras pestañas del hub | Es Feature #12692 (solo Usuarios) |
| Correos Aprobado/Rechazado y PDF FUR | Feature #12691, aún no implementada |

## HUs ↔ pantallas

| HU | Criterio resumido | Pantalla |
|---|---|---|
| #12695 | Cabeceras listado OT = gestor para el mismo dato | `/tramites` + bandeja OT |
| #12696 | Labels de detalle y wizard | Detalle gestor + modal OT + wizard |
| #12697 | Ver consolidado, Exportar, Aprobar/Rechazar, Adjuntar LT | Fila, detalle y diálogo OT |
| #12698 | Chips = catálogo B; KPI de cola no se inventa | Listado gestor + bandeja OT |
