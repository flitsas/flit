# core-comparendos — Comparendos

Servicio (API .NET) de **Comparendos** (gestión de comparendos de tránsito) en la FLIT Suite. Producto `comparendos` del contrato de plataforma
v1 (§1). Por ahora la carpeta solo reserva el lugar; todavía no tiene código.

| | DEV y local | QA | PDN |
|---|---|---|---|
| Puerto | 4060 | 5060 | 6060 |
| Su app web | `frontend-comparendos` (4061) | 5061 | 6061 |

Valida los tokens de la suite sin llamar a otro servicio (como `core-api`, ver
[identidad-frontera.md](../../docs/suite/identidad-frontera.md) §7) y guarda sus datos en su propio schema `comparendos`.

Hasta que esté desplegado, el hub lo presenta como «Próximamente»: el código `comparendos` está en
`Suite:Hosts:ComingSoon` (`services/core-api/src/Flit.Api/appsettings.json`) y su tarjeta lleva a
`/proximamente/comparendos` del hub. **Al desplegarlo en un ambiente, se quita de esa lista para ese ambiente.**

Se crea desde la plantilla de producto (`templates/flit-product`, C-07 del [plan maestro](../../docs/suite/plan-maestro.md)),
no a mano: así llega con login de la suite (`@flit/auth`), barra común (`@flit/shell`), habilitación por empresa y
su propio schema. Las reglas comunes están en el [contrato de plataforma v1](../../docs/suite/contrato-plataforma-v1.md).

Seguimiento en ADO: Epic #12870 (FLIT - GESTION COMPARENDOS).
