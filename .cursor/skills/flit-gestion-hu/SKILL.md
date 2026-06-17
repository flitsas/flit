---
name: flit-gestion-hu
description: Guía el ciclo de implementación de una HU en Azure DevOps: activación (Active), build, cierre técnico (Resolved) y entrega a QA con comentarios HTML y menciones mailto. Usar cuando backend-agent o frontend-agent implementen una HU asignada. Triggers Active, Resolved, implementar HU, skill-gestion-hu, entrega QA.
---

## Requisitos

- `.env.user-identity`: `USER_REAL_NAME`, `USER_REAL_EMAIL`
- Opcional: `QA_LEAD_NAME`, `QA_LEAD_EMAIL` (si no existen, pedir al supervisor quién debe recibir la entrega QA)

## Checklist

- [ ] Estado `Active` + comentario de inicio
- [ ] Implementación según AC
- [ ] `npm run build` exitoso
- [ ] Estado `Resolved` + comentario de cierre
- [ ] Mención QA en HTML para validación

## Paso 1 — Activación

1. Cambiar estado a **`Active`**.
2. Comentario:

```html
<div>🤖 [@{Nombre-del-Agente}] usando <b>@skill-gestion-hu</b>: Iniciando desarrollo bajo supervisión de <a href="mailto:{USER_REAL_EMAIL}">@{USER_REAL_NAME}</a></div>
```

## Paso 2 — Desarrollo

1. Cumplir Acceptance Criteria y stack del repo.
2. Ejecutar `npm run build` (o el comando de build del monorepo).
3. Verificar criterios antes de cerrar.

## Paso 3 — Cierre técnico

1. Estado **`Resolved`** solo si el build pasa.
2. Comentario de entrega a QA:

```html
<div>✅ [@{Nombre-del-Agente}] usando <b>@skill-gestion-hu</b>: Desarrollo completado y listo para pruebas de QA.</div>
<div><a href="mailto:{QA_LEAD_EMAIL}">@{QA_LEAD_NAME}</a> — Por favor proceder con la validación de esta HU.</div>
```

## Reglas

- Todas las menciones `@` en ADO deben usar `<a href="mailto:...">`.
- Prohibido `Resolved` si el build falla.
- La auditoría formal de QA usa `skill-gestion-qa-hu` o el `qa-agent`.
