---
name: flit-gestion-qa
description: Audita HUs en estado Resolved con revisión de código ligera y registro de hallazgos en comentarios HTML. No cierra la HU (exclusivo del PO) ni cambia System.State — alinear tags QA_PDN/QA_NOVEDAD con qa-agent v2.0. Para ejecución formal de TCs usar qa-agent Modo B; para bugs formales usar bug-reporter. Triggers auditoría HU, Resolved, hallazgos, skill-gestion-qa-hu.
---

## Requisitos previos

1. HU en estado **`Resolved`** (entregada por desarrollo — el QA Agent no la mueve a ese estado).
2. Historial de la HU: solicitante, agente desarrollador, `Assigned To`.
3. `.env.user-identity` para supervisor y destinatarios.

> El qa-agent (Modo B) opera sobre HUs ya en `Resolved` y es el canal formal para ejecución de TCs, evidencia E2E y tags `QA_PDN` / `QA_NOVEDAD`. Esta skill cubre **auditoría ligera** (código + AC) cuando no aplica el ciclo completo del qa-agent. **Nunca** transicionar la HU a `Closed` — eso es exclusivo del PO humano (regla qa-agent #1).

## Checklist

- [ ] Revisión de código (arquitectura, errores, seguridad obvia)
- [ ] Verificar cada Acceptance Criterion
- [ ] Registrar evidencias en checklist
- [ ] Comentario HTML en ADO (aprobación o hallazgos)
- [ ] Notificar al supervisor en chat

## Paso 1 — Auditoría

- Checklist por criterio de aceptación (✅ / ❌).
- Evidencias: capturas, logs, rutas de prueba.

## Paso 2 — Registro en Azure DevOps

### Con hallazgos (permanece Resolved)

```html
<div>🤖 <b>[@{Nombre-del-Agente}]</b> usando <b>@skill-gestion-qa-hu</b>: Se han detectado hallazgos en las pruebas.</div>
<div>Supervisión: <a href="mailto:{USER_REAL_EMAIL}">@{USER_REAL_NAME}</a></div>
<div>Cc: <a href="mailto:{EMAIL_DESARROLLADOR}">@{NOMBRE_DESARROLLADOR}</a></div>
<div><b>--- HALLAZGOS ---</b></div>
<div>[TIPO] Descripción e impacto.</div>
```

Para defectos que requieran work item Bug, invocar `bug-reporter` tras acordar con el supervisor.

### Sin hallazgos (HU permanece `Resolved`)

Agregar tag `QA_PDN` y comentario de certificación. **No** cambiar `System.State` — el PO humano cierra la HU cuando corresponda.

```html
<div>✅ <b>[@{Nombre-del-Agente}]</b> usando <b>@skill-gestion-qa-hu</b>: Auditoría finalizada con éxito. Tag <code>QA_PDN</code> aplicado.</div>
<div>Certificado bajo <a href="mailto:{USER_REAL_EMAIL}">@{USER_REAL_NAME}</a>.</div>
<div><small>La HU permanece en <code>Resolved</code> — cierre (<code>Closed</code>) exclusivo del PO.</small></div>
```

## Paso 3 — Chat al supervisor

```markdown
{USER_REAL_NAME}, revisión terminada HU #[ID].
Notificado a @{AGENTE_PREVIO}.
Estado: [APROBADO — tag QA_PDN, HU en Resolved | CON HALLAZGOS — tag QA_NOVEDAD]
```

## Reglas

- HTML con `mailto:` en todas las menciones.
- No certificar sin validar el 100% de los AC.
- Nunca transicionar la HU a `Closed` ni modificar `System.State` — solo tags, campos de testing y comentarios (alineado con qa-agent v2.0).
- Identificar siempre el agente que desarrolló la HU.
