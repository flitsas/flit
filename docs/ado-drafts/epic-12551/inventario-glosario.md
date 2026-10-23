# Inventario / glosario — Épica #12551

Homologación de textos entre vista del **Organismo de Tránsito** y vista del **Gestor**.

| Campo | Valor |
|---|---|
| Épica | [#12551](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12551/) |
| Fecha | 2026-09-18 |
| Fuente | Lectura del código en el workspace (frontend + catálogo de correos) |

## Decisiones cerradas

| # | Decisión | Elección |
|---|---|---|
| 1 | Alcance | **C** — se barre toda la plataforma |
| 2 | Fuente de verdad | **A** — este glosario; el PO marca el ganador por fila |
| 3 | Layout distinto | **A** — RN-07: se homologa el **dato**, no la composición de columnas |
| 4 | Estados de trámite | **No se tocan.** B01–B14 quedan como están en `estados.ts` (Feature aparte ya unificó el catálogo). Confirmado PO 2026-09-18. |
| 5 | Quién cierra el copy restante | **PO + diseñador** (conocen el producto). El agente no elige Ganador de A20 en adelante ni de microcopy de Reportes/Usuarios/Ayuda/SOAT. |

## Cómo llenar

Columna **Ganador**: escribir el texto canónico, o `N/A` si RN-07 aplica (solo un rol lo ve).

No hace falta unificar pantallas. Hace falta que, si ambos ven el mismo concepto, **la palabra sea la misma**.

Leyenda de **Estado**:

| Estado | Significado |
|---|---|
| `PO` | Ambos lo ven (o es el mismo dato) y el texto **difiere**. El PO elige. |
| `OK` | Ya usan la misma fuente o el mismo texto. El PO solo confirma. |
| `RN-07` | Solo un rol lo ve. Inventariado; no es conflicto. |

---

## Bloque A — El PO decide (prioridad)

Estas filas son el núcleo de la épica. Reunión corta: ir de arriba abajo.

| ID | Módulo | Superficie | Tipo | Texto gestor | Texto OT | Ambos | Estado | Ganador | Nota |
|---|---|---|---|---|---|---|---|---|---|
| A01 | Trámites | Listado | Columna | Vendedor | Propietario / vendedor | sí | PO | Vendedor | Mismo actor. En detalle gestor a veces ya dice «Propietario / vendedor». PO 2026-09-18: gana `Vendedor` (también en OT). |
| A02 | Trámites | Listado | Columna | Vehículo (placa + VIN + marca) | VIN + Placa (separados) | sí | PO | VIN · Placa | **Layout RN-07:** no unificar columnas. PO 2026-09-18: palabras canónicas `VIN` y `Placa`. |
| A03 | Trámites | Listado | Columna | Trámite / Estado | Tipo trámite + Estado | sí | PO | Trámite | Layout distinto. PO 2026-09-18: el tipo se llama `Trámite` (no `Tipo trámite`). `Estado` sigue el catálogo B. |
| A04 | Trámites | Listado | Columna | Fecha de creación / Fecha de actualización | Fecha radicación | sí | PO | Fecha radicación | PO 2026-09-18: mismo instante; nombre canónico `Fecha radicación`. |
| A05 | Trámites | Listado / wizard | Campo | Secretaría · Organismo de tránsito · Secretaría de destino | Organismo (OCR LT); no hay columna en bandeja | parcial | PO | Organismo de tránsito | PO 2026-09-18: un solo nombre. Arrastra D06 y E04. |
| A06 | Trámites | Listado | Columna | Gestor | Empresa / Gestor | sí | PO | Gestor | PO 2026-09-18: la persona se llama `Gestor`. Layout OT (empresa + persona) se conserva. |
| A07 | Trámites | Detalle | Sección | Actores del trámite | Actores del Trámite | sí | PO | Actores del trámite | PO 2026-09-18: casing frase. |
| A08 | Trámites | Detalle | Sección | Especificaciones técnicas | Especificaciones del vehículo | sí | PO | Especificaciones técnicas | PO 2026-09-18. |
| A09 | Trámites | Detalle | Campo | Nº Motor / Nº Chasis / Nº Serie (wizard) · N. Motor (detalle) | N. Motor / N. Chasis / N. Serie | sí | PO | Nº Motor · Nº Chasis · Nº Serie | PO 2026-09-18: un solo formato `Nº` en gestor y OT. |
| A10 | Trámites | Detalle / wizard | Campo | Capacidad (detalle) · Pasajeros (wizard) | Capacidad | sí | PO | Capacidad | PO 2026-09-18: el wizard deja de decir `Pasajeros` para este campo. |
| A11 | Trámites | Detalle | Tab / sección | FUR y expediente | (sin tab FUR; documentos + consolidado) | sí | PO | N/A | PO 2026-09-18: no clonar la pestaña al OT (RN-07 de superficie). El botón/PDF sigue A12 y D07. |
| A12 | Trámites | Acciones | Botón | Ver consolidado | Ver consolidado · Ver consolidado del expediente | sí | PO | Ver consolidado | PO 2026-09-18: un solo largo. Arrastra D07. |
| A13 | Trámites | Acciones | Botón | Exportar | Exportar a Excel | sí | PO | Exportar | PO 2026-09-18. |
| A14 | Trámites | KPI OT vs chip | Estado | Entregado | Por decidir (tarjeta KPI; el chip sigue diciendo Entregado) | sí | PO | N/A | PO 2026-09-18: se acepta. La tarjeta nombra la cola; el chip sigue B06 `Entregado`. |
| A15 | Trámites | KPI OT | Estado | Asignado | Asignados | sí | PO | N/A | PO 2026-09-18: plural en contador OT se acepta. El chip sigue B05 `Asignado` (igual Aprobados/Rechazados/Revocados). |
| A16 | Trámites | Chip | Estado | Rechazado preasignación (si viene de preasignación) | Rechazado | sí | PO | N/A | PO 2026-09-18: distintivo solo gestor. OT no inventa un quinto estado; chip OT = B08 `Rechazado`. |
| A17 | Identidad | Dock vs H1 | Módulo | Dock: Identidad · Página: Validaciones | Igual si tiene el módulo | condicional | PO | Identidad | PO 2026-09-18: H1 alineado al dock. |
| A18 | Dashboard | Título | Cabecera | Hola, {nombre} | Tu cola de trabajo | sí | PO | N/A | PO 2026-09-18: héroes distintos; no se homologan títulos. |
| A19 | Dashboard | KPI | Label | Total Trámites · Matrículas · Traspasos · Otros Trámites · Completados | Esperan mi decisión · Pendientes en total · Entregados hoy · Tiempo mediano de decisión | sí | PO | N/A | PO 2026-09-18: métricas distintas (RN-07). Plural de contador alineado a A15. |
| A20 | Reportes | Tabs | Cabecera | Resumen general · Operación / Trámites · Organismo de Tránsito · Uso del aplicativo · Productividad · Consultas personalizadas | Ahora mismo · Análisis · Informe · Revisores · Consultas personalizadas | sí | PO | N/A | Supervisor 2026-09-18: paneles distintos; no clonar tabs. `Consultas personalizadas` ya coincide. |
| A21 | Reportes | Tab | Cabecera | Productividad | Revisores | sí | PO | N/A | Supervisor 2026-09-18: no es el mismo concepto (empresa vs personal OT). |
| A22 | Reportes | Panel | Cabecera | Ahora mismo (dentro de Resumen) | Ahora mismo (tab propia) | sí | PO | Ahora mismo | Supervisor 2026-09-18: el nombre se queda; los KPI de dentro no se unifican (A19). |
| A23 | Usuarios | Título | Cabecera | Usuarios | Usuarios / Administración OT — Usuarios | sí | PO | Usuarios | Supervisor 2026-09-18: H1 = píldora del dock. Prefijo de hub OT no es el título. |
| A24 | Documentos | Tipo | Label | SOAT vigente (checklist) | SOAT RUNT (ficha OT) | parcial | PO | SOAT | Supervisor 2026-09-18: el tipo es `SOAT`. Vigente = requisito; RUNT = fuente. |
| A25 | Ayuda | Manual | Títulos | Artículos «Gestor» | Artículos «Organismo de Tránsito» | no | PO | Gestor · Organismo de tránsito | Supervisor 2026-09-18: intro usa A06 y A05. No reescribir el cuerpo de cada manual. |

---

## Bloque B — Ya homologado (confirmar, no reescribir)

Fuente: `frontend/lib/tramites/estados.ts`. OT usa `formatOtProcedureStatus` → `estadoLabel`.

**B01–B14 cerrados por el PO (2026-09-18):** no se reescribe el catálogo de estados ni `estados.ts`. **B15–B23:** visto supervisor 2026-09-18 (se quedan).

| ID | Concepto | Texto canónico actual | Estado | Ganador (confirmar o corregir) |
|---|---|---|---|---|
| B01 | Estado | Borrador | OK | Borrador |
| B02 | Estado | Anulado | OK | Anulado |
| B03 | Estado | Preparado | OK | Preparado |
| B04 | Estado | Preasignación | OK | Preasignación |
| B05 | Estado | Asignado | OK | Asignado |
| B06 | Estado | Entregado | OK | Entregado |
| B07 | Estado | Aprobado | OK | Aprobado |
| B08 | Estado | Rechazado | OK | Rechazado |
| B09 | Estado | Revocado | OK | Revocado |
| B10 | Estado | En subsanación | OK | En subsanación |
| B11 | Subestado | Revocatoria solicitada | OK | Revocatoria solicitada |
| B12 | Subestado | Revocatoria en revisión | OK | Revocatoria en revisión |
| B13 | Subestado | Revocatoria aprobada | OK | Revocatoria aprobada |
| B14 | Subestado | Revocatoria rechazada | OK | Revocatoria rechazada |
| B15 | Columna | Radicado | OK | Radicado |
| B16 | Columna | VIN | OK | VIN |
| B17 | Columna | Comprador | OK | Comprador |
| B18 | Columna | Estado | OK | Estado |
| B19 | Botón (ambos) | Ver documentos | OK | Ver documentos |
| B20 | CTA usuarios | Invitar usuario | OK | Invitar usuario |
| B21 | Dock | Trámites · Reportes · Usuarios · Ayuda | OK | Trámites · Reportes · Usuarios · Ayuda |
| B22 | Ayuda | Centro de Ayuda | OK | Centro de Ayuda |
| B23 | Firma actor | Firmado · Sin firma · Rechazado · Sin registrar | OK | Firmado · Sin firma · Rechazado · Sin registrar |

---

## Bloque C — RN-07 (inventario; no es conflicto)

Solo un rol lo ve. No se pide ganador salvo que el PO quiera unificar nombres **internos** de ese rol.

### Solo gestor (o AdminCompany / Radicador)

| ID | Módulo | Texto | Nota |
|---|---|---|---|
| C01 | Listado trámites | Marcas · Confirmado en RUNT · Fuente · Paso · Cliente | Columnas que OT no tiene |
| C02 | Listado trámites | Enviar al OT · Pausar · Reanudar · Anular trámite · Subsanar · Ver historial de la placa | Acciones de ciclo gestor |
| C03 | Listado trámites | Cambiar estado · Gestionar consolidado · Reenviar validación · Reasignar gestor · Consultar ahora en RUNT | Acciones admin |
| C04 | Wizard | Preparar · Confirmar radicación · Re-radicar | Solo radicador |
| C05 | Dock | Reportes Detallados | OT lo omite |
| C06 | Compañía | Placas preasignadas (visor) · Representantes legales · Mandatarios · Matrícula inicial / Traspasos | Consola empresa |
| C07 | Reportes SPA | Organismo de Tránsito (tab desde la empresa) | No es el hub OT |
| C08 | Dashboard | Validaciones Biométricas | Card solo gestor |

### Solo OT

| ID | Módulo | Texto | Nota |
|---|---|---|---|
| C09 | Bandeja | Aprobar · Rechazar · Aprobar trámite · Rechazar trámite | Decisión; gestor no decide. Interno OT = E01 → `Aprobar` / `Rechazar`. |
| C10 | Bandeja | Asignar placa · Liberar placa · Actualizar placa · Adjuntar LT | Cola de placa / LT |
| C11 | Bandeja | Decidir revocatoria · Aprobar revocatoria | Subflujo OT |
| C12 | Dock | Preasignación · Reglas · Documentos · Requisitos | Hub OT |
| C13 | Hub | Motor de reglas · Documentos y prelación · Requisitos | Títulos de módulo |
| C14 | Detalle | SOAT RUNT · Prenda · Transformaciones solicitadas · Tipo trámite solicitado · Fecha radicación (bloque datos) | Ficha OT. Tipo documental SOAT = A24. Fecha radicación = A04. Tipo trámite solicitado → A03 `Trámite`. |
| C15 | KPI bandeja | Solicitudes de revocatoria | No es estado del trámite |

---

## Bloque D — Correos y PDFs (alcance C)

Homologar **si el mismo evento** se nombra distinto hacia gestor y OT, o si el cuerpo usa un estado/campo distinto al glosario B.

| ID | Canal | Asunto / etiqueta actual | ¿Ambos roles? | Estado | Ganador |
|---|---|---|---|---|---|
| D01 | Correo | `[FLIT] Notificación radicación del trámite — {placa} — APROBADO` | Destinatario de negocio (no necesariamente OT+gestor a la vez) | PO | Aprobado | Supervisor 2026-09-18: catálogo B07. No cambia estados. |
| D02 | Correo | `[FLIT] Notificación radicación del trámite — {placa} — RECHAZADO` | Igual | PO | Rechazado | Supervisor 2026-09-18: catálogo B08. |
| D03 | Correo | Invitación / reset / bienvenida | Cuenta (ambos perfiles posibles) | OK |  | Fuera del trámite; no chocan OT vs gestor. |
| D04 | Correo | Informes y alertas de analítica | Quien programa el reporte | RN-07 | N/A | |
| D05 | Correo | Kyverum (identidad) | Lo envía Kyverum, no FLIT | RN-07 | N/A | Fuera de control de copy FLIT. |
| D06 | PDF | FUR (Formulario Único) | Ambos pueden descargarlo | PO | Organismo de tránsito | Labels del overlay: sigue A05. PO 2026-09-18. |
| D07 | PDF | Expediente consolidado | Ambos: `Ver consolidado` | PO | Ver consolidado | Sigue A12. PO 2026-09-18. Portada no contradice el botón. |

Nota: en el inventario de correos (2026-08) `tramites.aprobado` / `tramites.rechazado` **no tenían disparador productivo**. Revalidar antes de implementar D01/D02.

---

## Bloque E — Acciones de un solo rol que igual deben ser consistentes **dentro** de ese rol

No son OT vs gestor, pero RN-06 (“toda la plataforma”) los deja en el barrido.

| ID | Rol | Textos que hoy conviven | Ganador |
|---|---|---|---|
| E01 | OT | Menú fila `Aprobar` / `Rechazar` vs pie de detalle `Aprobar trámite` / `Rechazar trámite` | `Aprobar` / `Rechazar` (nombre accesible puede ser «Aprobar trámite») |
| E02 | OT | `Adjuntar LT` vs diálogo `Adjuntar Licencia de Tránsito (LT)` | `Adjuntar LT` (diálogo puede anunciar Licencia de Tránsito) |
| E03 | Gestor | Dashboard `Total Trámites` vs Reportes `Total trámites` | `Total trámites` |
| E04 | Gestor | Wizard `Organismo de tránsito` vs listado `Secretaría` vs acordeón `Secretaría de destino` | = A05 → `Organismo de tránsito` |
| E05 | Ambos | Toast consolidado gestor `Consolidado regenerado.` / `Consolidado cargado.` vs OT `Consolidado generado.` | `Consolidado generado.` |

---

## Superficies barridas vs pendientes de fila a fila

| Superficie | ¿Barrido? | Profundidad |
|---|---|---|
| Listados trámites gestor vs OT | Sí | Cabeceras y acciones |
| Estados y revocatoria | Sí | Catálogo único |
| Detalle gestor vs modal OT | Sí | Secciones y campos principales; no cada label del wizard (50+ campos) |
| Dock / menú | Sí | Píldoras |
| Dashboard | Sí | Títulos y KPIs de primer nivel |
| Reportes SPA vs hub OT | Sí | Tabs; no cada eje de gráfica |
| Usuarios | Sí | Título y CTA |
| Identidad | Sí | Nombre de módulo |
| Correos | Sí | Catálogo de plantillas |
| PDFs FUR/consolidado | Parcial | Nombres de artefacto; no cada casilla del FUR |
| Wizard campo a campo | Pendiente | Tras A09/A10/A05 |
| Quipux / ICT / Log QX | Pendiente | Típico SuperAdmin; RN-07 si OT/gestor no los ven |
| Mandatos (copy de contrato) | Pendiente | Documento generado; ExpertDocEngine si toca norma |

---

## Estado del glosario

**Cerrado** 2026-09-18 (supervisor + propuestas diseño/norma). Bloque A–E con Ganador o `N/A`. Bloque C sigue como inventario RN-07 (no se clona).

HUs en ADO (#12693–#12704), Sprint 7, estado New. Siguiente paso de código: Motivo A sobre **#12693** (catálogo de copy) con sí explícito. No implementar antes.
