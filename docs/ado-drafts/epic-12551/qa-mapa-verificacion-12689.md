# Mapa QA — Feature #12689 Glosario canónico

**Para:** certificación de la épica #12551  
**Feature:** [#12689](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12689)  
**HUs:** [#12693](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12693) · [#12694](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12694)  
**Estado de código:** mergeado en `develop` (PR #405)

Esta Feature **no cambia pantallas**. Deja la fuente de verdad y la lista de campos a certificar. Las pantallas se recorren en #12690 y #12692.

## Fuente de verdad

- [Glosario (columna Ganador)](https://github.com/flitsas/flit/blob/develop/docs/ado-drafts/epic-12551/inventario-glosario.md)
- [Lista de campos cambiados](https://github.com/flitsas/flit/blob/develop/docs/ado-drafts/epic-12551/lista-qa-campos-cambiados.md)

## Cómo verificar

1. Cada fila de la lista tiene ID de glosario, superficie, texto anterior y ganador.
2. No certificar como cambio: A02, A11, A14–A16, A18–A22, B01–B23, D03–D05, D07.
3. B01–B14 (estados de trámite) no se reescriben.
4. Las Features de UI (#12690, #12692) y correos/PDF (#12691) se certifican contra esta lista.

## Lista de campos a certificar

| ID | Superficie | Texto anterior | Texto ganador |
|---|---|---|---|
| A01 | Listado OT | Propietario / vendedor | **Vendedor** |
| A03 | Listado OT | Tipo trámite | **Trámite** |
| A04 | Listado gestor | Fecha de creación / actualización | **Fecha radicación** |
| A05 | Listado / wizard gestor | Secretaría · Secretaría de destino | **Organismo de tránsito** |
| A05 | OCR LT OT | Organismo | **Organismo de tránsito** |
| A06 | Listado OT (persona) | Empresa / Gestor | **Gestor** |
| A07 | Detalle OT | Actores del Trámite | **Actores del trámite** |
| A08 | Detalle OT | Especificaciones del vehículo | **Especificaciones técnicas** |
| A09 | Detalle gestor y OT | N. Motor / N. Chasis / N. Serie | **Nº Motor / Nº Chasis / Nº Serie** |
| A10 | Wizard gestor | Pasajeros | **Capacidad** |
| A12 | Detalle OT | Ver consolidado del expediente | **Ver consolidado** |
| A13 | Listado OT | Exportar a Excel | **Exportar** |
| A17 | H1 módulo Identidad | Validaciones | **Identidad** |
| A23 | H1 Usuarios OT | Administración OT — Usuarios | **Usuarios** |
| A24 | Checklist gestor | SOAT vigente | **SOAT** |
| A24 | Ficha OT | SOAT RUNT | **SOAT** |
| A25 | Intro Centro de Ayuda | términos no unificados | **Gestor · Organismo de tránsito** |
| D01 | Correo trámite aprobado | APROBADO | **Aprobado** (Feature #12691, aún no UI) |
| D02 | Correo trámite rechazado | RECHAZADO | **Rechazado** (Feature #12691, aún no UI) |
| D06 | PDF FUR | Secretaría / Organismo | **Organismo de tránsito** (Feature #12691) |
| E01 | Detalle OT | Aprobar trámite / Rechazar trámite | **Aprobar / Rechazar** |
| E02 | Diálogo OT | Adjuntar Licencia de Tránsito (LT) | **Adjuntar LT** |
| E03 | Dashboard gestor | Total Trámites | **Total trámites** |
| E04 | Listado / acordeón gestor | Secretaría · Secretaría de destino | **Organismo de tránsito** |
| E05 | Toast consolidado | Consolidado regenerado/cargado | **Consolidado generado.** |

## Módulos de producto

Ninguno en esta Feature. Si un texto de UI no coincide con el ganador, el defecto se radica en #12690, #12692 o #12691.
