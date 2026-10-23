# Mapa QA — Feature #12691 Correos y documentos

**Para:** saber **dónde abrir** cada criterio  
**Feature:** [#12691](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12691)  
**HUs:** [#12703](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12703) · [#12704](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12704)  
**Rama:** `feature/AB-12691-correos-documentos`

Lista maestra: [campos cambiados](https://github.com/flitsas/flit/blob/develop/docs/ado-drafts/epic-12551/lista-qa-campos-cambiados.md).

## Módulos y rutas

| Rol | Superficie | Cómo llegar | Qué homologó |
|---|---|---|---|
| Destinatario de negocio | **Correo trámite aprobado** | Aprobar un trámite (o banco SuperAdmin → `tramites.aprobado`) | D01 asunto/cuerpo **Aprobado** (no APROBADO) |
| Destinatario de negocio | **Correo trámite rechazado** | Rechazar un trámite (o banco SuperAdmin → `tramites.rechazado`) | D02 asunto/cuerpo **Rechazado** (no RECHAZADO) |
| Gestor y OT | **Portada del consolidado** | Detalle → Ver consolidado | A05 **Organismo de tránsito** (ya no Secretaría de Tránsito) |
| Gestor y OT | **Pie de páginas SOAT / FUR** | Mismo PDF consolidado | A24 pie **SOAT**; FUR sin label Secretaría |
| Gestor y OT | **Overlay FUR** | Descargar FUR | D06: casillas oficiales del formulario; el overlay no pinta «Secretaría» |

## Qué mirar (texto ganador)

| ID | Dónde | Antes | Ahora |
|---|---|---|---|
| D01 | Asunto y cuerpo `tramites.aprobado` | APROBADO | **Aprobado** |
| D02 | Asunto y cuerpo `tramites.rechazado` | RECHAZADO | **Rechazado** |
| A05 / D06 | Portada consolidado + fila organismo del correo de cambio de estado | Secretaría de Tránsito | **Organismo de tránsito** |
| A24 | Pie/certificado SOAT del expediente | SOAT vigente / SOAT RUNT | **SOAT** |
| A12 / D07 | Botón UI vs portada PDF | Ver consolidado | Portada sigue diciendo **TRÁMITE:** + código; no dice «del expediente» |

## Qué no certificar como fallo de este Feature

| Tema | Por qué |
|---|---|
| Invitación, reset, bienvenida | D03 — fuera |
| Informes de analítica | D04 N/A |
| Correos Kyverum | D05 — no los compone FLIT |
| `tramites.asignacion-placa` | No es D01/D02 |
| Casillas impresas del PDF oficial del Mintransporte | Overlay rellena valores; no reescribe el formulario |
| Checklist «SOAT vigente» como requisito | Vigente = requisito; el tipo es SOAT |
| Listado/detalle de trámites | Feature #12690 |

## HUs ↔ pantallas

| HU | Criterio resumido | Dónde |
|---|---|---|
| #12703 | Aprobado/Rechazado en asunto y cuerpo; no se inventa un envío nuevo | Composer + banco de plantillas |
| #12704 | Organismo de tránsito y SOAT en consolidado/FUR; no contradice Ver consolidado | Portada + pie + overlay |
