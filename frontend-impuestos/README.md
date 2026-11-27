# frontend-impuestos — Impuestos

App web de **Impuestos** (liquidación del impuesto vehicular por placa y masiva) en la FLIT Suite. Producto `impuestos`
(plan `docs/impuestos/plan-implementacion.md` §6.1). Esqueleto (M15): login por el hub (OIDC con PKCE, cliente público
`impuestos`, cookie `flit_session_impuestos`), BFF `/api/v1/*` con el Bearer de la sesión, `ImpuestosShell` sobre
`@flit/shell` con los módulos por rol, selector de compañía del Super Admin (`?empresa=`), banners de estado del producto
y campana de avisos. Sin formulario de credenciales propio.

| | DEV y local | QA | PDN |
|---|---|---|---|
| Puerto | 4034 | — | — |
| Su API | `services/core-impuestos`, por la red interna detrás del gateway (sin puerto publicado) | | |

No importa nada de `frontend-diagnostico` ni de otro producto: solo los paquetes compartidos `@flit/auth`, `@flit/shell`,
`@flit/ui` y `@flit/brand` (`__tests__/independencia.test.ts`, EP-CF1). Las reglas comunes están en el
[contrato de plataforma v1](../docs/suite/contrato-plataforma-v1.md).

## Módulos por rol (plan §5.2)

| Módulo | Ruta | Gestor | Administrador | Super Admin |
|---|---|---|---|---|
| Dashboard | `/` (botón de inicio) | Sí | Sí | Sí, con vista global |
| Impuestos | `/liquidaciones` | Sí | Sí | Sí |
| Reportes | `/reportes` | Sí | Sí | Sí |
| Usuarios | `/usuarios` | — | Sí | Sí |
| Configuración | `/configuracion` | — | Sí | Sí |
| Backoffice | `/backoffice/empresas` | — | — | Sí |

## Local

```
pnpm --filter @flit/frontend-impuestos dev      # http://127.0.0.1:4034 (nunca localhost)
pnpm --filter @flit/frontend-impuestos test
```

Variables (solo servidor, ver `.env.example`): `CORE_API_ORIGIN`, `FLIT_INTERNAL_API_KEY`, `FLIT_HUB_URL`, `TRAMITES_URL` y
`FLIT_SESSION_SECRET` (obligatoria fuera de `next dev`). Si `GET /api/v1/impuestos/sesion` responde 403 (sin rol,
compañía sin el producto o suspendida), la app muestra `/403` con el motivo sin volver al login.
