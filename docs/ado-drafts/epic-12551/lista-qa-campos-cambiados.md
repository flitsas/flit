# Lista QA de campos cambiados — Feature #12689

| Campo | Valor |
|---|---|
| Épica | [#12551](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12551/) |
| Feature | [#12689](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12689/) |
| HU | [#12694](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12694/) |
| Fuente | `inventario-glosario.md` (columna Ganador) + `frontend/lib/copy/qa-campos-cambiados.ts` |
| Fecha | 2026-09-18 |

Cada fila es un cambio a certificar: **ID de glosario**, **superficie**, **texto anterior**, **texto ganador**.

No entran filas N/A, RN-07 (solo un rol lo ve y no hay conflicto de vocablo) ni Ganador igual al texto actual (A02 palabras VIN/Placa, A22 Ahora mismo, B01–B23, D03–D05, D07).

## Cambios a certificar

| ID | Superficie | Texto anterior | Texto ganador |
|---|---|---|---|
| A01 | Listado OT | Propietario / vendedor | Vendedor |
| A03 | Listado OT | Tipo trámite | Trámite |
| A04 | Listado gestor | Fecha de creación / Fecha de actualización | Fecha radicación |
| A05 | Listado / wizard gestor | Secretaría · Secretaría de destino | Organismo de tránsito |
| A05 | OCR LT OT | Organismo | Organismo de tránsito |
| A06 | Listado OT (persona) | Empresa / Gestor | Gestor |
| A07 | Detalle OT | Actores del Trámite | Actores del trámite |
| A08 | Detalle OT | Especificaciones del vehículo | Especificaciones técnicas |
| A09 | Detalle gestor y OT | N. Motor / N. Chasis / N. Serie | Nº Motor / Nº Chasis / Nº Serie |
| A10 | Wizard gestor | Pasajeros | Capacidad |
| A12 | Detalle OT | Ver consolidado del expediente | Ver consolidado |
| A13 | Listado OT | Exportar a Excel | Exportar |
| A17 | H1 módulo Identidad (ambos) | Validaciones | Identidad |
| A23 | H1 Usuarios OT | Administración OT — Usuarios | Usuarios |
| A24 | Checklist gestor | SOAT vigente | SOAT |
| A24 | Ficha OT | SOAT RUNT | SOAT |
| A25 | Intro Centro de Ayuda | términos no unificados en intro | Gestor · Organismo de tránsito |
| D01 | Correo trámite aprobado | APROBADO | Aprobado |
| D02 | Correo trámite rechazado | RECHAZADO | Rechazado |
| D06 | PDF FUR | Secretaría / Organismo | Organismo de tránsito |
| E01 | Detalle OT | Aprobar trámite / Rechazar trámite | Aprobar / Rechazar |
| E02 | Diálogo OT | Adjuntar Licencia de Tránsito (LT) | Adjuntar LT |
| E03 | Dashboard gestor | Total Trámites | Total trámites |
| E04 | Listado / acordeón gestor | Secretaría · Secretaría de destino | Organismo de tránsito |
| E05 | Toast consolidado | Consolidado regenerado. / Consolidado cargado. | Consolidado generado. |

## Excluidos (no certificar como cambio)

| IDs | Motivo |
|---|---|
| A02 | Palabras VIN y Placa ya coinciden; layout de columnas es RN-07 |
| A11, A14–A16, A18–A21 | Ganador N/A |
| A22 | Ganador = texto actual (`Ahora mismo`) |
| B01–B14 | Estados: Feature aparte; no se tocan |
| B15–B23 | Ganador = texto actual |
| C01–C15 | RN-07: solo un rol lo ve |
| D03 | Fuera del trámite OT vs gestor |
| D04, D05 | RN-07 / N/A |
| D07 | Ganador = texto actual (`Ver consolidado`); sigue A12 |

Cuando la Feature #12689 quede Resolved, citar esta ruta en Discussion:

`docs/ado-drafts/epic-12551/lista-qa-campos-cambiados.md`
