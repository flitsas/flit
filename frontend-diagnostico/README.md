# frontend-diagnostico — Diagnóstico

App web de **Diagnóstico** (diagnóstico de flotas y vehículos) en la FLIT Suite. Producto `diagnostico` del contrato de plataforma v1 (§1):
`dev.diagnostico.flitsas.online`, `qa.diagnostico.flitsas.online` y `diagnostico.flitsas.online`. Por ahora la carpeta solo reserva el lugar; todavía no tiene código.

| | DEV y local | QA | PDN |
|---|---|---|---|
| Puerto | 4024 | 5024 | 6024 |
| Su API | `services/core-diagnostico`, por la red interna detrás del gateway (sin puerto publicado) | | |

Hasta que esté desplegado, el hub lo presenta como «Próximamente»: el código `diagnostico` está en
`Suite:Hosts:ComingSoon` (`services/core-api/src/Flit.Api/appsettings.json`) y su tarjeta lleva a
`/proximamente/diagnostico` del hub. **Al desplegarlo en un ambiente, se quita de esa lista para ese ambiente.**

Se crea desde la plantilla de producto (`templates/flit-product`, C-07 del [plan maestro](../docs/suite/plan-maestro.md)),
no a mano: así llega con login de la suite (`@flit/auth`), barra común (`@flit/shell`), habilitación por empresa y
su propio schema. Las reglas comunes están en el [contrato de plataforma v1](../docs/suite/contrato-plataforma-v1.md).
