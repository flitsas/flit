# frontend-diagnostico — Diagnóstico

App web de **Diagnóstico** (diagnóstico de flotas y vehículos) en la FLIT Suite. Producto `diagnostico` del contrato de plataforma v1 (§1):
`dev.diagnostico.flitsas.online`, `qa.diagnostico.flitsas.online` y `diagnostico.flitsas.online`. Base de la app (HU #13732): login por el hub (OIDC con PKCE, cliente público `diagnostico`), BFF `/api/v1/*` con el Bearer de la sesión, `DiagnosticoShell` sobre `@flit/shell` y la pantalla base. Sin formulario de credenciales propio.

| | DEV y local | QA | PDN |
|---|---|---|---|
| Puerto | 4024 | 5024 | 6024 |
| Su API | `services/core-diagnostico`, por la red interna detrás del gateway (sin puerto publicado) | | |

Hasta que esté desplegado, el hub lo presenta como «Próximamente»: el código `diagnostico` está en
`Suite:Hosts:ComingSoon` (`services/core-api/src/Flit.Api/appsettings.json`) y su tarjeta lleva a
`/proximamente/diagnostico` del hub. **Al desplegarlo en un ambiente, se quita de esa lista para ese ambiente.**

Se clonó de `frontend-comparendos` sin sus pantallas de negocio; el siguiente producto se crea desde la plantilla de producto (`templates/flit-product`, C-07 del [plan maestro](../docs/suite/plan-maestro.md)),
no a mano: así llega con login de la suite (`@flit/auth`), barra común (`@flit/shell`), habilitación por empresa y
su propio schema. Las reglas comunes están en el [contrato de plataforma v1](../docs/suite/contrato-plataforma-v1.md).

## Local

```
pnpm --filter @flit/frontend-diagnostico dev      # http://127.0.0.1:4024 (nunca localhost)
pnpm --filter @flit/frontend-diagnostico test
```

Variables (solo servidor, ver `.env.example`): `CORE_API_ORIGIN`, `FLIT_INTERNAL_API_KEY`, `FLIT_HUB_URL`, `TRAMITES_URL` y
`FLIT_SESSION_SECRET` (obligatoria fuera de `next dev`). Si `GET /api/v1/diagnostico/sesion` responde 403
(`PRODUCTO_NO_HABILITADO` o sin permisos `diagnostico.*`), la app muestra `/403` sin volver al login.
