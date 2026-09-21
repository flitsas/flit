# Mapa QA — Feature #12692 Operación transversal

**Para:** saber **dónde abrir** cada criterio  
**Feature:** [#12692](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12692)  
**HUs:** [#12699](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12699) · [#12700](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12700) · [#12701](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12701) · [#12702](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12702)  
**Rama:** `feature/AB-12692-operacion-transversal` (sin PR al armar este mapa)

## Módulos y rutas

| Rol | Módulo | Cómo llegar | Qué homologó |
|---|---|---|---|
| Gestor y OT | **Dock** | Barra inferior de píldoras | A17 Identidad; B21 Trámites / Reportes / Usuarios / Ayuda (mismo label) |
| Gestor (y OT con `validaciones.read`) | **Identidad** | Dock → Identidad · `?m=validaciones` | H1 **Identidad** (ya no «Validaciones») |
| Gestor | **Dashboard / Inicio** | FAB central | KPI **Total trámites** (E03). Hero sigue «Hola, {nombre}» |
| OT | **Dashboard OT** | FAB en sesión OT | Hero **Tu cola de trabajo**; KPI **Esperan mi decisión** / **Entregados hoy** (no se clonan al gestor) |
| Gestor | **Reportes** | Dock → Reportes | Panel **Ahora mismo** dentro de Resumen; KPI Total trámites; pestañas propias (Uso del aplicativo, etc.) |
| OT | **Reportes del organismo** | Hub OT → Reportes | Pestaña **Ahora mismo**; no debe aparecer «Uso del aplicativo» |
| Gestor | **Usuarios** | Dock → Usuarios | H1 **Usuarios** |
| OT | **Usuarios del organismo** | Hub OT → Usuarios · `/admin/transit-offices/{id}/usuarios` | H1 **Usuarios** (ya no «Administración OT — Usuarios») |
| Ambos | **Ayuda** | Dock → Ayuda · `/manual` | Intro y FAQ: **Gestor** y **Organismo de tránsito** |

## Qué mirar (texto ganador)

| ID / HU | Dónde clicar | Antes | Ahora |
|---|---|---|---|
| A17 / #12699 | Dock + H1 del módulo | Validaciones (H1) vs Identidad (píldora) | **Identidad** en los dos |
| B21 / #12699 | Píldoras Trámites, Reportes, Usuarios, Ayuda | — | Mismo label en OT y gestor |
| E03 / #12700 | Dashboard gestor, tarjeta de volumen | Total Trámites | **Total trámites** |
| A22 / #12701 | Reportes gestor (panel) y Reportes OT (pestaña) | — | **Ahora mismo** (ya coincidía; queda cableado al catálogo) |
| A23 / #12701 | Hub OT Usuarios | Administración OT — Usuarios | **Usuarios** |
| A25 / #12702 | `/manual` intro + FAQ de Ayuda | Organismo de Tránsito (T mayúscula) en intro | **Organismo de tránsito** + **Gestor** |

## Qué no certificar como fallo de este Feature

| Tema | Por qué |
|---|---|
| Identidad no sale en el dock OT | Sin permiso `validaciones.read` es RN-07, no conflicto |
| «Hola, {nombre}» vs «Tu cola de trabajo» (A18) | N/A: no unificar héroes |
| Total trámites vs Esperan mi decisión (A19) | N/A: métricas distintas; no clonar KPI |
| «Entregados hoy» vs chip «Entregado» | A19 N/A: no forzar el mismo largo |
| Pestaña Uso del aplicativo solo en gestor; Análisis/Revisores solo en OT (A20/A21) | N/A: no clonar pestañas |
| Otras páginas «Administración OT — Reportes/Reglas/…» | Solo se unificó el H1 de **Usuarios** |
| Cuerpo de artículos Gestor/OT del manual | A25 solo intro; RN-07 no reescribe cada manual |
| Títulos «Ayuda con DR. FLIT (Gestor)» y «(OT)» | Se conservan los sufijos de audiencia |
| Listado/detalle de trámites | Feature #12690 |
| Correos y PDF FUR | Feature #12691, no implementada |

## HUs ↔ pantallas

| HU | Criterio resumido | Pantalla |
|---|---|---|
| #12699 | Dock = H1 Identidad; B21 igual en ambos roles | Dock + módulo Identidad |
| #12700 | E03 casing; no unificar héroes ni KPI N/A | Dashboard gestor + Dashboard OT |
| #12701 | Ahora mismo compartido; Usuarios H1; no clonar tabs | Reportes SPA + hub OT Reportes/Usuarios |
| #12702 | Intro canónica; no reescribir cuerpos | Ayuda + `/manual` intro |
