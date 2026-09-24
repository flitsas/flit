# Borradores de ADR — FLIT Suite

> **BORRADORES.** Regla FLIT 15 y skill `flit-adr-generator`: un ADR entra a `docs/decisions/`
> solo después de que un humano apruebe el borrador, y queda en `Propuesto` hasta que el Líder
> Técnico lo acepte en un PR aparte.
>
> **Numeración verificada sobre `origin/develop@e8b7ca65`:** el número más alto usado en el repo es
> ADR-0060 (`docs/decisions/ADR-0060-marca-blanca-identidad-dominio-y-tema-de-correo.md`). Se
> reservan 0061–0065. Si otra rama toma alguno antes, renumerar al mover. Citar siempre por slug.

| Borrador | Decisión |
|---|---|
| [ADR-0061](ADR-0061-suite-productos-plataforma-y-monorepo.md) | Suite: plataforma compartida, productos como servicios propios, monorepo y hosts en `flitsas.online` |
| [ADR-0062](ADR-0062-identidad-oidc-sobre-dominio-sellado.md) | Identidad OIDC que extiende el dominio sellado de ADR-0060; sesión por host (BFF); token por producto |
| [ADR-0063](ADR-0063-suscripciones-producto-y-rbac-por-producto.md) | Suscripción de productos por empresa y RBAC por producto, sin revertir HU #10664 |
| [ADR-0064](ADR-0064-datos-separados-eventos-y-reportes.md) | Datos separados por producto, integración por eventos y reportes consolidados |
| [ADR-0065](ADR-0065-consultas-externas-capacidad-de-plataforma.md) | Consultas externas como capacidad compartida de plataforma, con medición de consumo |

Contexto: [`docs/plan-suite-flitsas-diagnostico-y-plan.md`](../plan-suite-flitsas-diagnostico-y-plan.md).
