# Features bajo la épica #12551 — borradores (no registrados en ADO)

> **Estado:** espera aprobación explícita para `feature-creator` / tech-lead Modo A.  
> **Padre:** Epic [#12551](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12551/)  
> **Glosario:** [inventario-glosario.md](inventario-glosario.md)  
> **AssignedTo propuesto:** Willyn Londoño Calle (`willyn.londono@flitsas.com`)  
> **Sprint:** **Sprint 7** (misma iteración de la épica #12551, por instrucción explícita del supervisor; exceptúa la regla de no usar el sprint activo). Tag `DOR`. Area `FLIT - EVOLUTION`.  
> **Alcance C + RN-07 + fuente glosario** (decisiones cerradas 2026-09-18).

La columna Ganador del inventario sigue vacía. Las Features **no inventan copy**: cada HU de implementación toma el texto de esa columna. Filas sin ganador no se implementan.

No hay schema nuevo ni ADR de persistencia. El diseño técnico de cada Feature es el glosario + las superficies listadas.

---

## Feature 1

Título: `[HOMOLOGACION] - Glosario canónico de textos OT y Gestor`

# OBJETIVO

Dejar un glosario único, aprobado por el PO, que define el texto ganador de cada concepto visible tanto para el Organismo de Tránsito como para el Gestor, y que sirve de fuente de verdad a las Features de homologación de la épica #12551.

# DESCRIPTION

El inventario vive en `docs/ado-drafts/epic-12551/inventario-glosario.md` (bloques A–E). Cubre listados y detalle de trámites, estados, dock, dashboard, reportes, usuarios, identidad, correos, PDFs y excepciones RN-07.

Reglas de negocio ya cerradas:

- Alcance de barrido: toda la plataforma.
- Fuente de verdad: columna Ganador de este glosario.
- Layout distinto no obliga a la misma cabecera de columna; se homologa el dato (VIN, Placa, estado).
- Un elemento oculto para un rol no genera conflicto.
- Los estados de trámite del catálogo `estados.ts` (B01–B14) **no se tocan**: confirmados por el PO 2026-09-18 (Feature aparte ya unificó el catálogo).

# CRITERIOS FUNCIONALES

- [x] Cada fila del Bloque A tiene Ganador o N/A explícito (supervisor 2026-09-18).
- [x] B01–B23 confirmados. B01–B14 no se reescriben. B15–B23 se quedan.
- [ ] El Bloque C (RN-07) permanece como inventario sin exigir cambio de copy entre roles.
- [ ] Las Features 2, 3 y 4 solo implementan filas con Ganador distinto del texto actual o N/A documentado.
- [ ] Una fila sin Ganador no entra a desarrollo ni a certificación QA.

---

## Feature 2

Título: `[HOMOLOGACION] - Homologación de textos en trámites`

# OBJETIVO

Que listado, detalle y acciones compartidas del trámite muestren el mismo texto para OT y Gestor cuando ambos ven el mismo dato, según el glosario (filas A01–A16, B15–B19, E01–E05).

# DESCRIPTION

Superficies: `/tramites` (gestor), bandeja OT `client-procedures`, detalle gestor, modal/detalle OT, chips de estado, botones Ver documentos / Ver consolidado / Exportar, KPIs de bandeja OT que nombran un estado.

No se rediseñan tablas: se conservan columnas compuestas distintas (Vehículo vs VIN+Placa) si el glosario A02 lo marca como layout RN-07. No se tocan acciones exclusivas de un rol (Enviar al OT, Aprobar, Asignar placa) salvo consistencia interna del mismo rol (E01, E02).

Fuentes de código a alinear: `tramites-table-columns.ts`, `ot-procedures-columns.ts`, `estados.ts`, detalle gestor vs `ClientProcedureDetailModal` / `OtDetalle*`.

# CRITERIOS FUNCIONALES

- [ ] Los labels de listado y detalle que ambos roles ven coinciden con el Ganador de A01–A13.
- [ ] Los chips de estado usan el catálogo confirmado en Bloque B; A14–A16 (Por decidir, plurales de KPI, Rechazado preasignación) respetan el Ganador.
- [ ] Ver documentos permanece igual en ambos roles; Ver consolidado y Exportar usan el Ganador de A12 y A13.
- [ ] Acciones solo-OT o solo-gestor no se clonan al otro rol (RN-07).
- [ ] QA certifica con la lista de campos cambiados tomada del glosario (criterio pedido en la épica).

---

## Feature 3

Título: `[HOMOLOGACION] - Homologación de textos en operación transversal`

# OBJETIVO

Homologar los textos de dock, dashboard, reportes, usuarios, identidad y ayuda donde OT y Gestor ven el mismo concepto, según A17–A25 y B20–B22.

# DESCRIPTION

Superficies: píldoras del dock, Dashboard vs OtDashboard, pestañas de Reportes SPA vs hub OT, módulo Usuarios vs hub OT Usuarios, módulo Identidad/Validaciones, Centro de Ayuda (intro y términos canónicos).

No se unifican paneles de métricas distintos (cola del OT vs operación de la empresa) si el glosario marca RN-07 de métrica (A19, A20). Sí se unifica la palabra de estado cuando aparece (`Entregado`, `Aprobado`).

Fuera de esta Feature: Quipux, ICT, preasignación de rangos, reglas OT, consola de compañía (RN-07).

# CRITERIOS FUNCIONALES

- [ ] Dock e Identidad (A17, B21) usan el Ganador: un solo nombre de módulo por superficie compartida.
- [ ] Palabras de estado en dashboard y reportes coinciden con Bloque B cuando el KPI nombra un estado.
- [ ] Pestañas y títulos de Reportes/Usuarios/Ayuda siguen A20–A23 y A25; lo marcado N/A no se fuerza.
- [ ] El prefijo «Administración OT —» del hub no contradice el H1 canónico si el Ganador de A23 lo unifica.
- [ ] QA certifica contra las filas A17–A25 con Ganador.

---

## Feature 4

Título: `[HOMOLOGACION] - Homologación de textos en correos y documentos generados`

# OBJETIVO

Que correos de cambio de estado del trámite y artefactos PDF que OT y Gestor pueden ver (FUR, expediente consolidado) usen los mismos nombres de estado, organismo y documentos que el glosario (D01, D02, D06, D07, A05, A24).

# DESCRIPTION

Plantillas `tramites.aprobado` y `tramites.rechazado`; overlay FUR; portada y tipos del consolidado. Correos de cuenta (invitación, reset) no chocan OT vs gestor. El correo de Kyverum no lo compone FLIT.

Dependencia: Features 1 y 2 (estados y A05/A24 cerrados). Revalidar si D01/D02 ya tienen disparador productivo antes de implementar.

# CRITERIOS FUNCIONALES

- [ ] Asuntos y cuerpos de D01/D02 usan el estado canónico del Bloque B (Aprobado, Rechazado), no un sinónimo.
- [ ] FUR y consolidado nombran el organismo y el SOAT según Ganador de A05 y A24.
- [ ] El botón o título de consolidado en el PDF/UI no contradice A12.
- [ ] Invitación, reset, analítica y Kyverum quedan fuera de cambio OT-vs-gestor (D03–D05).
- [ ] QA incluye un correo de aprobado/rechazado y un consolidado/FUR con los strings ganadores listados.

---

## DoR-Feature (previo a Active)

| Criterio | F1 | F2 | F3 | F4 |
|---|---|---|---|---|
| Módulo en título | PASS | PASS | PASS | PASS |
| Objetivo | PASS | PASS | PASS | PASS |
| Descripción extendida | PASS | PASS | PASS | PASS |
| ≥3 criterios funcionales | PASS | PASS | PASS | PASS |
| Sprint 7 (instrucción explícita) | #12689 | #12690 | #12692 | #12691 |
| Area FLIT | PASS | PASS | PASS | PASS |
| Tag DOR | PASS | PASS | PASS | PASS |
| AssignedTo humano | propuesto Willyn | propuesto Willyn | propuesto Willyn | propuesto Willyn |
| Sin placeholders TODO/TBD | PASS | PASS | PASS | PASS |
| Sin datos sensibles | PASS | PASS | PASS | PASS |

Registradas en ADO en **Sprint 7** (instrucción explícita). Estado `New`. No pasar a Active F2–F4 hasta que #12689 tenga Ganadores.

HUs hijas (12): ver `HUS.md` — #12693–#12704. Estado `New`. No implementar código hasta Ganador + Motivo A.

## Orden de implementación

1. Feature 1 (glosario lleno)  
2. Feature 2 (trámites)  
3. Feature 3 (transversal)  
4. Feature 4 (correos/PDF), depende de 1 y 2
